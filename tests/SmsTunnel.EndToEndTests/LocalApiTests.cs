using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SmsTunnel.EndToEndTests;

// One at a time: the test server allows 30 sends a minute per key.
[Collection("SMSTunnel test server")]
public class LocalApiTests
{
    [Fact]
    public async Task Lists_the_phones()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();

        IReadOnlyList<Phone> phones = await client.GetPhonesAsync();

        Assert.NotEmpty(phones);
        if (server.DeviceId is not null)
        {
            Phone phone = Assert.Single(phones, p => p.Id == server.DeviceId);
            Assert.True(phone.IsOnline, "The test phone should be online.");
            Assert.NotNull(phone.LastSeenAt);
        }
    }

    [Fact]
    public async Task Reads_the_usage()
    {
        using SmsTunnelClient client = TestServer.Get().Client();

        Usage usage = await client.GetUsageAsync();

        Assert.False(string.IsNullOrEmpty(usage.Plan));
        Assert.True(usage.Used.SmsClean >= 0);
        Assert.Equal(JsonValueKindObject, usage.Raw.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Sends_a_text_that_the_phone_sends()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();
        string to = TestServer.Recipient();

        SendResult result = await client.SendSmsAsync(to, server.Text("a plain text"));
        MessageStatus status = await TestServer.WaitUntilSentAsync(client, result.MessageId);

        Assert.False(string.IsNullOrEmpty(result.MessageId));
        Assert.False(result.Replayed);
        Assert.Equal(result.MessageId, status.Id);
        Assert.Equal(to, status.PhoneNumber);
        Assert.Contains(status.State, new[] { MessageState.Sent, MessageState.Delivered });
        Assert.NotNull(status.SentAt);
    }

    [Fact]
    public async Task Completes_a_local_number_with_the_texts_country_code_from_the_chosen_phone()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();
        string to = TestServer.Recipient();

        SendResult result = await client.SendSmsAsync(to.Substring(2), server.Text("a local number"), new SendOptions
        {
            DefaultCountryCode = "1",
            DeviceId = server.DeviceId,
        });
        MessageStatus status = await TestServer.WaitUntilSentAsync(client, result.MessageId);

        Assert.Equal(to, status.PhoneNumber);
        Assert.Equal(MessageState.Sent, status.State);
    }

    [Fact]
    public async Task Completes_a_local_number_with_the_clients_country_code()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client(o => o.DefaultCountryCode = "+1");
        string to = TestServer.Recipient();

        SendResult result = await client.SendSmsAsync(to.Substring(2), server.Text("the client's country code"));
        MessageStatus status = await client.GetMessageStatusAsync(result.MessageId);

        Assert.Equal(to, status.PhoneNumber);
    }

    [Fact]
    public async Task An_idempotency_key_answers_a_retry_without_a_second_text()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();
        string to = TestServer.Recipient();
        var once = new SendOptions { IdempotencyKey = $"dotnet-e2e-{server.RunId}-{to}" };

        SendResult first = await client.SendSmsAsync(to, server.Text("sent once, asked twice"), once);
        SendResult retry = await client.SendSmsAsync(to, server.Text("sent once, asked twice"), once);
        SmsTunnelApiException other = await Assert.ThrowsAsync<SmsTunnelApiException>(
            () => client.SendSmsAsync(to, server.Text("another text, same key"), once));

        Assert.False(first.Replayed);
        Assert.True(retry.Replayed);
        Assert.Equal(first.MessageId, retry.MessageId);
        Assert.Equal(422, other.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.IdempotencyKeyReused, other.ErrorCode);
    }

    [Fact]
    public async Task Sends_a_sign_in_code()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();
        string to = TestServer.Recipient();

        SendResult result = await client.SendSignInCodeAsync(to, server.Text("your sign-in code is 482913"));
        MessageStatus status = await TestServer.WaitUntilSentAsync(client, result.MessageId);

        Assert.Equal(to, status.PhoneNumber);
        Assert.Equal(MessageState.Sent, status.State);
    }

    [Fact]
    public async Task Refuses_a_sign_in_code_text_without_a_code()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(
            () => client.SendSignInCodeAsync(TestServer.Recipient(), "Welcome aboard!"));

        Assert.Equal(400, error.StatusCode);
        Assert.Contains("No valid verification code found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schedules_a_text_reads_it_lists_it_and_cancels_it_before_its_time()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();
        DateTimeOffset sendAt = DateTimeOffset.UtcNow.AddHours(2).ToOffset(TimeSpan.FromHours(2));

        ScheduledSendResult scheduled = await client.ScheduleSmsAsync(TestServer.Recipient(), server.Text("scheduled, then canceled"), sendAt);
        ScheduledText read = await client.GetScheduledTextAsync(scheduled.ScheduledId);
        ScheduledTextPage waiting = await client.GetScheduledTextsAsync(new ScheduledTextsQuery { Limit = 100 });
        ScheduledText canceled = await client.CancelScheduledTextAsync(scheduled.ScheduledId);
        SmsTunnelApiException again = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.CancelScheduledTextAsync(scheduled.ScheduledId));

        Assert.False(string.IsNullOrEmpty(scheduled.ScheduledId));
        Assert.Equal(ScheduledTextStatus.Scheduled, scheduled.Status);
        Assert.Equal(sendAt.AddTicks(-(sendAt.Ticks % TimeSpan.TicksPerSecond)), scheduled.SendAt);
        Assert.Equal(scheduled.ScheduledId, read.Id);
        Assert.True(read.IsWaiting);
        Assert.Contains(waiting.Texts, text => text.Id == scheduled.ScheduledId);
        Assert.True(waiting.Max > 0);
        Assert.Equal(ScheduledTextStatus.Canceled, canceled.Status);
        Assert.Equal(409, again.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.ScheduleNotEditable, again.ErrorCode);
    }

    [Fact]
    public async Task Refuses_a_time_less_than_a_minute_ahead()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(
            () => client.ScheduleSmsAsync(TestServer.Recipient(), server.Text("too soon"), DateTimeOffset.UtcNow.AddSeconds(5)));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.ScheduleTooSoon, error.ErrorCode);
    }

    [Fact]
    public async Task Lists_received_texts()
    {
        TestServer server = TestServer.Get();
        using SmsTunnelClient client = server.Client();

        ReceivedTextPage page = await client.GetReceivedTextsAsync(new ReceivedTextsQuery
        {
            Limit = 5,
            Page = 1,
            Since = DateTimeOffset.UtcNow.AddDays(-7),
            DeviceId = server.DeviceId,
        });

        Assert.Equal(1, page.Page);
        Assert.Equal(5, page.Limit);
        Assert.True(page.Total >= page.Texts.Count);
        Assert.All(page.Texts, text => Assert.False(string.IsNullOrEmpty(text.Id)));
    }

    [Fact]
    public async Task A_wrong_key_is_refused()
    {
        TestServer server = TestServer.Get();
        using var client = new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = "sk_live_" + new string('0', 32), BaseUrl = server.BaseUrl });

        SmsTunnelAuthenticationException error = await Assert.ThrowsAsync<SmsTunnelAuthenticationException>(() => client.GetPhonesAsync());

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("Invalid or expired API key", error.Message);
    }

    [Fact]
    public async Task An_unknown_message_is_not_found()
    {
        using SmsTunnelClient client = TestServer.Get().Client();

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetMessageStatusAsync("000000000000000000000000"));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task Sends_through_dependency_injection()
    {
        TestServer server = TestServer.Get();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmsTunnel:ApiKey"] = server.ApiKey,
                ["SmsTunnel:BaseUrl"] = server.BaseUrl.ToString(),
                ["SmsTunnel:DefaultCountryCode"] = "1",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSmsTunnel();
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISmsTunnelClient sms = provider.GetRequiredService<ISmsTunnelClient>();
        string to = TestServer.Recipient();

        SendResult result = await sms.SendSmsAsync(to.Substring(2), server.Text("through AddSmsTunnel()"));
        MessageStatus status = await TestServer.WaitUntilSentAsync(sms, result.MessageId);

        Assert.Equal(to, status.PhoneNumber);
        Assert.Equal(MessageState.Sent, status.State);
        Assert.Contains(await sms.GetPhonesAsync(), phone => phone.IsOnline);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindObject = System.Text.Json.JsonValueKind.Object;
}

[CollectionDefinition("SMSTunnel test server", DisableParallelization = true)]
public class TestServerCollection
{
}
