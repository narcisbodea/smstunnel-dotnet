using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SmsTunnel.Tests;

/// <summary>What a request looked like when it reached the handler.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? ContentType, string? Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out string? value) ? value : null;
}

/// <summary>An HttpMessageHandler that answers without a network and keeps every request.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHandler(Func<RecordedRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public FakeHandler(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest Single => Assert.Single(Requests);

    public static FakeHandler Answering(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        return new FakeHandler(_ => Json(status, json, headers));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        foreach ((string name, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.ToString(), body);
        Requests.Add(recorded);
        HttpResponseMessage response = await _respond(recorded, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }
}

public static class Clients
{
    // Split so that secret scanners do not take this fake key for a real one.
    public const string Key = "sk_" + "live_" + "0123456789abcdef" + "0123456789abcdef";

    public static SmsTunnelClient With(FakeHandler handler, Action<SmsTunnelClientOptions>? configure = null)
    {
        var options = new SmsTunnelClientOptions { ApiKey = Key };
        configure?.Invoke(options);
        return new SmsTunnelClient(new HttpClient(handler), options);
    }

    public const string SendAnswer =
        """{"success":true,"messageId":"6aea38f9b38ed3bed4f8569f","data":{"messageId":"6aea38f9b38ed3bed4f8569f","branded":false,"remaining":{"clean":-1,"total":"unlimited"}}}""";
}
