namespace OetLearner.Api.Contracts;

public sealed record CurrentUserResponse(
    string UserId,
    string Email,
    string Role,
    string? DisplayName,
    bool IsEmailVerified,
    bool IsAuthenticatorEnabled,
    bool RequiresEmailVerification,
    bool RequiresMfa,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? AuthenticatorEnabledAt,
    string[]? AdminPermissions = null,
    string? ActiveProfessionId = null,
    string? ActiveProfessionLabel = null,
    string? AvatarUrl = null);

public sealed record AuthSessionResponse(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserResponse CurrentUser);

public sealed record OtpChallengeResponse(
    string ChallengeId,
    string Purpose,
    string DeliveryChannel,
    string DestinationHint,
    DateTimeOffset ExpiresAt,
    int RetryAfterSeconds);

public sealed record AuthenticatorSetupResponse(
    string SecretKey,
    string OtpAuthUri,
    string QrCodeDataUrl,
    IReadOnlyList<string> RecoveryCodes);

public sealed record SignupCatalogResponse(
    IReadOnlyList<SignupExamTypeResponse> ExamTypes,
    IReadOnlyList<SignupProfessionResponse> Professions,
    IReadOnlyList<string> ExternalAuthProviders,
    IReadOnlyList<string> TargetCountryOptions);

public sealed record SignupExamTypeResponse(
    string Id,
    string Label,
    string Code,
    string Description);

public sealed record SignupProfessionResponse(
    string Id,
    string Label,
    IReadOnlyList<string> CountryTargets,
    IReadOnlyList<string> ExamTypeIds,
    string Description);

public sealed record ExternalRegistrationPromptResponse(
    string RegistrationToken,
    string Provider,
    string Email,
    string? FirstName,
    string? LastName,
    string? NextPath);

public sealed record ExternalAuthExchangeResponse(
    string Status,
    AuthSessionResponse? Session,
    ExternalRegistrationPromptResponse? Registration);

public sealed record ActiveSessionResponse(
    Guid Id,
    string? DeviceInfo,
    string? IpAddress,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    bool IsCurrent,
    string? CountryCode = null,
    string? Platform = null,
    string? DeviceId = null);

/// <summary>The account's currently-trusted device (Security spec §3.2), as
/// shown on the learner's own sessions screen.</summary>
public sealed record TrustedDeviceSelfResponse(
    string? DeviceName,
    string? Platform,
    DateTimeOffset TrustedAt,
    DateTimeOffset? LastSeenAt,
    bool IsCurrentDevice,
    int ActiveDeviceCount = 1,
    int MaxDevices = 1);

public sealed record ActiveSessionListResponse(
    IReadOnlyList<ActiveSessionResponse> Sessions);
