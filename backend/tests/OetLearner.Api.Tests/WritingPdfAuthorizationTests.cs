using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class WritingPdfAuthorizationTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public WritingPdfAuthorizationTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AssignedExpert_CanDownloadWritingPdf()
    {
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.GetAsync("/v1/writing/attempts/wa-001/pdf");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnassignedExpert_CannotDownloadWritingPdf()
    {
        using var unassignedExpert = _factory.CreateAuthenticatedClient(SeedData.ExpertSecondaryEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await unassignedExpert.GetAsync("/v1/writing/attempts/wa-001/pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleasedExpertAssignment_CannotDownloadWritingPdf()
    {
        await SeedReleasedWritingAssignmentAsync();
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.GetAsync("/v1/writing/attempts/wa-released-pdf/pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedReleasedWritingAssignmentAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (await db.Attempts.AnyAsync(a => a.Id == "wa-released-pdf"))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.Attempts.Add(new Attempt
        {
            Id = "wa-released-pdf",
            UserId = "mock-user-001",
            ContentId = "wt-001",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "practice",
            State = AttemptState.Completed,
            StartedAt = now.AddHours(-1),
            SubmittedAt = now.AddMinutes(-20),
            CompletedAt = now.AddMinutes(-10),
            ElapsedSeconds = 2400,
            DraftContent = "Released assignment should not retain PDF access."
        });
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = "review-released-pdf",
            AttemptId = "wa-released-pdf",
            SubtestCode = "writing",
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
            Id = "era-released-pdf",
            ReviewRequestId = "review-released-pdf",
            AssignedReviewerId = "expert-001",
            AssignedAt = now.AddMinutes(-45),
            ClaimState = ExpertAssignmentState.Released,
            ReleasedAt = now.AddMinutes(-30),
            ReasonCode = "released_for_regression_test"
        });
        await db.SaveChangesAsync();
    }
}