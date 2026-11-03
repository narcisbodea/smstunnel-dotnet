using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.EndToEndTests;

/// <summary>
/// The SMSTunnel test server the end-to-end tests run against, from the environment:
/// <list type="bullet">
/// <item><c>SMSTUNNEL_E2E_KEY_FILE</c>: a JSON file <c>{"serverUrl", "apiKey", "deviceId"}</c> (a <c>"keys"</c> object is
/// read too, with <c>SMSTUNNEL_E2E_KEY_NAME</c> choosing the key);</item>
/// <item>or <c>SMSTUNNEL_E2E_URL</c> and <c>SMSTUNNEL_E2E_API_KEY</c> (and <c>SMSTUNNEL_E2E_DEVICE_ID</c>), which also override the file.</item>
/// </list>
/// Without them every test is skipped. They never run against smstunnel.io.
/// </summary>
internal sealed record TestServer(Uri BaseUrl, string ApiKey, string? DeviceId, string RunId)
{
    private static readonly Lazy<TestServer?> Current = new(Load);

    public static TestServer Get()
    {
        TestServer? server = Current.Value;
        if (server is null)
        {
            Assert.Skip("No test server: set SMSTUNNEL_E2E_KEY_FILE, or SMSTUNNEL_E2E_URL and SMSTUNNEL_E2E_API_KEY.");
        }

        return server;
    }

    public SmsTunnelClient Client(Action<SmsTunnelClientOptions>? configure = null)
    {
        var options = new SmsTunnelClientOptions { ApiKey = ApiKey, BaseUrl = BaseUrl };
        configure?.Invoke(options);
        return new SmsTunnelClient(options);
    }

    /// <summary>A test number, +1415555xxxx.</summary>
    public static string Recipient() => "+1415555" + RandomNumberGenerator.GetInt32(1000, 10000).ToString(CultureInfo.InvariantCulture);

    public string Text(string what) => $"SMSTunnel .NET e2e {RunId}: {what}";

    /// <summary>Asks for the status until the test phone has sent the text.</summary>
    public static async Task<MessageStatus> WaitUntilSentAsync(ISmsTunnelClient client, string messageId)
    {
        DateTimeOffset giveUp = DateTimeOffset.UtcNow.AddSeconds(45);
        while (true)
        {
            MessageStatus status = await client.GetMessageStatusAsync(messageId);
            if (status.State != MessageState.Pending || DateTimeOffset.UtcNow > giveUp)
            {
                return status;
            }

            await Task.Delay(500);
        }
    }

    private static TestServer? Load()
    {
        string? url = null;
        string? key = null;
        string? deviceId = null;

        string? file = Environment.GetEnvironmentVariable("SMSTUNNEL_E2E_KEY_FILE");
        if (!string.IsNullOrWhiteSpace(file))
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = json.RootElement;
            url = Field(root, "serverUrl");
            key = Field(root, "apiKey");
            deviceId = Field(root, "deviceId");
            string? keyName = Environment.GetEnvironmentVariable("SMSTUNNEL_E2E_KEY_NAME");
            if (!string.IsNullOrWhiteSpace(keyName) && root.TryGetProperty("keys", out JsonElement keys))
            {
                key = Field(keys, keyName) ?? throw new InvalidOperationException($"The key file has no keys.{keyName}.");
            }
        }

        url = Environment.GetEnvironmentVariable("SMSTUNNEL_E2E_URL") ?? url;
        key = Environment.GetEnvironmentVariable("SMSTUNNEL_E2E_API_KEY") ?? key;
        deviceId = Environment.GetEnvironmentVariable("SMSTUNNEL_E2E_DEVICE_ID") ?? deviceId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var baseUrl = new Uri(url, UriKind.Absolute);
        string host = baseUrl.Host.ToLowerInvariant();
        if (host == "smstunnel.io" || host.EndsWith(".smstunnel.io", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The end-to-end tests send real texts: never run them against smstunnel.io. Use a test server.");
        }

        string runId = Environment.GetEnvironmentVariable("SMSTUNNEL_E2E_RUN_ID")
            ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        return new TestServer(baseUrl, key, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId, runId);
    }

    private static string? Field(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
