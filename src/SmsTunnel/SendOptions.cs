namespace SmsTunnel;

/// <summary>Optional settings for one text.</summary>
public sealed class SendOptions
{
    /// <summary>
    /// The phone that sends the text: its ID from the dashboard (<b>Phones</b>, the Copy button) or from
    /// <see cref="ISmsTunnelClient.GetPhonesAsync"/>. Leave it empty and SMSTunnel picks one of your phones.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// The country code (<c>"+49"</c>, <c>"49"</c> or <c>"0049"</c>) that completes the number when it is written without
    /// <c>+</c> or <c>00</c>. It wins over the client's <see cref="SmsTunnelClientOptions.DefaultCountryCode"/> and over
    /// the phone's own country.
    /// </summary>
    public string? DefaultCountryCode { get; set; }

    /// <summary>
    /// Makes a retry safe. A second call with the same key within 24 hours gets the first call's answer back
    /// (<see cref="SendResult.Replayed"/> is <see langword="true"/>) instead of sending a second text. Use a value that
    /// names this one text, such as <c>"order-1042-shipped"</c> or a GUID you store with it: 1 to 255 printable ASCII
    /// characters. Reusing a key for a different text fails with status 422 and
    /// <see cref="SmsTunnelErrorCodes.IdempotencyKeyReused"/>.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
