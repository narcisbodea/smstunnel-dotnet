using SmsTunnel;

namespace IdentitySms;

/// <summary>
/// The SMS sender of the ASP.NET Core Identity templates: Identity makes the phone codes
/// (<c>GenerateChangePhoneNumberTokenAsync</c>, <c>GenerateTwoFactorTokenAsync</c>) and the app sends them.
/// </summary>
public interface ISmsSender
{
    Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}

/// <summary>Sends Identity's codes from your own Android phone, from your own number, with SMSTunnel.</summary>
public sealed class SmsTunnelSmsSender(ISmsTunnelClient smsTunnel, ILogger<SmsTunnelSmsSender> logger) : ISmsSender
{
    public async Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        // Identity's phone codes are 6 digits on their own, so they go out as sign-in codes
        // (POST /api/v1/sms/send-2fa), with their own daily allowance.
        SendResult result = await smsTunnel.SendSignInCodeAsync(phoneNumber, message, cancellationToken: cancellationToken);

        if (result.Queued)
        {
            logger.LogWarning("No phone of yours is online: code {MessageId} goes out as soon as one is back.", result.MessageId);
        }
    }
}
