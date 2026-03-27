using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class AuthFlowsTests
{
    [Fact]
    public async Task Login_IssuesJwt_AndCurrentUserCanBeResolved()
    {
        await using var db = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"auth-tests-{Guid.NewGuid():N}")
                .Options);

        db.AuthAccounts.Add(new AuthAccount
        {
            Id = "auth-1",
            SubjectId = "expert-001",
            Role = "expert",
            Email = "expert@example.test",
            DisplayName = "Expert Reviewer",
            PasswordHash = AuthService.HashPassword("Sup3rSecure!Passphrase#2026"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AuthService(
            db,
            Options.Create(new AuthOptions
            {
                Audience = "oet-auth-tests",
                Issuer = "https://auth.test.local",
                SigningKey = "ThisIsATestOnlyJwtSigningKeyThatIsDefinitelyMoreThan32Chars!"
            }),
            NullLogger<AuthService>.Instance);

        var login = await service.LoginAsync(new AuthLoginRequest("expert@example.test", "Sup3rSecure!Passphrase#2026"), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.Equal("expert", login.User.Role);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal("expert-001", token.Claims.First(claim => claim.Type == "user_id").Value);
        Assert.Equal("expert", token.Claims.First(claim => claim.Type == "role").Value);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(token.Claims, "Bearer"));
        var currentUser = await service.GetCurrentUserAsync(principal, CancellationToken.None);
        Assert.Equal("expert@example.test", currentUser.Email);
        Assert.Equal("Expert Reviewer", currentUser.DisplayName);
    }
}
