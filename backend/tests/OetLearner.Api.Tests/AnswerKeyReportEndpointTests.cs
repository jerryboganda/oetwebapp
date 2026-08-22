using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class AnswerKeyReportEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AnswerKeyReportEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reading_CreateReport_RequiresSubmittedOwnAttempt_AndWritesAudit()
    {
        var seed = await SeedReadingAsync();
        using var client = CreateLearnerClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "wrong_official_answer", details = "Key should be B." });

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(seed.QuestionId, json.RootElement.GetProperty("questionId").GetString());
        Assert.Equal("wrong_official_answer", json.RootElement.GetProperty("reasonCode").GetString());
        Assert.Equal("open", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Key should be B.", json.RootElement.GetProperty("details").GetString());
        var reportId = json.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reportId));

        var list = await client.GetAsync($"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports");
        list.EnsureSuccessStatusCode();
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listed.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(reportId, listed.RootElement.GetProperty("items")[0].GetProperty("id").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var audit = await db.AuditEvents.AsNoTracking()
            .Where(x => x.ResourceType == "AnswerKeyReport" && x.ResourceId == reportId)
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("AnswerKeyReport.Created", audit!.Action);
    }

    [Fact]
    public async Task Reading_CreateReport_RejectsDuplicatePending()
    {
        var seed = await SeedReadingAsync();
        using var client = CreateLearnerClient(seed.UserId);
        var first = await client.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "missing_accepted_variant" });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "other", details = "still wrong" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var error = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("answer_key_report_already_open", error.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reading_CreateReport_HidesOtherUsersAttempt()
    {
        var seed = await SeedReadingAsync();
        var otherId = $"akr-other-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(otherId, $"{otherId}@example.test", "Other Learner");
        using var client = CreateLearnerClient(otherId);

        var response = await client.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "wrong_official_answer" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_CreateReport_RejectsInProgressAttempt()
    {
        var seed = await SeedReadingAsync(submitted: false);
        using var client = CreateLearnerClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "wrong_official_answer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("answer_key_report_unavailable", error.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reading_CreateReport_RejectsUnknownQuestion()
    {
        var seed = await SeedReadingAsync();
        using var client = CreateLearnerClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = "missing-question", reasonCode = "wrong_official_answer" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listening_CreateReport_WorksOnSubmittedAttempt()
    {
        var seed = await SeedListeningAsync();
        using var client = CreateLearnerClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/v1/listening-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "missing_accepted_variant", details = "Accept US spelling." });

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("listening", json.RootElement.GetProperty("assessment").GetString());
        Assert.Equal(seed.QuestionId, json.RootElement.GetProperty("questionId").GetString());
        Assert.Equal("open", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Admin_ListAndUpdate_RequiresPermissions_AndNeverLeaksEmail()
    {
        var seed = await SeedReadingAsync();
        using var learner = CreateLearnerClient(seed.UserId);
        var created = await learner.PostAsJsonAsync(
            $"/v1/reading-papers/attempts/{seed.AttemptId}/answer-reports",
            new { questionId = seed.QuestionId, reasonCode = "wrong_official_answer", details = "Printed key is B." });
        created.EnsureSuccessStatusCode();
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var reportId = createdJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reportId));

        using var reader = CreateAdminClient(AdminPermissions.ContentRead);
        var list = await reader.GetAsync("/v1/admin/answer-key-reports?status=open&assessment=reading&limit=50");
        list.EnsureSuccessStatusCode();
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.test", listBody, StringComparison.OrdinalIgnoreCase);
        using var listed = JsonDocument.Parse(listBody);
        var ours = listed.RootElement.GetProperty("items").EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == reportId);
        Assert.Equal("Reading Answer Report Paper", ours.GetProperty("paperTitle").GetString());
        Assert.Equal(seed.UserId, ours.GetProperty("reportedByUserId").GetString());
        Assert.Equal("Answer Report Learner", ours.GetProperty("reportedByUserDisplayName").GetString());
        Assert.False(ours.TryGetProperty("reportedByUserEmail", out _));
        Assert.Equal($"/admin/content/reading/{seed.PaperId}/questions", ours.GetProperty("editorUrl").GetString());
        Assert.Equal("/admin/content/scoring-system", ours.GetProperty("scoringSystemUrl").GetString());
        Assert.Contains("\"B\"", ours.GetProperty("learnerAnswerSnapshot").GetString());
        Assert.Contains("\"A\"", ours.GetProperty("officialAnswerSnapshot").GetString());

        using var writerDenied = CreateAdminClient(AdminPermissions.ContentRead);
        var denied = await writerDenied.PatchAsJsonAsync($"/v1/admin/answer-key-reports/{reportId}", new
        {
            status = "investigating"
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var writer = CreateAdminClient(AdminPermissions.ContentRead, AdminPermissions.ContentWrite);
        var updated = await writer.PatchAsJsonAsync($"/v1/admin/answer-key-reports/{reportId}", new
        {
            status = "resolved",
            resolutionNote = "Key confirmed against the booklet."
        });
        updated.EnsureSuccessStatusCode();
        using var updatedJson = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal("resolved", updatedJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("Key confirmed against the booklet.", updatedJson.RootElement.GetProperty("resolutionNote").GetString());

        var locked = await writer.PatchAsJsonAsync($"/v1/admin/answer-key-reports/{reportId}", new
        {
            status = "open"
        });
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        using var lockedJson = JsonDocument.Parse(await locked.Content.ReadAsStringAsync());
        Assert.Equal("answer_key_report_status_locked", lockedJson.RootElement.GetProperty("code").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var audit = await db.AuditEvents.AsNoTracking()
            .Where(x => x.ResourceType == "AnswerKeyReport" && x.ResourceId == reportId && x.Action == "AnswerKeyReport.Updated")
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
    }

    private HttpClient CreateLearnerClient(string learnerId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{learnerId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", learnerId);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");
        return client;
    }

    private HttpClient CreateAdminClient(params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", string.Join(',', permissions));
        return client;
    }

    private async Task<SeededAttempt> SeedReadingAsync(bool submitted = true)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"akr-read-{suffix}";
        var paperId = $"akr-read-paper-{suffix}";
        var partId = $"akr-read-part-{suffix}";
        var questionId = $"akr-read-q-{suffix}";
        var attemptId = $"akr-read-att-{suffix}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Answer Report Learner");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "reading",
            Title = "Reading Answer Report Paper",
            Slug = paperId,
            Status = ContentStatus.Published,
            SourceProvenance = "Test-authored answer-key report paper.",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ReadingParts.Add(new ReadingPart
        {
            Id = partId,
            PaperId = paperId,
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ReadingQuestions.Add(new ReadingQuestion
        {
            Id = questionId,
            ReadingPartId = partId,
            DisplayOrder = 1,
            QuestionType = ReadingQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            OptionsJson = "[\"A\",\"B\",\"C\"]",
            CorrectAnswerJson = "\"A\"",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = attemptId,
            UserId = userId,
            PaperId = paperId,
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = submitted ? now : null,
            Status = submitted ? ReadingAttemptStatus.Submitted : ReadingAttemptStatus.InProgress,
            MaxRawScore = 1,
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = $"akr-read-ans-{suffix}",
            ReadingAttemptId = attemptId,
            ReadingQuestionId = questionId,
            UserAnswerJson = "\"B\"",
            AnsweredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return new SeededAttempt(userId, paperId, questionId, attemptId);
    }

    private async Task<SeededAttempt> SeedListeningAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"akr-list-{suffix}";
        var paperId = $"akr-list-paper-{suffix}";
        var partId = $"akr-list-part-{suffix}";
        var questionId = $"akr-list-q-{suffix}";
        var attemptId = $"akr-list-att-{suffix}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", "Answer Report Learner");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "listening",
            Title = "Listening Answer Report Paper",
            Slug = paperId,
            Status = ContentStatus.Published,
            SourceProvenance = "Test-authored answer-key report paper.",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = partId,
            PaperId = paperId,
            PartCode = ListeningPartCode.B1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = questionId,
            PaperId = paperId,
            ListeningPartId = partId,
            QuestionNumber = 25,
            DisplayOrder = 1,
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Stem = "Which option is supported?",
            CorrectAnswerJson = "\"A\"",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = attemptId,
            UserId = userId,
            PaperId = paperId,
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ListeningAttemptStatus.Submitted,
            MaxRawScore = 1,
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = $"akr-list-ans-{suffix}",
            ListeningAttemptId = attemptId,
            ListeningQuestionId = questionId,
            UserAnswerJson = "\"B\"",
            AnsweredAt = now,
        });
        await db.SaveChangesAsync();
        return new SeededAttempt(userId, paperId, questionId, attemptId);
    }

    private sealed record SeededAttempt(string UserId, string PaperId, string QuestionId, string AttemptId);
}
