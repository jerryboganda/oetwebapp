using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-learner-tests-storage-{Guid.NewGuid():N}");
    private readonly string _databaseName = $"oet-learner-tests-{Guid.NewGuid():N}";
    private readonly bool _useFirstPartyAuth;
    private readonly Dictionary<string, string?> _previousEnvironmentValues = new();
    private readonly Dictionary<string, string?>? _firstPartyConfiguration;

    public TestWebApplicationFactory()
        : this(useFirstPartyAuth: false)
    {
    }

    protected TestWebApplicationFactory(bool useFirstPartyAuth)
    {
        _useFirstPartyAuth = useFirstPartyAuth;

        if (!_useFirstPartyAuth)
        {
            foreach (var key in new[] { "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT" })
            {
                _previousEnvironmentValues[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, "Development");
            }

            _previousEnvironmentValues["Billing__CheckoutBaseUrl"] = Environment.GetEnvironmentVariable("Billing__CheckoutBaseUrl");
            Environment.SetEnvironmentVariable("Billing__CheckoutBaseUrl", "https://app.example.test/billing/checkout");
        }

        if (!_useFirstPartyAuth)
        {
            return;
        }

        _firstPartyConfiguration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"InMemory:{_databaseName}",
            ["Auth:UseDevelopmentAuth"] = "false",
            ["Bootstrap:AutoMigrate"] = "false",
            ["Bootstrap:SeedDemoData"] = "true",
            ["Platform:PublicApiBaseUrl"] = "http://localhost",
            ["Platform:PublicWebBaseUrl"] = "http://localhost",
            ["Platform:FallbackEmailDomain"] = "example.test",
            ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
            ["Storage:LocalRootPath"] = _storageRoot,
            ["PasswordPolicy:BreachCheckEnabled"] = "false",
            [$"{AuthTokenOptions.SectionName}:Issuer"] = "https://api.example.test",
            [$"{AuthTokenOptions.SectionName}:Audience"] = "oet-learner-web",
            [$"{AuthTokenOptions.SectionName}:AccessTokenSigningKey"] = "access-token-signing-key-12345678901234567890",
            [$"{AuthTokenOptions.SectionName}:RefreshTokenSigningKey"] = "refresh-token-signing-key-1234567890123456789",
            [$"{AuthTokenOptions.SectionName}:AccessTokenLifetime"] = "00:15:00",
            [$"{AuthTokenOptions.SectionName}:RefreshTokenLifetime"] = "30.00:00:00",
            [$"{AuthTokenOptions.SectionName}:OtpLifetime"] = "00:10:00",
            [$"{AuthTokenOptions.SectionName}:AuthenticatorIssuer"] = "OET Learner"
        };

        foreach (var (key, value) in _firstPartyConfiguration)
        {
            var environmentVariableName = ToEnvironmentVariableName(key);
            _previousEnvironmentValues[environmentVariableName] = Environment.GetEnvironmentVariable(environmentVariableName);
            Environment.SetEnvironmentVariable(environmentVariableName, value);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Replace AI provider registrations so tests run against a
        // deterministic local provider instead of OpenAi/Anthropic, which
        // would fail without real credentials. Q3 of the Speaking module
        // (docs/SPEAKING-MODULE-PLAN.md §6) is now fail-loud on AI errors,
        // so the previous silent rule-engine fallback is gone — tests must
        // supply a working provider.
        builder.ConfigureTestServices(services =>
        {
            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (IsHostedService(services[i]))
                {
                    services.RemoveAt(i);
                }
            }

            for (var i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(OetLearner.Api.Services.Rulebook.IAiModelProvider))
                {
                    services.RemoveAt(i);
                }
            }
            services.AddSingleton<IAiModelProvider, TestAiModelProvider>();

            // Register BackgroundJobProcessor as a directly-resolvable singleton
            // so tests can call its internal ProcessOnceAsync to deterministically
            // drain queued jobs (the hosted-service tick is stripped above to
            // avoid 2-second race loops during tests).
            services.AddSingleton<BackgroundJobProcessor>();
        });

        if (_useFirstPartyAuth)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(_firstPartyConfiguration!);
            });

            return;
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"InMemory:{_databaseName}",
                ["Auth:UseDevelopmentAuth"] = "true",
                ["Bootstrap:SeedDemoData"] = "true",
                ["Platform:PublicApiBaseUrl"] = "http://localhost",
                ["Platform:PublicWebBaseUrl"] = "http://localhost",
                ["Platform:FallbackEmailDomain"] = "example.test",
                ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
                ["Storage:LocalRootPath"] = _storageRoot,
                ["PasswordPolicy:BreachCheckEnabled"] = "false"
            });
        });
    }

    private static bool IsHostedService(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(IHostedService);

    public HttpClient CreateAuthenticatedClient(string email, string password, string? expectedRole = null)
    {
        var client = CreateClient();
        var signInResponse = client.PostAsJsonAsync(
                "/v1/auth/sign-in",
                new PasswordSignInRequest(email, password, RememberMe: true))
            .GetAwaiter()
            .GetResult();
        signInResponse.EnsureSuccessStatusCode();

        var session = signInResponse.Content
            .ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options)
            .GetAwaiter()
            .GetResult();

        if (session is null)
        {
            throw new InvalidOperationException("Expected a sign-in session for the seeded auth account.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var currentUserResponse = client.GetAsync("/v1/auth/me").GetAwaiter().GetResult();
        currentUserResponse.EnsureSuccessStatusCode();
        var currentUser = currentUserResponse.Content
            .ReadFromJsonAsync<CurrentUserResponse>(JsonSupport.Options)
            .GetAwaiter()
            .GetResult();
        if (currentUser is null)
        {
            throw new InvalidOperationException("Expected current-user details for the seeded auth account.");
        }

        if (!string.Equals(currentUser.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected seeded auth client for {email} to resolve the same email, but '/v1/auth/me' returned '{currentUser.Email}'.");
        }

        if ((string.Equals(currentUser.Role, "expert", StringComparison.Ordinal)
                || string.Equals(currentUser.Role, "admin", StringComparison.Ordinal))
            && !currentUser.IsEmailVerified)
        {
            throw new InvalidOperationException($"Seeded privileged account {email} is not marked as email verified.");
        }

        if (!string.IsNullOrWhiteSpace(expectedRole)
            && !string.Equals(currentUser.Role, expectedRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected seeded auth client for {email} to resolve role '{expectedRole}', but '/v1/auth/me' returned '{currentUser.Role}'.");
        }

        return client;
    }

    /// <summary>
    /// Drives one or more passes of the background job pipeline so tests can
    /// drain queued evaluations deterministically. The hosted-service loop is
    /// stripped from the test host (see <c>IsHostedService</c>) so
    /// queued <c>JobType.WritingEvaluation</c> / <c>JobType.SpeakingEvaluation</c>
    /// rows would otherwise never advance past "queued". Default 3 passes
    /// covers the common "evaluation → side-effects → notification fan-out"
    /// cascade in one call.
    ///
    /// Before each pass we backdate <c>AvailableAt</c> on every Queued job
    /// so deterministic drains do not have to wait for the production-default
    /// 1-second submit-time buffer (or any retry backoff). Without this,
    /// LearnerService.QueueJobAsync's <c>AvailableAt = now + 1s</c> would
    /// cause tight test polling loops to skip the job on every pass.
    /// </summary>
    public async Task DrainBackgroundJobsAsync(int passes = 3, CancellationToken cancellationToken = default)
    {
        var processor = Services.GetRequiredService<BackgroundJobProcessor>();
        for (var i = 0; i < passes; i++)
        {
            await ForceQueuedJobsImmediatelyAvailableAsync(cancellationToken);
            await processor.ProcessOnceAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Sets <c>AvailableAt</c> for every Queued job to "now - 1s" so the
    /// BackgroundJobProcessor immediately considers it eligible. Production
    /// uses the AvailableAt buffer to space retries; tests want deterministic
    /// drain semantics with no wall-clock waits.
    /// </summary>
    private async Task ForceQueuedJobsImmediatelyAvailableAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var jobs = await db.BackgroundJobs
            .Where(x => x.State == AsyncState.Queued && x.AvailableAt > now)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0) return;
        foreach (var job in jobs)
        {
            job.AvailableAt = now.AddSeconds(-1);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Grants AI credits to the user so AI-debited features (writing grade,
    /// speaking grade, etc.) do not throw <c>ai_credits_insufficient</c>.
    /// Production credit accounting is enforced by <c>AiGatewayService</c>
    /// for paid features; tests need to short-circuit that fail-closed gate
    /// without disabling the production code path.
    /// </summary>
    public async Task EnsureAiCreditsAsync(string userId, int tokens = 10_000, CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var existing = await db.AiCreditLedger
            .Where(x => x.UserId == userId && x.Source == AiCreditSource.Promo)
            .Select(x => x.TokensDelta)
            .ToListAsync(cancellationToken);
        var existingTotal = existing.Sum();
        if (existingTotal >= tokens) return;

        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = $"aicl-test-{Guid.NewGuid():N}",
            UserId = userId,
            TokensDelta = tokens - existingTotal,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.Promo,
            Description = "Test fixture grant",
            ReferenceId = null,
            ExpiresAt = null,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureLearnerProfileAsync(string userId, string email, string displayName)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var learner = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (learner is null)
        {
            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = displayName,
                Email = email,
                Timezone = "UTC",
                Locale = "en-AU",
                ActiveProfessionId = "medicine",
                CreatedAt = now,
                LastActiveAt = now,
                AccountStatus = "active"
            });
        }
        else
        {
            learner.DisplayName = displayName;
            learner.Email = email;
            learner.Timezone = "UTC";
            learner.Locale = "en-AU";
            learner.ActiveProfessionId ??= "medicine";
            learner.LastActiveAt = now;
            learner.AccountStatus = "active";
        }

        if (!await db.Goals.AnyAsync(x => x.UserId == userId))
        {
            db.Goals.Add(new LearnerGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProfessionId = "medicine",
                WeakSubtestsJson = "[]",
                DraftStateJson = "{}",
                UpdatedAt = now
            });
        }

        if (!await db.Settings.AnyAsync(x => x.UserId == userId))
        {
            db.Settings.Add(new LearnerSettings
            {
                Id = Guid.NewGuid(),
                UserId = userId
            });
        }

        if (!await db.Wallets.AnyAsync(x => x.UserId == userId))
        {
            db.Wallets.Add(new Wallet
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                CreditBalance = 0,
                LedgerSummaryJson = "[]",
                LastUpdatedAt = now
            });
        }

        if (!await db.ReadinessSnapshots.AnyAsync(x => x.UserId == userId))
        {
            db.ReadinessSnapshots.Add(new ReadinessSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ComputedAt = now,
                Version = 1,
                PayloadJson = JsonSupport.Serialize(new
                {
                    targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)).ToString("yyyy-MM-dd"),
                    weeksRemaining = 12,
                    overallRisk = "moderate",
                    recommendedStudyHours = 12,
                    weakestLink = "Writing - Conciseness & Clarity",
                    subTests = new[]
                    {
                        new { id = "rd-w", name = "Writing", readiness = 62, target = 80, status = "Needs attention", isWeakest = true },
                        new { id = "rd-s", name = "Speaking", readiness = 68, target = 80, status = "On track", isWeakest = false },
                        new { id = "rd-r", name = "Reading", readiness = 82, target = 80, status = "Target met", isWeakest = false },
                        new { id = "rd-l", name = "Listening", readiness = 76, target = 80, status = "Almost there", isWeakest = false }
                    },
                    blockers = new[]
                    {
                        new { id = 1, title = "Writing conciseness remains below threshold", description = "Recent writing evidence shows extra detail that weakens GP-focused communication." },
                        new { id = 2, title = "Speaking fluency markers still appear", description = "Filler words and soft starts reduce handover authority." }
                    },
                    evidence = new { mocksCompleted = 2, practiceQuestions = 48, expertReviews = 1, recentTrend = "Improving", lastUpdated = now }
                })
            });
        }

        if (!await db.StudyPlans.AnyAsync(x => x.UserId == userId))
        {
            var planId = Guid.NewGuid().ToString("N");
            db.StudyPlans.Add(new StudyPlan
            {
                Id = planId,
                UserId = userId,
                Version = 1,
                GeneratedAt = now,
                State = AsyncState.Completed,
                Checkpoint = "Initial plan",
                WeakSkillFocus = "Writing conciseness and speaking fluency"
            });

            db.StudyPlanItems.Add(new StudyPlanItem
            {
                Id = Guid.NewGuid().ToString("N"),
                StudyPlanId = planId,
                Title = "Writing task focused on discharge summaries",
                SubtestCode = "writing",
                DurationMinutes = 45,
                Rationale = "Practice concise patient handover language.",
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                Status = StudyPlanItemStatus.NotStarted,
                Section = "writing",
                ContentId = "wt-001",
                ItemType = "practice"
            });
        }

        if (!await db.Subscriptions.AnyAsync(x => x.UserId == userId))
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                PlanId = "basic-monthly",
                Status = SubscriptionStatus.Active,
                NextRenewalAt = now.AddMonths(1),
                StartedAt = now,
                ChangedAt = now,
                PriceAmount = 0m,
                Currency = "AUD",
                Interval = "monthly"
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task EnsureExpertProfileAsync(string userId, string email, string displayName)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var expert = await db.ExpertUsers.FirstOrDefaultAsync(x => x.Id == userId);
        if (expert is null)
        {
            db.ExpertUsers.Add(new ExpertUser
            {
                Id = userId,
                DisplayName = displayName,
                Email = email,
                Timezone = "UTC",
                IsActive = true,
                CreatedAt = now
            });
        }
        else
        {
            expert.DisplayName = displayName;
            expert.Email = email;
            expert.Timezone = "UTC";
            expert.IsActive = true;
        }

        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        foreach (var (key, value) in _previousEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only for test temp files.
        }
    }

    private static string ToEnvironmentVariableName(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);

    private sealed class TestAiModelProvider : IAiModelProvider
    {
        public string Name => "mock";

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            const string writingText = """
                {
                    "findings": [],
                    "criteriaScores": {
                        "purpose": 2,
                        "content": 5,
                        "conciseness_clarity": 5,
                        "genre_style": 5,
                        "organisation_layout": 5,
                        "language": 5
                    },
                    "estimatedScaledScore": 360,
                    "estimatedGrade": "B",
                    "passed": true,
                    "passRequires": "350",
                    "advisory": "Test AI provider returned a complete grounded Writing scoring contract.",
                    "strengths": ["Clear purpose."]
                }
                """;

            const string speakingText = """
                {
                    "findings": [],
                    "criterionScores": {
                        "intelligibility": 5,
                        "fluency": 5,
                        "appropriateness": 5,
                        "grammarExpression": 5,
                        "relationshipBuilding": 2,
                        "patientPerspective": 2,
                        "structure": 2,
                        "informationGathering": 2,
                        "informationGiving": 2
                    },
                    "estimatedScaledScore": 360,
                    "estimatedGrade": "B",
                    "passed": true,
                    "advisory": "Test AI provider returned a complete grounded Speaking scoring contract.",
                    "strengths": ["Clear interaction structure."]
                }
                """;

            var isSpeakingPrompt = request.UserPrompt.Contains("advisory Speaking feedback", StringComparison.OrdinalIgnoreCase);
            var text = isSpeakingPrompt
                ? speakingText
                : writingText;

            return Task.FromResult(new AiProviderCompletion { Text = text, Usage = new AiUsage() });
        }
    }
}

public sealed class FirstPartyAuthTestWebApplicationFactory : TestWebApplicationFactory
{
    public FirstPartyAuthTestWebApplicationFactory()
        : base(useFirstPartyAuth: true)
    {
    }
}
