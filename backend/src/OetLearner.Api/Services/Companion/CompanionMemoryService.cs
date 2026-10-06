using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// The companion memory spine (SAMI §3.2, F-041..F-047). Everything is namespaced to the
/// learner; consequential facts require explicit confirmation before they are marked
/// confirmed; superseded history is retained for the journey view, never replayed as current.
/// </summary>
public interface ICompanionMemoryService
{
    /// <summary>Records (or supersedes-and-replaces) one memory entry. Returns the stored entry.</summary>
    Task<CompanionMemoryEntry> RecordAsync(string userId, int layer, string kind, string subtest,
        string content, string? dataJson, string provenanceType, string? provenanceId,
        DateTimeOffset? confirmedAt, CancellationToken ct);

    /// <summary>Marks an existing pending entry confirmed (the learner agreed to the echoed values).</summary>
    Task<CompanionMemoryEntry?> ConfirmAsync(string userId, string entryId, CancellationToken ct);

    Task<IReadOnlyList<CompanionMemoryEntry>> GetCurrentAsync(string userId, int layer, CancellationToken ct);

    Task<IReadOnlyList<CompanionMemoryEntry>> ExportAsync(string userId, CancellationToken ct);

    /// <summary>Deletes one scoped entry (memory control, F-047). Academic history in OTHER entries is untouched.</summary>
    Task<bool> DeleteAsync(string userId, string entryId, CancellationToken ct);

    /// <summary>Supersedes every current entry of (layer, kind, subtest) — a scoped preference edit, not a wipe.</summary>
    Task<int> SupersedeScopedAsync(string userId, int layer, string kind, string subtest, CancellationToken ct);

    /// <summary>Compact prompt block: the current learning + journey facts, bounded.</summary>
    Task<string> BuildPromptSummaryAsync(string userId, CancellationToken ct);
}

public sealed class CompanionMemoryService(LearnerDbContext db, TimeProvider clock) : ICompanionMemoryService
{
    public async Task<CompanionMemoryEntry> RecordAsync(string userId, int layer, string kind, string subtest,
        string content, string? dataJson, string provenanceType, string? provenanceId,
        DateTimeOffset? confirmedAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // A current entry of the same (layer, kind, subtest) is superseded, not deleted:
        // superseded scores remain historical (SAMI §3.3), never current.
        var existing = await db.CompanionMemoryEntries
            .Where(m => m.UserId == userId && m.Layer == layer && m.Kind == kind
                        && m.Subtest == subtest && m.SupersededAt == null)
            .ToListAsync(ct);
        foreach (var stale in existing)
        {
            stale.SupersededAt = now;
            stale.UpdatedAt = now;
        }

        var entry = new CompanionMemoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Layer = layer,
            Kind = kind,
            Subtest = string.IsNullOrWhiteSpace(subtest) ? "general" : subtest.ToLowerInvariant(),
            Content = content.Length > 1024 ? content[..1024] : content,
            DataJson = dataJson,
            ProvenanceType = provenanceType,
            ProvenanceId = provenanceId,
            ConfirmedAt = confirmedAt,
            RecordedAt = now,
            UpdatedAt = now,
        };
        db.CompanionMemoryEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<CompanionMemoryEntry?> ConfirmAsync(string userId, string entryId, CancellationToken ct)
    {
        var entry = await db.CompanionMemoryEntries
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Id == entryId && m.SupersededAt == null, ct);
        if (entry is null) return null;
        entry.ConfirmedAt = clock.GetUtcNow();
        entry.UpdatedAt = entry.ConfirmedAt.Value;
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<CompanionMemoryEntry>> GetCurrentAsync(string userId, int layer, CancellationToken ct)
        => await db.CompanionMemoryEntries.AsNoTracking()
            .Where(m => m.UserId == userId && m.Layer == layer && m.SupersededAt == null)
            .OrderByDescending(m => m.RecordedAt)
            .Take(100)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CompanionMemoryEntry>> ExportAsync(string userId, CancellationToken ct)
        => await db.CompanionMemoryEntries.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.RecordedAt)
            .ToListAsync(ct);

    public async Task<bool> DeleteAsync(string userId, string entryId, CancellationToken ct)
    {
        var entry = await db.CompanionMemoryEntries
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Id == entryId, ct);
        if (entry is null) return false;
        db.CompanionMemoryEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SupersedeScopedAsync(string userId, int layer, string kind, string subtest, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var current = await db.CompanionMemoryEntries
            .Where(m => m.UserId == userId && m.Layer == layer && m.Kind == kind
                        && m.Subtest == subtest && m.SupersededAt == null)
            .ToListAsync(ct);
        foreach (var entry in current)
        {
            entry.SupersededAt = now;
            entry.UpdatedAt = now;
        }
        if (current.Count > 0) await db.SaveChangesAsync(ct);
        return current.Count;
    }

    public async Task<string> BuildPromptSummaryAsync(string userId, CancellationToken ct)
    {
        var entries = await db.CompanionMemoryEntries.AsNoTracking()
            .Where(m => m.UserId == userId && m.SupersededAt == null
                        && (m.Layer == CompanionMemoryLayers.Learning || m.Layer == CompanionMemoryLayers.Journey)
                        && m.ConfirmedAt != null)
            .OrderByDescending(m => m.RecordedAt)
            .Take(24)
            .ToListAsync(ct);
        if (entries.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## What you already know about this learner (confirmed memory)");
        foreach (var e in entries)
        {
            var layer = e.Layer == CompanionMemoryLayers.Journey ? "journey" : "learning";
            sb.AppendLine($"- [{layer}/{e.Kind}/{e.Subtest}] {e.Content} (recorded {e.RecordedAt:yyyy-MM-dd})");
        }
        sb.AppendLine("Never contradict or re-ask for these facts. Superseded values are history — quote only the newest.");
        return sb.ToString();
    }
}

