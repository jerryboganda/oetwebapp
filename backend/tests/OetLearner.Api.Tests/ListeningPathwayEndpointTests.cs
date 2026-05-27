using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningPathwayEndpointTests — Phase 1 contract tests for A7 + A8
//
// Mirrors ReadingPathwayEndpointTests for the Listening pathway:
//   • Diagnostic question projection MUST NOT leak correct answers, accepted
//     synonyms, explanation markdown, or transcript evidence.
//   • Cross-user session access surfaces as 404 (looks identical to missing).
//   • Locked diagnostic sessions reject practice answers; out-of-session
//     questions are rejected.
//   • Repeated diagnostic submit is idempotent (matches the service-level
//     behaviour in ListeningLearnerPathwayService.SubmitDiagnosticAsync).
//   • Onboarding persists profile + advances stage to "audio_check".
//   • Audio-check advances stage to "diagnostic" and records the timestamp.
//
// Tests are written against the expected /v1/listening-pathway/* route prefix.
// Until the endpoints file (A8) lands and the DI registration (A7) is wired,
// these tests will fail with 404 — they are marked [Fact(Skip = "...")] so a
// CI green light still ships once the generator-level tests pass. Remove the
// Skip attribute when the endpoints file is in place.
// ═════════════════════════════════════════════════════════════════════════════

