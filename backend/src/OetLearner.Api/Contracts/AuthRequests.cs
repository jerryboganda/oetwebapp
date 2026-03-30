namespace OetLearner.Api.Contracts;

public record RegisterRequest(
    string Email,
    string Password,
    string Role,
    string? DisplayName,
    string? FirstName = null,
    string? LastName = null,
    string? MobileNumber = null,
    string? ExamTypeId = null,
    string? ProfessionId = null,
    string? SessionId = null,
    string? CountryTarget = null,
    bool? AgreeToTerms = null,
    bool? AgreeToPrivacy = null,
    bool? MarketingOptIn = null,
    string? ExternalRegistrationToken = null);

public record PasswordSignInRequest(
    string Email,
    string Password,
    bool RememberMe);

public record RefreshTokenRequest(string RefreshToken);

public record SignOutRequest(string RefreshToken);

public record SendEmailOtpRequest(
    string Email,
    string Purpose);

public record VerifyEmailOtpRequest(
    string Email,
    string Purpose,
    string Code);

public record BeginAuthenticatorSetupRequest();

public record ConfirmAuthenticatorSetupRequest(string Code);

public record MfaChallengeRequest(
    string Email,
    string Code,
    string? ChallengeToken,
    string? RecoveryCode);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(
    string Email,
    string ResetToken,
    string NewPassword);

public record ExternalAuthExchangeRequest(string ExchangeToken);
