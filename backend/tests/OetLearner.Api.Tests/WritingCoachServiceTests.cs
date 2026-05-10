using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public class WritingCoachServiceTests
{
    private static (LearnerDbContext Db, WritingCoachService Service, StubAiGateway Gateway) Build(
        string? cannedCompletion = null,
        Exception? completeThrows = null,
        IReadOnlyList<string>? appliedRuleIds = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);

        var loader = new RulebookLoader();
        var ruleEngine = new WritingRuleEngine(loader);
        var gateway = new StubAiGateway(cannedCompletion, completeThrows, appliedRuleIds);

        var service = new WritingCoachService(
            db,
            gateway,
            ruleEngine,
            NullLogger<WritingCoachService>.Instance);

        return (db, service, gateway);
    }

    [Fact]
    public async Task CheckTextAsync_PersistsOnlyRuleCitedSuggestions_NoGenericLengthOutput()
    {
        // Per Writing Module Technical Specification v1.0, the coach must
        // never emit "generic" suggestions sourced purely from response
        // length / word count. The new pipeline routes everything through
        // the rule engine + the grounded gateway, so every persisted finding
        // MUST carry an explicit rule citation in its explanation. The AI
        // gateway is stubbed out (empty list) so this test only exercises
        // the rule-engine path; the assertion guards against any future
        // regression that re-introduces ungrounded length heuristics.
        var (db, service, _) = Build(cannedCompletion: "{ \"suggestions\": [] }");
        await SeedWritingAttemptAsync(db, "learner-1", "attempt-1");
        var longText = string.Join(" ", Enumerable.Range(0, 220).Select(_ => "clinically"));

        await service.CheckTextAsync(
            userId: "learner-1",
            attemptId: "attempt-1",
            request: new WritingCoachCheckRequest(longText, CursorPosition: null),
            ct: CancellationToken.None);

        var suggestions = await db.WritingCoachSuggestions.ToListAsync();
        Assert.All(suggestions, s =>
            Assert.Matches(@"^\[(R|RULE_)[A-Za-z0-9_.]+", s.Explanation));

        var session = await db.WritingCoachSessions.SingleAsync();
        Assert.Equal(suggestions.Count, session.SuggestionsGenerated);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task CheckTextAsync_HappyPath_PersistsAiSuggestionsAndRuleEngineFindings()
    {
        const string aiResponse = """
            {
              "suggestions": [
                {
                  "ruleId": "R12.1",
                  "category": "grammar",
                  "severity": "major",
                  "anchor": { "start": 0, "end": 11, "snippet": "Dear Doctor" },
                  "message": "Use a named recipient where possible.",
                  "suggestedReplacement": "Dear Dr Smith",
                  "rationale": "Genre conventions favour named salutations."
                },
                {
                  "ruleId": "R03.4",
                  "category": "structure",
                  "severity": "critical",
                  "anchor": { "start": 0, "end": 5, "snippet": "Hello" },
                  "message": "Salutation must use 'Dear'.",
                  "suggestedReplacement": "Dear",
                  "rationale": "Letter must open with formal salutation."
                }
              ]
            }
            """;
        var (db, service, gateway) = Build(
            cannedCompletion: aiResponse,
            appliedRuleIds: new[] { "R12.1", "R03.4" });
        await SeedWritingAttemptAsync(db, "learner-2", "attempt-2");

        var letterText = "Dear Doctor\n\nThe patient presented with cough.\n\nYours sincerely\nDr Hesham";
        await service.CheckTextAsync(
            userId: "learner-2",
            attemptId: "attempt-2",
            request: new WritingCoachCheckRequest(
                letterText,
                CursorPosition: null,
                LetterType: "routine_referral",
                Profession: "Medicine",
                CandidateCountry: "UK"),
            ct: CancellationToken.None);

        var persisted = await db.WritingCoachSuggestions.ToListAsync();
        Assert.NotEmpty(persisted);

        // The two AI items must both be present (deduped by ruleId+anchor).
        Assert.Contains(persisted, s => s.Explanation.Contains("[R12.1", StringComparison.Ordinal));
        Assert.Contains(persisted, s => s.Explanation.Contains("[R03.4", StringComparison.Ordinal));

        var session = await db.WritingCoachSessions.SingleAsync();
        Assert.Equal(persisted.Count, session.SuggestionsGenerated);

        // Gateway was actually called with the W3A grounded contract.
        Assert.Equal(1, gateway.CompleteCalls);
        Assert.Equal(AiFeatureCodes.WritingCoachSuggest, gateway.LastRequest?.FeatureCode);
        Assert.Equal("learner-2", gateway.LastRequest?.UserId);
        Assert.Equal(letterText, gateway.LastRequest?.UserInput);
        Assert.Equal(RuleKind.Writing, gateway.LastContext?.Kind);
        Assert.Equal(AiTaskMode.Coach, gateway.LastContext?.Task);
        Assert.Equal("routine_referral", gateway.LastContext?.LetterType);
        Assert.Equal("UK", gateway.LastContext?.CandidateCountry);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task CheckTextAsync_DropsAiSuggestionsWithUnknownRuleIds()
    {
        const string aiResponse = """
            {
              "suggestions": [
                {
                  "ruleId": "R12.1",
                  "category": "grammar",
                  "severity": "major",
                  "anchor": { "start": 0, "end": 11, "snippet": "Dear Doctor" },
                  "message": "Use the rulebook citation supplied in the grounded prompt.",
                  "suggestedReplacement": "Dear Dr Smith"
                },
                {
                  "ruleId": "R99.9",
                  "category": "grammar",
                  "severity": "major",
                  "anchor": { "start": 0, "end": 11, "snippet": "Dear Doctor" },
                  "message": "Invented rule IDs must not be persisted.",
                  "suggestedReplacement": "Dear Dr Smith"
                }
              ]
            }
            """;
        var (db, service, _) = Build(
            cannedCompletion: aiResponse,
            appliedRuleIds: new[] { "R12.1" });
        await SeedWritingAttemptAsync(db, "learner-unknown-rule", "attempt-unknown-rule");

        await service.CheckTextAsync(
            userId: "learner-unknown-rule",
            attemptId: "attempt-unknown-rule",
            request: new WritingCoachCheckRequest(
                "Dear Doctor\n\nThe patient presented with cough.\n\nYours sincerely\nDr Hesham",
                CursorPosition: null,
                LetterType: "routine_referral",
                Profession: "Medicine",
                CandidateCountry: "UK"),
            ct: CancellationToken.None);

        var persisted = await db.WritingCoachSuggestions.ToListAsync();
        Assert.Contains(persisted, s => s.Explanation.Contains("[R12.1", StringComparison.Ordinal));
        Assert.DoesNotContain(persisted, s => s.Explanation.Contains("[R99.9", StringComparison.Ordinal));

        await db.DisposeAsync();
    }

    [Fact]
    public async Task CheckTextAsync_GatewayThrows_PersistsRuleEngineFindingsAndDoesNotBubble()
    {
        var (db, service, gateway) = Build(completeThrows: new InvalidOperationException("boom"));
        await SeedWritingAttemptAsync(db, "learner-3", "attempt-3");

        var letterText = "Dear Doctor\n\nThe patient presented with cough.\n\nYours sincerely\nDr Hesham";

        // Must not throw despite the gateway exploding.
        var response = await service.CheckTextAsync(
            userId: "learner-3",
            attemptId: "attempt-3",
            request: new WritingCoachCheckRequest(
                letterText,
                CursorPosition: null,
                LetterType: "routine_referral",
                Profession: "Medicine"),
            ct: CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(1, gateway.CompleteCalls);

        // No AI-tagged suggestions should have leaked into the table even
        // though the call failed. Rule-engine findings (if any) are allowed.
        var persisted = await db.WritingCoachSuggestions.ToListAsync();
        // The R12.1 / R03.4 IDs only appear in the AI canned response (which
        // we did not feed into this test), so they must NOT be present.
        Assert.DoesNotContain(persisted, s => s.Explanation.Contains("[R12.1", StringComparison.Ordinal));

        var session = await db.WritingCoachSessions.SingleAsync();
        Assert.Equal(persisted.Count, session.SuggestionsGenerated);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task CheckTextAsync_RejectsAttemptOwnedByAnotherUser()
    {
        var (db, service, _) = Build(cannedCompletion: "{ \"suggestions\": [] }");
        await SeedWritingAttemptAsync(db, "attempt-owner", "owned-attempt");

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CheckTextAsync(
            userId: "attempt-intruder",
            attemptId: "owned-attempt",
            request: new WritingCoachCheckRequest(
                "Dear Doctor\n\nThe patient presented with cough.\n\nYours sincerely\nDr Hesham",
                CursorPosition: null),
            ct: CancellationToken.None));

        Assert.Equal("WRITING_ATTEMPT_NOT_FOUND", ex.ErrorCode);
        Assert.False(await db.WritingCoachSessions.AnyAsync());

        await db.DisposeAsync();
    }

    private static async Task SeedWritingAttemptAsync(
        LearnerDbContext db,
        string userId,
        string attemptId,
        string contentId = "coach-writing-content")
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.ContentItems.AnyAsync(content => content.Id == contentId))
        {
            db.ContentItems.Add(new ContentItem
            {
                Id = contentId,
                ContentType = "writing_task",
                SubtestCode = "writing",
                ProfessionId = "medicine",
                Title = "Coach Writing Task",
                Difficulty = "standard",
                EstimatedDurationMinutes = 45,
                ScenarioType = "routine_referral",
                PublishedRevisionId = "test-revision",
                Status = ContentStatus.Published,
                CaseNotes = "Patient notes: cough and review required.",
                DetailJson = "{\"letterType\":\"routine_referral\"}",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });
        }

        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = contentId,
            SubtestCode = "writing",
            Context = "practice",
            Mode = "timed",
            State = AttemptState.InProgress,
            StartedAt = now,
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // Stub gateway: bypasses provider plumbing entirely so we can drive
    // both the success and failure branches of WritingCoachService
    // without spinning up RulebookPromptBuilder + a fake provider.
    // ------------------------------------------------------------------

    private sealed class StubAiGateway(
        string? cannedCompletion,
        Exception? completeThrows,
        IReadOnlyList<string>? appliedRuleIds) : IAiGatewayService
    {
        public AiGroundingContext? LastContext { get; private set; }
        public AiGatewayRequest? LastRequest { get; private set; }
        public int CompleteCalls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            LastContext = context;
            var groundedRuleIds = appliedRuleIds ?? new[] { "R12.1", "R03.4" };
            // Minimal grounded-shaped prompt. WritingCoachService never
            // inspects it — it just forwards it to CompleteAsync — so an
            // empty prompt object is sufficient for these unit tests.
            return new AiGroundedPrompt
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt (test stub)",
                TaskInstruction = "Return suggestions JSON.",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookVersion = "test",
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    ScoringPassMark = 350,
                    ScoringGrade = "B",
                    AppliedRulesCount = groundedRuleIds.Count,
                    AppliedRuleIds = groundedRuleIds,
                },
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            CompleteCalls++;
            LastRequest = request;
            if (completeThrows is not null) throw completeThrows;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = cannedCompletion ?? "{}",
                Metadata = request.Prompt?.Metadata ?? new AiGroundedPromptMetadata(),
                AppliedRuleIds = appliedRuleIds ?? Array.Empty<string>(),
            });
        }
    }
}
