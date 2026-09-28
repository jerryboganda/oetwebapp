using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests.Writing;

public class WritingOptionsProviderTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task Get_BootstrapsSingletonRow_OnFirstRead()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        var first = await provider.GetAsync(default);

        Assert.Equal("global", first.Id);
        Assert.True(first.AiGradingEnabled);
        Assert.True(first.AiCoachEnabled);
        Assert.False(first.FreeTierEnabled);
        Assert.Equal(0, first.FreeTierLimit);
        Assert.Equal(7, first.FreeTierWindowDays);
    }

    [Fact]
    public async Task UpdateThenGet_ReturnsUpdatedValues_AndInvalidatesCache()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        // Seed cache.
        var initial = await provider.GetAsync(default);
        Assert.True(initial.AiGradingEnabled);

        // Update flips kill switch + enables free tier.
        var saved = await provider.UpdateAsync(new WritingOptions
        {
            Id = "global",
            AiGradingEnabled = false,
            AiCoachEnabled = true,
            KillSwitchReason = "Maintenance window",
            FreeTierEnabled = true,
            FreeTierLimit = 5,
            FreeTierWindowDays = 14,
        }, "admin-99", default);

        Assert.False(saved.AiGradingEnabled);
        Assert.True(saved.FreeTierEnabled);

        // Re-read after invalidation must reflect the update, not cached old.
        var after = await provider.GetAsync(default);
        Assert.False(after.AiGradingEnabled);
        Assert.Equal("Maintenance window", after.KillSwitchReason);
        Assert.Equal(5, after.FreeTierLimit);
        Assert.Equal(14, after.FreeTierWindowDays);
        Assert.Equal("admin-99", after.UpdatedByAdminId);

        // Audit event written.
        var audits = await db.AuditEvents.Where(a => a.ResourceType == "WritingOptions").ToListAsync();
        Assert.Single(audits);
        Assert.Equal("WritingOptionsUpdated", audits[0].Action);
    }

    [Fact]
    public async Task Update_RejectsNegativeLimit()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(() =>
            provider.UpdateAsync(new WritingOptions
            {
                Id = "global",
                FreeTierLimit = -1,
                FreeTierWindowDays = 7,
            }, "admin-1", default));
    }

    [Fact]
    public async Task Update_RejectsOutOfRangeWindowDays()
    {
        await using var db = new LearnerDbContext(NewInMemoryOptions());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new WritingOptionsProvider(db, cache);

        await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(() =>
            provider.UpdateAsync(new WritingOptions
            {
                Id = "global",
                FreeTierLimit = 5,
                FreeTierWindowDays = 0,
            }, "admin-1", default));

        await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(() =>
            provider.UpdateAsync(new WritingOptions
            {
                Id = "global",
                FreeTierLimit = 5,
                FreeTierWindowDays = 366,
            }, "admin-1", default));
    }
}
