using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class SpeakingPdfAuthorizationTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SpeakingPdfAuthorizationTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LearnerOwner_CanDownloadSpeakingPdf()
    {
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await learner.GetAsync("/v1/speaking/evaluations/se-001/pdf");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task AssignedExpert_CanDownloadSpeakingPdf()
    {
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.GetAsync("/v1/speaking/evaluations/se-001/pdf");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AdminWithReviewPermission_CanDownloadSpeakingPdf()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");

        var response = await admin.GetAsync("/v1/speaking/evaluations/se-001/pdf");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnassignedExpert_CannotDownloadSpeakingPdf()
    {
        using var unassignedExpert = _factory.CreateAuthenticatedClient(SeedData.ExpertSecondaryEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await unassignedExpert.GetAsync("/v1/speaking/evaluations/se-001/pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleasedExpertAssignment_CannotDownloadSpeakingPdf()
    {
        await SeedReleasedSpeakingAssignmentAsync();
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.GetAsync("/v1/speaking/evaluations/se-released-pdf/pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedReleasedSpeakingAssignmentAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (await db.Attempts.AnyAsync(a => a.Id == "sa-released-pdf"))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.Attempts.Add(new Attempt
        {
            Id = "sa-released-pdf",
            UserId = "mock-user-001",
            ContentId = "st-001",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "ai",
            State = AttemptState.Completed,
            StartedAt = now.AddHours(-1),
            SubmittedAt = now.AddMinutes(-25),
            CompletedAt = now.AddMinutes(-20),
            ElapsedSeconds = 300,
            AudioUploadState = UploadState.Uploaded,
            AudioObjectKey = "audio/sa-released-pdf.wav",
            TranscriptJson = JsonSupport.Serialize(new[]
            {
                new { id = "tl-1", speaker = "candidate", text = "Good morning, I am here to discuss your discharge plan." }
            }),
            AnalysisJson = "{}",
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "se-released-pdf",
            AttemptId = "sa-released-pdf",
            SubtestCode = "speaking",
            State = AsyncState.Completed,
            ScoreRange = "330-360",
            GradeRange = null,
            ConfidenceBand = ConfidenceBand.Medium,
            StrengthsJson = JsonSupport.Serialize(new[] { "Clear opening" }),
            IssuesJson = JsonSupport.Serialize(new[] { "Needs more detail" }),
            CriterionScoresJson = JsonSupport.Serialize(new[]
            {
                new { criterionCode = "fluency", scoreRange = "4/6", confidenceBand = "medium", explanation = "Mostly steady pace." }
            }),
            FeedbackItemsJson = "[]",
            GeneratedAt = now.AddMinutes(-20),
            ModelExplanationSafe = "Practice estimate for released assignment regression test.",
            LearnerDisclaimer = "Training estimate only.",
            StatusReasonCode = "completed",
            StatusMessage = "Speaking evaluation completed successfully.",
            LastTransitionAt = now.AddMinutes(-20),
        });
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = "review-released-speaking-pdf",
            AttemptId = "sa-released-pdf",
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
            Id = "era-released-speaking-pdf",
            ReviewRequestId = "review-released-speaking-pdf",
            AssignedReviewerId = "expert-001",
            AssignedAt = now.AddMinutes(-45),
            ClaimState = ExpertAssignmentState.Released,
            ReleasedAt = now.AddMinutes(-30),
            ReasonCode = "released_for_regression_test",
        });
        await db.SaveChangesAsync();
    }
}