/// <summary>
/// Error DNA (F-044) + spaced reinforcement (F-046): recurring evidenced weaknesses with
/// mastery tracking and scheduled re-tests. Recording is upsert-by-pattern; reviews follow a
/// simple expanding-interval ladder gated by mastery score.
/// </summary>
public interface IErrorDnaService
{
    Task<ErrorDnaEntry> RecordEvidenceAsync(string userId, string category, string pattern,
        string subtest, string? sourceKind, string? sourceId, CancellationToken ct);

    Task<IReadOnlyList<ErrorDnaEntry>> TopWeaknessesAsync(string userId, int take, CancellationToken ct);

    Task<IReadOnlyList<ErrorDnaEntry>?> GetByKeysAsync(string userId, IReadOnlyList<string> patternKeys, CancellationToken ct);

    /// <summary>Records one spaced-review outcome: correct raises mastery and pushes the next review out; a miss resets the ladder.</summary>
    Task<ErrorDnaEntry?> RecordReviewAsync(string userId, string entryId, bool correct, CancellationToken ct);

    Task<IReadOnlyList<ErrorDnaEntry>> DueReviewsAsync(string userId, int take, CancellationToken ct);

    /// <summary>Bounded prompt block of the learner's top evidenced error patterns.</summary>
    Task<string> BuildPromptSummaryAsync(string userId, CancellationToken ct);
}

public sealed class ErrorDnaService(LearnerDbContext db, TimeProvider clock) : IErrorDnaService
{
    private static readonly int[] ReviewIntervalDays = [1, 3, 7, 14, 30];

