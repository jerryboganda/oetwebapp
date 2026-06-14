using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Expert;

/// <summary>
/// Load-balanced auto-assigner for Writing expert reviews (spec gap #2).
/// Two responsibilities:
/// <list type="bullet">
///   <item><c>ProcessPendingAssignmentsAsync</c> — find writing review
///     requests with no active assignment and pick the lowest-loaded
///     competent expert for each one.</item>
///   <item><c>ProcessSlaEscalationsAsync</c> — find assignments past the
///     turnaround SLA (48h standard / 12h express) whose expert hasn't
///     submitted a draft yet, release them, and re-pool for the assigner.</item>
/// </list>
/// Both methods are idempotent — safe to call from the background job loop
/// and from inline test harnesses. No expert-specialty data means
/// "generalist", i.e. eligible for every profession; the load balancer
/// becomes purely active-load based until specialties are seeded.
/// </summary>
public interface IExpertAutoAssignmentService
{
    Task<int> ProcessPendingAssignmentsAsync(CancellationToken ct);
    Task<int> ProcessSlaEscalationsAsync(CancellationToken ct);
}

/// <summary>Notification surface the auto-assigner depends on. Lets unit
/// tests substitute a stub without standing up the full NotificationService.</summary>
public interface IExpertAssignmentNotifier
{
    Task NotifyAssignedAsync(string expertUserId, string reviewRequestId, string? professionId, string turnaroundOption, DateTimeOffset slaDueAt, CancellationToken ct);
    Task NotifyReleasedAsync(string expertUserId, string reviewRequestId, string reason, DateTimeOffset slaDueAt, CancellationToken ct);
}

public sealed class ExpertAssignmentNotifier(NotificationService notifications) : IExpertAssignmentNotifier
{
    public Task NotifyAssignedAsync(string expertUserId, string reviewRequestId, string? professionId, string turnaroundOption, DateTimeOffset slaDueAt, CancellationToken ct)
        => notifications.CreateForExpertAsync(
            NotificationEventKey.ExpertReviewAssigned,
            expertUserId,
            "review_request",
            reviewRequestId,
            slaDueAt.UtcTicks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = reviewRequestId,
                ["professionId"] = professionId,
                ["turnaroundOption"] = turnaroundOption,
                ["slaDueAt"] = slaDueAt.ToString("o"),
                ["assignedBy"] = "system:auto-assign",
            }, ct);

    public Task NotifyReleasedAsync(string expertUserId, string reviewRequestId, string reason, DateTimeOffset slaDueAt, CancellationToken ct)
        => notifications.CreateForExpertAsync(
            NotificationEventKey.ExpertReviewReleased,
            expertUserId,
            "review_request",
            reviewRequestId,
            slaDueAt.UtcTicks.ToString(),
            new Dictionary<string, object?>
            {
                ["reviewRequestId"] = reviewRequestId,
                ["reason"] = reason,
                ["slaDueAt"] = slaDueAt.ToString("o"),
            }, ct);
}

