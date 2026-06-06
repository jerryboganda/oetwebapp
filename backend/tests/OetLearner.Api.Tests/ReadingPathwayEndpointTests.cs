using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class ReadingPathwayEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ReadingPathwayEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DiagnosticQuestions_ReturnLearnerSafeProjectionOnly()
    {
        var userId = NewUserId("diagnostic-safe");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("safe");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, sessionId, "diagnostic", questionId);

        var response = await client.GetAsync($"/v1/reading-pathway/diagnostic/sessions/{sessionId}/questions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var question = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(questionId, question.GetProperty("id").GetString());
        Assert.Equal("B", question.GetProperty("partCode").GetString());
        Assert.Equal("MultipleChoice3", question.GetProperty("questionType").GetString());
        Assert.Equal("Clinical notice", question.GetProperty("textTitle").GetString());
        Assert.Contains("Review the policy notice", question.GetProperty("textHtml").GetString());
        Assert.Contains("Check the policy", body);

        Assert.DoesNotContain("correctAnswer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedSynonyms", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanationMarkdown", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Do not leak this explanation", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-OPTION", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnosticSessionEndpoints_RejectCrossUserAccess()
    {
        var ownerId = NewUserId("owner");
        var otherUserId = NewUserId("other");
        var otherClient = await CreateLearnerClientAsync(otherUserId);
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(ownerId, sessionId, "diagnostic", NewQuestionId("owned"));

        var questionsResponse = await otherClient.GetAsync($"/v1/reading-pathway/diagnostic/sessions/{sessionId}/questions");
        var submitResponse = await otherClient.PostAsJsonAsync(
            "/v1/reading-pathway/diagnostic/submit",
            new { sessionId, answers = new Dictionary<string, string>() });

        Assert.Equal(HttpStatusCode.NotFound, questionsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, submitResponse.StatusCode);
    }

    [Fact]
    public async Task PracticeAnswerEndpoint_RejectsLockedSessionTypesAndOutOfSessionQuestions()
    {
        var userId = NewUserId("locked-answer");
        var client = await CreateLearnerClientAsync(userId);
        var diagnosticSessionId = Guid.NewGuid();
        var drillSessionId = Guid.NewGuid();
        var inSessionQuestionId = NewQuestionId("in-session");

        await SeedQuestionAndSessionAsync(userId, diagnosticSessionId, "diagnostic", inSessionQuestionId);
        await SeedSessionAsync(userId, drillSessionId, "drill", [inSessionQuestionId]);

        var diagnosticAnswerResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{diagnosticSessionId}/answers",
            new { questionId = inSessionQuestionId, selectedOption = "A", timeSpentSeconds = 4 });
        var outOfSessionResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{drillSessionId}/answers",
            new { questionId = NewQuestionId("outside"), selectedOption = "A", timeSpentSeconds = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, diagnosticAnswerResponse.StatusCode);
        Assert.Contains("session_answers_locked", await diagnosticAnswerResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, outOfSessionResponse.StatusCode);
        Assert.Contains("question_not_in_session", await outOfSessionResponse.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Empty(await db.ReadingQuestionAttempts.Where(a => a.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task DiagnosticSubmit_RejectsRepeatedSubmissionWithoutDuplicatingAttempts()
    {
        var userId = NewUserId("repeat-submit");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("repeat");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, sessionId, "diagnostic", questionId, withReadingProfile: true);

        var payload = new { sessionId, answers = new Dictionary<string, string> { [questionId] = "A" } };
        var firstResponse = await client.PostAsJsonAsync("/v1/reading-pathway/diagnostic/submit", payload);
        var secondResponse = await client.PostAsJsonAsync("/v1/reading-pathway/diagnostic/submit", payload);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        Assert.Contains("diagnostic_already_submitted", await secondResponse.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await db.ReadingQuestionAttempts.CountAsync(a => a.UserId == userId && a.PracticeSessionId == sessionId));
    }

    [Fact]
    public async Task PracticeQuestions_ReturnLearnerSafeProjectionOnly()
    {
        var userId = NewUserId("practice-safe");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("practice-safe");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, sessionId, "drill", questionId);

        var response = await client.GetAsync($"/v1/reading-pathway/practice/sessions/{sessionId}/questions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var question = Assert.Single(json.RootElement.GetProperty("questions").EnumerateArray());
        var passage = Assert.Single(json.RootElement.GetProperty("passages").EnumerateArray());
        Assert.Equal(questionId, question.GetProperty("id").GetString());
        Assert.Equal("Clinical notice", passage.GetProperty("title").GetString());
        Assert.Contains("Check the policy", body);

        Assert.DoesNotContain("correctAnswer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptedSynonyms", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanationMarkdown", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Do not leak this explanation", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-OPTION", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongReviewSession_UsesResolvableReadingQuestionIds()
    {
        var userId = NewUserId("wrong-review");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("wrong-review");
        var drillSessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, drillSessionId, "drill", questionId);

        var answerResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{drillSessionId}/answers",
            new { questionId, selectedOption = "B", timeSpentSeconds = 4 });

        Assert.Equal(HttpStatusCode.OK, answerResponse.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var entry = await db.ReadingErrorBankEntries.SingleAsync(e => e.UserId == userId && e.ReadingQuestionId == questionId);
            Assert.False(entry.IsResolved);
        }

        var startResponse = await client.PostAsJsonAsync(
            "/v1/reading-pathway/practice/sessions",
            new { sessionType = "wrong_review", targetMinutes = 10 });

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var startJson = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var sessionId = startJson.RootElement.GetProperty("sessionId").GetGuid();

        var questionsResponse = await client.GetAsync($"/v1/reading-pathway/practice/sessions/{sessionId}/questions");

        Assert.Equal(HttpStatusCode.OK, questionsResponse.StatusCode);
        using var questionsJson = JsonDocument.Parse(await questionsResponse.Content.ReadAsStringAsync());
        var question = Assert.Single(questionsJson.RootElement.GetProperty("questions").EnumerateArray());
        Assert.Equal(questionId, question.GetProperty("id").GetString());
    }

    [Fact]
    public async Task DrillSelection_ExcludesRecentlySeenStringQuestionIds()
    {
        var userId = NewUserId("recent-drill");
        var client = await CreateLearnerClientAsync(userId);
        var seenQuestionId = NewQuestionId("recent-seen");
        var freshQuestionId = NewQuestionId("recent-fresh");
        var drillSessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, drillSessionId, "drill", seenQuestionId, skillTag: "S8");
        await SeedQuestionAndSessionAsync(userId, Guid.NewGuid(), "drill", freshQuestionId, skillTag: "S8");

        var answerResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{drillSessionId}/answers",
            new { questionId = seenQuestionId, selectedOption = "A", timeSpentSeconds = 4 });

        Assert.Equal(HttpStatusCode.OK, answerResponse.StatusCode);

        var startResponse = await client.PostAsJsonAsync(
            "/v1/reading-pathway/practice/sessions",
            new { sessionType = "drill", focusSkill = "S8", targetMinutes = 10 });

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var startJson = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var sessionId = startJson.RootElement.GetProperty("sessionId").GetGuid();

        var questionsResponse = await client.GetAsync($"/v1/reading-pathway/practice/sessions/{sessionId}/questions");

        Assert.Equal(HttpStatusCode.OK, questionsResponse.StatusCode);
        using var questionsJson = JsonDocument.Parse(await questionsResponse.Content.ReadAsStringAsync());
        var question = Assert.Single(questionsJson.RootElement.GetProperty("questions").EnumerateArray());
        Assert.Equal(freshQuestionId, question.GetProperty("id").GetString());
    }

    [Fact]
    public async Task PracticeAnswerEndpoint_ReturnsPersistedResultAndRejectsChangedReplay()
    {
        var userId = NewUserId("answer-replay");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("answer-replay");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, sessionId, "drill", questionId);

        var firstResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{sessionId}/answers",
            new { questionId, selectedOption = "A", timeSpentSeconds = 4 });
        var sameAnswerResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{sessionId}/answers",
            new { questionId, selectedOption = "A", timeSpentSeconds = 7 });
        var changedAnswerResponse = await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{sessionId}/answers",
            new { questionId, selectedOption = "B", timeSpentSeconds = 8 });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sameAnswerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changedAnswerResponse.StatusCode);
        Assert.Contains("answer_already_submitted", await changedAnswerResponse.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempts = await db.ReadingQuestionAttempts.Where(a => a.UserId == userId && a.PracticeSessionId == sessionId).ToListAsync();
        var attempt = Assert.Single(attempts);
        Assert.Equal("A", attempt.SelectedOption);
    }

    [Fact]
    public async Task ExplanationEndpoint_IsDisabledToAvoidAnswerKeyLeakage()
    {
        var userId = NewUserId("explanation-disabled");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("explanation-disabled");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, sessionId, "diagnostic", questionId);

        var response = await client.GetAsync($"/v1/reading-pathway/questions/{questionId}/explanation?wrongOption=B");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("explanation_route_disabled", body);
        Assert.DoesNotContain("Check the policy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Do not leak this explanation", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PracticeSubmit_IsIdempotentForCompletedSessions()
    {
        var userId = NewUserId("submit-idempotent");
        var client = await CreateLearnerClientAsync(userId);
        var questionId = NewQuestionId("submit-idempotent");
        var sessionId = Guid.NewGuid();
        await SeedQuestionAndSessionAsync(userId, sessionId, "drill", questionId);

        await client.PostAsJsonAsync(
            $"/v1/reading-pathway/practice/sessions/{sessionId}/answers",
            new { questionId, selectedOption = "A", timeSpentSeconds = 4 });

        var firstSubmit = await client.PostAsync($"/v1/reading-pathway/practice/sessions/{sessionId}/submit", null);
        var secondSubmit = await client.PostAsync($"/v1/reading-pathway/practice/sessions/{sessionId}/submit", null);

        Assert.Equal(HttpStatusCode.OK, firstSubmit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondSubmit.StatusCode);
        var firstBody = await firstSubmit.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await secondSubmit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstBody.GetProperty("score").GetInt32(), secondBody.GetProperty("score").GetInt32());
        Assert.Equal(firstBody.GetProperty("totalQuestions").GetInt32(), secondBody.GetProperty("totalQuestions").GetInt32());
    }

    [Fact]
    public async Task MockResults_RequireCompletedMockAndDoNotReturnAttempts()
    {
        var userId = NewUserId("mock-results");
        var client = await CreateLearnerClientAsync(userId);
        var completedSessionId = Guid.NewGuid();
        var unfinishedSessionId = Guid.NewGuid();
        await SeedMockSessionAsync(userId, completedSessionId, completed: true);
        await SeedMockSessionAsync(userId, unfinishedSessionId, completed: false);

        var unfinishedResponse = await client.GetAsync($"/v1/reading-pathway/mocks/sessions/{unfinishedSessionId}/results");
        var completedResponse = await client.GetAsync($"/v1/reading-pathway/mocks/sessions/{completedSessionId}/results");

        Assert.Equal(HttpStatusCode.NotFound, unfinishedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        var body = await completedResponse.Content.ReadAsStringAsync();
        Assert.Contains("scaledScore", body);
        Assert.DoesNotContain("attempts", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selectedOption", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lessons_ReturnLessonAndProgressWrappers()
    {
        var userId = NewUserId("lesson-wrapper");
        var client = await CreateLearnerClientAsync(userId);
        var lessonId = Guid.NewGuid();
        var slug = $"pathway-contract-{Guid.NewGuid():N}";
        await SeedLessonAsync(userId, lessonId, slug);

        var response = await client.GetAsync("/v1/reading-pathway/lessons");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var wrapper = json.RootElement.EnumerateArray()
            .Single(element => element.GetProperty("lesson").GetProperty("slug").GetString() == slug);
        Assert.Equal(lessonId, wrapper.GetProperty("lesson").GetProperty("id").GetGuid());
        Assert.Equal(lessonId, wrapper.GetProperty("progress").GetProperty("lessonId").GetGuid());
        Assert.True(wrapper.GetProperty("progress").GetProperty("bodyRead").GetBoolean());
    }

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

    private async Task SeedQuestionAndSessionAsync(
        string userId,
        Guid sessionId,
        string sessionType,
        string questionId,
        string skillTag = "S2",
        bool withReadingProfile = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var paperId = NewEntityId("paper");
        var partId = NewEntityId("part");
        var textId = NewEntityId("text");

        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "reading",
            Title = "Reading pathway contract paper",
            Slug = paperId,
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = "Endpoint contract test",
            TagsCsv = "access:free",
            CreatedAt = now,
            UpdatedAt = now
        });

        var part = new ReadingPart
        {
            Id = partId,
            PaperId = paperId,
            PartCode = ReadingPartCode.B,
            TimeLimitMinutes = 45,
            MaxRawScore = 6,
            CreatedAt = now,
            UpdatedAt = now
        };
        var text = new ReadingText
        {
            Id = textId,
            ReadingPartId = partId,
            DisplayOrder = 1,
            Title = "Clinical notice",
            Source = "Test",
            BodyHtml = "<p>Review the policy notice before answering.</p>",
            WordCount = 7,
            CreatedAt = now,
            UpdatedAt = now,
            Part = part
        };
        var question = new ReadingQuestion
        {
            Id = questionId,
            ReadingPartId = partId,
            ReadingTextId = textId,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "What should the nurse do first?",
            OptionsJson = """
                                [
                                    {"value":"A","label":"Check the policy","correctAnswer":"SECRET-OPTION","acceptedSynonyms":["SECRET-OPTION-SYNONYM"],"explanationMarkdown":"SECRET-OPTION-EXPLANATION","isCorrect":true},
                                    {"value":"B","label":"Call reception"},
                                    {"value":"C","label":"Ignore the update"}
                                ]
                                """,
            CorrectAnswerJson = "\"A\"",
            AcceptedSynonymsJson = "[\"policy check\"]",
            ExplanationMarkdown = "Do not leak this explanation to diagnostic clients.",
            SkillTag = skillTag,
            ReviewState = ReadingReviewState.Published,
            CreatedAt = now,
            UpdatedAt = now,
            Part = part,
            Text = text
        };

        db.ReadingParts.Add(part);
        db.ReadingTexts.Add(text);
        db.ReadingQuestions.Add(question);

        if (withReadingProfile)
        {
            db.LearnerReadingProfiles.Add(new LearnerReadingProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TargetBand = "B",
                ExamDate = now.AddDays(90),
                HoursPerWeek = 6,
                Profession = "medicine",
                HasTakenBefore = false,
                SelfRatedSpeed = 3,
                SelfRatedVocabulary = 3,
                CurrentStage = "diagnostic",
                OnboardingCompletedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        await SeedSessionAsync(userId, sessionId, sessionType, [questionId], skillTag);
    }

    private async Task SeedSessionAsync(
        string userId,
        Guid sessionId,
        string sessionType,
        IReadOnlyCollection<string> questionIds,
        string skillTag = "S2")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var skillMap = questionIds.ToDictionary(id => id, _ => skillTag);
        db.ReadingPracticeSessions.Add(new ReadingPracticeSession
        {
            Id = sessionId,
            UserId = userId,
            SessionType = sessionType,
            FocusSkill = sessionType == "drill" ? skillTag : null,
            QuestionIdsJson = JsonSerializer.Serialize(questionIds),
            TotalQuestions = questionIds.Count,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            MetadataJson = JsonSerializer.Serialize(new { questionSkillMap = skillMap })
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedLessonAsync(string userId, Guid lessonId, string slug)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.ReadingLessons.Add(new ReadingLesson
        {
            Id = lessonId,
            Slug = slug,
            Title = "Distractor discipline",
            SkillCode = "S2",
            OrderIndex = 99,
            EstimatedMinutes = 12,
            BodyMarkdownEn = "Read carefully.",
            IsPublished = true
        });
        db.LearnerLessonProgresses.Add(new LearnerLessonProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LessonId = lessonId,
            BodyRead = true,
            QuizScore = 4,
            QuizAttempts = 1
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedMockSessionAsync(string userId, Guid sessionId, bool completed)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.ReadingPracticeSessions.Add(new ReadingPracticeSession
        {
            Id = sessionId,
            UserId = userId,
            SessionType = "mock",
            QuestionIdsJson = JsonSerializer.Serialize(new[] { Guid.NewGuid() }),
            TotalQuestions = 42,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = completed ? DateTimeOffset.UtcNow : null,
            DurationSeconds = completed ? 3600 : null,
            Score = completed ? 31 : null
        });
        db.ReadingQuestionAttempts.Add(new ReadingQuestionAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ReadingQuestionId = Guid.NewGuid(),
            PracticeSessionId = sessionId,
            SelectedOption = "A",
            IsCorrect = true,
            TimeSpentSeconds = 3,
            AttemptedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static string NewUserId(string prefix) => LimitId($"{prefix}-{Guid.NewGuid():N}");

    private static string NewQuestionId(string prefix) => LimitId($"rp-{prefix}-{Guid.NewGuid():N}");

    private static string NewEntityId(string prefix) => LimitId($"rp-{prefix}-{Guid.NewGuid():N}");

    private static string LimitId(string value) => value.Length <= 64 ? value : value[..64];
}