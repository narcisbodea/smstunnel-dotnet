using System;
using System.Diagnostics.CodeAnalysis;

namespace SmsTunnel;

/// <summary>
/// The base of every exception this library throws when a call does not go through. Catch it to handle them all:
/// <list type="bullet">
/// <item><see cref="SmsTunnelApiException"/>: SMSTunnel answered with an error (401, 403, 429, 5xx have their own subclasses).</item>
/// <item><see cref="SmsTunnelConnectionException"/>: no answer came back (network, TLS, timeout).</item>
/// </list>
/// Bad arguments (an empty number, an invalid country code) throw <see cref="ArgumentException"/> before anything is sent,
/// and cancelling the <see cref="System.Threading.CancellationToken"/> throws <see cref="OperationCanceledException"/>.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Thrown by the library, with the fields it needs.")]
public class SmsTunnelException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SmsTunnelException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// SMSTunnel answered, but with an error. Statuses with their own subclass:
/// 401 <see cref="SmsTunnelAuthenticationException"/>, 403 <see cref="SmsTunnelPermissionException"/>,
/// 429 <see cref="SmsTunnelRateLimitException"/> and 5xx <see cref="SmsTunnelServerException"/>.
/// Others: 400 (a number it cannot read, a sign-in code text without a code, a bad field), 404 (no such message for
/// this key), 409 and 422 (an <see cref="SendOptions.IdempotencyKey"/> still in progress or reused for another text).
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Needs the HTTP status.")]
public class SmsTunnelApiException : SmsTunnelException
{
    /// <summary>Creates the exception for an error answer.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="message">What went wrong, in SMSTunnel's words.</param>
    /// <param name="errorCode">SMSTunnel's <c>code</c>, such as <c>PERMISSION_REQUIRED</c>.</param>
    /// <param name="permission">With <c>PERMISSION_REQUIRED</c>: the permission the key lacks.</param>
    /// <param name="retryAfter">The <c>Retry-After</c> header, when SMSTunnel sent one.</param>
    /// <param name="responseBody">The answer's body as text.</param>
    public SmsTunnelApiException(
        int statusCode,
        string message,
        string? errorCode = null,
        string? permission = null,
        TimeSpan? retryAfter = null,
        string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Permission = permission;
        RetryAfter = retryAfter;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status SMSTunnel answered with.</summary>
    public int StatusCode { get; }

    /// <summary>SMSTunnel's own code for errors your software can act on (see <see cref="SmsTunnelErrorCodes"/>), or <see langword="null"/>.</summary>
    public string? ErrorCode { get; }

    /// <summary>With <see cref="SmsTunnelErrorCodes.PermissionRequired"/>: the permission the key lacks, such as <c>sms:inbox</c>.</summary>
    public string? Permission { get; }

    /// <summary>How long to wait before trying again, when SMSTunnel says (the <c>Retry-After</c> header).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The answer's body as SMSTunnel sent it (usually JSON).</summary>
    public string? ResponseBody { get; }
}

/// <summary>401: the API key is wrong, was deleted or turned off, or its account is suspended. Check it in the dashboard under <b>Apps</b>.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Needs the HTTP status.")]
public sealed class SmsTunnelAuthenticationException : SmsTunnelApiException
{
    /// <inheritdoc cref="SmsTunnelApiException(int, string, string?, string?, TimeSpan?, string?)"/>
    public SmsTunnelAuthenticationException(
        int statusCode,
        string message,
        string? errorCode = null,
        string? permission = null,
        TimeSpan? retryAfter = null,
        string? responseBody = null)
        : base(statusCode, message, errorCode, permission, retryAfter, responseBody)
    {
    }
}

/// <summary>
/// 403: the key is valid but may not do this. Either it lacks a permission (<see cref="SmsTunnelApiException.ErrorCode"/>
/// is <see cref="SmsTunnelErrorCodes.PermissionRequired"/> and <see cref="SmsTunnelApiException.Permission"/> names it,
/// such as <c>sms:read</c> for statuses or <c>sms:inbox</c> for received texts), or the plan doesn't include the feature.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Needs the HTTP status.")]
public sealed class SmsTunnelPermissionException : SmsTunnelApiException
{
    /// <inheritdoc cref="SmsTunnelApiException(int, string, string?, string?, TimeSpan?, string?)"/>
    public SmsTunnelPermissionException(
        int statusCode,
        string message,
        string? errorCode = null,
        string? permission = null,
        TimeSpan? retryAfter = null,
        string? responseBody = null)
        : base(statusCode, message, errorCode, permission, retryAfter, responseBody)
    {
    }
}

/// <summary>
/// 429: too many requests in a short time (30 a minute per key, <see cref="SmsTunnelApiException.RetryAfter"/> says how
/// long to wait), or the plan's limit for the day or month is reached (no <c>Retry-After</c>; the message says when
/// texts can go out again). The text was not sent.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Needs the HTTP status.")]
public sealed class SmsTunnelRateLimitException : SmsTunnelApiException
{
    /// <inheritdoc cref="SmsTunnelApiException(int, string, string?, string?, TimeSpan?, string?)"/>
    public SmsTunnelRateLimitException(
        int statusCode,
        string message,
        string? errorCode = null,
        string? permission = null,
        TimeSpan? retryAfter = null,
        string? responseBody = null)
        : base(statusCode, message, errorCode, permission, retryAfter, responseBody)
    {
    }
}

/// <summary>
/// 5xx: a problem on SMSTunnel's side. A text may or may not have been created: retry a send only with the same
/// <see cref="SendOptions.IdempotencyKey"/>, so it can't go out twice.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Needs the HTTP status.")]
public sealed class SmsTunnelServerException : SmsTunnelApiException
{
    /// <inheritdoc cref="SmsTunnelApiException(int, string, string?, string?, TimeSpan?, string?)"/>
    public SmsTunnelServerException(
        int statusCode,
        string message,
        string? errorCode = null,
        string? permission = null,
        TimeSpan? retryAfter = null,
        string? responseBody = null)
        : base(statusCode, message, errorCode, permission, retryAfter, responseBody)
    {
    }
}

/// <summary>
/// SMSTunnel could not be reached, or the connection broke before an answer came back (DNS, TLS, a refused or reset
/// connection). For a send, the text may or may not have been accepted: retry it only with the same
/// <see cref="SendOptions.IdempotencyKey"/>.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Thrown by the library.")]
public class SmsTunnelConnectionException : SmsTunnelException
{
    /// <summary>Creates the exception.</summary>
    public SmsTunnelConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// No answer within <see cref="SmsTunnelClientOptions.Timeout"/>. For a send, the text may already be on its way:
/// retry it only with the same <see cref="SendOptions.IdempotencyKey"/>.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Thrown by the library.")]
public sealed class SmsTunnelTimeoutException : SmsTunnelConnectionException
{
    /// <summary>Creates the exception.</summary>
    public SmsTunnelTimeoutException(string message, TimeSpan timeout, Exception? innerException = null)
        : base(message, innerException)
    {
        Timeout = timeout;
    }

    /// <summary>The timeout that ran out.</summary>
    public TimeSpan Timeout { get; }
}
