using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmsTunnel.Internal;

namespace SmsTunnel;

/// <summary>
/// Sends texts through your own Android phone, from your own number, with SMSTunnel (https://smstunnel.io).
/// </summary>
/// <remarks>
/// <para>
/// Make one client and reuse it: it is thread-safe. Without an <see cref="HttpClient"/> of your own, every instance
/// shares one connection pool, so creating several is harmless too. In ASP.NET Core and other apps with dependency
/// injection, use <c>services.AddSmsTunnel()</c> from the <c>SmsTunnel.Extensions.DependencyInjection</c> package.
/// </para>
/// <para>
/// The client never follows redirects with its own connection pool, so the API key is never sent to another address,
/// and it never retries a call by itself: a send that timed out may already be on its way. To retry a send safely, give
/// it an <see cref="SendOptions.IdempotencyKey"/>.
/// </para>
/// </remarks>
public sealed class SmsTunnelClient : ISmsTunnelClient, IDisposable
{
    private const string SendPath = "api/v1/sms/send";
    private const string SendSignInCodePath = "api/v1/sms/send-2fa";

    private static readonly Lazy<HttpMessageHandler> SharedHandler = new(CreateHandler, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson = new("application/json");
    private static readonly ProductInfoHeaderValue UserAgentProduct = new("SmsTunnel-dotnet", LibraryVersion());
    private static readonly ProductInfoHeaderValue UserAgentComment = new("(+https://smstunnel.io)");

    // The body is JSON for the API, never put in a web page: "+40" and "ț" stay as they are instead of +40 and ț.
    private static readonly JsonWriterOptions BodyWriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    private readonly string? _defaultCountryCode;
    private readonly TimeSpan _timeout;

    /// <summary>Creates a client for <c>https://smstunnel.io</c> with this API key.</summary>
    /// <param name="apiKey">The key from your dashboard: Apps → Add an app. Read it from configuration, never from source code.</param>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    public SmsTunnelClient(string apiKey)
        : this(new SmsTunnelClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>Creates a client with these settings. It sends through a connection pool shared by all such clients.</summary>
    /// <param name="options">The API key and the other settings.</param>
    /// <exception cref="ArgumentException">A setting is invalid, such as an empty key or a country code that isn't one.</exception>
    public SmsTunnelClient(SmsTunnelClientOptions options)
        : this(new HttpClient(SharedHandler.Value, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan }, options, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a client that sends through your <see cref="HttpClient"/>, for example one from <c>IHttpClientFactory</c>.
    /// Its <see cref="HttpClient.BaseAddress"/> and default headers don't matter: every call uses <see cref="SmsTunnelClientOptions.BaseUrl"/>
    /// and sets its own headers. Build its handler with <c>AllowAutoRedirect = false</c>, so the key is never sent to another address.
    /// </summary>
    /// <param name="httpClient">Your HttpClient. The SmsTunnelClient doesn't dispose it.</param>
    /// <param name="options">The API key and the other settings.</param>
    public SmsTunnelClient(HttpClient httpClient, SmsTunnelClientOptions options)
        : this(httpClient ?? throw new ArgumentNullException(nameof(httpClient)), options, ownsHttpClient: false)
    {
    }

    private SmsTunnelClient(HttpClient httpClient, SmsTunnelClientOptions options, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            _apiKey = Inputs.ApiKey(options.ApiKey, nameof(options));
            _baseUrl = Inputs.BaseUrl(options.BaseUrl, nameof(options));
            _defaultCountryCode = Inputs.CountryCode(options.DefaultCountryCode, nameof(options));
            _timeout = Inputs.Timeout(options.Timeout, nameof(options));
        }
        catch
        {
            if (ownsHttpClient)
            {
                httpClient.Dispose();
            }

            throw;
        }

        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <summary>The address the client calls.</summary>
    public Uri BaseUrl => _baseUrl;

    /// <inheritdoc/>
    public Task<SendResult> SendSmsAsync(string phoneNumber, string message, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        return SendAsync(SendPath, phoneNumber, message, options, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<SendResult> SendSignInCodeAsync(string phoneNumber, string message, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        return SendAsync(SendSignInCodePath, phoneNumber, message, options, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MessageStatus> GetMessageStatusAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Give the message ID that the send returned.", nameof(messageId));
        }

        Answer answer = await CallAsync(HttpMethod.Get, "api/v1/sms/status/" + Uri.EscapeDataString(messageId.Trim()), null, null, cancellationToken).ConfigureAwait(false);
        JsonElement data = DataOf(answer.Root);

        string? deliveryStatus = JsonFields.String(data, "deliveryStatus");
        string? state = deliveryStatus ?? (JsonFields.Bool(data, "unconfirmed") ? "unconfirmed" : JsonFields.String(data, "status"));
        return new MessageStatus
        {
            Id = JsonFields.String(data, "id") ?? messageId.Trim(),
            PhoneNumber = JsonFields.String(data, "phoneNumber"),
            State = StateOf(state),
            SentAt = JsonFields.Time(data, "sentAt"),
            DeliveredAt = JsonFields.Time(data, "deliveredAt"),
            ErrorMessage = JsonFields.String(data, "errorMessage"),
            Raw = answer.Root,
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Phone>> GetPhonesAsync(CancellationToken cancellationToken = default)
    {
        Answer answer = await CallAsync(HttpMethod.Get, "api/v1/devices", null, null, cancellationToken).ConfigureAwait(false);
        JsonElement data = DataOf(answer.Root);

        var phones = new List<Phone>();
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("devices", out JsonElement devices)
            && devices.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement device in devices.EnumerateArray())
            {
                if (device.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                phones.Add(new Phone
                {
                    Id = JsonFields.String(device, "id") ?? string.Empty,
                    Name = JsonFields.String(device, "name"),
                    IsOnline = JsonFields.Bool(device, "isOnline"),
                    LastSeenAt = JsonFields.Time(device, "lastSeenAt"),
                    Raw = device,
                });
            }
        }

        return phones.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<Usage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        Answer answer = await CallAsync(HttpMethod.Get, "api/v1/account/usage", null, null, cancellationToken).ConfigureAwait(false);
        JsonElement data = DataOf(answer.Root);
        JsonElement used = JsonFields.Object(data, "usage");
        JsonElement limits = JsonFields.Object(data, "limits");
        JsonElement remaining = JsonFields.Object(data, "remaining");
        JsonElement enterprise = JsonFields.Object(data, "enterprise");

        return new Usage
        {
            Plan = JsonFields.String(data, "plan"),
            AccountType = JsonFields.String(data, "accountType"),
            Used = new UsageCounts
            {
                SmsClean = JsonFields.Count(used, "smsClean"),
                SmsBranded = JsonFields.Count(used, "smsBranded"),
                TwoFaClean = JsonFields.Count(used, "twoFaClean"),
                SmsCleanMonth = JsonFields.Count(used, "smsCleanMonth"),
            },
            Limits = new UsageLimits
            {
                SmsClean = JsonFields.Quota(limits, "smsClean"),
                TwoFaClean = JsonFields.Quota(limits, "twoFaClean"),
                SmsCleanPerMonth = JsonFields.Quota(limits, "smsCleanPerMonth"),
                SmsBrandedPerDay = JsonFields.Quota(limits, "smsBrandedPerDay"),
            },
            Remaining = new UsageRemaining
            {
                SmsClean = JsonFields.Quota(remaining, "smsClean"),
                TwoFaClean = JsonFields.Quota(remaining, "twoFaClean"),
                SmsCleanMonth = JsonFields.Quota(remaining, "smsCleanMonth"),
                SmsBranded = JsonFields.Quota(remaining, "smsBranded"),
            },
            Enterprise = enterprise.ValueKind == JsonValueKind.Object
                ? new UsageEnterprise { Id = JsonFields.String(enterprise, "id"), Name = JsonFields.String(enterprise, "name") }
                : null,
            Raw = answer.Root,
        };
    }

    /// <inheritdoc/>
    public async Task<ReceivedTextPage> GetReceivedTextsAsync(ReceivedTextsQuery? query = null, CancellationToken cancellationToken = default)
    {
        string path = "api/v1/sms/received" + QueryString(query);
        Answer answer = await CallAsync(HttpMethod.Get, path, null, null, cancellationToken).ConfigureAwait(false);
        JsonElement data = DataOf(answer.Root);

        var texts = new List<ReceivedText>();
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("messages", out JsonElement messages)
            && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement text in messages.EnumerateArray())
            {
                if (text.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                texts.Add(new ReceivedText
                {
                    Id = JsonFields.String(text, "id") ?? string.Empty,
                    Sender = JsonFields.String(text, "sender"),
                    Message = JsonFields.String(text, "message"),
                    IsEncrypted = JsonFields.Bool(text, "isEncrypted"),
                    EncryptedPayload = JsonFields.String(text, "encryptedPayload"),
                    ReceivedAt = JsonFields.Time(text, "receivedAt"),
                    SimSlot = JsonFields.Int(text, "simSlot"),
                    SubscriptionId = JsonFields.Int(text, "subscriptionId"),
                    DeviceId = JsonFields.String(text, "deviceId"),
                    DeviceName = JsonFields.String(text, "deviceName"),
                    Raw = text,
                });
            }
        }

        return new ReceivedTextPage
        {
            Texts = texts.AsReadOnly(),
            Total = JsonFields.Int(data, "total") ?? texts.Count,
            Page = JsonFields.Int(data, "page") ?? query?.Page ?? 1,
            Limit = JsonFields.Int(data, "limit") ?? query?.Limit ?? texts.Count,
            HasMore = JsonFields.Bool(data, "hasMore"),
        };
    }

    /// <summary>Releases the client's own <see cref="HttpClient"/>. An HttpClient you passed in is left alone.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task<SendResult> SendAsync(string path, string phoneNumber, string message, SendOptions? options, CancellationToken cancellationToken)
    {
        Inputs.Recipient(phoneNumber, nameof(phoneNumber));
        Inputs.Text(message, nameof(message));
        string? countryCode = Inputs.CountryCode(options?.DefaultCountryCode, nameof(options)) ?? _defaultCountryCode;
        string? idempotencyKey = Inputs.IdempotencyKey(options?.IdempotencyKey, nameof(options));
        string? deviceId = string.IsNullOrWhiteSpace(options?.DeviceId) ? null : options!.DeviceId!.Trim();

        byte[] body = SendBody(phoneNumber.Trim(), message, deviceId, countryCode);
        Answer answer = await CallAsync(HttpMethod.Post, path, body, idempotencyKey, cancellationToken).ConfigureAwait(false);

        JsonElement root = answer.Root;
        JsonElement data = JsonFields.Object(root, "data");
        JsonElement remaining = JsonFields.Object(data, "remaining");
        return new SendResult
        {
            MessageId = JsonFields.String(data, "messageId") ?? JsonFields.String(root, "messageId") ?? string.Empty,
            Queued = JsonFields.Bool(root, "queued") || JsonFields.Bool(data, "queued"),
            Branded = JsonFields.Bool(data, "branded"),
            Remaining = remaining.ValueKind == JsonValueKind.Object
                ? new RemainingQuota { Clean = JsonFields.Quota(remaining, "clean"), Total = JsonFields.Quota(remaining, "total") }
                : null,
            Replayed = answer.Replayed,
            Raw = root,
        };
    }

    private async Task<Answer> CallAsync(HttpMethod method, string path, byte[]? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(method, new Uri(_baseUrl, path));
        request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        request.Headers.Accept.Add(AcceptJson);
        request.Headers.UserAgent.Add(UserAgentProduct);
        request.Headers.UserAgent.Add(UserAgentComment);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        using CancellationTokenSource? timeoutSource = _timeout == System.Threading.Timeout.InfiniteTimeSpan
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource?.CancelAfter(_timeout);
        CancellationToken token = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Timeout(method, timeoutSource, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsTunnelConnectionException($"SMSTunnel could not be reached at {_baseUrl.Host}: {ex.Message}", ex);
        }

        using (response)
        {
            string text;
            try
            {
                text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw Timeout(method, timeoutSource, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new SmsTunnelConnectionException($"The connection to SMSTunnel broke before the answer arrived: {ex.Message}", ex);
            }

            int status = (int)response.StatusCode;
            if (status is >= 300 and <= 399)
            {
                Uri? location = response.Headers.Location;
                throw new SmsTunnelApiException(
                    status,
                    $"SMSTunnel answered with a redirect ({status}){(location is null ? string.Empty : " to " + location)}. " +
                    "The client doesn't follow redirects, so your API key is never sent to another address: set BaseUrl to the address the API answers on.",
                    responseBody: text);
            }

            if (status is < 200 or > 299)
            {
                throw ErrorFactory.Create(status, response.ReasonPhrase, text, ErrorFactory.RetryAfter(response.Headers, DateTimeOffset.UtcNow));
            }

            JsonElement root = ErrorFactory.TryParse(text);
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SmsTunnelApiException(
                    status,
                    $"SMSTunnel answered with something that is not JSON. Check that BaseUrl ({_baseUrl}) is the SMSTunnel API.",
                    responseBody: text);
            }

            if (JsonFields.OptionalBool(root, "success") == false)
            {
                throw new SmsTunnelApiException(
                    status,
                    ErrorFactory.MessageOf(root) ?? "SMSTunnel did not accept the request.",
                    JsonFields.String(root, "code"),
                    JsonFields.String(root, "permission"),
                    responseBody: text);
            }

            bool replayed = response.Headers.TryGetValues("Idempotent-Replayed", out IEnumerable<string>? values)
                && string.Equals(string.Join(",", values).Trim(), "true", StringComparison.OrdinalIgnoreCase);
            return new Answer(root, replayed);
        }
    }

    private SmsTunnelTimeoutException Timeout(HttpMethod method, CancellationTokenSource? ours, Exception inner)
    {
        TimeSpan timeout = ours is { IsCancellationRequested: true } ? _timeout : _http.Timeout;
        string seconds = timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        string advice = method == HttpMethod.Post
            ? " The text may already be on its way: retry only with the same IdempotencyKey."
            : string.Empty;
        return new SmsTunnelTimeoutException($"SMSTunnel did not answer within {seconds} seconds.{advice}", timeout, inner);
    }

    private static JsonElement DataOf(JsonElement root)
    {
        JsonElement data = JsonFields.Object(root, "data");
        return data.ValueKind == JsonValueKind.Object ? data : root;
    }

    private static MessageState StateOf(string? state)
    {
        return state?.Trim().ToUpperInvariant() switch
        {
            "PENDING" => MessageState.Pending,
            "SENT" => MessageState.Sent,
            "DELIVERED" => MessageState.Delivered,
            "FAILED" => MessageState.Failed,
            "UNCONFIRMED" => MessageState.Unconfirmed,
            _ => MessageState.Unknown,
        };
    }

    private static byte[] SendBody(string to, string message, string? deviceId, string? countryCode)
    {
        var buffer = new ArrayBufferWriter<byte>(256 + (message.Length * 2));
        using (var writer = new Utf8JsonWriter(buffer, BodyWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("to", to);
            writer.WriteString("message", message);
            if (deviceId is not null)
            {
                writer.WriteString("deviceId", deviceId);
            }

            if (countryCode is not null)
            {
                writer.WriteString("defaultCountryCode", countryCode);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string QueryString(ReceivedTextsQuery? query)
    {
        if (query is null)
        {
            return string.Empty;
        }

        var parts = new StringBuilder();
        void Add(string name, string value)
        {
            parts.Append(parts.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        if (query.Limit is int limit)
        {
            if (limit is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(query), limit, "Limit must be between 1 and 100.");
            }

            Add("limit", limit.ToString(CultureInfo.InvariantCulture));
        }

        if (query.Page is int page)
        {
            if (page < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(query), page, "Page starts at 1.");
            }

            Add("page", page.ToString(CultureInfo.InvariantCulture));
        }

        if (query.Since is DateTimeOffset since)
        {
            Add("since", since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(query.Phone))
        {
            Add("phone", query.Phone.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            Add("deviceId", query.DeviceId.Trim());
        }

        return parts.ToString();
    }

    private static HttpMessageHandler CreateHandler()
    {
        if (SocketsHttpHandler.IsSupported)
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                // New connections every few minutes, so a change in SMSTunnel's address is picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
        }

        return new HttpClientHandler { AllowAutoRedirect = false };
    }

    private static string LibraryVersion()
    {
        string? version = typeof(SmsTunnelClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
        {
            return "1.0.0";
        }

        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? version.Substring(0, plus) : version;
    }

    private readonly record struct Answer(JsonElement Root, bool Replayed);
}
