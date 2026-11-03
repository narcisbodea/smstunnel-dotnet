using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.Tests;

public class TransportTests
{
    [Fact]
    public async Task Sends_real_http_with_the_key_and_user_agent()
    {
        await using var server = new TinyHttpServer(_ => TinyHttpServer.Response(200, "OK", """{"success":true,"data":{"devices":[]}}"""));
        using var client = new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, BaseUrl = server.Url });

        await client.GetPhonesAsync();

        Assert.True(server.Requests.TryDequeue(out string? request));
        Assert.StartsWith("GET /api/v1/devices HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains($"X-API-Key: {Clients.Key}\r\n", request, StringComparison.Ordinal);
        Assert.Matches(@"User-Agent: SmsTunnel-dotnet/\d+\.\d+\.\d+ \(\+https://smstunnel\.io\)\r\n", request);
    }

    [Fact]
    public async Task Never_follows_a_redirect_so_the_key_stays_home()
    {
        await using var elsewhere = new TinyHttpServer(_ => TinyHttpServer.Response(200, "OK", """{"success":true,"data":{"devices":[]}}"""));
        await using var server = new TinyHttpServer(_ => TinyHttpServer.Response(302, "Found", extraHeaders: $"Location: {elsewhere.Url}api/v1/devices\r\n"));
        using var client = new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, BaseUrl = server.Url });

        SmsTunnelApiException error = await Assert.ThrowsAsync<SmsTunnelApiException>(() => client.GetPhonesAsync());

        Assert.Equal(302, error.StatusCode);
        Assert.Contains(elsewhere.Url.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Empty(elsewhere.Requests);
    }

    [Fact]
    public async Task An_address_nobody_answers_on_is_a_connection_error()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var client = new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, BaseUrl = new Uri($"http://127.0.0.1:{port}/") });

        SmsTunnelConnectionException error = await Assert.ThrowsAsync<SmsTunnelConnectionException>(() => client.GetUsageAsync());

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Contains("127.0.0.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_network_failure_is_a_connection_error()
    {
        var handler = new FakeHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("No such host is known.")));
        using SmsTunnelClient client = Clients.With(handler);

        SmsTunnelConnectionException error = await Assert.ThrowsAsync<SmsTunnelConnectionException>(() => client.SendSmsAsync("+14155550123", "Hi"));

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.IsNotType<SmsTunnelTimeoutException>(error);
    }

    [Fact]
    public async Task No_answer_in_time_is_a_timeout()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using SmsTunnelClient client = Clients.With(handler, o => o.Timeout = TimeSpan.FromMilliseconds(200));

        SmsTunnelTimeoutException send = await Assert.ThrowsAsync<SmsTunnelTimeoutException>(() => client.SendSmsAsync("+14155550123", "Hi"));
        SmsTunnelTimeoutException read = await Assert.ThrowsAsync<SmsTunnelTimeoutException>(() => client.GetPhonesAsync());

        Assert.Equal(TimeSpan.FromMilliseconds(200), send.Timeout);
        Assert.Contains("within 0.2 seconds", send.Message, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", send.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IdempotencyKey", read.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_http_clients_own_timeout_is_a_timeout_too()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(150) };
        using var client = new SmsTunnelClient(http, new SmsTunnelClientOptions { ApiKey = Clients.Key, Timeout = Timeout.InfiniteTimeSpan });

        SmsTunnelTimeoutException error = await Assert.ThrowsAsync<SmsTunnelTimeoutException>(() => client.GetPhonesAsync());

        Assert.Equal(TimeSpan.FromMilliseconds(150), error.Timeout);
    }

    [Fact]
    public async Task Cancelling_is_not_an_error()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using SmsTunnelClient client = Clients.With(handler);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendSmsAsync("+14155550123", "Hi", cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task A_cancelled_call_sends_nothing()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using SmsTunnelClient client = Clients.With(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendSmsAsync("+14155550123", "Hi", cancellationToken: new CancellationToken(canceled: true)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Leaves_your_http_client_open()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"devices":[]}}""");
        using var http = new HttpClient(handler);

        new SmsTunnelClient(http, new SmsTunnelClientOptions { ApiKey = Clients.Key }).Dispose();
        using var again = new SmsTunnelClient(http, new SmsTunnelClientOptions { ApiKey = Clients.Key });
        await again.GetPhonesAsync();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_disposed_client_refuses_calls()
    {
        var client = new SmsTunnelClient(Clients.Key);
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetPhonesAsync());
    }
}

public class OptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sk_live_with space")]
    [InlineData("sk_live_ключ")]
    public void Refuses_a_missing_or_broken_key(string? key)
    {
        ArgumentException error = Assert.ThrowsAny<ArgumentException>(() => new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = key! }));

        Assert.Contains("key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trims_the_key()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, """{"success":true,"data":{"devices":[]}}""");
        using var client = new SmsTunnelClient(new HttpClient(handler), new SmsTunnelClientOptions { ApiKey = "  " + Clients.Key + "\r\n" });

        await client.GetPhonesAsync();

        Assert.Equal(Clients.Key, handler.Single.Header("X-API-Key"));
    }

    [Theory]
    [InlineData("ftp://smstunnel.io/")]
    [InlineData("https://smstunnel.io/?x=1")]
    [InlineData("https://smstunnel.io/#top")]
    public void Refuses_a_base_url_that_isnt_http(string url)
    {
        Assert.Throws<ArgumentException>(() => new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, BaseUrl = new Uri(url) }));
    }

    [Fact]
    public void Refuses_a_relative_base_url()
    {
        Assert.Throws<ArgumentException>(() => new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, BaseUrl = new Uri("/api", UriKind.Relative) }));
    }

    [Fact]
    public void Uses_smstunnel_io_by_default()
    {
        using var client = new SmsTunnelClient(Clients.Key);

        Assert.Equal(new Uri("https://smstunnel.io/"), client.BaseUrl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Refuses_a_timeout_that_isnt_positive(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, Timeout = TimeSpan.FromSeconds(seconds) }));
    }

    [Fact]
    public void Accepts_no_timeout()
    {
        using var client = new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, Timeout = Timeout.InfiniteTimeSpan });

        Assert.NotNull(client);
    }

    [Fact]
    public void Refuses_a_default_country_code_that_isnt_one()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new SmsTunnelClient(new SmsTunnelClientOptions { ApiKey = Clients.Key, DefaultCountryCode = "Romania" }));

        Assert.Contains("\"Romania\" is not a country code", error.Message, StringComparison.Ordinal);
    }
}
