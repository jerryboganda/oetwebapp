using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public sealed class AuthQueryPerformanceTests : IAsyncLifetime
{
    private readonly JwtValidationRelationalFactory _factory = new();

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task JwtValidation_UsesOneFailClosedRelationalCommandAcrossRoleAndStatusMatrix()
    {
        var scenarios = new (string Name, bool ShouldAuthenticate)[]
        {
            ("admin", true),
            ("learner-active", true),
            ("learner-missing", false),
            ("learner-suspended", false),
            ("learner-expired", false),
            ("expert-active", true),
            ("expert-missing", false),
            ("expert-inactive", false),
            ("account-deleted", false),
            ("account-missing", false),
            ("unknown-role", true)
        };

        foreach (var scenario in scenarios)
        {
            var issuedToken = await SeedScenarioAndIssueTokenAsync(scenario.Name);
            _factory.Commands.Clear();

            await using var scope = _factory.Services.CreateAsyncScope();
            var httpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };
            httpContext.Request.Headers.Authorization = $"Bearer {issuedToken.AccessToken}";

            var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var result = await authentication.AuthenticateAsync(
                httpContext,
                JwtBearerDefaults.AuthenticationScheme);

            Assert.True(
                result.Succeeded == scenario.ShouldAuthenticate,
                $"Unexpected JWT validation result for scenario '{scenario.Name}'.");
            if (scenario.ShouldAuthenticate)
            {
                Assert.Equal(
                    issuedToken.AuthAccountId,
                    result.Principal?.FindFirst(AuthTokenService.AuthAccountIdClaimType)?.Value);
            }

            var command = Assert.Single(_factory.Commands.ReaderCommands);
            Assert.Contains("ApplicationUserAccounts", command, StringComparison.Ordinal);
            Assert.Contains("Users", command, StringComparison.Ordinal);
            Assert.Contains("ExpertUsers", command, StringComparison.Ordinal);
            Assert.Contains("EXISTS", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INNER JOIN", command, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task LearnerSignIn_LoadsAuthenticationProfileOnce()
    {
        const string email = "query-count-learner@example.test";
        const string password = "Password123!";
        await SeedPasswordLearnerAsync(email, password);
        _factory.Commands.Clear();

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/v1/auth/sign-in",
            new PasswordSignInRequest(email, password, RememberMe: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.Commands.ReaderCommands.Where(command =>
            command.Contains("FROM \"Users\"", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<IssuedToken> SeedScenarioAndIssueTokenAsync(string scenario)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var authAccountId = $"auth-query-{scenario}-{Guid.NewGuid():N}";
        var role = scenario switch
        {
            "admin" => ApplicationUserRoles.Admin,
            "expert-active" or "expert-missing" or "expert-inactive" => ApplicationUserRoles.Expert,
            "unknown-role" => "unexpected-role",
            _ => ApplicationUserRoles.Learner
        };
        var userId = $"profile-query-{scenario}-{Guid.NewGuid():N}";

        if (!string.Equals(scenario, "account-missing", StringComparison.Ordinal))
        {
            db.ApplicationUserAccounts.Add(new ApplicationUserAccount
            {
                Id = authAccountId,
                Email = $"{Guid.NewGuid():N}@example.test",
                NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
                PasswordHash = "not-used",
                Role = role,
                DeletedAt = string.Equals(scenario, "account-deleted", StringComparison.Ordinal) ? now : null,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (scenario is "learner-active" or "learner-suspended" or "learner-expired" or "account-deleted")
        {
            db.Users.Add(new LearnerUser
            {
                Id = userId,
                AuthAccountId = authAccountId,
                Role = ApplicationUserRoles.Learner,
                DisplayName = "Query Learner",
                Email = $"{Guid.NewGuid():N}@example.test",
                AccountStatus = string.Equals(scenario, "learner-suspended", StringComparison.Ordinal)
                    ? "suspended"
                    : "AcTiVe",
                AccessExpiresAt = string.Equals(scenario, "learner-expired", StringComparison.Ordinal)
                    ? now.AddMinutes(-1)
                    : null,
                CreatedAt = now,
                LastActiveAt = now
            });
        }

        if (scenario is "expert-active" or "expert-inactive")
        {
            db.ExpertUsers.Add(new ExpertUser
            {
                Id = userId,
                AuthAccountId = authAccountId,
                Role = ApplicationUserRoles.Expert,
                DisplayName = "Query Expert",
                Email = $"{Guid.NewGuid():N}@example.test",
                IsActive = string.Equals(scenario, "expert-active", StringComparison.Ordinal),
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();

        var tokenService = scope.ServiceProvider.GetRequiredService<AuthTokenService>();
        var accessToken = tokenService.IssueSession(new AuthenticatedSessionSubject(
            userId,
            authAccountId,
            $"{Guid.NewGuid():N}@example.test",
            role,
            "Query Subject",
            IsEmailVerified: true,
            IsAuthenticatorEnabled: false,
            RequiresEmailVerification: false,
            RequiresMfa: false,
            EmailVerifiedAt: now,
            AuthenticatorEnabledAt: null)).AccessToken;

        return new IssuedToken(authAccountId, accessToken);
    }

    private async Task SeedPasswordLearnerAsync(string email, string password)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var account = new ApplicationUserAccount
        {
            Id = $"auth-query-sign-in-{Guid.NewGuid():N}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = ApplicationUserRoles.Learner,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = new PasswordHasher<ApplicationUserAccount>()
            .HashPassword(account, password);

        db.ApplicationUserAccounts.Add(account);
        db.Users.Add(new LearnerUser
        {
            Id = $"learner-query-sign-in-{Guid.NewGuid():N}",
            AuthAccountId = account.Id,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Query Count Learner",
            Email = email,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now
        });
        await db.SaveChangesAsync();
    }

    private sealed record IssuedToken(string AuthAccountId, string AccessToken);

    private sealed class JwtValidationRelationalFactory : TestWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"oet-auth-query-tests-{Guid.NewGuid():N}.db");

        public JwtValidationRelationalFactory()
            : base(useFirstPartyAuth: true, seedDemoData: false)
        {
        }

        public RelationalCommandCounter Commands { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={_databasePath}"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(Commands);
                services.AddDbContext<LearnerDbContext>((serviceProvider, options) =>
                    options.AddInterceptors(
                        serviceProvider.GetRequiredService<RelationalCommandCounter>()));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                try
                {
                    File.Delete(_databasePath + suffix);
                }
                catch
                {
                    // Best-effort cleanup for the isolated relational test database.
                }
            }
        }
    }

    private sealed class RelationalCommandCounter : DbCommandInterceptor
    {
        public List<string> ReaderCommands { get; } = [];

        public void Clear() => ReaderCommands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
