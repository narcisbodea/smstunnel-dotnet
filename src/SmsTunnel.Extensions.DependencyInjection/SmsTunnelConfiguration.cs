using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SmsTunnel.Extensions.DependencyInjection;

/// <summary>Reads <see cref="SmsTunnelClientOptions"/> from a configuration section, without reflection (trimming and AOT safe).</summary>
internal static class SmsTunnelConfiguration
{
    public const string SectionName = "SmsTunnel";

    public static void Apply(IConfiguration section, SmsTunnelClientOptions options)
    {
        string? apiKey = section["ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            options.ApiKey = apiKey.Trim();
        }

        string? baseUrl = section["BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? uri))
            {
                throw new InvalidOperationException($"SmsTunnel:BaseUrl \"{baseUrl}\" is not an absolute address such as https://smstunnel.io.");
            }

            options.BaseUrl = uri;
        }

        string? countryCode = section["DefaultCountryCode"];
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            options.DefaultCountryCode = countryCode.Trim();
        }

        string? timeout = section["Timeout"];
        if (!string.IsNullOrWhiteSpace(timeout))
        {
            options.Timeout = ParseTimeout(timeout.Trim());
        }
    }

    /// <summary><c>30</c> (seconds), <c>00:00:30</c> (a time span) or <c>Infinite</c>.</summary>
    private static TimeSpan ParseTimeout(string text)
    {
        if (string.Equals(text, "Infinite", StringComparison.OrdinalIgnoreCase))
        {
            return System.Threading.Timeout.InfiniteTimeSpan;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            if (seconds > 0 && seconds <= int.MaxValue / 1000.0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        else if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan span) && span > TimeSpan.Zero)
        {
            return span;
        }

        throw new InvalidOperationException(
            $"SmsTunnel:Timeout \"{text}\" is not a timeout. Give a number of seconds (30), a time span (00:00:30) or Infinite.");
    }
}
