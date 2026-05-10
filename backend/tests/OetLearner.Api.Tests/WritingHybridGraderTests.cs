using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// xUnit coverage for the hybrid Writing grader introduced on
/// audit/rulebook-compliance-2026-05-10 (commit d144967).
///
/// Source under test:
///   - BackgroundJobProcessor.CompleteWritingEvaluationAsync
///   - TryRunWritingAiGraderAsync, ExtractJsonObject, MapRuleToCriterion,
///     MapProfession, ResolveWritingLetterType
///   - Domain.WritingRuleViolation (EF entity + cascade FK)
///   - Evaluation.GraderMode / RuleViolationCount /
///     CriticalRuleViolationCount / RuleViolationsJson / AiRawResponseJson
///     (added by 20260510171105_AddWritingRuleViolation)
///   - LearnerService.GetWritingEvaluationSummaryAsync (extended response shape)
///
/// Each test owns its own <see cref="GraderTestFactory"/> with its own
/// in-memory DB and BackgroundJobProcessor, so the pinned
/// <see cref="ProgrammableAiGateway"/> can never be raced by an unrelated
/// processor.
/// </summary>
public class WritingHybridGraderTests
{
    // -------------------------------------------------------------------
    // Test 1 — Hybrid happy path
    // -------------------------------------------------------------------
    [Fact]
    public async Task HybridGrader_HappyPath_PersistsHybridModeAndAllSixCriteria()
    {
        var aiJson = """
        {
          "criteriaScores": {
            "purpose": 3,
            "content": 6,
            "conciseness_clarity": 6,
            "genre_style": 6,
            "organisation_layout": 6,
            "language": 6
          },
          "findings": [
            { "ruleId": "R03.1", "severity": "minor", "message": "Sample AI finding" }
          ],
          "strengths": ["Clear handover purpose"],
          "issues": ["Tighten paragraph two"]
        }
        """;
        await using var factory = new GraderTestFactory(ProgrammableAiGateway.ReturningJson(aiJson));
        var userId = $"hybrid-happy-{Guid.NewGuid():N}";
        using var client = await CreateLearnerClientAsync(factory, userId);

        var draft = string.Join('\n', new[]
        {
            "Dear Dr Patterson,",
            "",
            "Re: Mrs Anne Vance",
            "",
            "I am writing to update you regarding Mrs Vance after her recent knee replacement.",
            "She recovered well post-operatively and will require staple removal in 14 days.",
            "Please review her progress at her next appointment.",
            "",
            "Yours sincerely,",
            "Dr A. Resident",
        });

        var (evaluationId, _) = await SubmitWritingAttemptAsync(client, draft);
        await PollUntilCompletedAsync(client, evaluationId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var evaluation = await db.Evaluations.AsNoTracking().FirstAsync(e => e.Id == evaluationId);

        Assert.Equal(AsyncState.Completed, evaluation.State);
        Assert.Equal("hybrid", evaluation.GraderMode);

        // The deterministic rulebook fires structural critical violations on
        // any minimal/sparse OET letter (missing Re:, missing date, sparse
        // body, etc.). For the happy-path test we only assert: hybrid was
        // entered, the per-evaluation row count matches the persisted
        // counter, and the row counts are internally consistent.
        var rowCount = await db.WritingRuleViolations.AsNoTracking()
            .CountAsync(v => v.EvaluationId == evaluationId);
        var criticalRows = await db.WritingRuleViolations.AsNoTracking()
            .CountAsync(v => v.EvaluationId == evaluationId && v.Severity == "critical");
        Assert.Equal(evaluation.RuleViolationCount, rowCount);
        Assert.Equal(evaluation.CriticalRuleViolationCount, criticalRows);

        var crits = ParseCriterionScores(evaluation.CriterionScoresJson);
        Assert.Equal(6, crits.Count);
        var maxByCode = crits.ToDictionary(c => c.Code, c => c.Max);
        Assert.Equal(3, maxByCode["purpose"]);
        Assert.Equal(7, maxByCode["content"]);
        Assert.Equal(7, maxByCode["conciseness_clarity"]);
        Assert.Equal(7, maxByCode["genre_style"]);
        Assert.Equal(7, maxByCode["organisation_layout"]);
        Assert.Equal(7, maxByCode["language"]);
    }

    // -------------------------------------------------------------------
    // Test 2 — Critical violations cap (asserts SPEC intent: cap "language")
    // -------------------------------------------------------------------
    [Fact]
    public async Task HybridGrader_CriticalViolations_CapsLanguageCriterionAndPersistsRows()
    {
        var aiJson = """
        {
          "criteriaScores": {
            "purpose": 3,
            "content": 7,
            "conciseness_clarity": 7,
            "genre_style": 7,
            "organisation_layout": 7,
            "language": 7
          }
        }
        """;
        await using var factory = new GraderTestFactory(ProgrammableAiGateway.ReturningJson(aiJson));
        var userId = $"hybrid-cap-{Guid.NewGuid():N}";
        using var client = await CreateLearnerClientAsync(factory, userId);

        // Body deliberately seeds two critical violations:
        //   - "don't" → R12.1 no_contractions (critical)
        //   - "the patient" → R08.14 / R12.2 the_patient (critical)
        var draft = string.Join('\n', new[]
        {
            "Dear Dr Patterson,",
            "",
            "Re: Mrs Anne Vance",
            "",
            "I am writing because we don't have time to wait — the patient was advised to attend.",
            "She recovered well post-operatively and will require staple removal in 14 days.",
            "Please review her progress at her next appointment.",
            "",
            "Yours sincerely,",
            "Dr A. Resident",
        });

        var (evaluationId, _) = await SubmitWritingAttemptAsync(client, draft);
        await PollUntilCompletedAsync(client, evaluationId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var evaluation = await db.Evaluations.AsNoTracking().FirstAsync(e => e.Id == evaluationId);
        var rows = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.EvaluationId == evaluationId)
            .ToListAsync();

        Assert.Equal("hybrid", evaluation.GraderMode);
        Assert.True(rows.Count >= 2,
            $"Expected at least 2 deterministic violation rows, got {rows.Count}.");
        Assert.True(evaluation.CriticalRuleViolationCount >= 2,
            $"Expected CriticalRuleViolationCount >= 2, got {evaluation.CriticalRuleViolationCount}.");

        var crits = ParseCriterionScores(evaluation.CriterionScoresJson);
        var language = crits.Single(c => c.Code == "language");
        Assert.Equal(7, language.Max);

        // Spec expectation: contraction + 'the patient' criticals must cap
        // the LANGUAGE criterion at max-2. If this fails, MapRuleToCriterion
        // is routing R12.* to a non-language criterion (potential bug).
        var critByCriterion = string.Join(',',
            rows.Where(r => r.Severity == "critical")
                .GroupBy(r => r.CriterionCode)
                .Select(g => g.Key + "=" + g.Count()));
        Assert.True(language.Score <= language.Max - 2,
            $"Expected 'language' score <= max-2 ({language.Max - 2}); was {language.Score}/{language.Max}. " +
            $"Critical rows by criterion: [{critByCriterion}]. " +
            "If this fails, MapRuleToCriterion is routing R12.* to a non-language criterion.");
    }

    // -------------------------------------------------------------------
    // Test 3 — AI gateway throws → deterministic_only fallback
    // -------------------------------------------------------------------
    [Fact]
    public async Task HybridGrader_AiThrows_FallsBackToDeterministicOnly()
    {
        await using var factory = new GraderTestFactory(
            ProgrammableAiGateway.Throwing(new InvalidOperationException("simulated provider outage")));
        var userId = $"hybrid-throw-{Guid.NewGuid():N}";
        using var client = await CreateLearnerClientAsync(factory, userId);
        var draft = "Dear Dr Patterson,\n\nRe: Mrs Vance\n\nI am writing with a routine update.\n\nYours sincerely,\nDr A. Resident";

        var (evaluationId, _) = await SubmitWritingAttemptAsync(client, draft);
        await PollUntilCompletedAsync(client, evaluationId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var evaluation = await db.Evaluations.AsNoTracking().FirstAsync(e => e.Id == evaluationId);

        Assert.Equal(AsyncState.Completed, evaluation.State);
        Assert.Equal("deterministic_only", evaluation.GraderMode);

        var crits = ParseCriterionScores(evaluation.CriterionScoresJson);
        Assert.Equal(6, crits.Count);
        foreach (var c in crits)
        {
            var heuristic = (int)Math.Round(c.Max * 0.65);
            Assert.True(c.Score <= heuristic,
                $"Deterministic-only fallback for '{c.Code}' must not exceed heuristic {heuristic}/{c.Max}; was {c.Score}/{c.Max}.");
        }
    }

    // -------------------------------------------------------------------
    // Test 4 — AI returns plain text (non-JSON) → deterministic_only fallback
    // -------------------------------------------------------------------
    [Fact]
    public async Task HybridGrader_AiReturnsNonJson_FallsBackToDeterministicOnly()
    {
        await using var factory = new GraderTestFactory(
            ProgrammableAiGateway.ReturningText("This is not JSON. Just prose."));
        var userId = $"hybrid-text-{Guid.NewGuid():N}";
        using var client = await CreateLearnerClientAsync(factory, userId);
        var draft = "Dear Dr Patterson,\n\nRe: Mrs Vance\n\nI am writing with a routine update.\n\nYours sincerely,\nDr A. Resident";

        var (evaluationId, _) = await SubmitWritingAttemptAsync(client, draft);
        await PollUntilCompletedAsync(client, evaluationId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var evaluation = await db.Evaluations.AsNoTracking().FirstAsync(e => e.Id == evaluationId);

        Assert.Equal(AsyncState.Completed, evaluation.State);
        Assert.Equal("deterministic_only", evaluation.GraderMode);
        var crits = ParseCriterionScores(evaluation.CriterionScoresJson);
        Assert.Equal(6, crits.Count);
    }

    // -------------------------------------------------------------------
    // Test 5 — WritingRuleViolation FK + cascade delete
    // -------------------------------------------------------------------
    [Fact]
    public async Task WritingRuleViolation_CascadeDeletesWithEvaluation()
    {
        await using var db = BuildIsolatedInMemoryDb();
        var evaluation = SeedAttemptAndEvaluation(db, attemptIdSuffix: "casc");
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            db.WritingRuleViolations.Add(new WritingRuleViolation
            {
                Id = $"wrv-{Guid.NewGuid():N}",
                EvaluationId = evaluation.Id,
                AttemptId = evaluation.AttemptId,
                UserId = "user-casc",
                RuleId = $"R0{i + 1}.1",
                Severity = "critical",
                CriterionCode = "language",
                Quote = $"q{i}",
                Message = $"m{i}",
                FixSuggestion = null,
                CreatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.WritingRuleViolations.CountAsync(v => v.EvaluationId == evaluation.Id));

        // Materialize dependents into the change tracker so EF InMemory's
        // cascade-delete handler can see and remove them. The Evaluation
        // entity has no navigation collection back to violations.
        var tracked = await db.WritingRuleViolations.Where(v => v.EvaluationId == evaluation.Id).ToListAsync();
        Assert.Equal(3, tracked.Count);

        db.Evaluations.Remove(evaluation);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.WritingRuleViolations.CountAsync(v => v.EvaluationId == evaluation.Id));
    }

    // -------------------------------------------------------------------
    // Test 6 — Indexed columns are queryable (RuleId, UserId)
    // -------------------------------------------------------------------
    [Fact]
    public async Task WritingRuleViolation_QueryByIndexedColumns_ReturnsCorrectRows()
    {
        await using var db = BuildIsolatedInMemoryDb();
        var eval1 = SeedAttemptAndEvaluation(db, attemptIdSuffix: "qa");
        var eval2 = SeedAttemptAndEvaluation(db, attemptIdSuffix: "qb");
        var now = DateTimeOffset.UtcNow;

        WritingRuleViolation Make(string id, string evalId, string userId, string ruleId) => new()
        {
            Id = id,
            EvaluationId = evalId,
            AttemptId = $"att-{id}",
            UserId = userId,
            RuleId = ruleId,
            Severity = "critical",
            CriterionCode = "language",
            CreatedAt = now,
        };

        db.WritingRuleViolations.AddRange(
            Make("v1", eval1.Id, "alice", "R12.1"),
            Make("v2", eval1.Id, "alice", "R12.2"),
            Make("v3", eval2.Id, "bob", "R12.1"),
            Make("v4", eval2.Id, "bob", "R08.14"));
        await db.SaveChangesAsync();

        var byRule = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.RuleId == "R12.1").OrderBy(v => v.Id).ToListAsync();
        Assert.Equal(new[] { "v1", "v3" }, byRule.Select(v => v.Id).ToArray());

        var byUser = await db.WritingRuleViolations.AsNoTracking()
            .Where(v => v.UserId == "alice").OrderBy(v => v.Id).ToListAsync();
        Assert.Equal(new[] { "v1", "v2" }, byUser.Select(v => v.Id).ToArray());

        var entity = db.Model.FindEntityType(typeof(WritingRuleViolation))!;
        var indexCols = entity.GetIndexes()
            .Select(ix => string.Join('+', ix.Properties.Select(p => p.Name)))
            .ToHashSet();
        Assert.Contains("RuleId+CreatedAt", indexCols);
        Assert.Contains("UserId+CreatedAt", indexCols);
        Assert.Contains("EvaluationId", indexCols);
    }

    // -------------------------------------------------------------------
    // Test 7 — GetWritingEvaluationSummaryAsync exposes hybrid fields
    // -------------------------------------------------------------------
    [Fact]
    public async Task SummaryEndpoint_ExposesHybridGraderShape()
    {
        var aiJson = """
        {
          "criteriaScores": {
            "purpose": 3, "content": 6, "conciseness_clarity": 6,
            "genre_style": 6, "organisation_layout": 6, "language": 6
          },
          "strengths": ["Clear purpose"],
          "issues": ["Trim paragraph two"]
        }
        """;
        await using var factory = new GraderTestFactory(ProgrammableAiGateway.ReturningJson(aiJson));
        var userId = $"hybrid-shape-{Guid.NewGuid():N}";
        using var client = await CreateLearnerClientAsync(factory, userId);
        var draft = string.Join('\n', new[]
        {
            "Dear Dr Patterson,",
            "",
            "Re: Mrs Anne Vance",
            "",
            "I am writing to update you regarding Mrs Vance after her knee replacement.",
            "She is recovering well and will require staple removal in 14 days.",
            "",
            "Yours sincerely,",
            "Dr A. Resident",
        });
        var (evaluationId, _) = await SubmitWritingAttemptAsync(client, draft);
        await PollUntilCompletedAsync(client, evaluationId);

        var summaryResponse = await client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
        summaryResponse.EnsureSuccessStatusCode();
        using var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        var root = summaryJson.RootElement;

        Assert.Equal("completed", root.GetProperty("state").GetString());
        Assert.Equal("hybrid", root.GetProperty("graderMode").GetString());

        Assert.True(root.TryGetProperty("ruleViolations", out var ruleViolations));
        Assert.Equal(JsonValueKind.Array, ruleViolations.ValueKind);
        Assert.True(root.TryGetProperty("aiFeedback", out var aiFeedback));
        Assert.Equal(JsonValueKind.Array, aiFeedback.ValueKind);
        Assert.True(root.TryGetProperty("criterionScores", out var critScores));
        Assert.Equal(JsonValueKind.Array, critScores.ValueKind);
        Assert.Equal(6, critScores.GetArrayLength());

        foreach (var c in critScores.EnumerateArray())
        {
            Assert.True(c.TryGetProperty("criterionCode", out _));
            Assert.True(c.TryGetProperty("score", out _));
            Assert.True(c.TryGetProperty("max", out var max));
            Assert.Equal(JsonValueKind.Number, max.ValueKind);
            Assert.True(max.GetInt32() is 3 or 7,
                $"criterion max must be 3 (purpose) or 7 (others); was {max.GetInt32()}.");
        }
    }

    // ======================================================================
    // Test infrastructure
    // ======================================================================

    private static async Task<HttpClient> CreateLearnerClientAsync(GraderTestFactory factory, string userId)
    {
        await factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private static async Task<(string EvaluationId, string AttemptId)> SubmitWritingAttemptAsync(
        HttpClient client, string draft)
    {
        var createAttemptResponse = await client.PostAsJsonAsync("/v1/writing/attempts", new
        {
            contentId = "wt-001",
            context = "practice",
            mode = "timed",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        createAttemptResponse.EnsureSuccessStatusCode();
        using var createdJson = JsonDocument.Parse(await createAttemptResponse.Content.ReadAsStringAsync());
        var attemptId = createdJson.RootElement.GetProperty("attemptId").GetString()!;

        var draftResponse = await client.PatchAsJsonAsync($"/v1/writing/attempts/{attemptId}/draft", new
        {
            content = draft,
            scratchpad = "",
            checklist = new Dictionary<string, bool>(),
            draftVersion = 1
        });
        draftResponse.EnsureSuccessStatusCode();

        var submitResponse = await client.PostAsJsonAsync($"/v1/writing/attempts/{attemptId}/submit", new
        {
            content = draft,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        submitResponse.EnsureSuccessStatusCode();
        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var evaluationId = submitJson.RootElement.GetProperty("evaluationId").GetString()!;
        return (evaluationId, attemptId);
    }

    private static async Task PollUntilCompletedAsync(HttpClient client, string evaluationId)
    {
        for (var i = 0; i < 30; i++)
        {
            var resp = await client.GetAsync($"/v1/writing/evaluations/{evaluationId}/summary");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.GetProperty("state").GetString() == "completed")
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        throw new InvalidOperationException($"Evaluation {evaluationId} did not reach 'completed' within polling window.");
    }

    private static LearnerDbContext BuildIsolatedInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-hybrid-{Guid.NewGuid():N}")
            .Options;
        return new LearnerDbContext(options);
    }

    private static Evaluation SeedAttemptAndEvaluation(LearnerDbContext db, string attemptIdSuffix)
    {
        var attemptId = $"att-{attemptIdSuffix}-{Guid.NewGuid():N}";
        var evalId = $"eval-{attemptIdSuffix}-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = $"user-{attemptIdSuffix}",
            ContentId = "wt-001",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "timed",
            State = AttemptState.Submitted,
            StartedAt = now,
        });
        var evaluation = new Evaluation
        {
            Id = evalId,
            AttemptId = attemptId,
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "350",
            GradeRange = "B",
            ConfidenceBand = ConfidenceBand.Medium,
            GeneratedAt = now,
            ModelExplanationSafe = "test",
            LearnerDisclaimer = "test",
            LastTransitionAt = now,
            GraderMode = "hybrid",
        };
        db.Evaluations.Add(evaluation);
        db.SaveChanges();
        return evaluation;
    }

    private sealed record CriterionRow(string Code, int Score, int Max);

    private static List<CriterionRow> ParseCriterionScores(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var rows = new List<CriterionRow>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            rows.Add(new CriterionRow(
                el.GetProperty("criterionCode").GetString() ?? "",
                el.GetProperty("score").GetInt32(),
                el.GetProperty("max").GetInt32()));
        }
        return rows;
    }

    /// <summary>
    /// Per-test factory subclass that pins a programmable
    /// <see cref="IAiGatewayService"/>. Each test owns its own factory so
    /// the gateway swap cannot be raced by an unrelated BackgroundJobProcessor.
    /// </summary>
    private sealed class GraderTestFactory : TestWebApplicationFactory
    {
        private readonly IAiGatewayService _gateway;

        public GraderTestFactory(IAiGatewayService gateway)
        {
            _gateway = gateway;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(IAiGatewayService))
                    {
                        services.RemoveAt(i);
                    }
                }
                services.AddSingleton(_gateway);
            });
        }
    }

    /// <summary>
    /// Programmable IAiGatewayService test double. Builds a syntactically
    /// valid grounded prompt (with the rulebook header that the real gateway
    /// enforces) and returns either a canned completion string or throws.
    /// </summary>
    private sealed class ProgrammableAiGateway : IAiGatewayService
    {
        private readonly Func<CancellationToken, Task<AiGatewayResult>> _complete;

        private ProgrammableAiGateway(Func<CancellationToken, Task<AiGatewayResult>> complete)
        {
            _complete = complete;
        }

        public static ProgrammableAiGateway ReturningJson(string json)
            => new(_ => Task.FromResult(new AiGatewayResult { Completion = json }));

        public static ProgrammableAiGateway ReturningText(string text)
            => new(_ => Task.FromResult(new AiGatewayResult { Completion = text }));

        public static ProgrammableAiGateway Throwing(Exception ex)
            => new(_ => throw ex);

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => _complete(ct);

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\n[stub for tests]",
                TaskInstruction = "stub",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }
}
