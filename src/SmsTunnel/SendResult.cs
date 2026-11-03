using System.Text.Json;

namespace SmsTunnel;

/// <summary>What SMSTunnel answered to a text it accepted.</summary>
public sealed record SendResult
{
    /// <summary>The text's ID. Keep it to look up the status with <see cref="ISmsTunnelClient.GetMessageStatusAsync"/>.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// <see langword="true"/> when no phone of yours was online: the text waits on your account and goes out as soon as
    /// a phone is back (within 3 days, otherwise it is marked failed). Nothing to resend.
    /// </summary>
    public bool Queued { get; init; }

    /// <summary><see langword="true"/> when the text went out with a short SMSTunnel line added (Free plan, past its texts as written).</summary>
    public bool Branded { get; init; }

    /// <summary>What is left today after this text, when SMSTunnel says.</summary>
    public RemainingQuota? Remaining { get; init; }

    /// <summary>
    /// <see langword="true"/> when this is the stored answer to an earlier call with the same
    /// <see cref="SendOptions.IdempotencyKey"/>: no second text was sent.
    /// </summary>
    public bool Replayed { get; init; }

    /// <summary>SMSTunnel's whole answer, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>Today's allowance left after a text. <see langword="null"/> means no limit.</summary>
public sealed record RemainingQuota
{
    /// <summary>Texts (or sign-in codes, after <see cref="ISmsTunnelClient.SendSignInCodeAsync"/>) that can still go out as written.</summary>
    public long? Clean { get; init; }

    /// <summary>All that can still go out, including the ones with the added SMSTunnel line on the Free plan.</summary>
    public long? Total { get; init; }
}
