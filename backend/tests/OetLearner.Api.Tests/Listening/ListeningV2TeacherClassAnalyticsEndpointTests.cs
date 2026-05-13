using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

public class ListeningV2TeacherClassAnalyticsEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ListeningV2TeacherClassAnalyticsEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Teacher_class_analytics_json_excludes_raw_wrong_answer_histograms_and_common_misspellings()
    {
        var teacherId = $"teacher-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var classId = $"class-{Guid.NewGuid():N}";
        var paperId = $"paper-{Guid.NewGuid():N}";
        var questionId = $"question-{Guid.NewGuid():N}";
        var attemptId = $"attempt-{Guid.NewGuid():N}";

        await _factory.EnsureLearnerProfileAsync(teacherId, $"{teacherId}@example.test", teacherId);
        await _factory.EnsureLearnerProfileAsync(learnerId, $"{learnerId}@example.test", learnerId);
        await SeedClassAnalyticsScenarioAsync(teacherId, learnerId, classId, paperId, questionId, attemptId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", teacherId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");

        var response = await client.GetAsync($"/v1/listening/v2/teacher/classes/{classId}/analytics?days=30");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("commonMisspellings", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrongAnswerHistogram", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patient.email@example.test", json, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json);
        var heat = document.RootElement
            .GetProperty("analytics")
            .GetProperty("distractorHeat");
        var item = Assert.Single(heat.EnumerateArray());
        Assert.Equal(1, item.GetProperty("wrongAnswerCount").GetInt32());
    }

    [Fact]
    public async Task Teacher_class_analytics_hides_class_existence_from_non_owner()
    {
        var ownerId = $"teacher-owner-{Guid.NewGuid():N}";
        var otherTeacherId = $"teacher-other-{Guid.NewGuid():N}";
        var learnerId = $"learner-{Guid.NewGuid():N}";
        var classId = $"class-{Guid.NewGuid():N}";
        var paperId = $"paper-{Guid.NewGuid():N}";
        var questionId = $"question-{Guid.NewGuid():N}";
        var attemptId = $"attempt-{Guid.NewGuid():N}";

        await _factory.EnsureLearnerProfileAsync(ownerId, $"{ownerId}@example.test", ownerId);
        await _factory.EnsureLearnerProfileAsync(otherTeacherId, $"{otherTeacherId}@example.test", otherTeacherId);
        await _factory.EnsureLearnerProfileAsync(learnerId, $"{learnerId}@example.test", learnerId);
        await SeedClassAnalyticsScenarioAsync(ownerId, learnerId, classId, paperId, questionId, attemptId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", otherTeacherId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");

        var response = await client.GetAsync($"/v1/listening/v2/teacher/classes/{classId}/analytics?days=30");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Endpoint Listening Class", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(classId, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Learner_role_cannot_create_teacher_class()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"learner-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.PostAsJsonAsync(
            "/v1/listening/v2/teacher/classes",
            new { name = "Unauthorized roster", description = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Add_teacher_class_member_rejects_null_member_user_id()
    {
        var teacherId = $"teacher-{Guid.NewGuid():N}";
        var classId = $"class-{Guid.NewGuid():N}";
        await SeedEmptyClassAsync(teacherId, classId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", teacherId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");

        var response = await client.PostAsJsonAsync(
            $"/v1/listening/v2/teacher/classes/{classId}/members",
            new { memberUserId = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SeedEmptyClassAsync(string teacherId, string classId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        db.TeacherClasses.Add(new TeacherClass
        {
            Id = classId,
            OwnerUserId = teacherId,
            Name = "Endpoint Listening Class",
            Description = null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedClassAnalyticsScenarioAsync(
        string teacherId,
        string learnerId,
        string classId,
        string paperId,
        string questionId,
        string attemptId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var partId = $"part-{Guid.NewGuid():N}";
        var extractId = $"extract-{Guid.NewGuid():N}";

        db.TeacherClasses.Add(new TeacherClass
        {
            Id = classId,
            OwnerUserId = teacherId,
            Name = "Endpoint Listening Class",
            Description = "Endpoint privacy test",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.TeacherClassMembers.Add(new TeacherClassMember
        {
            Id = Guid.NewGuid().ToString("N"),
            TeacherClassId = classId,
            UserId = learnerId,
            AddedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Endpoint Part B Paper",
            Slug = $"endpoint-part-b-{Guid.NewGuid():N}",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = partId,
            PaperId = paperId,
            PartCode = ListeningPartCode.B,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = extractId,
            ListeningPartId = partId,
            DisplayOrder = 1,
            Kind = ListeningExtractKind.Workplace,
            Title = "Workplace extract",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = questionId,
            PaperId = paperId,
            ListeningPartId = partId,
            ListeningExtractId = extractId,
            QuestionNumber = 8,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "What is the speaker's main concern?",
            CorrectAnswerJson = "\"B\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestionOptions.AddRange(
            new ListeningQuestionOption
            {
                Id = $"option-{Guid.NewGuid():N}",
                ListeningQuestionId = questionId,
                OptionKey = "A",
                DisplayOrder = 1,
                Text = "The appointment time",
                IsCorrect = false,
            },
            new ListeningQuestionOption
            {
                Id = $"option-{Guid.NewGuid():N}",
                ListeningQuestionId = questionId,
                OptionKey = "B",
                DisplayOrder = 2,
                Text = "The medication instruction",
                IsCorrect = true,
            },
            new ListeningQuestionOption
            {
                Id = $"option-{Guid.NewGuid():N}",
                ListeningQuestionId = questionId,
                OptionKey = "C",
                DisplayOrder = 3,
                Text = "The discharge date",
                IsCorrect = false,
            });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = attemptId,
            UserId = learnerId,
            PaperId = paperId,
            StartedAt = now.AddMinutes(-45),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Exam,
            RawScore = 0,
            ScaledScore = 200,
            MaxRawScore = 42,
            PolicySnapshotJson = "{}",
            LastQuestionVersionMapJson = "{}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = $"answer-{Guid.NewGuid():N}",
            ListeningAttemptId = attemptId,
            ListeningQuestionId = questionId,
            UserAnswerJson = "\"patient.email@example.test\"",
            IsCorrect = false,
            PointsEarned = 0,
            AnsweredAt = now,
        });

        await db.SaveChangesAsync();
    }
}