    public static string PatternKeyOf(string category, string pattern)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{category.Trim().ToLowerInvariant()}|{pattern.Trim().ToLowerInvariant()}"));
        return Convert.ToHexString(bytes)[..64];
    }

    public async Task<ErrorDnaEntry> RecordEvidenceAsync(string userId, string category, string pattern,
        string subtest, string? sourceKind, string? sourceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var key = PatternKeyOf(category, pattern);
        var entry = await db.ErrorDnaEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.PatternKey == key, ct);
        if (entry is null)
        {
            entry = new ErrorDnaEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Category = category.ToLowerInvariant(),
                Pattern = pattern.Length > 256 ? pattern[..256] : pattern,
                PatternKey = key,
                Subtest = string.IsNullOrWhiteSpace(subtest) ? "general" : subtest.ToLowerInvariant(),
                EvidenceCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now,
                MasteryScore = 0,
                ReviewCount = 0,
                NextReviewAt = now.AddDays(ReviewIntervalDays[0]),
                SourceKind = sourceKind,
                SourceId = sourceId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ErrorDnaEntries.Add(entry);
        }
        else
        {
            entry.EvidenceCount += 1;
            entry.LastSeenAt = now;
            // New evidence of the same weakness means it is NOT mastered, even if reviews passed before.
            entry.MasteryScore = Math.Max(0, entry.MasteryScore - 10);
            entry.NextReviewAt = now.AddDays(ReviewIntervalDays[0]);
            entry.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<ErrorDnaEntry>> TopWeaknessesAsync(string userId, int take, CancellationToken ct)
        => await db.ErrorDnaEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.MasteryScore < 80)
            .OrderByDescending(e => e.EvidenceCount)
            .ThenByDescending(e => e.LastSeenAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ErrorDnaEntry>?> GetByKeysAsync(string userId, IReadOnlyList<string> patternKeys, CancellationToken ct)
    {
        if (patternKeys.Count == 0) return null;
        var keys = patternKeys.Take(50).ToHashSet(StringComparer.Ordinal);
        var rows = await db.ErrorDnaEntries.AsNoTracking()
            .Where(e => e.UserId == userId && keys.Contains(e.PatternKey))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<ErrorDnaEntry?> RecordReviewAsync(string userId, string entryId, bool correct, CancellationToken ct)
    {
        var entry = await db.ErrorDnaEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Id == entryId, ct);
        if (entry is null) return null;
        var now = clock.GetUtcNow();
        entry.ReviewCount += 1;
        if (correct)
        {
            entry.MasteryScore = Math.Min(100, entry.MasteryScore + 20);
            var ladder = Math.Min(entry.MasteryScore / 20, ReviewIntervalDays.Length - 1);
            entry.NextReviewAt = now.AddDays(ReviewIntervalDays[ladder]);
        }
        else
        {
            entry.MasteryScore = Math.Max(0, entry.MasteryScore - 20);
            entry.NextReviewAt = now.AddDays(ReviewIntervalDays[0]);
        }
        entry.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<ErrorDnaEntry>> DueReviewsAsync(string userId, int take, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.ErrorDnaEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.NextReviewAt != null && e.NextReviewAt <= now && e.MasteryScore < 100)
            .OrderBy(e => e.NextReviewAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<string> BuildPromptSummaryAsync(string userId, CancellationToken ct)
    {
        var top = await TopWeaknessesAsync(userId, 8, ct);
        if (top.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine("## This learner's evidenced recurring errors (Error DNA)");
        foreach (var e in top)
        {
            sb.AppendLine($"- [{e.Category}/{e.Subtest}] {e.Pattern} — seen {e.EvidenceCount}x, mastery {e.MasteryScore}/100");
        }
        sb.AppendLine("Use ONLY these evidenced patterns when the learner asks to train on their mistakes; never invent extra weaknesses.");
        return sb.ToString();
    }
}

/// <summary>
/// Journeys (F-012/F-043): a resit opens a new active journey and the previous ones stay
/// readable for comparison. Starting a journey never overwrites academic history.
/// </summary>
public interface ICompanionJourneyService
{
    Task<CompanionJourney> GetCurrentAsync(string userId, CancellationToken ct);

    /// <summary>Starts a new journey (optionally subtest-focused) and closes the previous active one.</summary>
    Task<CompanionJourney> StartAsync(string userId, string label, DateOnly? examDate,
        IReadOnlyList<string>? focusSubtests, CancellationToken ct);

    Task<IReadOnlyList<CompanionJourney>> GetHistoryAsync(string userId, CancellationToken ct);
}

public sealed class CompanionJourneyService(LearnerDbContext db, TimeProvider clock) : ICompanionJourneyService
{
    public async Task<CompanionJourney> GetCurrentAsync(string userId, CancellationToken ct)
        => await db.CompanionJourneys.AsNoTracking()
            .Where(j => j.UserId == userId && j.IsActive)
            .OrderByDescending(j => j.StartedAt)
            .FirstOrDefaultAsync(ct)
            ?? new CompanionJourney { Id = string.Empty, UserId = userId, Label = "current preparation" };

    public async Task<CompanionJourney> StartAsync(string userId, string label, DateOnly? examDate,
        IReadOnlyList<string>? focusSubtests, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var current = await db.CompanionJourneys
            .Where(j => j.UserId == userId && j.IsActive)
            .ToListAsync(ct);
        foreach (var j in current)
        {
            j.IsActive = false;
            j.EndedAt = now;
            j.UpdatedAt = now;
        }
        var journey = new CompanionJourney
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Label = label.Length > 128 ? label[..128] : label,
            TargetExamDate = examDate,
            FocusSubtestsJson = JsonSerializer.Serialize(focusSubtests ?? []),
            StartedAt = now,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CompanionJourneys.Add(journey);
        await db.SaveChangesAsync(ct);
        return journey;
    }

    public async Task<IReadOnlyList<CompanionJourney>> GetHistoryAsync(string userId, CancellationToken ct)
        => await db.CompanionJourneys.AsNoTracking()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.StartedAt)
            .Take(20)
            .ToListAsync(ct);
}

/// <summary>
/// Study availability (F-009): per-weekday minutes, shift days and travel mode. The planner
/// (Wave 1-PLANNER) and next-best-action engine read this before fitting tasks into days.
/// </summary>
public interface ICompanionAvailabilityService
{
    Task<CompanionAvailability> GetAsync(string userId, CancellationToken ct);

    Task<CompanionAvailability> UpsertAsync(string userId, int[]? dailyMinutes, int[]? nightShiftDays,
        int[]? longDayShiftDays, bool? travelMode, int? travelMinutesPerDay, DateOnly? travelUntil,
        string? preferredStudyTime, CancellationToken ct);

    /// <summary>Study minutes available TODAY, honouring shifts and travel mode.</summary>
    Task<int> MinutesAvailableOnAsync(string userId, DateOnly date, CancellationToken ct);
}

public sealed class CompanionAvailabilityService(LearnerDbContext db, TimeProvider clock) : ICompanionAvailabilityService
{
    public async Task<CompanionAvailability> GetAsync(string userId, CancellationToken ct)
        => await db.CompanionAvailabilities.FindAsync([userId], ct)
           ?? new CompanionAvailability { UserId = userId };

    public async Task<CompanionAvailability> UpsertAsync(string userId, int[]? dailyMinutes, int[]? nightShiftDays,
        int[]? longDayShiftDays, bool? travelMode, int? travelMinutesPerDay, DateOnly? travelUntil,
        string? preferredStudyTime, CancellationToken ct)
    {
        var row = await db.CompanionAvailabilities.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (row is null)
        {
            row = new CompanionAvailability { UserId = userId };
            db.CompanionAvailabilities.Add(row);
        }
        if (dailyMinutes is { Length: 7 })
        {
            if (dailyMinutes.Any(m => m is < 0 or > 600))
                throw new ArgumentException("dailyMinutes must be 7 values between 0 and 600.");
            row.DailyMinutesJson = JsonSerializer.Serialize(dailyMinutes);
        }
        if (nightShiftDays is not null)
        {
            if (nightShiftDays.Any(d => d is < 0 or > 6))
                throw new ArgumentException("nightShiftDays must be weekday indices 0-6 (Monday=0).");
            row.NightShiftDaysJson = JsonSerializer.Serialize(nightShiftDays.Distinct().OrderBy(d => d));
        }
        if (longDayShiftDays is not null)
        {
            if (longDayShiftDays.Any(d => d is < 0 or > 6))
                throw new ArgumentException("longDayShiftDays must be weekday indices 0-6 (Monday=0).");
            row.LongDayShiftDaysJson = JsonSerializer.Serialize(longDayShiftDays.Distinct().OrderBy(d => d));
        }
        if (travelMode is not null) row.TravelMode = travelMode.Value;
        if (travelMinutesPerDay is not null)
        {
            if (travelMinutesPerDay is < 0 or > 600)
                throw new ArgumentException("travelMinutesPerDay must be between 0 and 600.");
            row.TravelModeMinutesPerDay = travelMinutesPerDay.Value;
        }
        if (travelUntil is not null) row.TravelUntil = travelUntil;
        if (preferredStudyTime is not null)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(preferredStudyTime, @"^([01]\d|2[0-3]):[0-5]\d$"))
                throw new ArgumentException("preferredStudyTime must be hh:mm (24h).");
            row.PreferredStudyTime = preferredStudyTime;
        }
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<int> MinutesAvailableOnAsync(string userId, DateOnly date, CancellationToken ct)
    {
        var row = await GetAsync(userId, ct);
        var index = ((int)date.DayOfWeek + 6) % 7; // Monday=0
        if (row.TravelMode) return row.TravelModeMinutesPerDay;
        var minutes = JsonSerializer.Deserialize<int[]>(row.DailyMinutesJson) is { Length: 7 } arr ? arr[index] : 0;
        var nights = JsonSerializer.Deserialize<int[]>(row.NightShiftDaysJson) ?? [];
        var longDays = JsonSerializer.Deserialize<int[]>(row.LongDayShiftDaysJson) ?? [];
        if (nights.Contains(index)) return 0; // night shift: recovery day, not a study day
        if (longDays.Contains(index)) return minutes / 2; // long day shift: halve the capacity
        return minutes;
    }
}
