using System;

namespace SmsTunnel;

/// <summary>
/// Settings for <see cref="SmsTunnelClient"/>. With <c>SmsTunnel.Extensions.DependencyInjection</c> they are read from
/// the <c>SmsTunnel</c> configuration section (<c>SmsTunnel:ApiKey</c>, <c>SmsTunnel:BaseUrl</c>,
/// <c>SmsTunnel:DefaultCountryCode</c>, <c>SmsTunnel:Timeout</c>).
/// </summary>
public sealed class SmsTunnelClientOptions
{
    /// <summary>The address of SMSTunnel's API: <c>https://smstunnel.io</c>.</summary>
    public static readonly Uri DefaultBaseUrl = new("https://smstunnel.io/");

    /// <summary>
    /// The API key (<c>sk_live_…</c>) from your SMSTunnel dashboard: <b>Apps → Add an app</b>. Keep it in user secrets,
    /// an environment variable or a secret store, never in source code.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Where the API is. The default is <see cref="DefaultBaseUrl"/>; change it only for a test server.</summary>
    public Uri BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>
    /// The country code (<c>"+40"</c>, <c>"40"</c> or <c>"0040"</c>) that completes numbers written without <c>+</c> or
    /// <c>00</c>, for every text this client sends. A text's own <see cref="SendOptions.DefaultCountryCode"/> wins over
    /// it. When neither is set, SMSTunnel uses the phone's country. Numbers in international format
    /// (<c>+14155550123</c>) never need it.
    /// </summary>
    public string? DefaultCountryCode { get; set; }

    /// <summary>
    /// How long a call may take before it fails with <see cref="SmsTunnelTimeoutException"/>. The default is 30 seconds;
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> turns it off.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
