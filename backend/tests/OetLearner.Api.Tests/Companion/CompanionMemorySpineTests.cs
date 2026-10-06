using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests.Companion;

/// <summary>
/// SAMI Wave 1 memory spine: layer scoping, supersede-not-overwrite history,
/// confirm-before-save marking, scoped deletes, Error DNA upsert + review ladder,
/// availability minutes math with shift days, and journey preservation.
/// </summary>
public sealed class CompanionMemorySpineTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionMemorySpineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private CompanionMemoryService NewMemoryService()
        => new(new LearnerDbContext(_options), new FixedTimeProvider());

    private ErrorDnaService NewErrorService()
        => new(new LearnerDbContext(_options), new FixedTimeProvider());

    private CompanionJourneyService NewJourneyService()
        => new(new LearnerDbContext(_options), new FixedTimeProvider());

    private CompanionAvailabilityService NewAvailabilityService()
        => new(new LearnerDbContext(_options), new FixedTimeProvider());

    [Fact]
    public async Task RecordAsync_SupersedesTheOldCurrentEntry_AndKeepsItReadableAsHistory()
    {
        var svc = NewMemoryService();
        var first = await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "score", "reading",
            "reading score 295", null, "chat", "t1", DateTimeOffset.UtcNow, CancellationToken.None);
        var second = await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "score", "reading",
            "reading score 315", null, "chat", "t1", DateTimeOffset.UtcNow, CancellationToken.None);

        var current = await svc.GetCurrentAsync("u1", CompanionMemoryLayers.Learning, CancellationToken.None);
        var all = await svc.ExportAsync("u1", CancellationToken.None);

        Assert.Single(current);
        Assert.Equal(second.Id, current[0].Id);
        Assert.Equal(2, all.Count);
        Assert.NotNull(first.SupersededAt);
        Assert.Null(second.SupersededAt);
    }

    [Fact]
    public async Task Memory_IsNamespacedByUser()
    {
        var svc = NewMemoryService();
        await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "note", "writing",
            "check articles", null, "chat", "t1", DateTimeOffset.UtcNow, CancellationToken.None);

        var other = await svc.GetCurrentAsync("someone-else", CompanionMemoryLayers.Learning, CancellationToken.None);
        Assert.Empty(other);

        var deletedOther = await svc.DeleteAsync("someone-else", "whatever", CancellationToken.None);
        Assert.False(deletedOther);
    }

    [Fact]
    public async Task ConfirmAsync_MarksTheEntryConfirmed()
    {
        var svc = NewMemoryService();
        var entry = await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "exam_date", "general",
            "exam 2026-10-25", null, "chat", "t1", null, CancellationToken.None);
        Assert.Null(entry.ConfirmedAt);

        var confirmed = await svc.ConfirmAsync("u1", entry.Id, CancellationToken.None);
        Assert.NotNull(confirmed?.ConfirmedAt);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheRequestedEntry()
    {
        var svc = NewMemoryService();
        var a = await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "score", "reading",
            "reading 300", null, "chat", "t1", DateTimeOffset.UtcNow, CancellationToken.None);
        var b = await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "score", "listening",
            "listening 310", null, "chat", "t1", DateTimeOffset.UtcNow, CancellationToken.None);

        var ok = await svc.DeleteAsync("u1", a.Id, CancellationToken.None);

        Assert.True(ok);
        var current = await svc.GetCurrentAsync("u1", CompanionMemoryLayers.Learning, CancellationToken.None);
        Assert.Single(current);
        Assert.Equal(b.Id, current[0].Id);
    }

    [Fact]
    public async Task ErrorDna_SamePatternUpsertsEvidence_NewPatternGetsOwnEntry()
    {
        var svc = NewErrorService();
        var e1 = await svc.RecordEvidenceAsync("u1", "grammar", "article before singular countable noun",
            "writing", "writing_grade", "r1", CancellationToken.None);
        var e2 = await svc.RecordEvidenceAsync("u1", "grammar", "Article Before Singular Countable Noun",
            "writing", "writing_grade", "r2", CancellationToken.None);
        Assert.Equal(e1.Id, e2.Id);
        Assert.Equal(2, e2.EvidenceCount);

        var e3 = await svc.RecordEvidenceAsync("u1", "timing", "runs out of time on Part C",
            "reading", "reading_answer", "r3", CancellationToken.None);
        Assert.NotEqual(e1.Id, e3.Id);

        var top = await svc.TopWeaknessesAsync("u1", 5, CancellationToken.None);
        Assert.Equal(2, top.Count);
        Assert.Equal(e1.Id, top[0].Id); // higher evidence first
    }

    [Fact]
    public async Task ErrorDna_ReviewLadder_CorrectRaisesMasteryAndPushesOut_MissResets()
    {
        var svc = NewErrorService();
        var entry = await svc.RecordEvidenceAsync("u1", "grammar", "subject-verb agreement", "writing",
            "writing_grade", "r1", CancellationToken.None);

        for (var i = 0; i < 4; i++)
        {
            entry = (await svc.RecordReviewAsync("u1", entry.Id, true, CancellationToken.None))!;
        }
        Assert.Equal(80, entry.MasteryScore);
        Assert.NotNull(entry.NextReviewAt);

        entry = (await svc.RecordReviewAsync("u1", entry.Id, false, CancellationToken.None))!;
        Assert.Equal(60, entry.MasteryScore);
    }

    [Fact]
    public async Task PromptSummary_OnlyIncludesConfirmedEntries()
    {
        var svc = NewMemoryService();
        await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "score", "writing",
            "writing 340", null, "chat", "t1", DateTimeOffset.UtcNow, CancellationToken.None);
        await svc.RecordAsync("u1", CompanionMemoryLayers.Learning, "note", "writing",
            "pending unconfirmed note", null, "chat", "t1", null, CancellationToken.None);

        var summary = await svc.BuildPromptSummaryAsync("u1", CancellationToken.None);
        Assert.Contains("writing 340", summary);
        Assert.DoesNotContain("pending unconfirmed note", summary);
    }

    [Fact]
    public async Task Journey_StartPreservesHistory_AndClosesThePreviousActive()
    {
        var svc = NewJourneyService();
        var first = await svc.StartAsync("u1", "October attempt", new DateOnly(2026, 10, 25), null, CancellationToken.None);
        var resit = await svc.StartAsync("u1", "Writing resit", null, ["writing"], CancellationToken.None);

        var history = await svc.GetHistoryAsync("u1", CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.False(first.IsActive);
        Assert.True(resit.IsActive);

        // The first journey's rows are still readable for comparison.
        Assert.Contains(history, j => j.Id == first.Id && j.EndedAt != null);
    }

    [Fact]
    public async Task Availability_ShiftAndTravelRulesChangeTodaysMinutes()
    {
        var svc = NewAvailabilityService();
        // Mon..Sun = 45 each; night shifts Monday (0); long day Wednesday (2).
        await svc.UpsertAsync("u1",
            [45, 45, 45, 45, 45, 120, 120],
            [0], [2],
            travelMode: false, travelMinutesPerDay: null, travelUntil: null, preferredStudyTime: null,
            CancellationToken.None);

        var monday = NextWeekday(DateOnly.FromDateTime(DateTime.UtcNow), DayOfWeek.Monday);
        var wednesday = NextWeekday(DateOnly.FromDateTime(DateTime.UtcNow), DayOfWeek.Wednesday);
        var tuesday = NextWeekday(DateOnly.FromDateTime(DateTime.UtcNow), DayOfWeek.Tuesday);

        Assert.Equal(0, await svc.MinutesAvailableOnAsync("u1", monday, CancellationToken.None));
        Assert.Equal(22, await svc.MinutesAvailableOnAsync("u1", wednesday, CancellationToken.None));
        Assert.Equal(45, await svc.MinutesAvailableOnAsync("u1", tuesday, CancellationToken.None));

        await svc.UpsertAsync("u1", null, null, null,
            travelMode: true, travelMinutesPerDay: 20, travelUntil: null, preferredStudyTime: null,
            CancellationToken.None);
        Assert.Equal(20, await svc.MinutesAvailableOnAsync("u1", monday, CancellationToken.None));
    }

    [Fact]
    public async Task Availability_RejectsOutOfRangeInput()
    {
        var svc = NewAvailabilityService();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertAsync("u1",
            [45, 45, 45, 45, 45, 120], null, null, null, null, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpsertAsync("u1",
            null, [9], null, null, null, null, null, CancellationToken.None));
    }

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek day)
    {
        var d = from;
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }
}
