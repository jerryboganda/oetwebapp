using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Services.Planner;

/// <summary>What the shaper changed, for honest tool/endpoint feedback.</summary>
public sealed record AvailabilityShaperReport(
    int MovedItems,
    int DroppedItems,
    int ProtectedItems);

/// <summary>
/// Fits a freshly generated plan to the learner's REAL week (SAMI §3.3/§5.1, F-009, F-036,
/// F-037, F-038): tasks on night-shift days move to the next day that still has capacity,
/// long-shift days keep at most half, and travel mode drops new-content items while keeping
/// spaced reviews. Never moves Completed/InProgress work — recovery only touches NotStarted.
/// </summary>
public interface IStudyPlanAvailabilityShaper
{
    Task<AvailabilityShaperReport> ShapeAsync(string userId, string planId, CancellationToken ct);
}

public sealed class StudyPlanAvailabilityShaper(
    LearnerDbContext db,
    ICompanionAvailabilityService availability,
    TimeProvider clock) : IStudyPlanAvailabilityShaper
{
    private static readonly HashSet<string> NewContentSlotKinds = new(StringComparer.Ordinal)
    {
        "next-unattempted-paper",
        "full-mock",
        "expert-review-submission",
    };

    public async Task<AvailabilityShaperReport> ShapeAsync(string userId, string planId, CancellationToken ct)
    {
        var row = await availability.GetAsync(userId, ct);
        var items = await db.StudyPlanItems
            .Where(i => i.StudyPlanId == planId && i.Status == StudyPlanItemStatus.NotStarted)
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.PriorityScore)
            .ToListAsync(ct);
        if (items.Count == 0) return new AvailabilityShaperReport(0, 0, 0);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var moved = 0;
        var dropped = 0;

        // Capacity per date, computed lazily and reduced as items are assigned.
        var capacity = new Dictionary<DateOnly, int>();
        var load = new Dictionary<DateOnly, int>();

        async Task<int> CapacityOnAsync(DateOnly date)
        {
            if (capacity.TryGetValue(date, out var cap)) return cap;
            cap = Math.Max(0, await availability.MinutesAvailableOnAsync(userId, date, ct));
            capacity[date] = cap;
            return cap;
        }

        foreach (var item in items)
        {
            if (row.TravelMode && NewContentSlotKinds.Contains(item.SlotKind ?? string.Empty))
            {
                db.StudyPlanItems.Remove(item);
                dropped += 1;
                continue;
            }

            var due = item.DueDate;
            var cap = await CapacityOnAsync(due);
            if (cap > 0 && load.GetValueOrDefault(due) + item.DurationMinutes <= cap) continue; // fits as generated

            // Walk forward for the first date (within 10 days) with room.
            var candidate = due;
            DateOnly? target = null;
            for (var step = 1; step <= 10; step += 1)
            {
                candidate = candidate.AddDays(1);
                if (candidate > today.AddDays(90)) break;
                var candidateCap = await CapacityOnAsync(candidate);
                if (candidateCap <= 0) continue; // another no-study day (night shift)
                if (load.GetValueOrDefault(candidate) + item.DurationMinutes <= candidateCap)
                {
                    target = candidate;
                    break;
                }
            }

            if (target is { } newDate)
            {
                item.DueDate = newDate;
                item.Status = StudyPlanItemStatus.Rescheduled;
                load[newDate] = load.GetValueOrDefault(newDate) + item.DurationMinutes;
                moved += 1;
            }
            else
            {
                // No capacity anywhere nearby: mark rescheduled onto the last day rather than
                // deleting evidence of the task — the replanner reviews Rescheduled items first.
                item.DueDate = item.DueDate.AddDays(10);
                item.Status = StudyPlanItemStatus.Rescheduled;
                moved += 1;
            }
        }

        await db.SaveChangesAsync(ct);
        return new AvailabilityShaperReport(moved, dropped, items.Count - moved - dropped);
    }
}
