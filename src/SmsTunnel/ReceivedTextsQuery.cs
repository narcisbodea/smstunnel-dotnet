using System;

namespace SmsTunnel;

/// <summary>Which received texts to list. Every filter is optional.</summary>
public sealed class ReceivedTextsQuery
{
    /// <summary>How many texts per page, 1 to 100. SMSTunnel's default is 50.</summary>
    public int? Limit { get; set; }

    /// <summary>The page, starting at 1.</summary>
    public int? Page { get; set; }

    /// <summary>Only texts received at or after this moment.</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>Only texts from this sender (part of the number is enough).</summary>
    public string? Phone { get; set; }

    /// <summary>Only texts received by this phone (its <see cref="SmsTunnel.Phone.Id"/>).</summary>
    public string? DeviceId { get; set; }
}
