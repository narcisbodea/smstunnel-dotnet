using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmsTunnel.Internal;

/// <summary>Turns an error answer into the matching exception, with SMSTunnel's own words.</summary>
internal static class ErrorFactory
{
    private const int MaxTextLength = 300;

    public static SmsTunnelApiException Create(int statusCode, string? reasonPhrase, string? body, TimeSpan? retryAfter)
    {
        string? errorCode = null;
        string? permission = null;
        string? message = null;
        int? max = null;

        JsonElement json = TryParse(body);
        if (json.ValueKind == JsonValueKind.Object)
        {
            errorCode = JsonFields.String(json, "code");
            permission = JsonFields.String(json, "permission");
            max = JsonFields.Int(json, "max"); // with SCHEDULE_LIMIT_REACHED
            message = MessageOf(json);
        }
        else if (!string.IsNullOrWhiteSpace(body))
        {
            // A proxy's error page, say: its words without the markup.
            message = PlainText(body);
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = string.IsNullOrWhiteSpace(reasonPhrase)
                ? $"SMSTunnel returned error: {statusCode}"
                : $"SMSTunnel returned error: {statusCode} {reasonPhrase}";
        }

        return statusCode switch
        {
            401 => new SmsTunnelAuthenticationException(statusCode, message, errorCode, permission, retryAfter, body) { Max = max },
            403 => new SmsTunnelPermissionException(statusCode, message, errorCode, permission, retryAfter, body) { Max = max },
            429 => new SmsTunnelRateLimitException(statusCode, message, errorCode, permission, retryAfter, body) { Max = max },
            >= 500 and <= 599 => new SmsTunnelServerException(statusCode, message, errorCode, permission, retryAfter, body) { Max = max },
            _ => new SmsTunnelApiException(statusCode, message, errorCode, permission, retryAfter, body) { Max = max },
        };
    }

    /// <summary>The <c>Retry-After</c> header as a wait: seconds, or an HTTP date from now.</summary>
    public static TimeSpan? RetryAfter(HttpResponseHeaders headers, DateTimeOffset now)
    {
        RetryConditionHeaderValue? value = headers.RetryAfter;
        if (value?.Delta is TimeSpan delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (value?.Date is DateTimeOffset date)
        {
            TimeSpan wait = date - now;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    /// <summary><c>message</c> (a string, or a list joined with "; "), else <c>error</c>.</summary>
    public static string? MessageOf(JsonElement json)
    {
        if (json.TryGetProperty("message", out JsonElement message))
        {
            if (message.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString();
            }

            if (message.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (JsonElement part in message.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(part.GetString()))
                    {
                        parts.Add(part.GetString()!);
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join("; ", parts);
                }
            }
        }

        string? error = JsonFields.String(json, "error");
        return string.IsNullOrWhiteSpace(error) ? null : error;
    }

    public static JsonElement TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string PlainText(string body)
    {
        var text = new StringBuilder(Math.Min(body.Length, 4 * MaxTextLength));
        bool inTag = false;
        bool space = false;
        foreach (char c in body)
        {
            if (c == '<')
            {
                inTag = true;
                continue;
            }

            if (inTag)
            {
                if (c == '>')
                {
                    inTag = false;
                    space = text.Length > 0;
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                space = text.Length > 0;
                continue;
            }

            if (space)
            {
                text.Append(' ');
                space = false;
            }

            text.Append(c);
            if (text.Length >= MaxTextLength)
            {
                break;
            }
        }

        return System.Net.WebUtility.HtmlDecode(text.ToString());
    }
}
