namespace OetLearner.Api.Services;

/// <summary>Security spec §3.2: thrown mid-sign-in when the account has a
/// different trusted device already and the presented device id needs an
/// email-OTP challenge before it can proceed. Mirrors
/// <see cref="MfaChallengeRequiredException"/> exactly — same
/// challenge-token transport, same 403 JSON shape (see Program.cs).</summary>
public sealed class DeviceVerificationRequiredException(
    string email,
    string challengeToken,
    string mode = "otp_required",
    IReadOnlyList<Security.TrustedDeviceSummary>? registeredDevices = null,
    int activeDeviceCount = 0,
    int maxDevices = Security.TrustedDeviceService.DefaultMaxDevices,
    DateTimeOffset? cooldownUntil = null,
    int? secondsRemaining = null,
    int? changeWindowDays = null,
    int? changeMaxPerWindow = null)
    : Exception("This device needs to be verified before sign-in can continue.")
{
    public string Email { get; } = email;
    public string ChallengeToken { get; } = challengeToken;
    public string Mode { get; } = mode;
    public IReadOnlyList<Security.TrustedDeviceSummary>? RegisteredDevices { get; } = registeredDevices;
    public int ActiveDeviceCount { get; } = activeDeviceCount;
    public int MaxDevices { get; } = maxDevices;
    public DateTimeOffset? CooldownUntil { get; } = cooldownUntil;
    public int? SecondsRemaining { get; } = secondsRemaining;
    public int? ChangeWindowDays { get; } = changeWindowDays;
    public int? ChangeMaxPerWindow { get; } = changeMaxPerWindow;
}

public sealed class DeviceChangeCooldownException(
    string message,
    DateTimeOffset cooldownUntil,
    int secondsRemaining,
    int changeWindowDays,
    int changeMaxPerWindow,
    int activeDeviceCount,
    int maxDevices)
    : Exception(message)
{
    public DateTimeOffset CooldownUntil { get; } = cooldownUntil;
    public int SecondsRemaining { get; } = secondsRemaining;
    public int ChangeWindowDays { get; } = changeWindowDays;
    public int ChangeMaxPerWindow { get; } = changeMaxPerWindow;
    public int ActiveDeviceCount { get; } = activeDeviceCount;
    public int MaxDevices { get; } = maxDevices;
}
