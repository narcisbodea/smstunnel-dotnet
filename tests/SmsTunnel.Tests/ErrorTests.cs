using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.Tests;

public class ErrorTests
{
    [Fact]
    public async Task A_wrong_key_is_an_authentication_error()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(
            HttpStatusCode.Unauthorized, """{"message":"Invalid or expired API key","error":"Unauthorized","statusCode":401}"""));

        SmsTunnelAuthenticationException error = await Assert.ThrowsAsync<SmsTunnelAuthenticationException>(() => client.GetPhonesAsync());

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("Invalid or expired API key", error.Message);
        Assert.Null(error.ErrorCode);
        Assert.Null(error.RetryAfter);
        Assert.Contains("statusCode", error.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_permission_has_its_code_and_name()
    {
        const string body = """
            {"statusCode":403,"error":"Forbidden","code":"PERMISSION_REQUIRED","permission":"sms:inbox",
             "message":"This API key may not read received texts. Allow it in the dashboard."}
            """;
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.Forbidden, body));

        SmsTunnelPermissionException error = await Assert.ThrowsAsync<SmsTunnelPermissionException>(() => client.GetReceivedTextsAsync());

        Assert.Equal(403, error.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.PermissionRequired, error.ErrorCode);
        Assert.Equal("sms:inbox", error.Permission);
        Assert.StartsWith("This API key may not read received texts.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_requests_says_how_long_to_wait()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(
            (HttpStatusCode)429,
            """{"statusCode":429,"message":"ThrottlerException: Too Many Requests"}""",
            ("Retry-After", "17")));

        SmsTunnelRateLimitException error = await Assert.ThrowsAsync<SmsTunnelRateLimitException>(() => client.SendSmsAsync("+14155550123", "Hi"));

        Assert.Equal(429, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(17), error.RetryAfter);
        Assert.Equal("ThrottlerException: Too Many Requests", error.Message);
    }

    [Fact]
    public async Task Retry_after_can_be_a_date()
    {
        string date = DateTimeOffset.UtcNow.AddSeconds(90).ToString("R");
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering((HttpStatusCode)429, "{}", ("Retry-After", date)));

        SmsTunnelRateLimitException error = await Assert.ThrowsAsync<SmsTunnelRateLimitException>(() => client.GetUsageAsync());

        Assert.NotNull(error.RetryAfter);
        Assert.InRange(error.RetryAfter.Value, TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(91));
        Assert.Equal("SMSTunnel returned error: 429 Too Many Requests", error.Message);
    }

    [Fact]
    public async Task The_daily_limit_has_no_retry_after()
    {
        const string body = """{"statusCode":429,"message":"You reached today's limit of 300 texts on the Starter plan. More can go out tomorrow, or on a higher plan."}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering((HttpStatusCode)429, body));

        SmsTunnelRateLimitException error = await Assert.ThrowsAsync<SmsTunnelRateLimitException>(() => client.SendSmsAsync("+14155550123", "Hi"));

        Assert.Null(error.RetryAfter);
        Assert.StartsWith("You reached today's limit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task A_server_problem_is_a_server_error(int status)
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering((HttpStatusCode)status, """{"statusCode":500,"message":"Internal server error"}"""));

        SmsTunnelServerException error = await Assert.ThrowsAsync<SmsTunnelServerException>(() => client.SendSmsAsync("+14155550123", "Hi"));

        Assert.Equal(status, error.StatusCode);
        Assert.Equal("Internal server error", error.Message);
    }

    [Fact]
    public async Task A_proxy_error_page_reads_as_plain_words()
    {
        const string page = "<html><head><title>503 Service Temporarily Unavailable</title></head><body><center><h1>503 Service Temporarily Unavailable</h1></center><hr><center>nginx</center></body></html>";
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(page, Encoding.UTF8, "text/html") });
        using SmsTunnelClient client = Clients.With(handler);

        SmsTunnelServerException error = await Assert.ThrowsAsync<SmsTunnelServerException>(() => client.GetPhonesAsync());

        Assert.Equal(503, error.StatusCode);
        Assert.Equal("503 Service Temporarily Unavailable 503 Service Temporarily Unavailable nginx", error.Message);
        Assert.Equal(page, error.ResponseBody);
    }

    [Fact]
    public async Task Refused_fields_are_listed()
    {
        const string body = """
            {"message":["Phone number must contain 7-25 characters (digits, spaces, dots, dashes, + allowed)",
             "to must be a string in international format, e.g. \"+14155550123\""],"error":"Bad Request","statusCode":400}
            """;
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.BadRequest, body));

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.SendSmsAsync("+1", "Hi"));

        Assert.Equal(typeof(SmsTunnelApiException), error.GetType());
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(
            "Phone number must contain 7-25 characters (digits, spaces, dots, dashes, + allowed); to must be a string in international format, e.g. \"+14155550123\"",
            error.Message);
    }

    [Fact]
    public async Task A_sign_in_code_text_without_a_code_is_refused()
    {
        const string body = """{"message":"Invalid 2FA message: No valid verification code found. Put the code in the text as a number of its own.","error":"Bad Request","statusCode":400}""";
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.BadRequest, body));

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.SendSignInCodeAsync("+14155550123", "Welcome!"));

        Assert.Contains("No valid verification code found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idempotency_errors_keep_their_code()
    {
        var answers = new[]
        {
            FakeHandler.Json(HttpStatusCode.Conflict, """{"statusCode":409,"error":"Conflict","code":"IDEMPOTENCY_KEY_IN_PROGRESS","message":"A request with this Idempotency-Key is still being processed."}""", ("Retry-After", "1")),
            FakeHandler.Json((HttpStatusCode)422, """{"statusCode":422,"error":"Unprocessable Entity","code":"IDEMPOTENCY_KEY_REUSED","message":"This Idempotency-Key was already used."}"""),
        };
        int call = 0;
        using SmsTunnelClient client = Clients.With(new FakeHandler(_ => answers[call++]));
        var options = new SendOptions { IdempotencyKey = "k-1" };

        SmsTunnelApiException inProgress = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.SendSmsAsync("+14155550123", "Hi", options));
        SmsTunnelApiException reused = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.SendSmsAsync("+14155550123", "Hi", options));

        Assert.Equal(409, inProgress.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.IdempotencyKeyInProgress, inProgress.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(1), inProgress.RetryAfter);
        Assert.Equal(422, reused.StatusCode);
        Assert.Equal(SmsTunnelErrorCodes.IdempotencyKeyReused, reused.ErrorCode);
    }

    [Fact]
    public async Task An_unknown_message_is_not_found()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.NotFound, """{"message":"Message not found","error":"Not Found","statusCode":404}"""));

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetMessageStatusAsync("000000000000000000000000"));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("Message not found", error.Message);
    }

    [Fact]
    public async Task An_empty_error_body_still_says_the_status()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using SmsTunnelClient client = Clients.With(handler);

        SmsTunnelPermissionException error = await Assert.ThrowsAsync<SmsTunnelPermissionException>(() => client.GetUsageAsync());

        Assert.Equal("SMSTunnel returned error: 403 Forbidden", error.Message);
    }

    [Fact]
    public async Task A_success_status_without_json_is_an_error()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Welcome</html>", Encoding.UTF8, "text/html") });
        using SmsTunnelClient client = Clients.With(handler);

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetPhonesAsync());

        Assert.Equal(200, error.StatusCode);
        Assert.Contains("not JSON", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_success_status_with_success_false_is_an_error()
    {
        using SmsTunnelClient client = Clients.With(FakeHandler.Answering(HttpStatusCode.OK, """{"success":false,"error":"Device not found"}"""));

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetPhonesAsync());

        Assert.Equal("Device not found", error.Message);
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://example.com/api/v1/devices");
            return response;
        });
        using SmsTunnelClient client = Clients.With(handler);

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetPhonesAsync());

        Assert.Equal(301, error.StatusCode);
        Assert.Contains("redirect", error.Message, StringComparison.Ordinal);
        Assert.Contains("https://example.com/api/v1/devices", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_error_is_an_SmsTunnelException()
    {
        Assert.True(typeof(SmsTunnelException).IsAssignableFrom(typeof(SmsTunnelAuthenticationException)));
        Assert.True(typeof(SmsTunnelApiException).IsAssignableFrom(typeof(SmsTunnelPermissionException)));
        Assert.True(typeof(SmsTunnelApiException).IsAssignableFrom(typeof(SmsTunnelRateLimitException)));
        Assert.True(typeof(SmsTunnelApiException).IsAssignableFrom(typeof(SmsTunnelServerException)));
        Assert.True(typeof(SmsTunnelConnectionException).IsAssignableFrom(typeof(SmsTunnelTimeoutException)));
        Assert.True(typeof(SmsTunnelException).IsAssignableFrom(typeof(SmsTunnelConnectionException)));
    }
}
