using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Professions;

namespace OetLearner.Api.Tests.Learner;

/// <summary>
/// The static Professions / Subtests lists embedded in every learner bootstrap are read-through
/// cached for 5 minutes with the <see cref="ProfessionCatalogService"/> pattern (IMemoryCache +
/// Invalidate()). A service built without the cache (unit tests, hand-built instances) reads the
/// database every time.
/// </summary>
public sealed class LearnerReferenceDataCacheTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static LearnerService NewService(LearnerDbContext db, IMemoryCache? cache)
        => new(db, null!, null!, null!, null!, null!, null!, null!, memoryCache: cache);

    private static ProfessionReference Profession(string id, int sortOrder) => new()
    {
        Id = id,
        Code = id,
        Label = char.ToUpperInvariant(id[0]) + id[1..],
        Status = "active",
        SortOrder = sortOrder,
    };

    private static SubtestReference Subtest(string id, string label) => new()
    {
        Id = id,
        Code = id,
        Label = label,
        SupportsProfessionSpecificContent = true,
    };

    [Fact]
    public async Task Professions_are_read_once_then_served_from_the_cache()
    {
        await using var db = NewDb();
        db.Professions.Add(Profession("medicine", 1));
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        var first = (await service.GetProfessionsAsync(CancellationToken.None)).ToList();
        db.Professions.Add(Profession("nursing", 2));
        await db.SaveChangesAsync();
        var second = (await service.GetProfessionsAsync(CancellationToken.None)).ToList();

        Assert.Single(first);
        Assert.Single(second); // the new row is not visible until the entry is invalidated or expires
    }

    [Fact]
    public async Task Subtests_are_read_once_then_served_from_the_cache()
    {
        await using var db = NewDb();
        db.Subtests.Add(Subtest("writing", "Writing"));
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        var first = (await service.GetSubtestsAsync(CancellationToken.None)).ToList();
        db.Subtests.Add(Subtest("speaking", "Speaking"));
        await db.SaveChangesAsync();
        var second = (await service.GetSubtestsAsync(CancellationToken.None)).ToList();

        Assert.Single(first);
        Assert.Single(second);
    }

    [Fact]
    public async Task ProfessionCatalogService_Invalidate_drops_the_reference_lists_too()
    {
        await using var db = NewDb();
        db.Professions.Add(Profession("medicine", 1));
        db.Subtests.Add(Subtest("writing", "Writing"));
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);
        await service.GetProfessionsAsync(CancellationToken.None);
        await service.GetSubtestsAsync(CancellationToken.None);

        db.Professions.Add(Profession("nursing", 2));
        db.Subtests.Add(Subtest("speaking", "Speaking"));
        await db.SaveChangesAsync();
        new ProfessionCatalogService(db, cache).Invalidate();

        Assert.Equal(2, (await service.GetProfessionsAsync(CancellationToken.None)).Count());
        Assert.Equal(2, (await service.GetSubtestsAsync(CancellationToken.None)).Count());
    }

    [Fact]
    public async Task Without_a_cache_every_call_reads_the_database()
    {
        await using var db = NewDb();
        db.Professions.Add(Profession("medicine", 1));
        await db.SaveChangesAsync();
        var service = NewService(db, null);

        var first = (await service.GetProfessionsAsync(CancellationToken.None)).ToList();
        db.Professions.Add(Profession("nursing", 2));
        await db.SaveChangesAsync();
        var second = (await service.GetProfessionsAsync(CancellationToken.None)).ToList();

        Assert.Single(first);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public async Task Cached_lists_keep_their_order_and_shape()
    {
        await using var db = NewDb();
        db.Professions.AddRange(Profession("nursing", 2), Profession("medicine", 1));
        db.Subtests.AddRange(Subtest("writing", "Writing"), Subtest("listening", "Listening"));
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        var cold = System.Text.Json.JsonSerializer.Serialize(await service.GetProfessionsAsync(CancellationToken.None));
        var warm = System.Text.Json.JsonSerializer.Serialize(await service.GetProfessionsAsync(CancellationToken.None));
        var subtests = System.Text.Json.JsonSerializer.Serialize(await service.GetSubtestsAsync(CancellationToken.None));

        Assert.Equal(cold, warm);
        Assert.True(cold.IndexOf("medicine", StringComparison.Ordinal) < cold.IndexOf("nursing", StringComparison.Ordinal));
        Assert.True(subtests.IndexOf("Listening", StringComparison.Ordinal) < subtests.IndexOf("Writing", StringComparison.Ordinal));
        Assert.Contains("professionId", cold, StringComparison.Ordinal);
        Assert.Contains("supportsProfessionSpecificContent", subtests, StringComparison.Ordinal);
    }
}
