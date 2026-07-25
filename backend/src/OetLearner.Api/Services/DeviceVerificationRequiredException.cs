namespace OetLearner.Api.Services;

/// <summary>Security spec §3.2: thrown mid-sign-in when the account has a
/// different trusted device already and the presented device id needs an
/// email-OTP challenge before it can proceed. Mirrors
/// <see cref="MfaChallengeRequiredException"/> exactly — same
/// challenge-token transport, same 403 JSON shape (see Program.cs).</summary>
public sealed class DeviceVerificationRequiredException(string email, string challengeToken)
    : Exception("This device needs to be verified before sign-in can continue.")
{
    public string Email { get; } = email;

    public string ChallengeToken { get; } = challengeToken;
}
