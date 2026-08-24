using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed record LearnerAttemptHistoryItem(
    string AttemptId,
    string Subtest,
    string Title,
    string? ContentRef,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    string Status,
    string? BalanceSource,
    int CreditsUsed,
    string Route);

public sealed record LearnerAttemptHistoryResponse(IReadOnlyList<LearnerAttemptHistoryItem> Items);

public interface ILearnerAttemptHistoryService
{
    Task<LearnerAttemptHistoryResponse> GetHistoryAsync(string userId, int limit, CancellationToken ct);
}

/// <summary>
/// Mandatory candidate activity history covering all four subtests plus full
/// mocks (Master Catalogue §2): exact item title/ID, subtest, started
/// date/time, In progress/Completed status, balance source, credits used and
/// a Resume/Reopen/Review route. Reopening the same attempt never deducts
/// again — this view only reads.
/// </summary>
public sealed class LearnerAttemptHistoryService(LearnerDbContext db) : ILearnerAttemptHistoryService
{
    public async Task<LearnerAttemptHistoryResponse> GetHistoryAsync(string userId, int limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, 200);
        var now = DateTimeOffset.UtcNow;

        // Generic attempts (legacy reading/listening + writing/speaking).
        var generic = await db.Attempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, row.ContentId, row.SubtestCode, row.StartedAt, row.SubmittedAt, row.State })
            .ToListAsync(ct);

        // Relational Reading attempts (paper-first module).
        var readingAttempts = await db.ReadingAttempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, PaperId = row.PaperId, row.StartedAt, row.SubmittedAt, Status = (int)row.Status })
            .ToListAsync(ct);

        // Relational Listening attempts.
        var listeningAttempts = await db.ListeningAttempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, PaperId = row.PaperId, row.StartedAt, row.SubmittedAt, Status = (int)row.Status })
            .ToListAsync(ct);

        var mockAttempts = await db.MockAttempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, row.MockType, row.SubtestCode, row.StartedAt, row.SubmittedAt, State = (int)row.State })
            .ToListAsync(ct);

        // Title resolution.
        var contentIds = generic.Select(row => row.ContentId).Distinct().ToList();
        var paperIds = readingAttempts.Select(row => row.PaperId)
            .Concat(listeningAttempts.Select(row => row.PaperId))
            .Distinct().ToList();
        var contentTitles = await db.ContentItems.AsNoTracking()
            .Where(item => contentIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Title })
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
        var paperTitles = await db.ContentPapers.AsNoTracking()
            .Where(paper => paperIds.Contains(paper.Id))
            .Select(paper => new { paper.Id, paper.Title })
            .ToDictionaryAsync(paper => paper.Id, paper => paper.Title, ct);

        // Credit enrichment from the same ledger the admin sees.
        var debitRefs = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && (row.Reason == AiPackageCreditReason.ObjectivePracticeDeduct
                              || row.Reason == AiPackageCreditReason.GradingDeduct
                              || row.Reason == AiPackageCreditReason.MockDeduct))
            .OrderByDescending(row => row.CreatedAt)
            .Take(600)
            .Select(row => new DebitRow(
                row.ReferenceId,
                row.PackageType,
                row.SharedCreditsDelta,
                row.FlexibleCreditsDelta,
                row.WritingOnlyCreditsDelta,
                row.SpeakingOnlyCreditsDelta,
                row.ListeningTestsDelta,
                row.ReadingTestsDelta,
                row.MockExamsDelta))
            .ToListAsync(ct);

        var items = new List<LearnerAttemptHistoryItem>(generic.Count + readingAttempts.Count + listeningAttempts.Count + mockAttempts.Count);

        foreach (var row in generic)
        {
            var subtest = row.SubtestCode.ToLowerInvariant();
            items.Add(new LearnerAttemptHistoryItem(
                row.Id,
                subtest,
                contentTitles.TryGetValue(row.ContentId, out var title) ? title : row.ContentId,
                row.ContentId,
                row.StartedAt,
                row.SubmittedAt,
                MapGenericState(row.State),
                ResolveBalanceSource(debitRefs, subtest, row.ContentId),
                CountDebitedCredits(debitRefs, subtest, row.ContentId),
                RouteFor(subtest, row.ContentId, row.Id)));
        }

        foreach (var row in readingAttempts)
        {
            items.Add(new LearnerAttemptHistoryItem(
                row.Id,
                "reading",
                paperTitles.TryGetValue(row.PaperId, out var title) ? title : row.PaperId,
                row.PaperId,
                row.StartedAt,
                row.SubmittedAt,
                row.Status == 0 ? "in_progress" : "completed",
                ResolveBalanceSource(debitRefs, "reading", row.PaperId),
                CountDebitedCredits(debitRefs, "reading", row.PaperId),
                $"/reading/paper/{Uri.EscapeDataString(row.PaperId)}?attemptId={Uri.EscapeDataString(row.Id)}"));
        }

        foreach (var row in listeningAttempts)
        {
            items.Add(new LearnerAttemptHistoryItem(
                row.Id,
                "listening",
                paperTitles.TryGetValue(row.PaperId, out var title) ? title : row.PaperId,
                row.PaperId,
                row.StartedAt,
                row.SubmittedAt,
                row.Status == 0 ? "in_progress" : "completed",
                ResolveBalanceSource(debitRefs, "listening", row.PaperId),
                CountDebitedCredits(debitRefs, "listening", row.PaperId),
                $"/listening/paper/{Uri.EscapeDataString(row.PaperId)}?attemptId={Uri.EscapeDataString(row.Id)}"));
        }

        foreach (var row in mockAttempts)
        {
            var label = string.IsNullOrWhiteSpace(row.SubtestCode)
                ? $"Full Mock ({row.MockType})"
                : $"Full Mock — {row.SubtestCode} section";
            items.Add(new LearnerAttemptHistoryItem(
                row.Id,
                "mock",
                label,
                row.Id,
                row.StartedAt,
                row.SubmittedAt,
                row.State >= 1 && row.State <= 2 ? "in_progress" : "completed",
                "mock",
                row.SubtestCode is null ? 1 : 0,
                "/mocks"));
        }

        return new LearnerAttemptHistoryResponse(items
            .OrderByDescending(item => item.StartedAt)
            .Take(take)
            .ToList());
    }

    private static string MapGenericState(AttemptState state)
        => state == AttemptState.InProgress || state == AttemptState.NotStarted || state == AttemptState.Paused
            ? "in_progress"
            : "completed";

    private static string RouteFor(string subtest, string contentId, string attemptId)
        => subtest switch
        {
            "reading" => $"/reading/paper/{Uri.EscapeDataString(contentId)}?attemptId={Uri.EscapeDataString(attemptId)}",
            "listening" => $"/listening/paper/{Uri.EscapeDataString(contentId)}?attemptId={Uri.EscapeDataString(attemptId)}",
            "writing" => $"/writing/practice/session/{Uri.EscapeDataString(contentId)}?attemptId={Uri.EscapeDataString(attemptId)}",
            "speaking" => "/speaking",
            _ => "/submissions",
        };

    private static (string? Source, int Credits)? MatchDebit(List<DebitRow> debitRefs, string subtest, string contentId)
    {
        foreach (var row in debitRefs)
        {
            if (!string.Equals((row.PackageType ?? string.Empty).ToLowerInvariant(), subtest, StringComparison.Ordinal))
            {
                continue;
            }

            if (row.ReferenceId?.Contains(contentId, StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            var credits =
                Math.Abs(Math.Min(0, row.SharedCreditsDelta))
                + Math.Abs(Math.Min(0, row.FlexibleCreditsDelta))
                + Math.Abs(Math.Min(0, row.WritingOnlyCreditsDelta))
                + Math.Abs(Math.Min(0, row.SpeakingOnlyCreditsDelta))
                + Math.Abs(Math.Min(0, row.ListeningTestsDelta))
                + Math.Abs(Math.Min(0, row.ReadingTestsDelta))
                + Math.Abs(Math.Min(0, row.MockExamsDelta));
            return (ResolveSourceLabel(row), credits);
        }

        return null;
    }

    private static string? ResolveSourceLabel(DebitRow row)
    {
        if (row.MockExamsDelta < 0) return "mock";
        if (row.SharedCreditsDelta < 0) return "shared";
        if (row.FlexibleCreditsDelta < 0) return "flexible_ws";
        if (row.WritingOnlyCreditsDelta < 0 || row.SpeakingOnlyCreditsDelta < 0) return "dedicated";
        if (row.ListeningTestsDelta < 0) return "listening";
        if (row.ReadingTestsDelta < 0) return "reading";
        return null;
    }

    private static string? ResolveBalanceSource(List<DebitRow> debitRefs, string subtest, string contentId)
        => MatchDebit(debitRefs, subtest, contentId)?.Source;

    private static int CountDebitedCredits(List<DebitRow> debitRefs, string subtest, string contentId)
        => MatchDebit(debitRefs, subtest, contentId)?.Credits ?? 0;

    private sealed record DebitRow(
        string? ReferenceId,
        string? PackageType,
        int SharedCreditsDelta,
        int FlexibleCreditsDelta,
        int WritingOnlyCreditsDelta,
        int SpeakingOnlyCreditsDelta,
        int ListeningTestsDelta,
        int ReadingTestsDelta,
        int MockExamsDelta);
}