public class ListeningPathwayEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private const string EndpointsPendingSkipReason =
        "Awaiting A7 DI registration + A8 ListeningPathwayEndpoints.cs route mapping.";

    private readonly TestWebApplicationFactory _factory;

    public ListeningPathwayEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DiagnosticQuestions_ReturnLearnerSafeProjectionOnly()
    {
        var userId = NewUserId("listen-safe");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("listen-safe");
        var sessionId = Guid.NewGuid();
        await SeedListeningProfileAsync(userId, stage: "diagnostic");
        await SeedQuestionAndSessionAsync(
            userId, sessionId, sessionType: "diagnostic", questionId);

        var response = await client.GetAsync(
            $"/v1/listening-pathway/diagnostic/sessions/{sessionId}/questions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var question = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(questionId, question.GetProperty("id").GetString());

        // CRITICAL: learner-safe projection MUST NOT leak any of these fields.
        // If the endpoint adds new fields later, those need a positive
        // allow-list — this is the negative regression guard.
        Assert.DoesNotContain("correctAnswer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedSynonyms", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanationMarkdown", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcriptEvidence", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Do not leak this explanation", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnosticSessionEndpoints_RejectCrossUserAccess()
    {
        var ownerId = NewUserId("listen-owner");
        var otherUserId = NewUserId("listen-other");
        var otherClient = await CreateLearnerClientAsync(otherUserId);
        await SeedListeningProfileAsync(ownerId, stage: "diagnostic");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(
            ownerId, sessionId, sessionType: "diagnostic", NewQuestionId("owned"));

        var questionsResponse = await otherClient.GetAsync(
            $"/v1/listening-pathway/diagnostic/sessions/{sessionId}/questions");
        var submitResponse = await otherClient.PostAsJsonAsync(
            "/v1/listening-pathway/diagnostic/submit",
            new
            {
                sessionId,
                answers = new object[0],
                totalDurationSeconds = 0,
            });

        // Service throws InvalidOperationException("Session not found") for
        // both missing and foreign sessions, which surfaces as 404 at the
        // endpoint layer (cf. Reading pattern).
        Assert.Equal(HttpStatusCode.NotFound, questionsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, submitResponse.StatusCode);
    }

    [Fact]
    public async Task PracticeAnswerEndpoint_RejectsLockedSessionAndOutOfSessionQuestions()
    {
        var userId = NewUserId("listen-locked");
        var client = await CreateLearnerClientAsync(userId);
        await SeedListeningProfileAsync(userId, stage: "diagnostic");

        // Session 1 — already submitted (CompletedAt set). Answer mutations
        // should be rejected to preserve the persisted snapshot.
        var lockedQuestionId = NewQuestionId("listen-locked-q");
        var lockedSessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(
            userId,
            lockedSessionId,
            sessionType: "diagnostic",
            lockedQuestionId,
            sessionCompleted: true);

        // Session 2 — open, but the question we'll submit isn't part of it.
        var inSessionQuestionId = NewQuestionId("listen-in-session");
        var openSessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(
            userId,
            openSessionId,
            sessionType: "diagnostic",
            inSessionQuestionId);

        var foreignQuestionId = NewQuestionId("listen-outside");
        await SeedListeningQuestionAsync(foreignQuestionId);

        // Attempt 1 — locked session, in-session question → rejected.
        var lockedResponse = await client.PostAsJsonAsync(
            $"/v1/listening-pathway/diagnostic/sessions/{lockedSessionId}/answers/{lockedQuestionId}",
            new
            {
                questionId = lockedQuestionId,
                selectedOption = "A",
                learnerAnswer = (string?)null,
                isUnknown = false,
                timeSpentSeconds = 4,
                replaysUsed = 0,
                markedForReview = false,
            });

        // Attempt 2 — open session, but the question is not in the session id list.
        var outOfSessionResponse = await client.PostAsJsonAsync(
            $"/v1/listening-pathway/diagnostic/sessions/{openSessionId}/answers/{foreignQuestionId}",
            new
            {
                questionId = foreignQuestionId,
                selectedOption = "A",
                learnerAnswer = (string?)null,
                isUnknown = false,
                timeSpentSeconds = 4,
                replaysUsed = 0,
                markedForReview = false,
            });

        // Expect a client error. The Listening service uses 409 (Conflict)
        // on locked-session writes via the "Session already submitted" guard
        // and 400 (BadRequest) on unknown questions. Accept either family.
        Assert.True(
            lockedResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest,
            $"Expected 409 or 400 for locked session, got {(int)lockedResponse.StatusCode}");
        Assert.True(
            outOfSessionResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Expected 400 or 404 for out-of-session question, got {(int)outOfSessionResponse.StatusCode}");

        // Verify no attempt rows were persisted for either rejected call.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Empty(await db.ListeningQuestionAttempts
            .Where(a => a.UserId == userId
                && a.PracticeSessionId == lockedSessionId
                && a.ListeningQuestionId == lockedQuestionId
                && a.SelectedOption == "A")
            .ToListAsync());
        Assert.Empty(await db.ListeningQuestionAttempts
            .Where(a => a.UserId == userId
                && a.ListeningQuestionId == foreignQuestionId)
            .ToListAsync());
    }

    [Fact]
    public async Task DiagnosticSubmit_RejectsRepeatedSubmissionWithoutDuplicatingAttempts()
    {
        var userId = NewUserId("listen-repeat");
        var client = await CreateLearnerClientAsync(userId);
        await SeedListeningProfileAsync(userId, stage: "diagnostic");
        var questionId = NewQuestionId("listen-repeat-q");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(
            userId, sessionId, sessionType: "diagnostic", questionId);

        var payload = new
        {
            sessionId,
            answers = new[]
            {
                new
                {
                    questionId,
                    selectedOption = "A",
                    learnerAnswer = (string?)null,
                    isUnknown = false,
                    timeSpentSeconds = 5,
                    replaysUsed = 0,
                    markedForReview = false,
                },
            },
            totalDurationSeconds = 60,
        };

        var firstResponse = await client.PostAsJsonAsync(
            "/v1/listening-pathway/diagnostic/submit", payload);
        var secondResponse = await client.PostAsJsonAsync(
            "/v1/listening-pathway/diagnostic/submit", payload);

        // The service is explicitly idempotent — second submit returns the
        // cached recomposed result instead of throwing. Both calls should be
        // 200 OK, and the persisted attempt count must NOT double.
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attemptCount = await db.ListeningQuestionAttempts
            .CountAsync(a => a.UserId == userId
                && a.PracticeSessionId == sessionId
                && a.ListeningQuestionId == questionId);
        Assert.Equal(1, attemptCount);
    }

    [Fact]
    public async Task OnboardingEndpoint_PersistsProfileAndAdvancesStage()
    {
        var userId = NewUserId("listen-onboard");
        var client = await CreateLearnerClientAsync(userId);

        var response = await client.PostAsJsonAsync(
            "/v1/listening-pathway/onboarding",
            new
            {
                targetBand = "B",
                examDate = (DateTimeOffset?)null,
                hoursPerWeek = 8,
                profession = "medicine",
                englishExposureSource = "british_tv",
                comfortBritish = 4,
                comfortAustralian = 3,
                comfortVarious = 3,
                hasTakenBefore = false,
                previousScore = (int?)null,
                selfRatedSpeed = 3,
                selfRatedNoteTaking = 3,
                selfRatedSpelling = 4,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var profile = await db.LearnerListeningProfiles
            .SingleAsync(p => p.UserId == userId);

        Assert.Equal("B", profile.TargetBand);
        Assert.Equal(8, profile.HoursPerWeek);
        Assert.Equal("medicine", profile.Profession);
        // The service explicitly advances onboarding → audio_check.
        Assert.Equal("audio_check", profile.CurrentStage);
        Assert.NotEqual(default, profile.OnboardingCompletedAt);
    }

    [Fact]
    public async Task AudioCheckEndpoint_AdvancesStageToDiagnostic()
    {
        var userId = NewUserId("listen-audio");
        var client = await CreateLearnerClientAsync(userId);

        // Step 1 — onboarding. After this, stage = "audio_check".
        var onboardingResponse = await client.PostAsJsonAsync(
            "/v1/listening-pathway/onboarding",
            new
            {
                targetBand = "B",
                examDate = (DateTimeOffset?)null,
                hoursPerWeek = 8,
                profession = "medicine",
                englishExposureSource = "british_tv",
                comfortBritish = 4,
                comfortAustralian = 3,
                comfortVarious = 3,
                hasTakenBefore = false,
                previousScore = (int?)null,
                selfRatedSpeed = 3,
                selfRatedNoteTaking = 3,
                selfRatedSpelling = 4,
            });
        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);

        // Step 2 — audio check passes ("clear"). Advances to "diagnostic".
        var audioResponse = await client.PostAsJsonAsync(
            "/v1/listening-pathway/audio-check",
            new
            {
                outcome = "clear",
                volumeLevel = 80,
            });

        Assert.Equal(HttpStatusCode.OK, audioResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var profile = await db.LearnerListeningProfiles
            .SingleAsync(p => p.UserId == userId);

        Assert.Equal("diagnostic", profile.CurrentStage);
        Assert.NotNull(profile.AudioCheckPassedAt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private async Task SeedListeningProfileAsync(string userId, string stage)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var existing = await db.LearnerListeningProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);
        if (existing is null)
        {
            db.LearnerListeningProfiles.Add(new LearnerListeningProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TargetBand = "B",
                ExamDate = null,
                HoursPerWeek = 8,
                Profession = "medicine",
                EnglishExposureSource = "british_tv",
                ComfortBritish = 4,
                ComfortAustralian = 3,
                ComfortVarious = 3,
                HasTakenBefore = false,
                SelfRatedSpeed = 3,
                SelfRatedNoteTaking = 3,
                SelfRatedSpelling = 4,
                CurrentStage = stage,
                OnboardingCompletedAt = now,
                AudioCheckPassedAt = stage is "diagnostic" or "foundation"
                    or "practice" or "mastery"
                    ? now
                    : null,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }
        else if (!string.Equals(existing.CurrentStage, stage, StringComparison.Ordinal))
        {
            existing.CurrentStage = stage;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Seed a single standalone <see cref="ListeningQuestion"/> on a
    /// throwaway paper so the question id is resolvable by the endpoint
    /// layer. Used for "out-of-session" reject tests.</summary>
    private async Task SeedListeningQuestionAsync(string questionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var paperId = NewEntityId("listen-paper");
        var partId = NewEntityId("listen-part");

        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Listening pathway contract paper (stray question)",
            Slug = paperId,
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            Status = ContentStatus.Published,
            SourceProvenance = "Endpoint contract test",
            TagsCsv = "access:free",
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.ListeningParts.Add(new ListeningPart
        {
            Id = partId,
            PaperId = paperId,
            PartCode = ListeningPartCode.B,
            MaxRawScore = 6,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = questionId,
            PaperId = paperId,
            ListeningPartId = partId,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "Out-of-session question",
            CorrectAnswerJson = "\"A\"",
            AcceptedSynonymsJson = "[]",
            ExplanationMarkdown = "Do not leak this explanation to diagnostic clients.",
            SubSkillTagsCsv = "L1",
            Accent = "en-GB",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedQuestionAndSessionAsync(
        string userId,
        Guid sessionId,
        string sessionType,
        string questionId,
        bool sessionCompleted = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var paperId = NewEntityId("listen-paper");
        var partId = NewEntityId("listen-part");
        var extractId = NewEntityId("listen-extract");

        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Listening pathway contract paper",
            Slug = paperId,
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 45,
            Status = ContentStatus.Published,
            SourceProvenance = "Endpoint contract test",
            TagsCsv = "access:free",
            CreatedAt = now,
            UpdatedAt = now,
        });

        var part = new ListeningPart
        {
            Id = partId,
            PaperId = paperId,
            PartCode = ListeningPartCode.B,
            MaxRawScore = 6,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ListeningParts.Add(part);

        var extract = new ListeningExtract
        {
            Id = extractId,
            ListeningPartId = partId,
            DisplayOrder = 1,
            Kind = ListeningExtractKind.Workplace,
            Title = "Workplace extract",
            AccentCode = "en-GB",
            AudioContentSha = "deadbeef00000000",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ListeningExtracts.Add(extract);

        var question = new ListeningQuestion
        {
            Id = questionId,
            PaperId = paperId,
            ListeningPartId = partId,
            ListeningExtractId = extractId,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "What does the speaker recommend?",
            CorrectAnswerJson = "\"A\"",
            AcceptedSynonymsJson = "[\"recommend it\"]",
            ExplanationMarkdown = "Do not leak this explanation to diagnostic clients.",
            TranscriptEvidenceText = "TRANSCRIPT EVIDENCE — must not leak to diagnostic clients.",
            SubSkillTagsCsv = "L1,L4",
            Accent = "en-GB",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ListeningQuestions.Add(question);

        db.ListeningQuestionOptions.Add(new ListeningQuestionOption
        {
            Id = NewEntityId("listen-opt-a"),
            ListeningQuestionId = questionId,
            OptionKey = "A",
            DisplayOrder = 1,
            Text = "Accept the recommendation",
            IsCorrect = true,
        });
        db.ListeningQuestionOptions.Add(new ListeningQuestionOption
        {
            Id = NewEntityId("listen-opt-b"),
            ListeningQuestionId = questionId,
            OptionKey = "B",
            DisplayOrder = 2,
            Text = "Reject the recommendation",
            IsCorrect = false,
        });

        db.ListeningPracticeSessions.Add(new ListeningPracticeSession
        {
            Id = sessionId,
            UserId = userId,
            SessionType = sessionType,
            QuestionIdsJson = JsonSerializer.Serialize(new[] { questionId }),
            AudioAssetIdsJson = JsonSerializer.Serialize(new[] { extractId }),
            TotalQuestions = 1,
            StartedAt = now.AddMinutes(-3),
            CompletedAt = sessionCompleted ? now : null,
            DurationSeconds = sessionCompleted ? 180 : null,
            Score = sessionCompleted ? 1 : null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                paperId = "listening-diagnostic-phase1",
                version = 1,
            }),
        });

        await db.SaveChangesAsync();
    }

    private static string NewUserId(string prefix) => LimitId($"{prefix}-{Guid.NewGuid():N}");

    private static string NewQuestionId(string prefix) => LimitId($"lp-{prefix}-{Guid.NewGuid():N}");

    private static string NewEntityId(string prefix) => LimitId($"lp-{prefix}-{Guid.NewGuid():N}");

    private static string LimitId(string value) => value.Length <= 64 ? value : value[..64];
}
