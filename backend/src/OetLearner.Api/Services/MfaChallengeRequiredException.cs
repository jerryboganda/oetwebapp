namespace OetLearner.Api.Services;

public sealed class MfaChallengeRequiredException(string email, string challengeToken)
    : Exception("Multi-factor authentication is required to complete sign-in.")
{
    public string Email { get; } = email;

    public string ChallengeToken { get; } = challengeToken;
}
