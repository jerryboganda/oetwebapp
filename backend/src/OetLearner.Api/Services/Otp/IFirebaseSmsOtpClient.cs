namespace OetLearner.Api.Services.Otp;

public sealed record FirebaseSmsSendResult(bool Success, string? SessionInfo, string? ErrorCode, bool TransportError = false);

public sealed record FirebaseSmsVerifyResult(bool Success, string? PhoneNumber, string? ErrorCode, bool TransportError = false);

/// <summary>
/// Identity Toolkit REST client for Firebase Phone Auth. SMS transport only —
/// callers must discard any returned ID token and never treat Firebase as the
/// app session authority.
/// </summary>
public interface IFirebaseSmsOtpClient
{
    Task<FirebaseSmsSendResult> SendVerificationCodeAsync(
        string phoneNumber,
        string recaptchaToken,
        string webApiKey,
        CancellationToken cancellationToken = default);

    Task<FirebaseSmsVerifyResult> VerifyCodeAsync(
        string sessionInfo,
        string code,
        string webApiKey,
        CancellationToken cancellationToken = default);
}
