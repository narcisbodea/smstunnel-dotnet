using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.Tests;

public class SendTests
{
    [Fact]
    public async Task Sends_one_text_with_the_key_as_json()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await client.SendSmsAsync("+14155550123", "Your order #1042 has shipped.");

        RecordedRequest request = handler.Single;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://smstunnel.io/api/v1/sms/send", request.Uri.ToString());
        Assert.Equal(Clients.Key, request.Header("X-API-Key"));
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.StartsWith("SmsTunnel-dotnet/", request.Header("User-Agent"), StringComparison.Ordinal);
        Assert.Null(request.Header("Idempotency-Key"));
        Assert.Null(request.Header("Authorization"));
        Assert.Equal("""{"to":"+14155550123","message":"Your order #1042 has shipped."}""", request.Body);
    }

    [Fact]
    public async Task Reads_the_answer()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer));

        SendResult result = await client.SendSmsAsync("+14155550123", "Hi");

        Assert.Equal("6aea38f9b38ed3bed4f8569f", result.MessageId);
        Assert.False(result.Queued);
        Assert.False(result.Branded);
        Assert.False(result.Replayed);
        Assert.NotNull(result.Remaining);
        Assert.Null(result.Remaining.Clean);
        Assert.Null(result.Remaining.Total);
        Assert.True(result.Raw.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Says_when_the_text_waits_for_an_offline_phone()
    {
        const string answer = """{"success":true,"messageId":"m1","queued":true,"data":{"messageId":"m1","branded":true,"queued":true,"remaining":{"clean":0,"total":24}}}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, answer));

        SendResult result = await client.SendSmsAsync("+14155550123", "Hi");

        Assert.True(result.Queued);
        Assert.True(result.Branded);
        Assert.Equal(0, result.Remaining!.Clean);
        Assert.Equal(24, result.Remaining.Total);
    }

    [Fact]
    public async Task Sends_the_options()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await client.SendSmsAsync(" 0721 123 456 ", "Hi", new SendOptions
        {
            DeviceId = " 6aea3248fedd7cccac9abf42 ",
            DefaultCountryCode = "0040",
            IdempotencyKey = "order-1042-shipped",
        });

        RecordedRequest request = handler.Single;
        Assert.Equal("""{"to":"0721 123 456","message":"Hi","deviceId":"6aea3248fedd7cccac9abf42","defaultCountryCode":"+40"}""", request.Body);
        Assert.Equal("order-1042-shipped", request.Header("Idempotency-Key"));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("49", null, "+49")]
    [InlineData("+49", "+40", "+40")]
    [InlineData(null, "1", "+1")]
    [InlineData("0049", " ", "+49")]
    public async Task The_texts_country_code_wins_over_the_clients(string? clientDefault, string? perText, string? sent)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler, o => o.DefaultCountryCode = clientDefault);

        await client.SendSmsAsync("4155550123", "Hi", new SendOptions { DefaultCountryCode = perText });

        using JsonDocument body = JsonDocument.Parse(handler.Single.Body!);
        bool present = body.RootElement.TryGetProperty("defaultCountryCode", out JsonElement value);
        Assert.Equal(sent is not null, present);
        if (sent is not null)
        {
            Assert.Equal(sent, value.GetString());
        }
    }

    [Fact]
    public async Task Sends_sign_in_codes_to_send_2fa()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        SendResult result = await client.SendSignInCodeAsync("+14155550123", "Your sign-in code is 482913", new SendOptions { IdempotencyKey = "login-77" });

        Assert.Equal("https://smstunnel.io/api/v1/sms/send-2fa", handler.Single.Uri.ToString());
        Assert.Equal("""{"to":"+14155550123","message":"Your sign-in code is 482913"}""", handler.Single.Body);
        Assert.Equal("login-77", handler.Single.Header("Idempotency-Key"));
        Assert.Equal("6aea38f9b38ed3bed4f8569f", result.MessageId);
    }

    [Fact]
    public async Task Says_when_the_answer_is_a_replay()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer, ("Idempotent-Replayed", "true")));

        SendResult result = await client.SendSmsAsync("+14155550123", "Hi", new SendOptions { IdempotencyKey = "k-1" });

        Assert.True(result.Replayed);
    }

    [Fact]
    public async Task Keeps_text_as_written()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await client.SendSmsAsync("+14155550123", "  Ți-am scris: \"ok\" 👍\nLinia 2  ");

        using JsonDocument body = JsonDocument.Parse(handler.Single.Body!);
        Assert.Equal("  Ți-am scris: \"ok\" 👍\nLinia 2  ", body.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("", "Hi")]
    [InlineData("   ", "Hi")]
    [InlineData("+14155550123, +14155550124", "Hi")]
    [InlineData("+14155550123;+14155550124", "Hi")]
    [InlineData("+14155550123", "")]
    [InlineData("+14155550123", "  ")]
    public async Task Refuses_a_missing_or_second_number_or_an_empty_text_before_sending(string to, string message)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendSmsAsync(to, message));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("+0")]
    [InlineData("12345")]
    [InlineData("+4O")]
    [InlineData("++40")]
    [InlineData("٤٠")]
    public async Task Refuses_a_country_code_that_isnt_one(string code)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendSmsAsync("0721123456", "Hi", new SendOptions { DefaultCountryCode = code }));

        Assert.Contains("is not a country code", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("ключ")]
    [InlineData("line\nbreak")]
    [InlineData("   ")]
    public async Task Refuses_an_idempotency_key_the_server_would_refuse(string key)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendSmsAsync("+14155550123", "Hi", new SendOptions { IdempotencyKey = key }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendSmsAsync("+14155550123", "Hi", new SendOptions { IdempotencyKey = new string('k', 256) }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_empty_idempotency_key_means_none()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await client.SendSmsAsync("+14155550123", "Hi", new SendOptions { IdempotencyKey = string.Empty });
        await client.SendSmsAsync("+14155550123", "Hi", new SendOptions { IdempotencyKey = new string('k', 255) });

        Assert.Null(handler.Requests[0].Header("Idempotency-Key"));
        Assert.Equal(new string('k', 255), handler.Requests[1].Header("Idempotency-Key"));
    }
}
