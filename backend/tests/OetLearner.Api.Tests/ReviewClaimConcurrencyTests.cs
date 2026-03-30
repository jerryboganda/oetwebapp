using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests;

public class ReviewClaimConcurrencyTests
{
    [Fact]
    public void ReviewRequest_State_IsConfiguredAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);
        var reviewEntity = db.Model.FindEntityType(typeof(ReviewRequest));

        Assert.NotNull(reviewEntity);
        var stateProperty = reviewEntity!.FindProperty(nameof(ReviewRequest.State));
        Assert.NotNull(stateProperty);
        Assert.True(stateProperty!.IsConcurrencyToken);
    }

    [Fact]
    public async Task ReviewRequest_ConcurrentClaimWrites_ThrowConcurrencyException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        var now = new DateTimeOffset(2026, 03, 30, 12, 0, 0, TimeSpan.Zero);
        await using (var seedDb = new LearnerDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();

            seedDb.ReviewRequests.Add(new ReviewRequest
            {
                Id = "review-001",
                AttemptId = "attempt-001",
                SubtestCode = "writing",
                State = ReviewRequestState.Queued,
                TurnaroundOption = "standard",
                FocusAreasJson = "[]",
                LearnerNotes = string.Empty,
                PaymentSource = "wallet",
                PriceSnapshot = 1,
                CreatedAt = now,
                EligibilitySnapshotJson = "{}"
            });

            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = new LearnerDbContext(options);
        await using var secondDb = new LearnerDbContext(options);

        var firstReview = await firstDb.ReviewRequests.SingleAsync(x => x.Id == "review-001");
        var secondReview = await secondDb.ReviewRequests.SingleAsync(x => x.Id == "review-001");

        firstDb.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = "era-001",
            ReviewRequestId = "review-001",
            AssignedReviewerId = "expert-001",
            AssignedAt = now,
            ClaimState = ExpertAssignmentState.Claimed
        });
        firstReview.State = ReviewRequestState.InReview;
        firstReview.CompletedAt = null;
        await firstDb.SaveChangesAsync();

        secondDb.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = "era-002",
            ReviewRequestId = "review-001",
            AssignedReviewerId = "expert-002",
            AssignedAt = now.AddMinutes(1),
            ClaimState = ExpertAssignmentState.Claimed
        });
        secondReview.State = ReviewRequestState.InReview;
        secondReview.CompletedAt = null;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());

        await using var verificationDb = new LearnerDbContext(options);
        var activeAssignments = await verificationDb.ExpertReviewAssignments
            .Where(x => x.ReviewRequestId == "review-001" && x.ClaimState != ExpertAssignmentState.Released)
            .ToListAsync();

        Assert.Single(activeAssignments);
        Assert.Equal("expert-001", activeAssignments[0].AssignedReviewerId);
    }
}