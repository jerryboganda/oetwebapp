using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests.Rulebook;

/// <summary>
/// POST /v1/ai/complete: only an admin may pin the provider row. A pin skips the feature route and
/// can name the keyless subscription sidecar rows (one shared Claude Max lane and one shared circuit),
/// so a learner or expert must never be able to choose it.
/// </summary>
public sealed class RulebookAiCompleteProviderPinTests
{
    private const string SidecarRow = "writing-claude-sub";

    private static ClaimsPrincipal InRole(string role)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));

    [Theory]
    [InlineData(ApplicationUserRoles.Learner)]
    [InlineData(ApplicationUserRoles.Expert)]
    [InlineData(ApplicationUserRoles.Sponsor)]
    public void Non_admin_pin_is_ignored(string role)
        => Assert.Equal(string.Empty, RulebookEndpoints.ResolveRequestedProvider(InRole(role), SidecarRow));

    [Fact]
    public void Unauthenticated_pin_is_ignored()
        => Assert.Equal(string.Empty, RulebookEndpoints.ResolveRequestedProvider(new ClaimsPrincipal(new ClaimsIdentity()), SidecarRow));

    [Fact]
    public void Admin_pin_is_kept()
        => Assert.Equal(SidecarRow, RulebookEndpoints.ResolveRequestedProvider(InRole(ApplicationUserRoles.Admin), SidecarRow));

    [Fact]
    public void Admin_without_a_pin_stays_unpinned()
        => Assert.Equal(string.Empty, RulebookEndpoints.ResolveRequestedProvider(InRole(ApplicationUserRoles.Admin), null));
}
