using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class SpacedRepetitionServiceTests
{
    private sealed class FakeScheduler : ISpacedRepetitionScheduler
    {
        public Sm2Update Schedule(Sm2State state, int quality, DateOnly? today = null)
        {
            var todayDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var correct = quality >= 3;
            var reviewCount = state.ReviewCount + 1;
            var correctCount = state.CorrectCount + (correct ? 1 : 0);
            var interval = correct ? state.IntervalDays + 1 : 1;
            var ease = correct ? state.EaseFactor + 0.1 : state.EaseFactor - 0.2;
            return new Sm2Update(ease, interval, reviewCount, correctCount, todayDate.AddDays(interval), correct);
        }
    }

    private static (LearnerDbContext db, SpacedRepetitionService svc) Build()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(opts);
        var svc = new SpacedRepetitionService(db, new FakeScheduler());
        return (db, svc);
    }

    private static ReviewItem AddItem(LearnerDbContext db, string userId, DateOnly due, string status = "active",
        string sourceType = "vocabulary", string? sourceId = null)
    {
        var item = new ReviewItem
        {
            Id = $"ri-{Guid.NewGuid():N}",
            UserId = userId,
            ExamTypeCode = "OET",
            SourceType = sourceType,
            SourceId = sourceId ?? Guid.NewGuid().ToString("N"),
            SubtestCode = "writing",
            QuestionJson = "{}",
            AnswerJson = "{}",
            EaseFactor = 2.5,
            IntervalDays = 1,
            DueDate = due,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = status,
        };
        db.ReviewItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static CreateReviewItemRequest Req(string sourceId = "src-1") =>
        new(ExamTypeCode: "OET",
            SourceType: "vocabulary",
            SourceId: sourceId,
            SubtestCode: "writing",
            CriterionCode: null,
            QuestionJson: "{\"q\":1}",
            AnswerJson: "{\"a\":1}");

    // ── GetDueItemsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDueItemsAsync_returns_only_active_items_due_today_or_earlier_for_user()
    {
        var (db, svc) = Build();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        AddItem(db, "u1", today.AddDays(-2));            // due
        AddItem(db, "u1", today);                         // due
        AddItem(db, "u1", today.AddDays(1));              // not due
        AddItem(db, "u1", today, status: "mastered");     // not active
        AddItem(db, "u2", today);                         // different user

        var result = await svc.GetDueItemsAsync("u1", 10, CancellationToken.None);
        var list = (System.Collections.IEnumerable)result;
        var count = 0;
        foreach (var _ in list) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetDueItemsAsync_respects_limit()
    {
        var (db, svc) = Build();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 0; i < 5; i++) AddItem(db, "u1", today);
        var result = await svc.GetDueItemsAsync("u1", 3, CancellationToken.None);
        var count = 0;
        foreach (var _ in (System.Collections.IEnumerable)result) count++;
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetDueItemsAsync_returns_empty_when_no_items()
    {
        var (_, svc) = Build();
        var result = await svc.GetDueItemsAsync("nobody", 10, CancellationToken.None);
        var count = 0;
        foreach (var _ in (System.Collections.IEnumerable)result) count++;
        Assert.Equal(0, count);
    }

    // ── GetReviewSummaryAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetReviewSummaryAsync_counts_each_bucket_correctly()
    {
        var (db, svc) = Build();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        AddItem(db, "u1", today);                         // due + dueToday
        AddItem(db, "u1", today.AddDays(-1));             // due (overdue)
        AddItem(db, "u1", today.AddDays(3));              // upcoming (within 7)
        AddItem(db, "u1", today.AddDays(8));              // beyond upcoming
        AddItem(db, "u1", today, status: "mastered");     // mastered
        AddItem(db, "u2", today);                         // other user

        dynamic summary = await svc.GetReviewSummaryAsync("u1", CancellationToken.None);
        Assert.Equal(4, (int)summary.total);
        Assert.Equal(2, (int)summary.due);
        Assert.Equal(1, (int)summary.dueToday);
        Assert.Equal(1, (int)summary.mastered);
        Assert.Equal(1, (int)summary.upcoming);
    }

    // ── CreateReviewItemAsync ────────────────────────────────────────────

    [Fact]
    public async Task CreateReviewItemAsync_persists_a_new_active_item()
    {
        var (db, svc) = Build();
        var result = await svc.CreateReviewItemAsync("u1", Req("s-1"), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(db.ReviewItems.Where(r => r.UserId == "u1"));
        var stored = db.ReviewItems.Single(r => r.UserId == "u1");
        Assert.Equal("active", stored.Status);
        Assert.Equal(2.5, stored.EaseFactor);
        Assert.Equal(1, stored.IntervalDays);
    }

    [Fact]
    public async Task CreateReviewItemAsync_is_idempotent_for_same_source()
    {
        var (db, svc) = Build();
        await svc.CreateReviewItemAsync("u1", Req("s-1"), CancellationToken.None);
        await svc.CreateReviewItemAsync("u1", Req("s-1"), CancellationToken.None);
        await svc.CreateReviewItemAsync("u1", Req("s-1"), CancellationToken.None);
        Assert.Single(db.ReviewItems.Where(r => r.UserId == "u1"));
    }

    [Fact]
    public async Task CreateReviewItemAsync_creates_separate_items_for_distinct_sources()
    {
        var (db, svc) = Build();
        await svc.CreateReviewItemAsync("u1", Req("s-1"), CancellationToken.None);
        await svc.CreateReviewItemAsync("u1", Req("s-2"), CancellationToken.None);
        Assert.Equal(2, db.ReviewItems.Count(r => r.UserId == "u1"));
    }

    // ── SubmitReviewAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SubmitReviewAsync_updates_item_and_returns_new_schedule()
    {
        var (db, svc) = Build();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var item = AddItem(db, "u1", today);

        dynamic result = await svc.SubmitReviewAsync("u1", item.Id, quality: 5, CancellationToken.None);
        Assert.Equal(item.Id, (string)result.itemId);
        Assert.Equal(2, (int)result.intervalDays); // fake adds 1 to interval on correct
        Assert.True((double)result.easeFactor > 2.5);

        var stored = await db.ReviewItems.AsNoTracking().FirstAsync(r => r.Id == item.Id);
        Assert.Equal(1, stored.ConsecutiveCorrect);
        Assert.NotNull(stored.LastReviewedAt);
    }

    [Fact]
    public async Task SubmitReviewAsync_resets_consecutive_correct_on_failure()
    {
        var (db, svc) = Build();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var item = AddItem(db, "u1", today);
        item.ConsecutiveCorrect = 5;
        await db.SaveChangesAsync();

        await svc.SubmitReviewAsync("u1", item.Id, quality: 1, CancellationToken.None);
        var stored = await db.ReviewItems.AsNoTracking().FirstAsync(r => r.Id == item.Id);
        Assert.Equal(0, stored.ConsecutiveCorrect);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public async Task SubmitReviewAsync_throws_for_invalid_quality(int quality)
    {
        var (db, svc) = Build();
        var item = AddItem(db, "u1", DateOnly.FromDateTime(DateTime.UtcNow));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SubmitReviewAsync("u1", item.Id, quality, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitReviewAsync_throws_when_item_not_found()
    {
        var (_, svc) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SubmitReviewAsync("u1", "missing-id", 3, CancellationToken.None));
        Assert.Equal("REVIEW_ITEM_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitReviewAsync_throws_when_item_belongs_to_other_user()
    {
        var (db, svc) = Build();
        var item = AddItem(db, "owner", DateOnly.FromDateTime(DateTime.UtcNow));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SubmitReviewAsync("intruder", item.Id, 3, CancellationToken.None));
    }

    // ── DeleteReviewItemAsync ────────────────────────────────────────────

    [Fact]
    public async Task DeleteReviewItemAsync_removes_the_item()
    {
        var (db, svc) = Build();
        var item = AddItem(db, "u1", DateOnly.FromDateTime(DateTime.UtcNow));

        dynamic result = await svc.DeleteReviewItemAsync("u1", item.Id, CancellationToken.None);
        Assert.True((bool)result.deleted);
        Assert.Empty(db.ReviewItems.Where(r => r.Id == item.Id));
    }

    [Fact]
    public async Task DeleteReviewItemAsync_throws_when_not_found()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.DeleteReviewItemAsync("u1", "missing", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteReviewItemAsync_throws_when_user_mismatch()
    {
        var (db, svc) = Build();
        var item = AddItem(db, "owner", DateOnly.FromDateTime(DateTime.UtcNow));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.DeleteReviewItemAsync("intruder", item.Id, CancellationToken.None));
    }
}
