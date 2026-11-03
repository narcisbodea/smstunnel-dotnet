using System;
using System.Text.Json;

namespace SmsTunnel;

/// <summary>One of your phones, from <see cref="ISmsTunnelClient.GetPhonesAsync"/>.</summary>
public sealed record Phone
{
    /// <summary>The phone's ID: pass it as <see cref="SendOptions.DeviceId"/> to send from this phone.</summary>
    public required string Id { get; init; }

    /// <summary>The phone's name, as shown under <b>Phones</b> in the dashboard.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the SMSTunnel app on the phone is connected now. Texts for an offline phone wait until it is back.</summary>
    public bool IsOnline { get; init; }

    /// <summary>When the phone was last connected.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>SMSTunnel's whole answer for this phone, for fields this version does not map.</summary>
    public JsonElement Raw { get; init; }
}