public sealed class ExpertAutoAssignmentService(
    LearnerDbContext db,
    TimeProvider clock,
    IExpertAssignmentNotifier notifier,
    ILogger<ExpertAutoAssignmentService> logger,
    IRuntimeSettingsProvider runtimeSettings)
    : IExpertAutoAssignmentService
{
    public async Task<int> ProcessPendingAssignmentsAsync(CancellationToken ct)
    {
        var opts = (await runtimeSettings.GetAsync(ct)).ExpertAutoAssignment;
        if (!opts.Enabled) return 0;

        var now = clock.GetUtcNow();
        var lookbackStart = now.AddHours(-opts.LookbackHoursForLoad);

        // Pending writing review requests that have NO non-Released assignment yet.
        var pendingRequests = await db.ReviewRequests
            .AsNoTracking()
            .Where(r => r.SubtestCode == "writing"
                     && (r.State == ReviewRequestState.Queued || r.State == ReviewRequestState.Submitted)
                     && !db.ExpertReviewAssignments
                        .Any(a => a.ReviewRequestId == r.Id && a.ClaimState != ExpertAssignmentState.Released))
            .OrderBy(r => r.CreatedAt)
            .Take(opts.BatchSize)
            .Select(r => new { r.Id, r.AttemptId, r.TurnaroundOption })
            .ToListAsync(ct);

        if (pendingRequests.Count == 0) return 0;

        // Pre-load eligible experts and their current load tallies in a few
        // bulk queries — avoids N+1 across the batch.
        var experts = await db.ExpertUsers
            .AsNoTracking()
            .Select(u => new { u.Id, u.SpecialtiesJson })
            .ToListAsync(ct);
        if (experts.Count == 0)
        {
            logger.LogWarning("ExpertAutoAssignment: no active experts available; {Count} requests stay unassigned.", pendingRequests.Count);
            return 0;
        }

        var activeCounts = await db.ExpertReviewAssignments
            .AsNoTracking()
            .Where(a => a.AssignedReviewerId != null
                     && (a.ClaimState == ExpertAssignmentState.Assigned
                         || a.ClaimState == ExpertAssignmentState.Claimed))
            .GroupBy(a => a.AssignedReviewerId!)
            .Select(g => new { ExpertId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ExpertId, x => x.Count, ct);

        var completedRecently = await db.ExpertReviewDrafts
            .AsNoTracking()
            .Where(d => d.State == "submitted" && d.DraftSavedAt >= lookbackStart)
            .GroupBy(d => d.ReviewerId)
            .Select(g => new { ExpertId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ExpertId, x => x.Count, ct);

        // Attempt → profession lookup. We pull profession from the
        // ContentPaper linked via Attempt.ContentId. Bulk query for the batch.
        var attemptIds = pendingRequests.Select(r => r.AttemptId).ToList();
        var attemptToProfession = await db.Attempts
            .AsNoTracking()
            .Where(a => attemptIds.Contains(a.Id))
            .Join(db.ContentPapers.AsNoTracking(),
                a => a.ContentId, p => p.Id,
                (a, p) => new { a.Id, p.ProfessionId })
            .ToDictionaryAsync(x => x.Id, x => x.ProfessionId, ct);

        var assignedCount = 0;
        foreach (var request in pendingRequests)
        {
            ct.ThrowIfCancellationRequested();

            attemptToProfession.TryGetValue(request.AttemptId, out var professionId);
            var eligible = experts
                .Where(e => SupportsProfession(e.SpecialtiesJson, professionId))
                .Where(e => GetCount(activeCounts, e.Id) < opts.MaxActiveAssignmentsPerExpert)
                .OrderBy(e => GetCount(activeCounts, e.Id) + GetCount(completedRecently, e.Id))
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (eligible is null)
            {
                logger.LogWarning(
                    "ExpertAutoAssignment: no eligible expert for review {ReviewId} (profession {Profession}); will retry next cycle.",
                    request.Id, professionId ?? "(none)");
                continue;
            }

            var assignment = new ExpertReviewAssignment
            {
                Id = $"era-{Guid.NewGuid():N}",
                ReviewRequestId = request.Id,
                AssignedReviewerId = eligible.Id,
                AssignedBy = "system:auto-assign",
                AssignedAt = now,
                ClaimState = ExpertAssignmentState.Assigned,
                ReasonCode = "auto_assign",
            };
            db.ExpertReviewAssignments.Add(assignment);

            // Update in-memory tally so the next iteration in this batch
            // sees the new load — keeps batches fair under bursty input.
            activeCounts[eligible.Id] = GetCount(activeCounts, eligible.Id) + 1;

            // Notify the expert.
            var slaHours = string.Equals(request.TurnaroundOption, "express", StringComparison.OrdinalIgnoreCase)
                ? opts.SlaHoursExpress : opts.SlaHoursStandard;
            var slaDueAt = now.AddHours(slaHours);
            await notifier.NotifyAssignedAsync(eligible.Id, request.Id, professionId, request.TurnaroundOption, slaDueAt, ct);

            assignedCount++;
        }

        if (assignedCount > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return assignedCount;
    }

    public async Task<int> ProcessSlaEscalationsAsync(CancellationToken ct)
    {
        var opts = (await runtimeSettings.GetAsync(ct)).ExpertAutoAssignment;
        if (!opts.Enabled) return 0;

        var now = clock.GetUtcNow();

        // Candidates: active assignments whose request hasn't been completed
        // and where no submitted draft exists yet.
        var candidates = await (
            from a in db.ExpertReviewAssignments
            where a.AssignedReviewerId != null
               && (a.ClaimState == ExpertAssignmentState.Assigned
                   || a.ClaimState == ExpertAssignmentState.Claimed)
            join r in db.ReviewRequests on a.ReviewRequestId equals r.Id
            where r.SubtestCode == "writing"
               && r.State != ReviewRequestState.Completed
            select new { Assignment = a, Request = r })
            .Take(opts.BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var released = 0;
        foreach (var c in candidates)
        {
            if (c.Assignment.AssignedAt is null) continue;
            var slaHours = string.Equals(c.Request.TurnaroundOption, "express", StringComparison.OrdinalIgnoreCase)
                ? opts.SlaHoursExpress : opts.SlaHoursStandard;
            var deadline = c.Assignment.AssignedAt.Value.AddHours(slaHours);
            if (deadline >= now) continue;

            // Skip if the expert has already submitted a draft for this request.
            var submitted = await db.ExpertReviewDrafts.AsNoTracking()
                .AnyAsync(d => d.ReviewRequestId == c.Request.Id
                            && d.ReviewerId == c.Assignment.AssignedReviewerId
                            && d.State == "submitted", ct);
            if (submitted) continue;

            var previousReviewerId = c.Assignment.AssignedReviewerId!;
            c.Assignment.ClaimState = ExpertAssignmentState.Released;
            c.Assignment.ReleasedAt = now;
            c.Assignment.ReasonCode = "sla_overdue";

            db.ExpertSlaSnapshots.Add(new ExpertSlaSnapshot
            {
                Id = $"sla-{Guid.NewGuid():N}",
                ReviewRequestId = c.Request.Id,
                ExpertId = previousReviewerId,
                SlaDueAt = deadline,
                WasMet = false,
                SlaState = "overdue",
                CreatedAt = now,
            });

            await notifier.NotifyReleasedAsync(previousReviewerId, c.Request.Id, "sla_overdue", deadline, ct);

            released++;
        }

        if (released > 0)
        {
            await db.SaveChangesAsync(ct);
            // Re-pool the now-unassigned requests by inline-calling the assigner.
            await ProcessPendingAssignmentsAsync(ct);
        }
        return released;
    }

    private static bool SupportsProfession(string? specialtiesJson, string? professionId)
    {
        // No profession on the underlying paper → treat as any expert can take it.
        if (string.IsNullOrWhiteSpace(professionId)) return true;
        if (string.IsNullOrWhiteSpace(specialtiesJson)) return true;
        try
        {
            using var doc = JsonDocument.Parse(specialtiesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return true;
            var list = doc.RootElement.EnumerateArray()
                .Where(el => el.ValueKind == JsonValueKind.String)
                .Select(el => el.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            // Empty list → generalist.
            if (list.Count == 0) return true;
            return list.Any(s => string.Equals(s, professionId, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            // Malformed JSON treated as generalist — fail open rather than blocking the queue.
            return true;
        }
    }

    private static int GetCount(IReadOnlyDictionary<string, int> dict, string key)
        => dict.TryGetValue(key, out var n) ? n : 0;
}
