using System;
using System.Text.Json;

namespace SmsTunnel;

/// <summary>Where a text is now.</summary>
public enum MessageState
{
    /// <summary>A state this version of the library does not know. <see cref="MessageStatus.Raw"/> has SMSTunnel's words.</summary>
    Unknown = 0,

    /// <summary>Waiting for the phone (for example while it is offline).</summary>
    Pending,

    /// <summary>The phone sent it.</summary>
    Sent,

    /// <summary>The recipient's network confirmed delivery.</summary>
    Delivered,

    /// <summary>It could not be sent; <see cref="MessageStatus.ErrorMessage"/> says why.</summary>
    Failed,

    /// <summary>
    /// The phone handed it to the SIM but never got the confirmation. It most likely went out, and it is never resent
    /// automatically. Don't resend it yourself: the recipient could get it twice.
    /// </summary>
    Unconfirmed,
}

/// <summary>The status of one text, from <see cref="ISmsTunnelClient.GetMessageStatusAsync"/>.</summary>
public sealed record MessageStatus
{
    /// <summary>The text's ID.</summary>
    public required string Id { get; init; }

    /// <summary>The number the text went to, in international format.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Where the text is now.</summary>
    public MessageState State { get; init; }

    /// <summary>When the phone sent it.</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>When delivery was confirmed.</summary>
    public DateTimeOffset? DeliveredAt { get; init; }

    /// <summary>Why it failed, or why it is unconfirmed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary><see langword="true"/> once the state will not change any more: delivered, failed or unconfirmed.</summary>
    public bool IsFinal => State is MessageState.Delivered or MessageState.Failed or MessageState.Unconfirmed;

    /// <summary>SMSTunnel's whole answer, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}
