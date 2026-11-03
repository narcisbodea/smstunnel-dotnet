using System;
using System.Text;

namespace SmsTunnel.Internal;

/// <summary>Checks done before anything is sent, with the same rules as SMSTunnel's server.</summary>
internal static class Inputs
{
    public const int IdempotencyKeyMaxLength = 255;

    /// <summary>
    /// <c>"40"</c>, <c>"+40"</c>, <c>"0040"</c> or <c>"+ 40"</c> → <c>"+40"</c>; <see langword="null"/> or blank → <see langword="null"/>.
    /// Anything else is refused here: SMSTunnel would skip it and complete the number with another country's code.
    /// </summary>
    public static string? CountryCode(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new StringBuilder(value.Length + 1);
        foreach (char c in value)
        {
            if (!char.IsWhiteSpace(c) && c is not ('-' or '(' or ')' or '.'))
            {
                cleaned.Append(c);
            }
        }

        string code = cleaned.ToString();
        if (code.StartsWith("00", StringComparison.Ordinal))
        {
            code = string.Concat("+", code.AsSpan(2));
        }
        else if (!code.StartsWith('+'))
        {
            code = "+" + code;
        }

        bool valid = code.Length is >= 2 and <= 5 && code[1] is >= '1' and <= '9';
        for (int i = 2; valid && i < code.Length; i++)
        {
            valid = code[i] is >= '0' and <= '9';
        }

        if (!valid)
        {
            throw new ArgumentException(
                $"\"{value}\" is not a country code. Give 1 to 4 digits, with or without + or 00, for example \"+40\" or \"49\".",
                parameterName);
        }

        return code;
    }

    /// <summary>The key as given, or <see langword="null"/> for none. It must be 1 to 255 printable ASCII characters.</summary>
    public static string? IdempotencyKey(string? value, string parameterName)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        bool printable = value.Length <= IdempotencyKeyMaxLength && !string.IsNullOrWhiteSpace(value);
        foreach (char c in value)
        {
            printable &= c is >= ' ' and <= '~';
        }

        if (!printable)
        {
            throw new ArgumentException(
                "IdempotencyKey must be 1 to 255 printable ASCII characters, for example a GUID or \"order-1042-shipped\".",
                parameterName);
        }

        return value;
    }

    public static void Recipient(string phoneNumber, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("Give the recipient's number, in international format such as +14155550123.", parameterName);
        }

        if (phoneNumber.Contains(',', StringComparison.Ordinal) || phoneNumber.Contains(';', StringComparison.Ordinal))
        {
            throw new ArgumentException("Give one number per text. To text several people, send once per number.", parameterName);
        }
    }

    public static void Text(string message, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("The text is empty.", parameterName);
        }
    }

    public static string ApiKey(string? apiKey, string parameterName)
    {
        string key = apiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            throw new ArgumentException(
                "An SMSTunnel API key is required. Create one in your dashboard: Apps → Add an app (https://dashboard.smstunnel.io/apps).",
                parameterName);
        }

        foreach (char c in key)
        {
            if (c is < '!' or > '~')
            {
                throw new ArgumentException("The SMSTunnel API key contains characters an API key never has. Copy it again from the dashboard.", parameterName);
            }
        }

        return key;
    }

    public static Uri BaseUrl(Uri? baseUrl, string parameterName)
    {
        if (baseUrl is null)
        {
            return SmsTunnelClientOptions.DefaultBaseUrl;
        }

        if (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"BaseUrl must be an absolute http or https address, such as {SmsTunnelClientOptions.DefaultBaseUrl}.", parameterName);
        }

        if (!string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
        {
            throw new ArgumentException("BaseUrl must not have a query or a fragment.", parameterName);
        }

        // A trailing slash, so that "api/v1/..." is added after the path instead of replacing its last part.
        string text = baseUrl.GetLeftPart(UriPartial.Path);
        return text.EndsWith('/') ? new Uri(text, UriKind.Absolute) : new Uri(text + "/", UriKind.Absolute);
    }

    public static TimeSpan Timeout(TimeSpan timeout, string parameterName)
    {
        if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, timeout, "Timeout must be positive, or Timeout.InfiniteTimeSpan.");
        }

        return timeout;
    }
}
