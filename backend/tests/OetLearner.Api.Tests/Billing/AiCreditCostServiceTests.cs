using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Billing;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>SAMI §9.1: the AI Credit action price table is live configuration,
/// seeded with the handover baseline, admin-editable, and never a code constant.</summary>
public sealed class AiCreditCostServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public AiCreditCostServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private AiCreditCostService NewService()
        => new(new LearnerDbContext(_options), new FixedClock());

    [Fact]
    public async Task SeedDefaults_PlantsTheHandoverBaseline_Once()
    {
        var svc = NewService();
        await svc.SeedDefaultsAsync(CancellationToken.None);
        await svc.SeedDefaultsAsync(CancellationToken.None); // idempotent

        var rows = await svc.GetAllAsync(CancellationToken.None);
        Assert.Equal(8, rows.Count);
        Assert.Equal(2, rows.Single(r => r.ActionCode == "writing.assessment").Credits);
        Assert.Equal(4, rows.Single(r => r.ActionCode == "speaking.exam_full").Credits);
        Assert.Equal(1, rows.Single(r => r.ActionCode == "reading.analysis").Credits);
        Assert.False(rows.Single(r => r.ActionCode == "voice.live").Enabled, "live voice ships unmetered until validated");
        Assert.False(rows.Single(r => r.ActionCode == "pdf.deep_analysis").Enabled);
    }

    [Fact]
    public async Task Upsert_OverridesSeed_AndRejectsNegative()
    {
        var svc = NewService();
        await svc.SeedDefaultsAsync(CancellationToken.None);

        var row = await svc.UpsertAsync("writing.assessment", 3, true, "Owner adjustment", "admin-1", CancellationToken.None);
        Assert.Equal(3, row.Credits);
        Assert.Equal("admin-1", row.UpdatedByAdminId);
        Assert.Equal(3, (await svc.GetAsync("writing.assessment", CancellationToken.None))!.Credits);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpsertAsync("writing.assessment", -1, true, "nope", null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpsertAsync("  ", 1, true, "nope", null, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_UnknownAction_ReturnsNull()
    {
        var svc = NewService();
        Assert.Null(await svc.GetAsync("does.not.exist", CancellationToken.None));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 12, 9, 0, 0, TimeSpan.Zero);
    }
}
