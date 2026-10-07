using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Tutor/support handoffs (F-107/F-108/F-123): prepare from real learner
/// evidence, never from guesses; show the learner the summary; route to the
/// right queue; give tutors a lifecycle.
/// </summary>
public interface ICompanionHandoffService
{
    Task<CompanionHandoff> CreateAsync(string userId, string threadId, string route, string issue, CancellationToken ct);

    Task<IReadOnlyList<CompanionHandoff>> ListForUserAsync(string userId, int take, CancellationToken ct);

    /// <summary>Admin/tutor queue: open (then claimed) handoffs, oldest first.</summary>
    Task<IReadOnlyList<CompanionHandoff>> ListOpenAsync(string? route, int take, CancellationToken ct);

    Task<CompanionHandoff?> SetStatusAsync(string id, string status, string? handledBy, CancellationToken ct);
}

public sealed class CompanionHandoffService(
    LearnerDbContext db,
    ICompanionMemoryService memory,
    IErrorDnaService errorDna,
    TimeProvider clock) : ICompanionHandoffService
{
    public async Task<CompanionHandoff> CreateAsync(string userId, string threadId, string route, string issue, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        route = route.Trim().ToLowerInvariant() is "support" ? "support" : "tutor";

        var scoreEntries = await memory.GetCurrentAsync(userId, CompanionMemoryLayers.Learning, ct);
        var scores = scoreEntries
            .Where(m => m.Kind is "score" or "test_date" || m.Subtest != "general")
            .Where(m => m.ConfirmedAt != null)
            .OrderByDescending(m => m.RecordedAt)
            .Take(8)
            .Select(m => new { kind = m.Kind, subtest = m.Subtest, content = m.Content, recordedAt = m.RecordedAt })
            .ToList();
        var weaknesses = await errorDna.TopWeaknessesAsync(userId, 5, ct);

        var summary = new System.Text.StringBuilder();
        summary.AppendLine(route == "support"
            ? "SUPPORT HANDOFF — prepared by Sami from the learner's live context."
            : "TUTOR HANDOFF — prepared by Sami from the learner's live context.");
        summary.AppendLine();
        summary.AppendLine($"Issue (learner's words): {issue}");
        if (scores.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Latest recorded scores/facts:");
            foreach (var s in scores) summary.AppendLine($"  - [{s.kind}/{s.subtest}] {s.content} ({s.recordedAt:yyyy-MM-dd})");
        }
        if (weaknesses.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Evidenced recurring errors:");
            foreach (var w in weaknesses) summary.AppendLine($"  - [{w.Category}/{w.Subtest}] {w.Pattern} — seen {w.EvidenceCount}x, mastery {w.MasteryScore}/100");
        }
        summary.AppendLine();
        summary.AppendLine($"Chat thread for full context: {threadId}");

        var handoff = new CompanionHandoff
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Route = route,
            ThreadId = threadId,
            Issue = issue.Length > 1024 ? issue[..1024] : issue,
            Summary = summary.ToString(),
            ScoresJson = JsonSerializer.Serialize(scores),
            TopErrorsJson = JsonSerializer.Serialize(weaknesses.Select(w => new
            {
                category = w.Category,
                subtest = w.Subtest,
                pattern = w.Pattern,
                evidenceCount = w.EvidenceCount,
                mastery = w.MasteryScore,
            })),
            Status = "open",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CompanionHandoffs.Add(handoff);
        await db.SaveChangesAsync(ct);
        return handoff;
    }

    public async Task<IReadOnlyList<CompanionHandoff>> ListForUserAsync(string userId, int take, CancellationToken ct)
        => await db.CompanionHandoffs.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CompanionHandoff>> ListOpenAsync(string? route, int take, CancellationToken ct)
    {
        var query = db.CompanionHandoffs.AsNoTracking()
            .Where(h => h.Status != "resolved");
        if (!string.IsNullOrWhiteSpace(route))
            query = query.Where(h => h.Route == route.Trim().ToLowerInvariant());
        return await query
            .OrderBy(h => h.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<CompanionHandoff?> SetStatusAsync(string id, string status, string? handledBy, CancellationToken ct)
    {
        var handoff = await db.CompanionHandoffs.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (handoff is null) return null;
        handoff.Status = status.Trim().ToLowerInvariant() switch
        {
            "open" => "open",
            "claimed" => "claimed",
            _ => "resolved",
        };
        handoff.HandledBy = handledBy;
        handoff.HandledAt = handoff.Status == "resolved" ? clock.GetUtcNow() : handoff.HandledAt;
        handoff.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return handoff;
    }
}
