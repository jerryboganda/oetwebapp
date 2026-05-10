using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Runtime-mutable Writing module options. Reads the singleton row
/// (id="global") and caches it for 30 seconds — mirrors the
/// <see cref="DbBackedRulebookLoader"/> + <see cref="AiQuotaService"/>
/// pattern. Bootstraps the row on first read if missing.
/// </summary>
public interface IWritingOptionsProvider
{
    Task<WritingOptions> GetAsync(CancellationToken ct);
    Task<WritingOptions> UpdateAsync(WritingOptions update, string? adminId, CancellationToken ct);
}

public sealed class WritingOptionsProvider(
    LearnerDbContext db,
    IMemoryCache cache) : IWritingOptionsProvider
{
    private const string CacheKey = "writing-options:global";
    private const string SingletonId = "global";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<WritingOptions> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out WritingOptions? cached) && cached is not null)
        {
            return cached;
        }

        var row = await db.WritingOptions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == SingletonId, ct);

        if (row is null)
        {
            row = new WritingOptions { Id = SingletonId, UpdatedAt = DateTimeOffset.UtcNow };
            db.WritingOptions.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost a race — another caller bootstrapped first. Re-read.
                db.ChangeTracker.Clear();
                row = await db.WritingOptions
                    .AsNoTracking()
                    .FirstAsync(x => x.Id == SingletonId, ct);
            }
        }

        cache.Set(CacheKey, row, CacheTtl);
        return row;
    }

    public async Task<WritingOptions> UpdateAsync(WritingOptions update, string? adminId, CancellationToken ct)
    {
        if (update.FreeTierLimit < 0)
        {
            throw ApiException.Validation(
                "writing_options_invalid",
                "FreeTierLimit must be >= 0.",
                [new ApiFieldError("freeTierLimit", "out_of_range", "Must be 0 or greater.")]);
        }

        if (update.FreeTierWindowDays < 1 || update.FreeTierWindowDays > 365)
        {
            throw ApiException.Validation(
                "writing_options_invalid",
                "FreeTierWindowDays must be between 1 and 365.",
                [new ApiFieldError("freeTierWindowDays", "out_of_range", "Must be between 1 and 365.")]);
        }

        var row = await db.WritingOptions.FirstOrDefaultAsync(x => x.Id == SingletonId, ct);
        if (row is null)
        {
            row = new WritingOptions { Id = SingletonId };
            db.WritingOptions.Add(row);
        }

        row.AiGradingEnabled = update.AiGradingEnabled;
        row.AiCoachEnabled = update.AiCoachEnabled;
        row.KillSwitchReason = update.KillSwitchReason;
        row.FreeTierLimit = update.FreeTierLimit;
        row.FreeTierWindowDays = update.FreeTierWindowDays;
        row.FreeTierEnabled = update.FreeTierEnabled;
        row.PreferredGradingProvider = update.PreferredGradingProvider;
        row.PreferredCoachProvider = update.PreferredCoachProvider;
        row.PreferredDraftProvider = update.PreferredDraftProvider;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByAdminId = adminId;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminId ?? "system",
            Action = "WritingOptionsUpdated",
            ResourceType = "WritingOptions",
            ResourceId = SingletonId,
            Details = $"AiGradingEnabled={row.AiGradingEnabled};AiCoachEnabled={row.AiCoachEnabled};"
                + $"FreeTierEnabled={row.FreeTierEnabled};FreeTierLimit={row.FreeTierLimit};"
                + $"FreeTierWindowDays={row.FreeTierWindowDays}",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
        return row;
    }
}
