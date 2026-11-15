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

    // Scheduled texts (ISmsTunnelClient.ScheduleSmsAsync and the calls that list, read and cancel them).

    /// <summary>400: the time is less than a minute from now. Pick a later time, or send the text now with <see cref="ISmsTunnelClient.SendSmsAsync"/>.</summary>
    public const string ScheduleTooSoon = "SCHEDULE_TOO_SOON";

    /// <summary>400: the time is more than 30 days from now.</summary>
    public const string ScheduleTooFar = "SCHEDULE_TOO_FAR";

    /// <summary>400: the number, the text or the time can't be read.</summary>
    public const string ScheduleInvalidInput = "SCHEDULE_INVALID_INPUT";

    /// <summary>400: a scheduled text can be up to 1600 characters.</summary>
    public const string ScheduleTextTooLong = "SCHEDULE_TEXT_TOO_LONG";

    /// <summary>400: texts to short codes and premium-rate numbers can't be sent.</summary>
    public const string ScheduleNumberNotAllowed = "SCHEDULE_NUMBER_NOT_ALLOWED";

    /// <summary>400: the phone in <see cref="SendOptions.DeviceId"/> sends only end-to-end encrypted texts, which can't be scheduled.</summary>
    public const string ScheduleSealedOnlyPhone = "SCHEDULE_SEALED_ONLY_PHONE";

    /// <summary>400: an end-to-end encrypted text can't be scheduled. This client never sends one.</summary>
    public const string ScheduleEncryptedText = "SCHEDULE_ENCRYPTED_TEXT";

    /// <summary>400: a sign-in code can't be scheduled: send it with <see cref="ISmsTunnelClient.SendSignInCodeAsync"/> when it is asked for.</summary>
    public const string ScheduleSignInCode = "SCHEDULE_SIGN_IN_CODE";

    /// <summary>404: the phone in <see cref="SendOptions.DeviceId"/> is not in your account.</summary>
    public const string SchedulePhoneNotFound = "SCHEDULE_PHONE_NOT_FOUND";

    /// <summary>
    /// 403: as many texts wait as your plan allows (<see cref="SmsTunnelApiException.Max"/> says how many). Wait until
    /// some go out, or cancel one.
    /// </summary>
    public const string ScheduleLimitReached = "SCHEDULE_LIMIT_REACHED";

    /// <summary>404: no text with this ID was scheduled with this API key.</summary>
    public const string ScheduleNotFound = "SCHEDULE_NOT_FOUND";

    /// <summary>409: the scheduled text already went out, failed or was canceled, so it can't be canceled.</summary>
    public const string ScheduleNotEditable = "SCHEDULE_NOT_EDITABLE";
}
