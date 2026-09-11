using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Writing Addendum Rev8 §16-§17 — P0 Nursing task-loading failure
/// ("An unexpected server error occurred" + "No task prompt available").
///
/// Root cause: the eligibility start reference
/// "writing-v2:{userId}:{scenarioId:D}:{n}" (86-90 chars for real learner ids)
/// was written into AiPackageCreditTransaction.JobId, a varchar(64) column →
/// Postgres 22001 → generic 500 for every learner with a finite Writing
/// balance. EF InMemory does not enforce MaxLength, so these tests assert the
/// persisted lengths explicitly. Drives the real eligibility handler
/// (<see cref="WritingScenarioEndpoints.CheckEligibilityAsync"/>) over the real
/// ledger, mirroring <see cref="WritingEntitlementAuthorizeStartTests"/>.
/// </summary>
public sealed class WritingRev8TaskLoadEligibilityTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static (WritingScenarioService scenarios, WritingEntitlementService entitlement, AiPackageCreditService credits) BuildServices(LearnerDbContext db)
    {
        var credits = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var options = new WritingOptionsProvider(db, new MemoryCache(new MemoryCacheOptions()));
        var entitlement = new WritingEntitlementService(db, new EffectiveEntitlementResolver(db), options, credits);
        return (new WritingScenarioService(db, TimeProvider.System), entitlement, credits);
    }

    private static Task GrantWritingCreditsAsync(AiPackageCreditService credits, string userId)
        => credits.GrantPackageAsync(
            userId,
            new BillingAddOn
            {
                Id = "addon_pkg_writing_starter",
                Code = "pkg_writing_starter",
                Name = "pkg_writing_starter",
                Price = 1m,
                Currency = "GBP",
                Interval = "one_time",
                Status = BillingAddOnStatus.Active,
                DurationDays = 30,
                GrantCredits = 3,
                GrantEntitlementsJson = """{"package_type":"writing","writing_only_credits":6}""",
                AddonKind = "ai_package",
                AppliesToAllPlans = true,
                IsStackable = true,
                QuantityStep = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            1,
            $"cs_{Guid.NewGuid():N}",
            null,
            CancellationToken.None);

    private static async Task<WritingScenario> SeedScenarioAsync(
        LearnerDbContext db,
        string status = "published",
        string? taskPrompt = "Using the case notes, write a discharge letter to the community nurse.",
        int sentenceCount = 2)
    {
        var scenario = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = "Nursing discharge — Mrs Patel",
            LetterType = "LT-DG",
            Profession = "Nursing",
            Difficulty = 3,
            Status = status,
            AuthorId = "admin",
            TaskPromptMarkdown = taskPrompt,
            WriterRole = "You are a charge nurse on the surgical ward.",
            TodayDate = "11 September 2026",
            FixedInstructionsJson = """["Expand the relevant notes into complete sentences.", "Do not use note form."]""",
            WordGuideMin = 180,
            WordGuideMax = 200,
            ReadingTimeSeconds = 300,
            WritingTimeSeconds = 2400,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.WritingScenarios.Add(scenario);
        for (var i = 1; i <= sentenceCount; i++)
        {
            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenario.Id,
                Ordinal = i,
                SentenceText = $"Case note sentence {i}.",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return scenario;
    }

    private static HttpContext LearnerContext(string userId)
        => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "learner") },
                "TestAuth")),
        };

    [Fact]
    public async Task FiniteBalanceStart_RealLengthLearnerId_FitsJobIdTo64AndKeepsFullReferenceId()
    {
        await using var db = NewContext();
        var (scenarios, entitlement, credits) = BuildServices(db);
        var userId = $"learner_{Guid.NewGuid():N}"; // 40 chars — the production id shape
        await GrantWritingCreditsAsync(credits, userId);
        var scenario = await SeedScenarioAsync(db);

        var first = await WritingScenarioEndpoints.CheckEligibilityAsync(
            scenario.Id, LearnerContext(userId), scenarios, entitlement, CancellationToken.None);
        // A refresh re-runs eligibility with the same reference: must dedupe.
        var retry = await WritingScenarioEndpoints.CheckEligibilityAsync(
            scenario.Id, LearnerContext(userId), scenarios, entitlement, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(first).StatusCode);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(retry).StatusCode);

        var debit = await db.AiPackageCreditTransactions.SingleAsync(t => t.Reason == AiPackageCreditReason.GradingDeduct);
        var expectedReference = $"writing-v2:{userId}:{scenario.Id:D}:0";
        Assert.True(expectedReference.Length > 64, "precondition: the real reference overflows varchar(64)");
        Assert.Equal(expectedReference, debit.ReferenceId); // full idempotency key, never truncated
        Assert.True(debit.ReferenceId!.Length <= 128);
        Assert.NotNull(debit.JobId);
        Assert.True(debit.JobId!.Length <= 64, $"JobId is {debit.JobId.Length} chars; column is varchar(64)");
        Assert.StartsWith("writing-v2:", debit.JobId);
        Assert.All(
            await db.AiPackageCreditTransactions.ToListAsync(),
            t =>
            {
                Assert.True((t.JobId?.Length ?? 0) <= 64);
                Assert.True((t.PackageId?.Length ?? 0) <= 64);
            });

        var snapshot = await credits.GetSnapshotAsync(userId, 0, CancellationToken.None);
        Assert.Equal(4, snapshot.WritingOnlyCredits); // exactly one activity (2 credits) charged
    }

    [Fact]
    public async Task UnpublishedScenario_Returns409Unavailable_WithoutAnyCharge()
    {
        await using var db = NewContext();
        var (scenarios, entitlement, credits) = BuildServices(db);
        const string userId = "learner_unpublished";
        await GrantWritingCreditsAsync(credits, userId);
        var scenario = await SeedScenarioAsync(db, status: "draft");

        var ex = await Assert.ThrowsAsync<ApiException>(() => WritingScenarioEndpoints.CheckEligibilityAsync(
            scenario.Id, LearnerContext(userId), scenarios, entitlement, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Equal("writing_task_unavailable", ex.ErrorCode);
        Assert.Empty(await db.AiPackageCreditTransactions.Where(t => t.Reason == AiPackageCreditReason.GradingDeduct).ToListAsync());
    }

    [Theory]
    [InlineData(false, 2)] // no written prompt and no stimulus PDF
    [InlineData(true, 0)]  // prompt present but zero case-note sentences
    public async Task IncompleteScenario_Returns409Incomplete_WithoutAnyCharge(bool hasPrompt, int sentenceCount)
    {
        await using var db = NewContext();
        var (scenarios, entitlement, credits) = BuildServices(db);
        const string userId = "learner_incomplete";
        await GrantWritingCreditsAsync(credits, userId);
        var scenario = await SeedScenarioAsync(
            db,
            taskPrompt: hasPrompt ? "Write a discharge letter to the community nurse." : null,
            sentenceCount: sentenceCount);

        var ex = await Assert.ThrowsAsync<ApiException>(() => WritingScenarioEndpoints.CheckEligibilityAsync(
            scenario.Id, LearnerContext(userId), scenarios, entitlement, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Equal("writing_task_incomplete", ex.ErrorCode);
        Assert.Empty(await db.AiPackageCreditTransactions.Where(t => t.Reason == AiPackageCreditReason.GradingDeduct).ToListAsync());
    }

    [Fact]
    public async Task MissingScenario_Returns404_WithoutAnyCharge()
    {
        await using var db = NewContext();
        var (scenarios, entitlement, credits) = BuildServices(db);
        const string userId = "learner_missing";
        await GrantWritingCreditsAsync(credits, userId);

        var ex = await Assert.ThrowsAsync<ApiException>(() => WritingScenarioEndpoints.CheckEligibilityAsync(
            Guid.NewGuid(), LearnerContext(userId), scenarios, entitlement, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Equal("writing_scenario_not_found", ex.ErrorCode);
        Assert.Empty(await db.AiPackageCreditTransactions.Where(t => t.Reason == AiPackageCreditReason.GradingDeduct).ToListAsync());
    }

    [Fact]
    public async Task LearnerScenarioResponse_CarriesTaskPromptAndTaskScreenFields()
    {
        await using var db = NewContext();
        var (scenarios, _, _) = BuildServices(db);
        var scenario = await SeedScenarioAsync(db);

        var response = await scenarios.GetScenarioAsync("learner_task_screen", scenario.Id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(scenario.TaskPromptMarkdown, response!.TaskPromptMarkdown);
        var instructions = Assert.IsAssignableFrom<IReadOnlyList<string>>(response.FixedInstructions);
        Assert.Equal(2, instructions.Count);
        Assert.Equal("Expand the relevant notes into complete sentences.", instructions[0]);
        Assert.Equal("Do not use note form.", instructions[1]);
        Assert.Equal(300, response.ReadingTimeSeconds);
        Assert.Equal(2400, response.WritingTimeSeconds);
        Assert.Equal(180, response.WordGuideMin);
        Assert.Equal(200, response.WordGuideMax);
        Assert.Equal("You are a charge nurse on the surgical ward.", response.WriterRole);
        Assert.Equal("11 September 2026", response.TodayDate);
        Assert.Equal(2, response.CaseNotesStructured.Count);
    }

    [Fact]
    public async Task LearnerScenarioGet_HidesUnpublishedTask_WhileAdminReadStillSeesIt()
    {
        await using var db = NewContext();
        var (scenarios, _, _) = BuildServices(db);
        var draft = await SeedScenarioAsync(db, status: "draft");

        Assert.Null(await scenarios.GetScenarioAsync("learner_draft", draft.Id, CancellationToken.None));
        Assert.NotNull(await scenarios.AdminGetScenarioAsync("admin", draft.Id, CancellationToken.None));
    }
}
