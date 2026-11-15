using System;
using System.Text.Json;

namespace SmsTunnel;

/// <summary>
/// What SMSTunnel answered to a text it scheduled, from
/// <see cref="ISmsTunnelClient.ScheduleSmsAsync(string, string, DateTimeOffset, SendOptions, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed record ScheduledSendResult
{
    /// <summary>
    /// The scheduled text's ID. Keep it to look the text up (<see cref="ISmsTunnelClient.GetScheduledTextAsync"/>) or to
    /// cancel it (<see cref="ISmsTunnelClient.CancelScheduledTextAsync"/>). It is not a message ID: the text gets one
    /// when it goes out (<see cref="SmsTunnel.ScheduledText.MessageId"/>).
    /// </summary>
    public required string ScheduledId { get; init; }

    /// <summary>When SMSTunnel sends it, in UTC.</summary>
    public DateTimeOffset SendAt { get; init; }

    /// <summary>Its status: <see cref="ScheduledTextStatus.Scheduled"/>.</summary>
    public ScheduledTextStatus Status { get; init; }

    /// <summary>The scheduled text as SMSTunnel keeps it.</summary>
    public required ScheduledText ScheduledText { get; init; }

    /// <summary>
    /// <see langword="true"/> when this is the stored answer to an earlier call with the same
    /// <see cref="SendOptions.IdempotencyKey"/>: no second text was scheduled.
    /// </summary>
    public bool Replayed { get; init; }

    /// <summary>SMSTunnel's whole answer, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}
