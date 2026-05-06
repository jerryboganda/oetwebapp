using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md - tutor calibration drift +
// inline transcript comments. Covers:
//   - Admin samples CRUD round-trip with rubric validation
//   - Tutor submission writes one row per (sample, tutor) and recomputes
//     totalAbsoluteError on re-submit
//   - Drift report aggregates per-tutor MAE
//   - Inline comment endpoint enforces expert assignment
//   - Learner can read their own attempt's comments; another user gets 403
[Collection("AuthFlows")]
public class SpeakingCalibrationFlowsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SpeakingCalibrationFlowsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static object Rubric(int linguistic, int clinical) => new
    {
        intelligibility = linguistic,
        fluency = linguistic,
        appropriateness = linguistic,
        grammarExpression = linguistic,
        relationshipBuilding = clinical,
        patientPerspective = clinical,
        structure = clinical,
        informationGathering = clinical,
        informationGiving = clinical,
    };

    private async Task<string> CreateAndPublishSampleAsync(HttpClient adminClient, string title)
    {
        var create = await adminClient.PostAsJsonAsync("/v1/admin/speaking/calibration/samples", new
        {
            title,
            sourceAttemptId = "sa-001",
            goldScores = Rubric(linguistic: 5, clinical: 2),
            description = "Wave 4 test sample",
        });
        create.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("sampleId").GetString()!;

        var publish = await adminClient.PostAsync($"/v1/admin/speaking/calibration/samples/{id}/publish", null);
        publish.EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task Admin_Sample_Create_Update_Publish_Archive_RoundTrips()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
        var sampleId = await CreateAndPublishSampleAsync(admin, "Admin CRUD calibration sample");

        var get = await admin.GetAsync($"/v1/admin/speaking/calibration/samples/{sampleId}");
        get.EnsureSuccessStatusCode();
        using (var detail = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
        {
            Assert.Equal("published", detail.RootElement.GetProperty("status").GetString());
            Assert.Equal(5, detail.RootElement.GetProperty("goldScores").GetProperty("intelligibility").GetInt32());
        }

        var update = await admin.PutAsJsonAsync($"/v1/admin/speaking/calibration/samples/{sampleId}", new
        {
            title = "Admin CRUD calibration sample v2",
            goldScores = Rubric(linguistic: 4, clinical: 3),
        });
        update.EnsureSuccessStatusCode();

        var archive = await admin.PostAsync($"/v1/admin/speaking/calibration/samples/{sampleId}/archive", null);
        archive.EnsureSuccessStatusCode();
        using var archivedDoc = JsonDocument.Parse(await archive.Content.ReadAsStringAsync());
        Assert.Equal("archived", archivedDoc.RootElement.GetProperty("status").GetString());

        // Update on archived → 409
        var updateArchived = await admin.PutAsJsonAsync($"/v1/admin/speaking/calibration/samples/{sampleId}", new
        {
            title = "should fail",
        });
        Assert.Equal(HttpStatusCode.Conflict, updateArchived.StatusCode);
    }

    [Fact]
    public async Task Admin_Sample_Create_RejectsOutOfRangeRubric()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
        var resp = await admin.PostAsJsonAsync("/v1/admin/speaking/calibration/samples", new
        {
            title = "Bad rubric",
            sourceAttemptId = "sa-001",
            // intelligibility max is 6 — 9 must be rejected.
            goldScores = new
            {
                intelligibility = 9,
                fluency = 5,
                appropriateness = 5,
                grammarExpression = 5,
                relationshipBuilding = 2,
                patientPerspective = 2,
                structure = 2,
                informationGathering = 2,
                informationGiving = 2,
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_Sample_Create_RejectsUnknownAttempt()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
        var resp = await admin.PostAsJsonAsync("/v1/admin/speaking/calibration/samples", new
        {
            title = "Bad source",
            sourceAttemptId = "does-not-exist",
            goldScores = Rubric(5, 2),
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Tutor_Submit_ComputesDrift_AndReSubmitUpdatesInPlace()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
        var sampleId = await CreateAndPublishSampleAsync(admin, "Tutor drift round-trip");

        using var tutor = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        // Tutor's first attempt: every score off by 1 across all 9 criteria → totalAbs = 9.
        var firstSubmit = await tutor.PostAsJsonAsync($"/v1/expert/calibration/speaking/samples/{sampleId}/scores", new
        {
            scores = Rubric(linguistic: 4, clinical: 1),
        });
        firstSubmit.EnsureSuccessStatusCode();
        using (var doc = JsonDocument.Parse(await firstSubmit.Content.ReadAsStringAsync()))
        {
            var perCriterion = doc.RootElement.GetProperty("perCriterionDelta");
            Assert.Equal(-1, perCriterion.GetProperty("intelligibility").GetInt32());
            Assert.Equal(-1, perCriterion.GetProperty("informationGiving").GetInt32());
        }

        // Re-submit with perfect scores — same row, totalAbs = 0.
        var resubmit = await tutor.PostAsJsonAsync($"/v1/expert/calibration/speaking/samples/{sampleId}/scores", new
        {
            scores = Rubric(linguistic: 5, clinical: 2),
        });
        resubmit.EnsureSuccessStatusCode();

        // Verify exactly one score row per (sample, tutor) — the re-submit
        // must update in place rather than insert.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rows = await db.SpeakingCalibrationScores
            .AsNoTracking()
            .Where(s => s.SampleId == sampleId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal(0, rows[0].TotalAbsoluteError);

        // Drift report should now include this tutor with MAE = 0.
        var drift = await admin.GetAsync("/v1/admin/speaking/calibration/drift?minSubmissions=1");
        drift.EnsureSuccessStatusCode();
        using var driftDoc = JsonDocument.Parse(await drift.Content.ReadAsStringAsync());
        var tutors = driftDoc.RootElement.GetProperty("tutors");
        var ourTutor = Enumerable.Range(0, tutors.GetArrayLength())
            .Select(i => tutors[i])
            .FirstOrDefault(t => t.GetProperty("tutorId").GetString() == rows[0].TutorId);
        Assert.NotEqual(default, ourTutor);
        Assert.Equal(0d, ourTutor.GetProperty("meanAbsoluteError").GetDouble());
    }

    [Fact]
    public async Task Tutor_Submit_RejectsUnpublishedSample()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
        var create = await admin.PostAsJsonAsync("/v1/admin/speaking/calibration/samples", new
        {
            title = "Draft only",
            sourceAttemptId = "sa-001",
            goldScores = Rubric(5, 2),
        });
        create.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var sampleId = createDoc.RootElement.GetProperty("sampleId").GetString()!;

        using var tutor = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");
        var resp = await tutor.PostAsJsonAsync($"/v1/expert/calibration/speaking/samples/{sampleId}/scores", new
        {
            scores = Rubric(5, 2),
        });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task ExpertComment_RequiresAssignment_AndLearnerCanRead()
    {
        // Seed an ExpertReviewAssignment + ReviewRequest so the expert is
        // authorised to comment on sa-001.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var expertId = await db.ExpertUsers
                .Where(u => u.Email == SeedData.ExpertEmail)
                .Select(u => u.Id).FirstAsync();

            var existing = await db.ReviewRequests
                .FirstOrDefaultAsync(r => r.AttemptId == "sa-001" && r.SubtestCode == "speaking");
            ReviewRequest reviewRequest;
            if (existing is null)
            {
                reviewRequest = new ReviewRequest
                {
                    Id = $"rr-cal-{Guid.NewGuid():N}".Substring(0, 32),
                    AttemptId = "sa-001",
                    SubtestCode = "speaking",
                    State = ReviewRequestState.Submitted,
                    TurnaroundOption = "standard",
                    PaymentSource = "credit",
                    PriceSnapshot = 0m,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ReviewRequests.Add(reviewRequest);
            }
            else
            {
                reviewRequest = existing;
            }

            var hasAssignment = await db.ExpertReviewAssignments
                .AnyAsync(a => a.ReviewRequestId == reviewRequest.Id && a.AssignedReviewerId == expertId);
            if (!hasAssignment)
            {
                db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
                {
                    Id = $"era-cal-{Guid.NewGuid():N}".Substring(0, 32),
                    ReviewRequestId = reviewRequest.Id,
                    AssignedReviewerId = expertId,
                    AssignedAt = DateTimeOffset.UtcNow,
                    ClaimState = ExpertAssignmentState.Claimed,
                });
            }
            await db.SaveChangesAsync();
        }

        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");
        var post = await expert.PostAsJsonAsync("/v1/expert/speaking/attempts/sa-001/comments", new
        {
            transcriptLineIndex = 1,
            criterionCode = "fluency",
            body = "Filler word at line opening — try a brief pause instead.",
        });
        post.EnsureSuccessStatusCode();

        // Learner who owns sa-001 reads back successfully.
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");
        var read = await learner.GetAsync("/v1/speaking/attempts/sa-001/comments");
        read.EnsureSuccessStatusCode();
        using var readDoc = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var comments = readDoc.RootElement.GetProperty("comments");
        Assert.True(comments.GetArrayLength() >= 1);
        Assert.Equal("fluency", comments[0].GetProperty("criterionCode").GetString());

        using var unassignedExpert = _factory.CreateAuthenticatedClient(SeedData.ExpertSecondaryEmail, SeedData.LocalSeedPassword, expectedRole: "expert");
        var forbiddenRead = await unassignedExpert.GetAsync("/v1/speaking/attempts/sa-001/comments");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
    }

    [Fact]
    public async Task ExpertComment_ReleasedAssignmentCannotPostOrRead()
    {
        await SeedReleasedSpeakingAssignmentAsync();
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var post = await expert.PostAsJsonAsync("/v1/expert/speaking/attempts/sa-released-comments/comments", new
        {
            transcriptLineIndex = 0,
            criterionCode = "fluency",
            body = "This released assignment must not allow a new comment.",
        });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        var read = await expert.GetAsync("/v1/speaking/attempts/sa-released-comments/comments");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    private async Task SeedReleasedSpeakingAssignmentAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (await db.Attempts.AnyAsync(a => a.Id == "sa-released-comments"))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.Attempts.Add(new Attempt
        {
            Id = "sa-released-comments",
            UserId = "mock-user-001",
            ContentId = "st-001",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "practice",
            State = AttemptState.Completed,
            StartedAt = now.AddHours(-1),
            SubmittedAt = now.AddMinutes(-20),
            CompletedAt = now.AddMinutes(-10),
            ElapsedSeconds = 600,
            TranscriptJson = "[]"
        });
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = "review-released-comments",
            AttemptId = "sa-released-comments",
            SubtestCode = "speaking",
            State = ReviewRequestState.Completed,
            TurnaroundOption = "standard",
            FocusAreasJson = "[]",
            LearnerNotes = string.Empty,
            PaymentSource = "credits",
            PriceSnapshot = 1m,
            CreatedAt = now.AddMinutes(-50),
            CompletedAt = now.AddMinutes(-30),
        });
        db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = "era-released-comments",
            ReviewRequestId = "review-released-comments",
            AssignedReviewerId = "expert-001",
            AssignedAt = now.AddMinutes(-45),
            ClaimState = ExpertAssignmentState.Released,
            ReleasedAt = now.AddMinutes(-30),
            ReasonCode = "released_for_regression_test"
        });
        await db.SaveChangesAsync();
    }
}
