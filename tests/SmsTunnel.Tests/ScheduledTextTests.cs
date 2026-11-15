using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.Tests;

public class ScheduledTextTests
{
    /// <summary>The server's own check of sendAt (scheduled-texts.rules.ts, checkSendAt).</summary>
    private const string ServerPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,3})?)?(Z|[+-]\d{2}:\d{2})$";

    private const string Id = "6738c0ffee6738c0ffee6738";

    private const string Waiting =
        """{"id":"6738c0ffee6738c0ffee6738","to":"+14155550123","message":"See you at 10:30.","sendAt":"2026-11-16T07:00:00.000Z","status":"scheduled","deviceId":null,"messageId":null,"error":null,"createdAt":"2026-11-15T10:00:00.000Z"}""";

    private const string Sent =
        """{"id":"6738c0ffee6738c0ffee6739","to":"0721 123 456","message":"Your table is ready.","sendAt":"2026-11-15T09:00:00.000Z","status":"sent","deviceId":"6aea3248fedd7cccac9abf42","messageId":"6aea38f9b38ed3bed4f8569f","error":null,"createdAt":"2026-11-14T18:00:00.000Z"}""";

    private const string ScheduleAnswer =
        """{"success":true,"scheduled":true,"scheduledId":"6738c0ffee6738c0ffee6738","sendAt":"2026-11-16T07:00:00.000Z","data":""" + Waiting + "}";

    private static readonly DateTimeOffset Bucharest = new(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(2));

    public static TheoryData<DateTimeOffset, string> Times => new()
    {
        { new DateTimeOffset(2026, 11, 16, 7, 0, 0, TimeSpan.Zero), "2026-11-16T07:00:00Z" },
        { Bucharest, "2026-11-16T09:00:00+02:00" },
        { new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.FromHours(-5)), "2026-11-16T02:00:00-05:00" },
        { new DateTimeOffset(2026, 11, 16, 12, 45, 0, new TimeSpan(5, 45, 0)), "2026-11-16T12:45:00+05:45" },
        { new DateTimeOffset(2026, 11, 16, 7, 0, 59, 999, TimeSpan.Zero).AddTicks(9999), "2026-11-16T07:00:59Z" },
    };

    [Fact]
    public async Task Schedules_a_text_through_send_with_its_time()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await client.ScheduleSmsAsync("+14155550123", "See you at 10:30.", Bucharest);

        RecordedRequest request = handler.Single;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://smstunnel.io/api/v1/sms/send", request.Uri.ToString());
        Assert.Equal(Clients.Key, request.Header("X-API-Key"));
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.Null(request.Header("Idempotency-Key"));
        Assert.Equal("""{"to":"+14155550123","message":"See you at 10:30.","sendAt":"2026-11-16T09:00:00+02:00"}""", request.Body);
    }

    [Theory]
    [MemberData(nameof(Times))]
    public async Task Sends_the_time_with_its_offset_in_whole_seconds(DateTimeOffset sendAt, string sent)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await client.ScheduleSmsAsync("+14155550123", "Hi", sendAt);

        Assert.Equal(sent, SentAt(handler.Single));
        Assert.Matches(ServerPattern, sent);
        Assert.Equal(sendAt.AddTicks(-(sendAt.Ticks % TimeSpan.TicksPerSecond)), DateTimeOffset.Parse(sent, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_time_does_not_depend_on_the_current_culture()
    {
        var dotted = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        dotted.DateTimeFormat.TimeSeparator = ".";
        Assert.Equal("09.00.00", Bucharest.ToString("HH:mm:ss", dotted)); // what the format would give without the invariant culture
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = dotted;
        try
        {
            await client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }

        Assert.Equal("2026-11-16T09:00:00+02:00", SentAt(handler.Single));
    }

    [Fact]
    public async Task Sends_the_options_and_the_idempotency_key_as_for_any_text()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler, o => o.DefaultCountryCode = "+49");

        await client.ScheduleSmsAsync(" 0721 123 456 ", "Hi", Bucharest, new SendOptions
        {
            DeviceId = " 6aea3248fedd7cccac9abf42 ",
            DefaultCountryCode = "0040",
            IdempotencyKey = "appointment-981-reminder",
        });
        await client.ScheduleSmsAsync("0151 2345678", "Hallo", Bucharest);

        Assert.Equal(
            """{"to":"0721 123 456","message":"Hi","deviceId":"6aea3248fedd7cccac9abf42","defaultCountryCode":"+40","sendAt":"2026-11-16T09:00:00+02:00"}""",
            handler.Requests[0].Body);
        Assert.Equal("appointment-981-reminder", handler.Requests[0].Header("Idempotency-Key"));
        Assert.Equal("""{"to":"0151 2345678","message":"Hallo","defaultCountryCode":"+49","sendAt":"2026-11-16T09:00:00+02:00"}""", handler.Requests[1].Body);
        Assert.Null(handler.Requests[1].Header("Idempotency-Key"));
    }

    [Fact]
    public async Task Reads_the_scheduled_text()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer));

        ScheduledSendResult result = await client.ScheduleSmsAsync("+14155550123", "See you at 10:30.", Bucharest);

        Assert.Equal(Id, result.ScheduledId);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 7, 0, 0, TimeSpan.Zero), result.SendAt);
        Assert.Equal(TimeSpan.Zero, result.SendAt.Offset);
        Assert.Equal(Bucharest, result.SendAt);
        Assert.Equal(ScheduledTextStatus.Scheduled, result.Status);
        Assert.False(result.Replayed);
        Assert.True(result.Raw.GetProperty("scheduled").GetBoolean());

        ScheduledText text = result.ScheduledText;
        Assert.Equal(Id, text.Id);
        Assert.Equal("+14155550123", text.To);
        Assert.Equal("See you at 10:30.", text.Message);
        Assert.Equal(result.SendAt, text.SendAt);
        Assert.Equal(ScheduledTextStatus.Scheduled, text.Status);
        Assert.True(text.IsWaiting);
        Assert.False(text.IsDone);
        Assert.Null(text.DeviceId);
        Assert.Null(text.MessageId);
        Assert.Null(text.Error);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), text.CreatedAt);
        Assert.Equal("scheduled", text.Raw.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Says_when_the_answer_is_a_replay()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer, ("Idempotent-Replayed", "true")));

        ScheduledSendResult result = await client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest, new SendOptions { IdempotencyKey = "k-1" });

        Assert.True(result.Replayed);
        Assert.Equal(Id, result.ScheduledId);
    }

    [Theory]
    [InlineData("", "Hi")]
    [InlineData("   ", "Hi")]
    [InlineData("+14155550123, +14155550124", "Hi")]
    [InlineData("+14155550123", "")]
    [InlineData("+14155550123", "  ")]
    public async Task Refuses_a_missing_or_second_number_or_an_empty_text_before_sending(string to, string message)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleSmsAsync(to, message, Bucharest));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Refuses_a_time_that_is_not_set_and_bad_options_before_sending()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        ArgumentException unset = await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", default(DateTimeOffset)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest, new SendOptions { DefaultCountryCode = "abc" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest, new SendOptions { IdempotencyKey = "line\nbreak" }));

        Assert.Equal("sendAt", unset.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Refuses_a_DateTime_without_a_time_zone_instead_of_guessing_it()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);
        ISmsTunnelClient sms = client;
        var unspecified = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Unspecified);

        ArgumentException direct = await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", unspecified));
        ArgumentException throughInterface = await Assert.ThrowsAsync<ArgumentException>(() => sms.ScheduleSmsAsync("+14155550123", "Hi", unspecified));

        Assert.Equal("sendAt", direct.ParamName);
        Assert.Contains("DateTimeKind.Unspecified", throughInterface.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Takes_a_DateTime_in_utc_or_in_local_time()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, ScheduleAnswer);
        using SmsTunnelClient client = Clients.With(handler);
        ISmsTunnelClient sms = client;
        var local = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Local);

        await client.ScheduleSmsAsync("+14155550123", "Hi", new DateTime(2026, 11, 16, 7, 0, 0, DateTimeKind.Utc));
        await sms.ScheduleSmsAsync("+14155550123", "Hi", local);

        Assert.Equal("2026-11-16T07:00:00Z", SentAt(handler.Requests[0]));
        DateTimeOffset sentLocal = DateTimeOffset.Parse(SentAt(handler.Requests[1]), CultureInfo.InvariantCulture);
        Assert.Equal(new DateTimeOffset(local), sentLocal);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(local), sentLocal.Offset);
    }

    [Fact]
    public async Task The_interfaces_DateTime_overload_forwards_to_the_implementation()
    {
        var implementation = new OnlyScheduleClient();
        ISmsTunnelClient sms = implementation;

        await sms.ScheduleSmsAsync("+14155550123", "Hi", new DateTime(2026, 11, 16, 7, 0, 0, DateTimeKind.Utc));
        await Assert.ThrowsAsync<ArgumentException>(() => sms.ScheduleSmsAsync("+14155550123", "Hi", new DateTime(2026, 11, 16, 7, 0, 0)));

        DateTimeOffset forwarded = Assert.Single(implementation.Times);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 7, 0, 0, TimeSpan.Zero), forwarded);
    }

    [Fact]
    public async Task An_implementation_written_for_1_0_still_compiles_and_says_what_it_lacks()
    {
        ISmsTunnelClient sms = new OneZeroClient();

        NotSupportedException schedule = await Assert.ThrowsAsync<NotSupportedException>(() => sms.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));
        await Assert.ThrowsAsync<NotSupportedException>(() => sms.ScheduleSmsAsync("+14155550123", "Hi", new DateTime(2026, 11, 16, 7, 0, 0, DateTimeKind.Utc)));
        await Assert.ThrowsAsync<NotSupportedException>(() => sms.GetScheduledTextsAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => sms.GetScheduledTextAsync(Id));
        NotSupportedException cancel = await Assert.ThrowsAsync<NotSupportedException>(() => sms.CancelScheduledTextAsync(Id));

        Assert.Contains("OneZeroClient", schedule.Message, StringComparison.Ordinal);
        Assert.Contains("ISmsTunnelClient.ScheduleSmsAsync", schedule.Message, StringComparison.Ordinal);
        Assert.Contains("ISmsTunnelClient.CancelScheduledTextAsync", cancel.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_without_a_scheduled_text_says_whether_the_text_went_out_now()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer));
        using SmsTunnelClient other = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, """{"success":true}"""));

        SmsTunnelApiException sentNow = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));
        SmsTunnelApiException empty = await Assert.ThrowsAsync<SmsTunnelApiException>(() => other.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));

        Assert.Equal(200, sentNow.StatusCode);
        Assert.Contains("sent the text now", sentNow.Message, StringComparison.Ordinal);
        Assert.Contains("6aea38f9b38ed3bed4f8569f", sentNow.Message, StringComparison.Ordinal);
        Assert.Equal(Clients.SendAnswer, sentNow.ResponseBody);
        Assert.Contains("no scheduled text", empty.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_the_waiting_texts_of_this_key()
    {
        string answer = """{"success":true,"data":{"items":[""" + Waiting + """],"total":1,"page":1,"limit":50,"waiting":3,"max":10}}""";
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, answer);
        using SmsTunnelClient client = Clients.With(handler);

        ScheduledTextPage page = await client.GetScheduledTextsAsync();

        Assert.Equal(HttpMethod.Get, handler.Single.Method);
        Assert.Equal("https://smstunnel.io/api/v1/sms/scheduled", handler.Single.Uri.AbsoluteUri);
        Assert.Null(handler.Single.Body);
        ScheduledText text = Assert.Single(page.Texts);
        Assert.Equal(Id, text.Id);
        Assert.True(text.IsWaiting);
        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.Page);
        Assert.Equal(50, page.Limit);
        Assert.Equal(3, page.Waiting);
        Assert.Equal(10, page.Max);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Lists_the_done_texts_page_by_page()
    {
        const string failed =
            """{"id":"6738c0ffee6738c0ffee673a","to":"+14155550124","message":"Hi","sendAt":"2026-11-15T08:00:00.000Z","status":"failed","deviceId":null,"messageId":null,"error":"limit_reached","createdAt":"2026-11-14T08:00:00.000Z"}""";
        const string canceled =
            """{"id":"6738c0ffee6738c0ffee673b","to":"+14155550125","message":"Hi","sendAt":"2026-11-15T07:00:00.000Z","status":"canceled","deviceId":null,"messageId":null,"error":null,"createdAt":"2026-11-14T07:00:00.000Z"}""";
        const string unknown =
            """{"id":"6738c0ffee6738c0ffee673c","sendAt":"2026-11-15T06:00:00.000Z","status":"read-by-martians","error":"new_reason"}""";
        string answer = """{"success":true,"data":{"items":[""" + Sent + "," + failed + "," + canceled + "," + unknown + """],"total":120,"page":2,"limit":4,"waiting":0,"max":200}}""";
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, answer);
        using SmsTunnelClient client = Clients.With(handler);

        ScheduledTextPage page = await client.GetScheduledTextsAsync(new ScheduledTextsQuery { Scope = ScheduledTextScope.Done, Page = 2, Limit = 4 });

        Assert.Equal("https://smstunnel.io/api/v1/sms/scheduled?scope=done&page=2&limit=4", handler.Single.Uri.AbsoluteUri);
        Assert.Equal(4, page.Texts.Count);
        Assert.True(page.HasMore);
        Assert.Equal(200, page.Max);

        ScheduledText sent = page.Texts[0];
        Assert.Equal(ScheduledTextStatus.Sent, sent.Status);
        Assert.True(sent.IsDone);
        Assert.False(sent.IsWaiting);
        Assert.Equal("6aea38f9b38ed3bed4f8569f", sent.MessageId);
        Assert.Equal("6aea3248fedd7cccac9abf42", sent.DeviceId);
        Assert.Equal("0721 123 456", sent.To);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero), sent.SendAt);

        Assert.Equal(ScheduledTextStatus.Failed, page.Texts[1].Status);
        Assert.Equal("limit_reached", page.Texts[1].Error);
        Assert.Equal(ScheduledTextStatus.Canceled, page.Texts[2].Status);
        Assert.True(page.Texts[2].IsDone);
        Assert.Equal(ScheduledTextStatus.Unknown, page.Texts[3].Status);
        Assert.False(page.Texts[3].IsWaiting);
        Assert.False(page.Texts[3].IsDone);
        Assert.Equal("new_reason", page.Texts[3].Error);
        Assert.Equal("read-by-martians", page.Texts[3].Raw.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Names_the_waiting_scope_when_a_query_is_given()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"items":[],"total":0,"page":1,"limit":10,"waiting":0,"max":10}}""");
        using SmsTunnelClient client = Clients.With(handler);

        ScheduledTextPage page = await client.GetScheduledTextsAsync(new ScheduledTextsQuery { Limit = 10 });

        Assert.Equal("https://smstunnel.io/api/v1/sms/scheduled?scope=waiting&limit=10", handler.Single.Uri.AbsoluteUri);
        Assert.Empty(page.Texts);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData(0, null, ScheduledTextScope.Waiting)]
    [InlineData(101, null, ScheduledTextScope.Waiting)]
    [InlineData(null, 0, ScheduledTextScope.Done)]
    [InlineData(null, null, (ScheduledTextScope)7)]
    public async Task Refuses_a_limit_page_or_scope_out_of_range(int? limit, int? page, ScheduledTextScope scope)
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, "{}");
        using SmsTunnelClient client = Clients.With(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetScheduledTextsAsync(new ScheduledTextsQuery { Limit = limit, Page = page, Scope = scope }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reads_one_scheduled_text()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":""" + Sent + "}");
        using SmsTunnelClient client = Clients.With(handler);

        ScheduledText text = await client.GetScheduledTextAsync(" 6738c0ffee6738c0ffee6739 ");

        Assert.Equal(HttpMethod.Get, handler.Single.Method);
        Assert.Equal("https://smstunnel.io/api/v1/sms/scheduled/6738c0ffee6738c0ffee6739", handler.Single.Uri.ToString());
        Assert.Null(handler.Single.Body);
        Assert.Equal("6738c0ffee6738c0ffee6739", text.Id);
        Assert.Equal(ScheduledTextStatus.Sent, text.Status);
        Assert.Equal("6aea38f9b38ed3bed4f8569f", text.MessageId);
        Assert.Equal("Your table is ready.", text.Message);
    }

    [Fact]
    public async Task Cancels_a_scheduled_text()
    {
        string canceled = Waiting.Replace("\"status\":\"scheduled\"", "\"status\":\"canceled\"", StringComparison.Ordinal);
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":""" + canceled + "}");
        using SmsTunnelClient client = Clients.With(handler);

        ScheduledText text = await client.CancelScheduledTextAsync(Id);

        Assert.Equal(HttpMethod.Delete, handler.Single.Method);
        Assert.Equal("https://smstunnel.io/api/v1/sms/scheduled/6738c0ffee6738c0ffee6738", handler.Single.Uri.ToString());
        Assert.Null(handler.Single.Body);
        Assert.Null(handler.Single.ContentType);
        Assert.Null(handler.Single.Header("Idempotency-Key"));
        Assert.Equal(Id, text.Id);
        Assert.Equal(ScheduledTextStatus.Canceled, text.Status);
        Assert.True(text.IsDone);
    }

    [Fact]
    public async Task Cancels_over_real_http_with_delete_and_no_body()
    {
        string canceled = Waiting.Replace("\"status\":\"scheduled\"", "\"status\":\"canceled\"", StringComparison.Ordinal);
        await using var server = new TinyHttpServer(_ => TinyHttpServer.Response(200, "OK", """{"success":true,"data":""" + canceled + "}"));
        using var client = new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, BaseUrl = server.Url });

        ScheduledText text = await client.CancelScheduledTextAsync(Id);

        Assert.True(server.Requests.TryDequeue(out string? request));
        Assert.StartsWith($"DELETE /api/v1/sms/scheduled/{Id} HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains($"X-API-Key: {Clients.Key}\r\n", request, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\n", request, StringComparison.Ordinal);
        Assert.Equal(ScheduledTextStatus.Canceled, text.Status);
    }

    [Fact]
    public async Task Escapes_the_id_and_refuses_a_blank_one()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"status":"scheduled"}}""");
        using SmsTunnelClient client = Clients.With(handler);

        ScheduledText text = await client.GetScheduledTextAsync(" a/b?c ");

        Assert.Equal("/api/v1/sms/scheduled/a%2Fb%3Fc", handler.Single.Uri.AbsolutePath);
        Assert.Equal(string.Empty, handler.Single.Uri.Query);
        Assert.Equal("a/b?c", text.Id); // the answer had no id
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetScheduledTextAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CancelScheduledTextAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CancelScheduledTextAsync(null!));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(400, "SCHEDULE_TOO_SOON", SmsTunnelErrorCodes.ScheduleTooSoon, "Pick a time at least a minute from now. To send it now, use Send.")]
    [InlineData(400, "SCHEDULE_TOO_FAR", SmsTunnelErrorCodes.ScheduleTooFar, "Pick a time within the next 30 days.")]
    [InlineData(400, "SCHEDULE_INVALID_INPUT", SmsTunnelErrorCodes.ScheduleInvalidInput, "Check the phone, the number, the text and the time.")]
    [InlineData(400, "SCHEDULE_TEXT_TOO_LONG", SmsTunnelErrorCodes.ScheduleTextTooLong, "A text can be up to 1600 characters.")]
    [InlineData(400, "SCHEDULE_NUMBER_NOT_ALLOWED", SmsTunnelErrorCodes.ScheduleNumberNotAllowed, "Texts to short codes and premium-rate numbers can't be sent.")]
    [InlineData(400, "SCHEDULE_SEALED_ONLY_PHONE", SmsTunnelErrorCodes.ScheduleSealedOnlyPhone, "This phone sends only end-to-end encrypted texts, which can’t be scheduled.")]
    [InlineData(400, "SCHEDULE_ENCRYPTED_TEXT", SmsTunnelErrorCodes.ScheduleEncryptedText, "End-to-end encrypted texts can't be scheduled.")]
    [InlineData(400, "SCHEDULE_SIGN_IN_CODE", SmsTunnelErrorCodes.ScheduleSignInCode, "Sign-in codes can't be scheduled: send them when they are asked for.")]
    [InlineData(404, "SCHEDULE_PHONE_NOT_FOUND", SmsTunnelErrorCodes.SchedulePhoneNotFound, "This phone isn't in your account anymore.")]
    public async Task A_refused_schedule_keeps_its_code(int status, string code, string constant, string message)
    {
        string error = status == 404 ? "Not Found" : "Bad Request";
        string body = $$"""{"statusCode":{{status}},"error":"{{error}}","code":"{{code}}","message":"{{message}}"}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering((HttpStatusCode)status, body));

        SmsTunnelApiException refused = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));

        Assert.Equal(status, refused.StatusCode);
        Assert.Equal(constant, refused.ErrorCode);
        Assert.Equal(code, refused.ErrorCode);
        Assert.Equal(message, refused.Message);
        Assert.Null(refused.Max);
        Assert.Equal(body, refused.ResponseBody);
    }

    [Fact]
    public async Task The_plans_cap_on_waiting_texts_says_its_max()
    {
        const string body = """{"statusCode":403,"error":"Forbidden","code":"SCHEDULE_LIMIT_REACHED","message":"You have as many scheduled texts as your plan allows. Wait until some go out, or cancel one.","max":10}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.Forbidden, body));

        SmsTunnelPermissionException error = await Assert.ThrowsAsync<SmsTunnelPermissionException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.ScheduleLimitReached, error.ErrorCode);
        Assert.Equal(10, error.Max);
        Assert.Null(error.Permission);
        Assert.StartsWith("You have as many scheduled texts as your plan allows.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_keys_text_or_an_unknown_id_is_not_found()
    {
        const string body = """{"statusCode":404,"error":"Not Found","code":"SCHEDULE_NOT_FOUND","message":"This scheduled text no longer exists."}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.NotFound, body));

        SmsTunnelApiException read = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetScheduledTextAsync("000000000000000000000000"));
        SmsTunnelApiException cancel = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.CancelScheduledTextAsync("000000000000000000000000"));

        Assert.Equal(404, read.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.ScheduleNotFound, read.ErrorCode);
        Assert.Equal("This scheduled text no longer exists.", read.Message);
        Assert.Equal(SmsTunnelErrorCodes.ScheduleNotFound, cancel.ErrorCode);
    }

    [Fact]
    public async Task A_text_that_already_went_out_or_was_canceled_cant_be_canceled()
    {
        const string body = """{"statusCode":409,"error":"Conflict","code":"SCHEDULE_NOT_EDITABLE","message":"This text has already gone out or was canceled, or was scheduled through the API (cancel it instead)."}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.Conflict, body));

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.CancelScheduledTextAsync(Id));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.ScheduleNotEditable, error.ErrorCode);
    }

    [Fact]
    public async Task Too_many_requests_says_how_long_to_wait()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(
            (HttpStatusCode)429, """{"statusCode":429,"message":"ThrottlerException: Too Many Requests"}""", ("Retry-After", "12")));

        SmsTunnelRateLimitException list = await Assert.ThrowsAsync<SmsTunnelRateLimitException>(() => client.GetScheduledTextsAsync());
        SmsTunnelRateLimitException schedule = await Assert.ThrowsAsync<SmsTunnelRateLimitException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));

        Assert.Equal(TimeSpan.FromSeconds(12), list.RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(12), schedule.RetryAfter);
    }

    [Fact]
    public async Task A_key_without_sms_read_is_told_which_permission_it_lacks()
    {
        const string body = """{"statusCode":403,"error":"Forbidden","code":"PERMISSION_REQUIRED","permission":"sms:read","message":"This API key may not read texts."}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.Forbidden, body));

        SmsTunnelPermissionException error = await Assert.ThrowsAsync<SmsTunnelPermissionException>(() => client.GetScheduledTextsAsync());

        Assert.Equal(SmsTunnelErrorCodes.PermissionRequired, error.ErrorCode);
        Assert.Equal("sms:read", error.Permission);
        Assert.Null(error.Max);
    }

    [Fact]
    public async Task A_server_before_scheduled_texts_refuses_sendAt_like_any_bad_field()
    {
        const string body = """{"message":["property sendAt should not exist"],"error":"Bad Request","statusCode":400}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.BadRequest, body));

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));

        Assert.Equal(typeof(SmsTunnelApiException), error.GetType());
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("property sendAt should not exist", error.Message);
        Assert.Null(error.ErrorCode);
        Assert.Null(error.Max);
    }

    [Fact]
    public async Task No_answer_to_a_cancel_says_how_to_check_it()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using SmsTunnelClient client = Clients.With(handler, o => o.Timeout = TimeSpan.FromMilliseconds(200));

        SmsTunnelTimeoutException cancel = await Assert.ThrowsAsync<SmsTunnelTimeoutException>(() => client.CancelScheduledTextAsync(Id));
        SmsTunnelTimeoutException schedule = await Assert.ThrowsAsync<SmsTunnelTimeoutException>(() => client.ScheduleSmsAsync("+14155550123", "Hi", Bucharest));

        Assert.Contains("GetScheduledTextAsync", cancel.Message, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", schedule.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_are_records_tests_can_build()
    {
        var text = new ScheduledText { Id = Id, Status = ScheduledTextStatus.Scheduled, SendAt = Bucharest };
        var result = new ScheduledSendResult { ScheduledId = Id, SendAt = Bucharest, Status = text.Status, ScheduledText = text };
        var page = new ScheduledTextPage { Texts = new[] { text }, Total = 3, Page = 1, Limit = 2, Waiting = 3, Max = 10 };

        Assert.True(result.ScheduledText.IsWaiting);
        Assert.True(page.HasMore);
        Assert.Equal(text, result.ScheduledText with { });
    }

    private static string SentAt(RecordedRequest request)
    {
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        return body.RootElement.GetProperty("sendAt").GetString()!;
    }

    /// <summary>An implementation of ISmsTunnelClient as written for version 1.0.0: none of the scheduled-text members.</summary>
    private class OneZeroClient : ISmsTunnelClient
    {
        public Task<SendResult> SendSmsAsync(string phoneNumber, string message, SendOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SendResult> SendSignInCodeAsync(string phoneNumber, string message, SendOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<MessageStatus> GetMessageStatusAsync(string messageId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Phone>> GetPhonesAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Usage> GetUsageAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ReceivedTextPage> GetReceivedTextsAsync(ReceivedTextsQuery? query = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    /// <summary>Implements only the DateTimeOffset ScheduleSmsAsync: the interface's DateTime overload must forward to it.</summary>
    private sealed class OnlyScheduleClient : OneZeroClient, ISmsTunnelClient
    {
        public List<DateTimeOffset> Times { get; } = new();

        public Task<ScheduledSendResult> ScheduleSmsAsync(string phoneNumber, string message, DateTimeOffset sendAt, SendOptions? options = null, CancellationToken cancellationToken = default)
        {
            Times.Add(sendAt);
            var text = new ScheduledText { Id = Id, SendAt = sendAt, Status = ScheduledTextStatus.Scheduled };
            return Task.FromResult(new ScheduledSendResult { ScheduledId = Id, SendAt = sendAt, Status = text.Status, ScheduledText = text });
        }
    }
}
