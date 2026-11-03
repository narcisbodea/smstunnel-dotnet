using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SmsTunnel;

/// <summary>A text one of your phones received and synced to your account.</summary>
public sealed record ReceivedText
{
    /// <summary>The text's ID.</summary>
    public required string Id { get; init; }

    /// <summary>The number that sent it.</summary>
    public string? Sender { get; init; }

    /// <summary>The words, or <see langword="null"/> when the phone synced it encrypted (see <see cref="EncryptedPayload"/>).</summary>
    public string? Message { get; init; }

    /// <summary>Whether the phone synced the text encrypted.</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>The encrypted text, for software that holds the key.</summary>
    public string? EncryptedPayload { get; init; }

    /// <summary>When the phone received it.</summary>
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>The SIM slot it arrived on, when the phone says.</summary>
    public int? SimSlot { get; init; }

    /// <summary>Android's subscription ID of that SIM, when the phone says.</summary>
    public int? SubscriptionId { get; init; }

    /// <summary>The ID of the phone that received it.</summary>
    public string? DeviceId { get; init; }

    /// <summary>The name of the phone that received it.</summary>
    public string? DeviceName { get; init; }

    /// <summary>SMSTunnel's whole answer for this text, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>One page of received texts, newest first, from <see cref="ISmsTunnelClient.GetReceivedTextsAsync"/>.</summary>
public sealed record ReceivedTextPage
{
    /// <summary>The texts on this page, newest first.</summary>
    public required IReadOnlyList<ReceivedText> Texts { get; init; }

    /// <summary>How many texts match, on every page.</summary>
    public int Total { get; init; }

    /// <summary>This page's number, starting at 1.</summary>
    public int Page { get; init; }

    /// <summary>Texts per page.</summary>
    public int Limit { get; init; }

    /// <summary>Whether there is a next page.</summary>
    public bool HasMore { get; init; }
}
