using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OetLearner.Api.Services;

namespace OetLearner.Api.Security;

public class DevelopmentAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-Debug-UserId"].FirstOrDefault() ?? "mock-user-001";
        var role = Request.Headers["X-Debug-Role"].FirstOrDefault() ?? "learner";
        var email = Request.Headers["X-Debug-Email"].FirstOrDefault() ?? "learner@oet-prep.dev";
        var name = Request.Headers["X-Debug-Name"].FirstOrDefault() ?? "Faisal Maqsood";
        var emailVerifiedHeader = Request.Headers["X-Debug-EmailVerified"].FirstOrDefault();
        var isEmailVerified = !bool.TryParse(emailVerifiedHeader, out var parsedEmailVerified) || parsedEmailVerified;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, name),
            new Claim(AuthTokenService.IsEmailVerifiedClaimType, isEmailVerified.ToString().ToLowerInvariant())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
