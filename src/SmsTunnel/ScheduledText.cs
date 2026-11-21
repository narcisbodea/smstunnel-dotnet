using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace SmsTunnel;

/// <summary>Where a scheduled text is now.</summary>
public enum ScheduledTextStatus
{
    /// <summary>A status this version of the library does not know. <see cref="ScheduledText.Raw"/> has SMSTunnel's words.</summary>
    Unknown = 0,

    /// <summary>Waiting for its time. It can still be canceled.</summary>
    Scheduled,

    /// <summary>
    /// It went out at its time: <see cref="ScheduledText.MessageId"/> is the text's ID, for
    /// <see cref="ISmsTunnelClient.GetMessageStatusAsync"/>.
    /// </summary>
    Sent,

    /// <summary>It could not go out at its time; <see cref="ScheduledText.Error"/> says why.</summary>
    Failed,

    /// <summary>It was canceled before its time.</summary>
    Canceled,
}

/// <summary>
/// A text scheduled with
/// <see cref="ISmsTunnelClient.ScheduleSmsAsync(string, string, DateTimeOffset, SendOptions, CancellationToken)"/>, from
/// <see cref="ISmsTunnelClient.GetScheduledTextsAsync"/>, <see cref="ISmsTunnelClient.GetScheduledTextAsync"/> or
/// <see cref="ISmsTunnelClient.CancelScheduledTextAsync"/>.
/// </summary>
public sealed record ScheduledText
{
    /// <summary>The scheduled text's ID, to look it up or cancel it.</summary>
    public required string Id { get; init; }

    /// <summary>The number it goes to, as it was given.</summary>
    public string? To { get; init; }

    /// <summary>The text.</summary>
    public string? Message { get; init; }

    /// <summary>When it goes out, or went out, in UTC (<c>ToLocalTime()</c> for this machine's time).</summary>
    public DateTimeOffset SendAt { get; init; }

    /// <summary>Where it is now.</summary>
    public ScheduledTextStatus Status { get; init; }

    /// <summary>
    /// The phone it goes out from, when one was named in <see cref="SendOptions.DeviceId"/>; <see langword="null"/> when
    /// SMSTunnel picks one of your phones at its time.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Once it went out: the text's ID, for <see cref="ISmsTunnelClient.GetMessageStatusAsync"/> (whether the phone
    /// sent it). <see langword="null"/> until then.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Why it failed, in SMSTunnel's words, or <see langword="null"/>: <c>key_not_active</c> (the API key was stopped,
    /// deleted or expired, or lost <c>sms:send</c>), <c>limit_reached</c> (the plan's allowance was used up at its time),
    /// <c>invalid_number</c>, <c>phone_not_found</c>, <c>phone_unlinked</c>, <c>sealed_only_phone</c>,
    /// <c>number_not_allowed</c>, <c>account_not_active</c>, <c>interrupted</c> or <c>failed</c>. Other values may come
    /// in later versions of SMSTunnel; they are kept as sent.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>When it was scheduled.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary><see langword="true"/> while it waits for its time (<see cref="ScheduledTextStatus.Scheduled"/>): it can still be canceled.</summary>
    public bool IsWaiting => Status == ScheduledTextStatus.Scheduled;

    /// <summary><see langword="true"/> once it went out, failed or was canceled: it won't change any more.</summary>
    public bool IsDone => Status is ScheduledTextStatus.Sent or ScheduledTextStatus.Failed or ScheduledTextStatus.Canceled;

    /// <summary>SMSTunnel's whole answer for this text, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>One page of the texts this API key scheduled, from <see cref="ISmsTunnelClient.GetScheduledTextsAsync"/>.</summary>
public sealed record ScheduledTextPage
{
    /// <summary>
    /// The texts on this page: the waiting ones soonest first, the done ones (<see cref="ScheduledTextScope.Done"/>)
    /// newest first.
    /// </summary>
    public required IReadOnlyList<ScheduledText> Texts { get; init; }

    /// <summary>How many texts match, on every page.</summary>
    public int Total { get; init; }

    /// <summary>This page's number, starting at 1.</summary>
    public int Page { get; init; }

    /// <summary>Texts per page.</summary>
    public int Limit { get; init; }

    /// <summary>How many texts wait in the whole account right now, scheduled with any key or in the dashboard.</summary>
    public int Waiting { get; init; }

    /// <summary>How many texts the plan lets wait at once (Free 10, Starter 50, Standard 200, Professional 500, Enterprise 1000).</summary>
    public int Max { get; init; }

    /// <summary>Whether there is a next page.</summary>
    public bool HasMore => Limit > 0 && (long)Page * Limit < Total;
}
