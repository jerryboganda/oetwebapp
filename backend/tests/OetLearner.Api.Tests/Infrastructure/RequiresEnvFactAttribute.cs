using Xunit;

namespace OetLearner.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="FactAttribute"/> that reports Skipped (not Passed) unless every
/// named environment variable is set. Use for live-provider tests instead of an
/// early <c>return</c>, which makes an unrun test look green.
/// </summary>
public sealed class RequiresEnvFactAttribute : FactAttribute
{
    public RequiresEnvFactAttribute(params string[] variables)
    {
        var missing = variables.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToArray();
        if (missing.Length > 0)
        {
            Skip = $"Set {string.Join(" and ", missing)} to run this live test.";
        }
    }
}
