namespace SmsTunnel;

/// <summary>Values of <see cref="SmsTunnelApiException.ErrorCode"/> your software can act on.</summary>
public static class SmsTunnelErrorCodes
{
    /// <summary>403: the API key lacks a permission; <see cref="SmsTunnelApiException.Permission"/> names it (for example <c>sms:inbox</c>).</summary>
    public const string PermissionRequired = "PERMISSION_REQUIRED";

    /// <summary>401: an API key sent from a web page on another site. Call SMSTunnel from your server.</summary>
    public const string ApiKeyFromWebPage = "API_KEY_FROM_WEB_PAGE";

    /// <summary>SMSTunnel support turned the key or the phone off.</summary>
    public const string DisabledBySupport = "DISABLED_BY_SUPPORT";

    /// <summary>400: the Idempotency-Key is not 1 to 255 printable ASCII characters.</summary>
    public const string IdempotencyKeyInvalid = "IDEMPOTENCY_KEY_INVALID";

    /// <summary>422: the Idempotency-Key was used in the last 24 hours for a different text. Use a new key for a new text.</summary>
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";

    /// <summary>409: a call with this Idempotency-Key is still running. Retry after <see cref="SmsTunnelApiException.RetryAfter"/> to get its answer.</summary>
    public const string IdempotencyKeyInProgress = "IDEMPOTENCY_KEY_IN_PROGRESS";
}
