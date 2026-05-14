using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

public class ListeningRelationalRuntimeTests
{
    private sealed class AllowAllContentEntitlementService : IContentEntitlementService
    {
        public Task<ContentEntitlementResult> AllowAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.FromResult(new ContentEntitlementResult(true, "test", "premium", null));

        public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
            => Task.CompletedTask;

        public bool IsAdmin(System.Security.Claims.ClaimsPrincipal? principal) => false;
    }

    private static (LearnerDbContext db, ListeningLearnerService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningLearnerService(db, new AllowAllContentEntitlementService()));
    }

    private static async Task<(string userId, string paperId, string questionId)> SeedRelationalPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new LearnerUser
        {
            Id = "learner-1",
            AuthAccountId = "auth-1",
            DisplayName = "Learner One",
            Email = "learner@example.test",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        };
        var paper = new ContentPaper
        {
            Id = "paper-1",
            SubtestCode = "listening",
            Title = "Relational Listening Paper",
            Slug = "relational-listening-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ListeningPart
        {
            Id = "part-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extract = new ListeningExtract
        {
            Id = "extract-a1",
            ListeningPartId = part.Id,
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation 1",
            AccentCode = "en-GB",
            SpeakersJson = "[{\"id\":\"s1\",\"role\":\"GP\",\"gender\":\"f\"}]",
            TranscriptSegmentsJson = "[{\"startMs\":1000,\"endMs\":3000,\"speakerId\":\"s1\",\"text\":\"The dose is five milligrams.\"}]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var question = new OetLearner.Api.Domain.ListeningQuestion
        {
            Id = "question-1",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Dose: ____ milligrams",
            CorrectAnswerJson = "\"five\"",
            AcceptedSynonymsJson = "[\"5\"]",
            CaseSensitive = false,
            ExplanationMarkdown = "The speaker says five milligrams.",
            SkillTag = "numbers_units",
            TranscriptEvidenceText = "The dose is five milligrams.",
            TranscriptEvidenceStartMs = 1000,
            TranscriptEvidenceEndMs = 3000,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        db.ContentPapers.Add(paper);
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(extract);
        db.ListeningQuestions.Add(question);
        db.ListeningPolicies.Add(new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 10 });
        await db.SaveChangesAsync();
        return (user.Id, paper.Id, question.Id);
    }

    private static async Task<(string userId, string paperId)> SeedJsonFallbackPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new LearnerUser
        {
            Id = "learner-json-1",
            AuthAccountId = "auth-json-1",
            DisplayName = "JSON Learner",
            Email = "json-learner@example.test",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active",
        };
        var paper = new ContentPaper
        {
            Id = "json-paper-1",
            SubtestCode = "listening",
            Title = "JSON Listening Paper",
            Slug = "json-listening-paper",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            ExtractedTextJson = JsonSerializer.Serialize(new
            {
                listeningQuestions = new[]
                {
                    new
                    {
                        id = "json-q1",
                        number = 1,
                        partCode = "A1",
                        type = "short_answer",
                        text = "Dose: ____ milligrams",
                        correctAnswer = "five",
                    },
                },
            }),
        };
        db.Users.Add(user);
        db.ContentPapers.Add(paper);
        db.ListeningPolicies.Add(new ListeningPolicy { Id = "global", FullPaperTimerMinutes = 45, GracePeriodSeconds = 10 });
        await db.SaveChangesAsync();
        return (user.Id, paper.Id);
    }

    [Fact]
    public async Task AuthoredPaper_UsesRelationalAttemptAnswersAndReview()
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);

        var session = await svc.GetSessionAsync(userId, paperId, "home", null, default);
        Assert.Contains("kiosk_fullscreen", System.Text.Json.JsonSerializer.Serialize(session));

        var started = await svc.StartAttemptAsync(userId, paperId, "home", default);
        var startedJson = JsonSerializer.Serialize(started);
        Assert.Contains("lat-", startedJson);
        Assert.Contains("home", startedJson);

        var attempt = await db.ListeningAttempts.SingleAsync(a => a.UserId == userId && a.PaperId == paperId);
        Assert.Equal(ListeningAttemptMode.Home, attempt.Mode);

        await svc.SaveAnswerAsync(userId, attempt.Id, questionId, new ListeningAnswerSaveRequest("five"), default);
        await svc.RecordIntegrityEventAsync(userId, attempt.Id, new ListeningIntegrityEventRequest("fullscreen_exit", "test", DateTimeOffset.UtcNow), default);
        var review = await svc.SubmitAsync(userId, attempt.Id, default);
        var reviewJson = JsonSerializer.Serialize(review);

        var savedAnswer = await db.ListeningAnswers.SingleAsync(a => a.ListeningAttemptId == attempt.Id);
        var submitted = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.True(savedAnswer.IsCorrect);
        Assert.Equal(1, savedAnswer.PointsEarned);
        Assert.Equal(ListeningAttemptStatus.Submitted, submitted.Status);
        Assert.Equal(1, submitted.RawScore);
        Assert.Contains("TranscriptSegments", reviewJson);
        Assert.Contains("fullscreen_exit", (await db.AuditEvents.SingleAsync(e => e.Action == "ListeningIntegrityEvent")).Details);
    }

    [Fact]
    public async Task Review_HidesHumanOverrideAuditReasonFromLearnerPayload()
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);
        const string internalReason = "Internal calibration note: accept phonetic spelling.";

        await svc.StartAttemptAsync(userId, paperId, "home", default);
        var attempt = await db.ListeningAttempts.SingleAsync(a => a.UserId == userId && a.PaperId == paperId);
        await svc.SaveAnswerAsync(userId, attempt.Id, questionId, new ListeningAnswerSaveRequest("wrong"), default);
        await svc.SubmitAsync(userId, attempt.Id, default);

        attempt.HumanScoreOverridesJson = $"[{{\"questionId\":\"{questionId}\",\"override\":1,\"by\":\"expert-1\",\"reason\":\"{internalReason}\"}}]";
        await db.SaveChangesAsync();

        var review = await svc.GetReviewAsync(userId, attempt.Id, default);
        var reviewJson = JsonSerializer.Serialize(review);

        Assert.Contains("Marked correct by human reviewer.", reviewJson);
        Assert.DoesNotContain(internalReason, reviewJson);
        Assert.DoesNotContain("expert-1", reviewJson);
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("five   milligrams", true)]
    [InlineData("five milligrams tablets", false)]
    public async Task RelationalSubmit_UsesAcceptedVariantsWithoutPartialCredit(
        string learnerAnswer,
        bool expectedCorrect)
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);
        var question = await db.ListeningQuestions.SingleAsync(item => item.Id == questionId);
        question.AcceptedSynonymsJson = "[\"5\",\"five milligrams\"]";
        await db.SaveChangesAsync();

        await svc.StartAttemptAsync(userId, paperId, "home", default);
        var attempt = await db.ListeningAttempts.SingleAsync(item => item.UserId == userId && item.PaperId == paperId);
        await svc.SaveAnswerAsync(userId, attempt.Id, questionId, new ListeningAnswerSaveRequest(learnerAnswer), default);
        await svc.SubmitAsync(userId, attempt.Id, default);

        var savedAnswer = await db.ListeningAnswers.SingleAsync(answer => answer.ListeningAttemptId == attempt.Id);
        Assert.Equal(expectedCorrect, savedAnswer.IsCorrect);
        Assert.Equal(expectedCorrect ? 1 : 0, savedAnswer.PointsEarned);
    }

    [Fact]
    public async Task SubmitAfterDeadline_GradesPersistedAnswersAndIgnoresLateFinalPayload()
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "home", default);
        var attempt = await db.ListeningAttempts.SingleAsync(a => a.UserId == userId && a.PaperId == paperId);
        await svc.SaveAnswerAsync(userId, attempt.Id, questionId, new ListeningAnswerSaveRequest("wrong"), default);

        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        await svc.SubmitAsync(
            userId,
            attempt.Id,
            new Dictionary<string, string?> { [questionId] = "five" },
            default);

        var savedAnswer = await db.ListeningAnswers.SingleAsync(a => a.ListeningAttemptId == attempt.Id);
        var submitted = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.False(savedAnswer.IsCorrect);
        Assert.Equal(0, savedAnswer.PointsEarned);
        Assert.Equal(ListeningAttemptStatus.Submitted, submitted.Status);
        Assert.Equal(0, submitted.RawScore);
    }

    [Fact]
    public async Task SubmitAfterWorkerExpiry_GradesPersistedAnswersAndIgnoresLateFinalPayload()
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "home", default);
        var attempt = await db.ListeningAttempts.SingleAsync(a => a.UserId == userId && a.PaperId == paperId);
        await svc.SaveAnswerAsync(userId, attempt.Id, questionId, new ListeningAnswerSaveRequest("wrong"), default);

        attempt.Status = ListeningAttemptStatus.Expired;
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        attempt.SubmittedAt = DateTimeOffset.UtcNow.AddSeconds(-20);
        await db.SaveChangesAsync();

        await svc.SubmitAsync(
            userId,
            attempt.Id,
            new Dictionary<string, string?> { [questionId] = "five" },
            default);

        var savedAnswer = await db.ListeningAnswers.SingleAsync(a => a.ListeningAttemptId == attempt.Id);
        var submitted = await db.ListeningAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.False(savedAnswer.IsCorrect);
        Assert.Equal(0, savedAnswer.PointsEarned);
        Assert.Equal(ListeningAttemptStatus.Submitted, submitted.Status);
        Assert.Equal(0, submitted.RawScore);
    }

    [Fact]
    public async Task GetSession_DoesNotAutoResumeAttemptFromDifferentMode()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "home", default);

        var paperSession = await svc.GetSessionAsync(userId, paperId, "paper", attemptId: null, default);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(paperSession));

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("attempt").ValueKind);
        Assert.Equal("paper", doc.RootElement.GetProperty("modePolicy").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task StartAttemptAsync_ScopesInProgressReuseByPathwayStage()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "practice", default);
        await svc.StartAttemptAsync(userId, paperId, "practice", "foundation_partA", default);
        await svc.StartAttemptAsync(userId, paperId, "practice", "foundation_partA", default);

        var attempts = await db.ListeningAttempts
            .Where(a => a.UserId == userId && a.PaperId == paperId)
            .OrderBy(a => a.StartedAt)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);
        Assert.DoesNotContain("pathwayStage", attempts[0].ScopeJson);
        Assert.Contains("foundation_partA", attempts[1].ScopeJson);
    }

    [Fact]
    public async Task StartAttemptAsync_WhenForcedCreatesFreshAttemptInsteadOfReusingPrefilledAttempt()
    {
        var (db, svc) = Build();
        var (userId, paperId, questionId) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "practice", "foundation_partA", default);
        var prefilledAttemptId = await db.ListeningAttempts
            .Where(a => a.UserId == userId && a.PaperId == paperId)
            .Select(a => a.Id)
            .SingleAsync();
        await svc.SaveAnswerAsync(userId, prefilledAttemptId, questionId, new ListeningAnswerSaveRequest("five"), default);

        await svc.StartAttemptAsync(
            userId,
            paperId,
            "practice",
            "foundation_partA",
            forceNewAttempt: true,
            ct: CancellationToken.None);

        var attempts = await db.ListeningAttempts
            .Where(a => a.UserId == userId && a.PaperId == paperId)
            .ToListAsync();
        var freshAttempt = attempts.Single(a => a.Id != prefilledAttemptId);
        var freshAnswerCount = await db.ListeningAnswers.CountAsync(a => a.ListeningAttemptId == freshAttempt.Id);

        Assert.Equal(2, attempts.Count);
        Assert.Equal(0, freshAnswerCount);
    }

    [Fact]
    public async Task StartAttemptAsync_ReusesLegacyStageCodeScopeKey()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "lat-existing-legacy-stage-code",
            UserId = userId,
            PaperId = paperId,
            StartedAt = now.AddMinutes(-5),
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Learning,
            MaxRawScore = OetLearner.Api.Services.OetScoring.ListeningReadingRawMax,
            ScopeJson = """{"mode":"practice","sourceKind":"content_paper","stageCode":"foundation_partA"}""",
        });
        await db.SaveChangesAsync();

        await svc.StartAttemptAsync(userId, paperId, "practice", "foundation_partA", default);

        var attempts = await db.ListeningAttempts
            .Where(a => a.UserId == userId && a.PaperId == paperId)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal("lat-existing-legacy-stage-code", attempts.Single().Id);
    }

    [Fact]
    public async Task GetSessionAsync_ScopesImplicitInProgressResumeByPathwayStage()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "practice", default);
        await svc.StartAttemptAsync(userId, paperId, "practice", "foundation_partA", default);

        var attempts = await db.ListeningAttempts
            .Where(a => a.UserId == userId && a.PaperId == paperId)
            .OrderBy(a => a.StartedAt)
            .ToListAsync();

        var genericSession = await svc.GetSessionAsync(userId, paperId, "practice", attemptId: null, default);
        var scopedSession = await svc.GetSessionAsync(userId, paperId, "practice", attemptId: null, pathwayStage: "foundation_partA", default);

        using var genericDoc = JsonDocument.Parse(JsonSerializer.Serialize(genericSession));
        using var scopedDoc = JsonDocument.Parse(JsonSerializer.Serialize(scopedSession));

        Assert.Equal(attempts[0].Id, genericDoc.RootElement.GetProperty("attempt").GetProperty("attemptId").GetString());
        Assert.Equal(attempts[1].Id, scopedDoc.RootElement.GetProperty("attempt").GetProperty("attemptId").GetString());
    }

    [Fact]
    public async Task StartAttemptAsync_CreatesDiagnosticRelationalAttemptForPathwayDiagnosticLaunch()
    {
        var (db, svc) = Build();
        var (userId, paperId, _) = await SeedRelationalPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "diagnostic", "diagnostic", default);

        var attempt = await db.ListeningAttempts.SingleAsync(a => a.UserId == userId && a.PaperId == paperId);
        Assert.Equal(ListeningAttemptMode.Diagnostic, attempt.Mode);
        Assert.Contains("diagnostic", attempt.ScopeJson);
    }

    [Fact]
    public async Task IntegrityEvent_IsRecordedForJsonFallbackAttempts()
    {
        var (db, svc) = Build();
        var (userId, paperId) = await SeedJsonFallbackPaperAsync(db);

        await svc.StartAttemptAsync(userId, paperId, "home", default);
        var attempt = await db.Attempts.SingleAsync(a => a.UserId == userId && a.ContentId == paperId);

        await svc.RecordIntegrityEventAsync(userId, attempt.Id, new ListeningIntegrityEventRequest("window_blur", "test", DateTimeOffset.UtcNow), default);

        var audit = await db.AuditEvents.SingleAsync(e => e.Action == "ListeningIntegrityEvent");
        var updatedAttempt = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal("Attempt", audit.ResourceType);
        Assert.Equal(attempt.Id, audit.ResourceId);
        Assert.Contains("window_blur", audit.Details);
        Assert.NotNull(updatedAttempt.LastClientSyncAt);
    }

    [Fact]
    public async Task AdminAnalytics_CountsRelationalAttemptScoreOnce()
    {
        var (db, _) = Build();
        var (_, paperId, _) = await SeedRelationalPaperAsync(db);
        var now = DateTimeOffset.UtcNow;
        var attempt = new ListeningAttempt
        {
            Id = "rel-attempt-analytics",
            UserId = "learner-1",
            PaperId = paperId,
            StartedAt = now.AddMinutes(-30),
            SubmittedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Exam,
            RawScore = 34,
            ScaledScore = 420,
            MaxRawScore = 42,
            PolicySnapshotJson = "{}",
        };
        db.ListeningAttempts.Add(attempt);
        db.Evaluations.Add(new Evaluation
        {
            Id = "eval-rel-attempt-analytics",
            AttemptId = attempt.Id,
            SubtestCode = "listening",
            State = AsyncState.Completed,
            ScoreRange = "300",
            GradeRange = "C+",
            ConfidenceBand = ConfidenceBand.High,
            CriterionScoresJson = "[{\"scaledScore\":300}]",
            GeneratedAt = now,
            ModelExplanationSafe = "test",
            LearnerDisclaimer = "test",
            LastTransitionAt = now,
        });
        await db.SaveChangesAsync();

        var analytics = await new ListeningAnalyticsService(db).GetAdminAnalyticsAsync(30, default);

        Assert.Equal(1, analytics.CompletedAttempts);
        Assert.Equal(420, analytics.AverageScaledScore);
        Assert.Equal(100, analytics.PercentLikelyPassing);
    }
}
