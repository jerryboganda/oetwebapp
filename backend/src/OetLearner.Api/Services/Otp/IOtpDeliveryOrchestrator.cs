using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Otp;

public sealed record OtpSmsDeliveryResult(
    bool Attempted,
    bool Delivered,
    string? PhoneNumber,
    string? SessionInfo);

/// <summary>
/// Decides whether reset_password / trust_device OTP may use Firebase SMS.
/// verify_email is never SMS. Does not send email — EmailOtpService owns that.
/// </summary>
public interface IOtpDeliveryOrchestrator
{
    Task<OtpSmsDeliveryResult> TrySendFirebaseSmsAsync(
        ApplicationUserAccount account,
        string purpose,
        string? recaptchaToken,
        CancellationToken cancellationToken = default);
}
