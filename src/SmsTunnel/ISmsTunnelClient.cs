using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmsTunnel.Internal;

namespace SmsTunnel;

/// <summary>
/// The SMSTunnel API: texts sent from your own Android phone, from your own number. Implemented by
/// <see cref="SmsTunnelClient"/>; mock this interface in your own tests.
/// </summary>
/// <remarks>
/// <para>
/// Every method throws <see cref="SmsTunnelApiException"/> (or a subclass) when SMSTunnel answers with an error,
/// <see cref="SmsTunnelConnectionException"/> when no answer comes back, and <see cref="System.OperationCanceledException"/>
/// when the <c>cancellationToken</c> is cancelled. Nothing is retried automatically.
/// </para>
/// <para>
/// The scheduled-text members came in version 1.1.0. They have default implementations that throw
/// <see cref="NotSupportedException"/>, so a class written against 1.0.0 still compiles; implement them to use them.
/// </para>
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

    /// <summary>
    /// Schedules one text to one number (<c>POST /api/v1/sms/send</c> with <c>sendAt</c>). SMSTunnel keeps it and, at
    /// <paramref name="sendAt"/>, sends it exactly as if <see cref="SendSmsAsync"/> were called then with the same key:
    /// the phone, the country code and the plan's daily allowance are those of that moment, and the text counts then. If
    /// the phone is offline then, the text waits for it, up to 3 days. Sign-in codes can't be scheduled.
    /// </summary>
    /// <remarks>
    /// The plan caps how many texts wait at once (Free 10, Starter 50, Standard 200, Professional 500, Enterprise 1000);
    /// over it, <see cref="SmsTunnelPermissionException"/> with <see cref="SmsTunnelErrorCodes.ScheduleLimitReached"/>.
    /// A time less than a minute or more than 30 days ahead, by SMSTunnel's clock, is refused with 400
    /// (<see cref="SmsTunnelErrorCodes.ScheduleTooSoon"/>, <see cref="SmsTunnelErrorCodes.ScheduleTooFar"/>).
    /// </remarks>
    /// <param name="phoneNumber">The recipient, in international format (<c>+14155550123</c>). A local number needs a default country code.</param>
    /// <param name="message">The text, up to 1600 characters.</param>
    /// <param name="sendAt">
    /// When to send it, 1 minute to 30 days from now. It is sent with its offset, in whole seconds, so it means the same
    /// moment wherever SMSTunnel runs.
    /// </param>
    /// <param name="options">The phone to send from, the country code, an idempotency key: as for <see cref="SendSmsAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scheduled text's ID (<see cref="ScheduledSendResult.ScheduledId"/>), to look it up or cancel it.</returns>
    /// <exception cref="ArgumentException">An empty number or text, or <paramref name="sendAt"/> not set (<see langword="default"/>).</exception>
    /// <exception cref="NotSupportedException">The implementation was written before version 1.1.0 and lacks this member.</exception>
    Task<ScheduledSendResult> ScheduleSmsAsync(string phoneNumber, string message, DateTimeOffset sendAt, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw Lacks(this, nameof(ScheduleSmsAsync));
    }

    /// <summary>
    /// Schedules one text to one number, at a <see cref="DateTime"/> in UTC or in this machine's local time. See
    /// <see cref="ScheduleSmsAsync(string, string, DateTimeOffset, SendOptions, CancellationToken)"/>.
    /// </summary>
    /// <param name="phoneNumber">The recipient, in international format (<c>+14155550123</c>).</param>
    /// <param name="message">The text, up to 1600 characters.</param>
    /// <param name="sendAt">
    /// When to send it, 1 minute to 30 days from now, with <see cref="DateTime.Kind"/> <see cref="DateTimeKind.Utc"/> or
    /// <see cref="DateTimeKind.Local"/>. A <see cref="DateTimeKind.Unspecified"/> time is refused, not guessed.
    /// </param>
    /// <param name="options">The phone to send from, the country code, an idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scheduled text's ID (<see cref="ScheduledSendResult.ScheduledId"/>), to look it up or cancel it.</returns>
    /// <exception cref="ArgumentException"><paramref name="sendAt"/> has no time zone (<see cref="DateTimeKind.Unspecified"/>).</exception>
    Task<ScheduledSendResult> ScheduleSmsAsync(string phoneNumber, string message, DateTime sendAt, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Without this overload, C# would turn the DateTime into a DateTimeOffset with this machine's offset, even one
        // without a time zone.
        return ScheduleSmsAsync(phoneNumber, message, Inputs.SendAt(sendAt, nameof(sendAt)), options, cancellationToken);
    }

    /// <summary>
    /// Lists the texts this API key scheduled (<c>GET /api/v1/sms/scheduled</c>): the waiting ones, soonest first, or
    /// with <see cref="ScheduledTextScope.Done"/> the ones that went out, failed or were canceled in the last 30 days,
    /// newest first. Texts scheduled with another key or in the dashboard are not listed. Needs a key with <c>sms:read</c>.
    /// </summary>
    /// <param name="query">Waiting or done, and the page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One page, with how many texts wait in the whole account and the plan's cap on them.</returns>
    /// <exception cref="NotSupportedException">The implementation was written before version 1.1.0 and lacks this member.</exception>
    Task<ScheduledTextPage> GetScheduledTextsAsync(ScheduledTextsQuery? query = null, CancellationToken cancellationToken = default)
    {
        throw Lacks(this, nameof(GetScheduledTextsAsync));
    }

    /// <summary>
    /// Reads one text this API key scheduled (<c>GET /api/v1/sms/scheduled/{id}</c>). Needs a key with <c>sms:read</c>.
    /// Another key's text, or an unknown ID, is a 404 with <see cref="SmsTunnelErrorCodes.ScheduleNotFound"/>.
    /// </summary>
    /// <param name="scheduledId">The <see cref="ScheduledSendResult.ScheduledId"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="NotSupportedException">The implementation was written before version 1.1.0 and lacks this member.</exception>
    Task<ScheduledText> GetScheduledTextAsync(string scheduledId, CancellationToken cancellationToken = default)
    {
        throw Lacks(this, nameof(GetScheduledTextAsync));
    }

    /// <summary>
    /// Cancels a text this API key scheduled, while it waits (<c>DELETE /api/v1/sms/scheduled/{id}</c>). Needs a key with
    /// <c>sms:send</c>. One that already went out, failed or was canceled is a 409 with
    /// <see cref="SmsTunnelErrorCodes.ScheduleNotEditable"/>; another key's text, or an unknown ID, a 404 with
    /// <see cref="SmsTunnelErrorCodes.ScheduleNotFound"/>.
    /// </summary>
    /// <param name="scheduledId">The <see cref="ScheduledSendResult.ScheduledId"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scheduled text, now <see cref="ScheduledTextStatus.Canceled"/>.</returns>
    /// <exception cref="NotSupportedException">The implementation was written before version 1.1.0 and lacks this member.</exception>
    Task<ScheduledText> CancelScheduledTextAsync(string scheduledId, CancellationToken cancellationToken = default)
    {
        throw Lacks(this, nameof(CancelScheduledTextAsync));
    }

    private static NotSupportedException Lacks(ISmsTunnelClient client, string member)
    {
        return new NotSupportedException(
            $"{client.GetType().FullName} does not implement ISmsTunnelClient.{member}, which SmsTunnel 1.1.0 added. SmsTunnelClient implements it.");
    }
}
