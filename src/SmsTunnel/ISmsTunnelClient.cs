using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmsTunnel;

/// <summary>
/// The SMSTunnel API: texts sent from your own Android phone, from your own number. Implemented by
/// <see cref="SmsTunnelClient"/>; mock this interface in your own tests.
/// </summary>
/// <remarks>
/// Every method throws <see cref="SmsTunnelApiException"/> (or a subclass) when SMSTunnel answers with an error,
/// <see cref="SmsTunnelConnectionException"/> when no answer comes back, and <see cref="System.OperationCanceledException"/>
/// when the <c>cancellationToken</c> is cancelled. Nothing is retried automatically.
/// </remarks>
public interface ISmsTunnelClient
{
    /// <summary>
    /// Sends one text to one number (<c>POST /api/v1/sms/send</c>). To text several people, call it once per number.
    /// </summary>
    /// <param name="phoneNumber">The recipient, in international format (<c>+14155550123</c>). A local number needs a default country code.</param>
    /// <param name="message">The text, up to 1600 characters.</param>
    /// <param name="options">The phone to send from, the country code, an idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The text's ID, and whether it waits for an offline phone (<see cref="SendResult.Queued"/>).</returns>
    Task<SendResult> SendSmsAsync(string phoneNumber, string message, SendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a sign-in or verification code (<c>POST /api/v1/sms/send-2fa</c>). The text must contain the code as a number
    /// of its own, 4 to 10 digits (<c>482913</c> or <c>123-456</c>), and be at most 320 characters; otherwise SMSTunnel
    /// answers 400. Codes count against their own daily allowance. Use it for codes only.
    /// </summary>
    /// <param name="phoneNumber">The recipient, in international format.</param>
    /// <param name="message">The text with the code, for example <c>"Your sign-in code is 482913"</c>.</param>
    /// <param name="options">The phone to send from, the country code, an idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<SendResult> SendSignInCodeAsync(string phoneNumber, string message, SendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Looks up where a text is now (<c>GET /api/v1/sms/status/{id}</c>). Needs a key with <c>sms:read</c>.</summary>
    /// <param name="messageId">The <see cref="SendResult.MessageId"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<MessageStatus> GetMessageStatusAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>Lists the phones of the key's account and whether each is online (<c>GET /api/v1/devices</c>).</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<IReadOnlyList<Phone>> GetPhonesAsync(CancellationToken cancellationToken = default);

    /// <summary>Your plan, today's usage and what is left (<c>GET /api/v1/account/usage</c>).</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<Usage> GetUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists texts your phones received, newest first (<c>GET /api/v1/sms/received</c>). Needs a key you allowed to read
    /// received texts (<b>Can also read received texts</b>, the <c>sms:inbox</c> permission); other keys get
    /// <see cref="SmsTunnelPermissionException"/> with <see cref="SmsTunnelErrorCodes.PermissionRequired"/>.
    /// </summary>
    /// <param name="query">Page and filters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<ReceivedTextPage> GetReceivedTextsAsync(ReceivedTextsQuery? query = null, CancellationToken cancellationToken = default);
}
