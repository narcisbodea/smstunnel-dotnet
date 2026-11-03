using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.Tests;

public class ReadTests
{
    [Fact]
    public async Task Reads_a_status()
    {
        const string answer = """
            {"success":true,"data":{"id":"m1","phoneNumber":"+14155550123","status":"sent","deliveryStatus":"sent",
             "sentAt":"2026-11-03T19:41:46.622Z","deliveredAt":null,"errorMessage":null}}
            """;
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, answer);
        using SmsTunnelClient client = Clients.With(handler);

        MessageStatus status = await client.GetMessageStatusAsync("m1");

        Assert.Equal(HttpMethod.Get, handler.Single.Method);
        Assert.Equal("https://smstunnel.io/api/v1/sms/status/m1", handler.Single.Uri.ToString());
        Assert.Null(handler.Single.Body);
        Assert.Equal("m1", status.Id);
        Assert.Equal("+14155550123", status.PhoneNumber);
        Assert.Equal(MessageState.Sent, status.State);
        Assert.False(status.IsFinal);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 19, 41, 46, 622, TimeSpan.Zero), status.SentAt);
        Assert.Null(status.DeliveredAt);
        Assert.Null(status.ErrorMessage);
    }

    [Theory]
    [InlineData("""{"status":"failed","unconfirmed":true,"deliveryStatus":"unconfirmed"}""", MessageState.Unconfirmed, true)]
    [InlineData("""{"status":"failed","unconfirmed":true}""", MessageState.Unconfirmed, true)]
    [InlineData("""{"status":"failed","deliveryStatus":"failed"}""", MessageState.Failed, true)]
    [InlineData("""{"status":"delivered"}""", MessageState.Delivered, true)]
    [InlineData("""{"status":"pending","deliveryStatus":"pending"}""", MessageState.Pending, false)]
    [InlineData("""{"status":"sent","deliveryStatus":"read-by-martians"}""", MessageState.Unknown, false)]
    [InlineData("""{}""", MessageState.Unknown, false)]
    public async Task Reads_the_real_state(string data, MessageState state, bool final)
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, $$"""{"success":true,"data":{{data}}}"""));

        MessageStatus status = await client.GetMessageStatusAsync("m1");

        Assert.Equal(state, status.State);
        Assert.Equal(final, status.IsFinal);
        Assert.Equal("m1", status.Id);
    }

    [Fact]
    public async Task Escapes_the_message_id()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"id":"a/b?c"}}""");
        using SmsTunnelClient client = Clients.With(handler);

        await client.GetMessageStatusAsync(" a/b?c ");

        Assert.Equal("/api/v1/sms/status/a%2Fb%3Fc", handler.Single.Uri.AbsolutePath);
        Assert.Equal(string.Empty, handler.Single.Uri.Query);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetMessageStatusAsync(" "));
    }

    [Fact]
    public async Task Lists_the_phones()
    {
        const string answer = """
            {"success":true,"data":{"devices":[
              {"id":"6aea3248fedd7cccac9abf42","name":"Test phone","isOnline":true,"lastSeenAt":"2026-11-03T19:13:38.656Z"},
              {"id":"p2","name":"Old phone","isOnline":false}],"online":1,"total":2}}
            """;
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, answer);
        using SmsTunnelClient client = Clients.With(handler);

        IReadOnlyList<Phone> phones = await client.GetPhonesAsync();

        Assert.Equal("https://smstunnel.io/api/v1/devices", handler.Single.Uri.ToString());
        Assert.Equal(2, phones.Count);
        Assert.Equal("6aea3248fedd7cccac9abf42", phones[0].Id);
        Assert.Equal("Test phone", phones[0].Name);
        Assert.True(phones[0].IsOnline);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 19, 13, 38, 656, TimeSpan.Zero), phones[0].LastSeenAt);
        Assert.False(phones[1].IsOnline);
        Assert.Null(phones[1].LastSeenAt);
        Assert.Equal("Old phone", phones[1].Raw.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Reads_the_usage()
    {
        const string answer = """
            {"success":true,"data":{"plan":"free","accountType":"sub_account",
              "usage":{"smsClean":12,"smsBranded":3,"twoFaClean":4,"smsCleanMonth":120},
              "limits":{"smsClean":25,"twoFaClean":50,"smsCleanPerMonth":500,"smsBrandedPerDay":25},
              "remaining":{"smsClean":13,"twoFaClean":46,"smsCleanMonth":380,"smsBranded":22},
              "enterprise":{"id":"e1","name":"Acme"}}}
            """;
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, answer);
        using SmsTunnelClient client = Clients.With(handler);

        Usage usage = await client.GetUsageAsync();

        Assert.Equal("https://smstunnel.io/api/v1/account/usage", handler.Single.Uri.ToString());
        Assert.Equal("free", usage.Plan);
        Assert.Equal("sub_account", usage.AccountType);
        Assert.Equal(new UsageCounts { SmsClean = 12, SmsBranded = 3, TwoFaClean = 4, SmsCleanMonth = 120 }, usage.Used);
        Assert.Equal(new UsageLimits { SmsClean = 25, TwoFaClean = 50, SmsCleanPerMonth = 500, SmsBrandedPerDay = 25 }, usage.Limits);
        Assert.Equal(new UsageRemaining { SmsClean = 13, TwoFaClean = 46, SmsCleanMonth = 380, SmsBranded = 22 }, usage.Remaining);
        Assert.Equal(new UsageEnterprise { Id = "e1", Name = "Acme" }, usage.Enterprise);
    }

    [Fact]
    public async Task Reads_unlimited_as_null()
    {
        const string answer = """
            {"success":true,"data":{"plan":"pro","accountType":"standard",
              "usage":{"smsClean":2,"smsBranded":0,"twoFaClean":0,"smsCleanMonth":2},
              "limits":{"smsClean":"unlimited","twoFaClean":"unlimited","smsCleanPerMonth":"unlimited","smsBrandedPerDay":0},
              "remaining":{"smsClean":"unlimited","twoFaClean":"unlimited","smsCleanMonth":"unlimited","smsBranded":0}}}
            """;
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, answer));

        Usage usage = await client.GetUsageAsync();

        Assert.Null(usage.Limits.SmsClean);
        Assert.Null(usage.Limits.TwoFaClean);
        Assert.Null(usage.Limits.SmsCleanPerMonth);
        Assert.Equal(0, usage.Limits.SmsBrandedPerDay);
        Assert.Null(usage.Remaining.SmsClean);
        Assert.Equal(0, usage.Remaining.SmsBranded);
        Assert.Equal(2, usage.Used.SmsClean);
        Assert.Null(usage.Enterprise);
    }

    [Fact]
    public async Task Lists_received_texts_with_the_filters()
    {
        const string answer = """
            {"success":true,"data":{"messages":[
              {"id":"r2","sender":"+14155550199","message":"Yes, see you then","isEncrypted":false,"receivedAt":"2026-11-03T10:00:00.000Z",
               "simSlot":1,"subscriptionId":3,"deviceId":"6aea3248fedd7cccac9abf42","deviceName":"Test phone"},
              {"id":"r1","sender":"+14155550199","message":null,"isEncrypted":true,"encryptedPayload":"abc==","receivedAt":"2026-11-03T09:00:00.000Z",
               "deviceId":"6aea3248fedd7cccac9abf42","deviceName":"Test phone"}],
             "total":7,"page":2,"limit":2,"hasMore":true}}
            """;
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, answer);
        using SmsTunnelClient client = Clients.With(handler);

        ReceivedTextPage page = await client.GetReceivedTextsAsync(new ReceivedTextsQuery
        {
            Limit = 2,
            Page = 2,
            Since = new DateTimeOffset(2026, 11, 3, 12, 30, 0, TimeSpan.FromHours(2)),
            Phone = "+1 415",
            DeviceId = "6aea3248fedd7cccac9abf42",
        });

        Assert.Equal(
            "https://smstunnel.io/api/v1/sms/received?limit=2&page=2&since=2026-11-03T10%3A30%3A00.000Z&phone=%2B1%20415&deviceId=6aea3248fedd7cccac9abf42",
            handler.Single.Uri.AbsoluteUri);
        Assert.Equal(7, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.Limit);
        Assert.True(page.HasMore);
        Assert.Equal(2, page.Texts.Count);

        ReceivedText first = page.Texts[0];
        Assert.Equal("r2", first.Id);
        Assert.Equal("+14155550199", first.Sender);
        Assert.Equal("Yes, see you then", first.Message);
        Assert.False(first.IsEncrypted);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), first.ReceivedAt);
        Assert.Equal(1, first.SimSlot);
        Assert.Equal(3, first.SubscriptionId);
        Assert.Equal("Test phone", first.DeviceName);

        ReceivedText encrypted = page.Texts[1];
        Assert.Null(encrypted.Message);
        Assert.True(encrypted.IsEncrypted);
        Assert.Equal("abc==", encrypted.EncryptedPayload);
        Assert.Null(encrypted.SimSlot);
    }

    [Fact]
    public async Task Lists_received_texts_without_filters()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"messages":[],"total":0,"page":1,"limit":50,"hasMore":false}}""");
        using SmsTunnelClient client = Clients.With(handler);

        ReceivedTextPage page = await client.GetReceivedTextsAsync();

        Assert.Equal("https://smstunnel.io/api/v1/sms/received", handler.Single.Uri.AbsoluteUri);
        Assert.Empty(page.Texts);
        Assert.Equal(50, page.Limit);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(101, null)]
    [InlineData(null, 0)]
    public async Task Refuses_a_limit_or_page_out_of_range(int? limit, int? page)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, "{}");
        using SmsTunnelClient client = Clients.With(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetReceivedTextsAsync(new ReceivedTextsQuery { Limit = limit, Page = page }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Calls_a_base_url_with_a_path()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"devices":[]}}""");
        using SmsTunnelClient client = Clients.With(handler, o => o.BaseUrl = new Uri("http://127.0.0.1:3141/smstunnel"));

        await client.GetPhonesAsync();

        Assert.Equal("http://127.0.0.1:3141/smstunnel/api/v1/devices", handler.Single.Uri.ToString());
        Assert.Equal(new Uri("http://127.0.0.1:3141/smstunnel/"), client.BaseUrl);
    }
}
