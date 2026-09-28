using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11FeedbackServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SubmitAsync_rejects_out_of_range_ratings(int rating)
    {
        await using var fixture = await Fixture.CreateAsync("owner");
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.SubmitAsync(
            "owner", fixture.SessionId, new(rating, null), CancellationToken.None));
        Assert.Equal("speaking_v11_feedback_rating_invalid", error.ErrorCode);
        Assert.Empty(await fixture.Db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task SubmitAsync_rejects_oversized_comments()
    {
        await using var fixture = await Fixture.CreateAsync("owner");
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.SubmitAsync(
            "owner", fixture.SessionId, new(5, new string('x', 2001)), CancellationToken.None));
        Assert.Equal("speaking_v11_feedback_comment_too_long", error.ErrorCode);
        Assert.Empty(await fixture.Db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task SubmitAsync_requires_finished_session()
    {
        await using var fixture = await Fixture.CreateAsync("owner");
        var session = await fixture.Db.SpeakingSessions.SingleAsync();
        session.State = SpeakingSessionState.Active;
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.SubmitAsync(
            "owner", fixture.SessionId, new(5, null), CancellationToken.None));
        Assert.Equal("speaking_v11_feedback_session_not_finished", error.ErrorCode);
        Assert.Empty(await fixture.Db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task SubmitAsync_requires_v11_assessment()
    {
        await using var fixture = await Fixture.CreateAsync("owner");
        fixture.Db.SpeakingSimulationV11Assessments.RemoveRange(
            await fixture.Db.SpeakingSimulationV11Assessments.ToListAsync());
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.SubmitAsync(
            "owner", fixture.SessionId, new(5, null), CancellationToken.None));
        Assert.Equal("speaking_v11_feedback_assessment_required", error.ErrorCode);
        Assert.Empty(await fixture.Db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task SubmitAsync_rejects_non_owner()
    {
        await using var fixture = await Fixture.CreateAsync("owner-1");

        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Service.SubmitAsync(
            "different-learner",
            fixture.SessionId,
            new SpeakingSimulationV11FeedbackRequest(5, "Great practice."),
            CancellationToken.None));

        Assert.Equal("speaking_v11_feedback_forbidden", error.ErrorCode);
        Assert.Empty(await fixture.Db.IdempotencyRecords.AsNoTracking()
            .Where(x => x.Scope == "speaking_v11_feedback")
            .ToListAsync());
    }

    [Fact]
    public async Task SubmitAsync_upserts_one_row_for_owned_finished_v11_session()
    {
        await using var fixture = await Fixture.CreateAsync("owner-2");

        var first = await fixture.Service.SubmitAsync(
            "owner-2",
            fixture.SessionId,
            new SpeakingSimulationV11FeedbackRequest(3, "  Useful.  "),
            CancellationToken.None);
        var second = await fixture.Service.SubmitAsync(
            "owner-2",
            fixture.SessionId,
            new SpeakingSimulationV11FeedbackRequest(5, "Very useful."),
            CancellationToken.None);

        Assert.Equal(first.FeedbackId, second.FeedbackId);
        Assert.Equal(5, second.Rating);
        Assert.Equal("Very useful.", second.Comment);
        var rows = await fixture.Db.IdempotencyRecords.AsNoTracking()
            .Where(x => x.Scope == "speaking_v11_feedback")
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(first.FeedbackId, row.Id);
        Assert.Equal(first.FeedbackId, row.Key);
        Assert.Contains("Very useful.", row.ResponseJson, StringComparison.Ordinal);
        Assert.Empty(await fixture.Db.AnalyticsEvents.AsNoTracking()
            .Where(x => x.EventName == "speaking_v11_feedback")
            .ToListAsync());
    }

    private sealed class Fixture(LearnerDbContext db, string sessionId) : IAsyncDisposable
    {
        public LearnerDbContext Db { get; } = db;
        public string SessionId { get; } = sessionId;
        public SpeakingSimulationV11FeedbackService Service { get; } = new(db, TimeProvider.System);

        public static async Task<Fixture> CreateAsync(string ownerId)
        {
            var options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase($"speaking-v11-feedback-{Guid.NewGuid():N}")
                .Options;
            var db = new LearnerDbContext(options);
            var sessionId = $"session-{Guid.NewGuid():N}";

            db.SpeakingSessions.Add(new SpeakingSession
            {
                Id = sessionId,
                UserId = ownerId,
                RolePlayCardId = "card-feedback",
                Mode = SpeakingSessionMode.AiSelfPractice,
                State = SpeakingSessionState.Finished,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.SpeakingSimulationV11Assessments.Add(new SpeakingSimulationV11Assessment
            {
                Id = $"assessment-{Guid.NewGuid():N}",
                SpeakingSessionId = sessionId,
                AssessmentKind = "card",
                CardSlot = "standalone",
                ProfessionId = "medicine",
                SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
                CalibrationVersion = SpeakingSimulationV11Contracts.CalibrationVersion,
                GraphDisclaimer = SpeakingSimulationV11Contracts.GraphDisclaimer,
                Status = SpeakingSimulationV11AssessmentStatus.Complete,
            });
            await db.SaveChangesAsync();
            return new Fixture(db, sessionId);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
