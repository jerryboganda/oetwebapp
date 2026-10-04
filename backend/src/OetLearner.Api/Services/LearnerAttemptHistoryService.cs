using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services;

/// <param name="ResultLabel">Speaking only: "{n}/500" once scored, "Marking in progress" while the
/// card or exam is finished but not graded yet, otherwise null. Never a raw state token.</param>
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
    string Route,
    string? ResultLabel = null);

public sealed record LearnerAttemptHistoryResponse(IReadOnlyList<LearnerAttemptHistoryItem> Items);

public interface ILearnerAttemptHistoryService
{
    Task<LearnerAttemptHistoryResponse> GetHistoryAsync(string userId, int limit, string? subtest, CancellationToken ct);
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
    private const string SpeakingMockTitle = "Full Speaking Mock";
    private const string MarkingInProgress = "Marking in progress";

    public async Task<LearnerAttemptHistoryResponse> GetHistoryAsync(string userId, int limit, string? subtest, CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, 200);
        var now = DateTimeOffset.UtcNow;
        // Normalized once and pushed into every per-table Where below, ahead of
        // each table's own Take(take). This is the fix for the truncation bug:
        // filtering AFTER the union (i.e. only in the final re-sort/Take) would
        // let a subtest with >take unrelated attempts crowd the requested
        // subtest's older rows out of the per-table page before they ever reach
        // the filter.
        var subtestFilter = string.IsNullOrWhiteSpace(subtest) ? null : subtest.Trim().ToLowerInvariant();

        // Generic attempts (legacy reading/listening + writing/speaking). The two
        // card attempts of a Speaking exam are left out HERE, in SQL and ahead of
        // the Take: the exam is listed as ONE row below, and collapsing after the
        // Take would let a mock show as two rows and eat the page size.
        var generic = await db.Attempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Where(row => subtestFilter == null || row.SubtestCode.ToLower() == subtestFilter)
            .Where(row => !db.SpeakingSessions.Any(s => s.UserId == userId
                && s.AttemptId == row.Id
                && s.ExamSessionId != null))
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, row.ContentId, row.SubtestCode, row.StartedAt, row.SubmittedAt, row.State })
            .ToListAsync(ct);

        // Full Speaking mocks: one row per exam that has begun a card (so a child
        // attempt exists). Ordered by CreatedAt, which equals IntroStartedAt.
        var exams = (subtestFilter is null || subtestFilter == "speaking")
            ? await db.SpeakingExamSessions.AsNoTracking()
                .Where(e => e.UserId == userId)
                .Where(e => db.SpeakingSessions.Any(s => s.ExamSessionId == e.Id))
                .OrderByDescending(e => e.CreatedAt)
                .Take(take)
                .Select(e => new ExamRow(
                    e.Id, e.Mode, e.State, e.CreatedAt, e.IntroStartedAt, e.CompletedAt,
                    e.CombinedScaledSnapshot, e.SessionAId, e.SessionBId))
                .ToListAsync(ct)
            : new List<ExamRow>();

        // Relational Reading attempts (paper-first module) — this table is only
        // ever "reading", so any other filter value excludes it entirely.
        var readingAttempts = await db.ReadingAttempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Where(row => subtestFilter == null || subtestFilter == "reading")
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, PaperId = row.PaperId, row.StartedAt, row.SubmittedAt, Status = (int)row.Status })
            .ToListAsync(ct);

        // Relational Listening attempts.
        var listeningAttempts = await db.ListeningAttempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Where(row => subtestFilter == null || subtestFilter == "listening")
            .OrderByDescending(row => row.StartedAt)
            .Take(take)
            .Select(row => new { row.Id, PaperId = row.PaperId, row.StartedAt, row.SubmittedAt, Status = (int)row.Status })
            .ToListAsync(ct);

        var mockAttempts = await db.MockAttempts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Where(row => subtestFilter == null || subtestFilter == "mock")
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

        // Standalone Speaking cards (live voice or recorder): find each one's session, which
        // decides its route, its ledger reference and its result.
        var speakingAttemptIds = generic
            .Where(row => string.Equals(row.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Id)
            .ToList();
        var sessionByAttempt = new Dictionary<string, SessionRow>(StringComparer.Ordinal);
        if (speakingAttemptIds.Count > 0)
        {
            var sessions = await db.SpeakingSessions.AsNoTracking()
                .Where(s => s.UserId == userId && s.AttemptId != null && speakingAttemptIds.Contains(s.AttemptId))
                .Select(s => new SessionRow(s.Id, s.AttemptId, s.Mode, s.State, s.SubmittedAt))
                .ToListAsync(ct);
            foreach (var sessionRow in sessions)
            {
                if (sessionRow.AttemptId is not null)
                {
                    sessionByAttempt.TryAdd(sessionRow.AttemptId, sessionRow);
                }
            }
        }

        var scoredSessionIds = sessionByAttempt.Values.Select(sessionRow => sessionRow.Id)
            .Concat(exams.SelectMany(examRow => new[] { examRow.SessionAId, examRow.SessionBId }).OfType<string>())
            .Distinct()
            .ToList();
        var cardScores = scoredSessionIds.Count == 0
            ? new Dictionary<string, int>()
            : await LoadCardScoresAsync(scoredSessionIds, ct);

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

        // A Speaking hold that is released (cancelled, never graded, or swept after 24 h) leaves a refund row
        // "{reference}:release"; it is netted off below so History never says a refunded mock cost 4.
        var speakingRefunds = await db.AiPackageCreditTransactions.AsNoTracking()
            .Where(row => row.UserId == userId
                          && row.Reason == AiPackageCreditReason.RefundOnFailure
                          && row.ReferenceId != null
                          && (row.ReferenceId.StartsWith("exam:") || row.ReferenceId.StartsWith("practice:")))
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

        var items = new List<LearnerAttemptHistoryItem>(
            generic.Count + readingAttempts.Count + listeningAttempts.Count + mockAttempts.Count + exams.Count);

        foreach (var row in generic)
        {
            var subtestCode = row.SubtestCode.ToLowerInvariant();
            var title = contentTitles.TryGetValue(row.ContentId, out var contentTitle) ? contentTitle : row.ContentId;

            if (sessionByAttempt.TryGetValue(row.Id, out var card))
            {
                // The hold for a practice card is "practice:{sessionId}"; the content id never appears in it.
                var resultsReady = card.State == SpeakingSessionState.Finished
                    || card.SubmittedAt is not null
                    || row.SubmittedAt is not null;
                var cardRoute = $"/speaking/sessions/{Uri.EscapeDataString(card.Id)}";
                var cardDebit = MatchDebitByReference(debitRefs, speakingRefunds, SpeakingCreditSettlement.PracticeReference(card.Id));
                items.Add(new LearnerAttemptHistoryItem(
                    row.Id,
                    subtestCode,
                    title,
                    row.ContentId,
                    row.StartedAt,
                    row.SubmittedAt,
                    MapGenericState(row.State),
                    cardDebit?.Source,
                    cardDebit?.Credits ?? 0,
                    resultsReady ? $"{cardRoute}/results" : cardRoute,
                    PracticeResultLabel(card, resultsReady, cardScores)));
                continue;
            }

            items.Add(new LearnerAttemptHistoryItem(
                row.Id,
                subtestCode,
                title,
                row.ContentId,
                row.StartedAt,
                row.SubmittedAt,
                MapGenericState(row.State),
                ResolveBalanceSource(debitRefs, subtestCode, row.ContentId),
                CountDebitedCredits(debitRefs, subtestCode, row.ContentId),
                RouteFor(subtestCode, row.ContentId, row.Id)));
        }

        foreach (var examRow in exams)
        {
            var finished = SpeakingExamStates.IsTerminal(examRow.State);
            var examRoute = $"/speaking/exam/{Uri.EscapeDataString(examRow.Id)}";
            var examDebit = MatchDebitByReference(debitRefs, speakingRefunds, $"exam:{examRow.Id}");
            items.Add(new LearnerAttemptHistoryItem(
                examRow.Id,
                "speaking",
                SpeakingMockTitle,
                examRow.Id,
                examRow.IntroStartedAt ?? examRow.CreatedAt,
                examRow.CompletedAt,
                finished ? "completed" : "in_progress",
                examDebit?.Source,
                examDebit?.Credits ?? 0,
                finished ? $"{examRoute}/results" : examRoute,
                ExamResultLabel(examRow, cardScores)));
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

    /// <summary>Latest score per Speaking session. A classic assessment wins; a complete v1.1 card
    /// report counts the same way <see cref="SpeakingCreditSettlement.IsGradedAsync"/> does.</summary>
    private async Task<Dictionary<string, int>> LoadCardScoresAsync(List<string> sessionIds, CancellationToken ct)
    {
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);

        var classic = await db.SpeakingAiAssessments.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SpeakingSessionId))
            .Select(a => new { a.SpeakingSessionId, a.EstimatedScaledScore, a.GeneratedAt })
            .ToListAsync(ct);
        // Ordered in memory (a session has a handful of rows); the later row overwrites, so the latest wins.
        foreach (var assessment in classic.OrderBy(a => a.GeneratedAt))
        {
            scores[assessment.SpeakingSessionId] = assessment.EstimatedScaledScore;
        }

        var v11 = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .Where(a => a.SpeakingSessionId != null
                && sessionIds.Contains(a.SpeakingSessionId)
                && a.AssessmentKind == "card"
                && a.Status == SpeakingSimulationV11AssessmentStatus.Complete)
            .Select(a => new { a.SpeakingSessionId, a.EstimatedPracticeScore, a.GeneratedAt })
            .ToListAsync(ct);
        var classicScored = classic.Select(a => a.SpeakingSessionId).ToHashSet(StringComparer.Ordinal);
        foreach (var report in v11.OrderBy(a => a.GeneratedAt))
        {
            if (report.SpeakingSessionId is { } sessionId && !classicScored.Contains(sessionId))
            {
                scores[sessionId] = report.EstimatedPracticeScore ?? 0;
            }
        }

        return scores;
    }

    /// <summary>The same number the exam results page shows: the persisted combined snapshot, else the
    /// rounded average of the two graded cards (<c>(int)Math.Round((a + b) / 2.0)</c>).</summary>
    private static string? ExamResultLabel(ExamRow exam, IReadOnlyDictionary<string, int> cardScores)
    {
        // A live-tutor exam is human-marked: there is never an AI number to show or to wait for.
        if (exam.Mode != SpeakingExamMode.Ai)
        {
            return null;
        }

        if (exam.CombinedScaledSnapshot is { } combined)
        {
            return $"{OetScoring.OetReportedScaledScore(combined)}/500";
        }

        if (exam.SessionAId is { } cardA
            && exam.SessionBId is { } cardB
            && cardScores.TryGetValue(cardA, out var scoreA)
            && cardScores.TryGetValue(cardB, out var scoreB))
        {
            return $"{OetScoring.OetReportedScaledScore((scoreA + scoreB) / 2.0)}/500";
        }

        return exam.State == SpeakingExamState.Completed ? MarkingInProgress : null;
    }

    private static string? PracticeResultLabel(SessionRow card, bool resultsReady, IReadOnlyDictionary<string, int> cardScores)
    {
        if (cardScores.TryGetValue(card.Id, out var score))
        {
            return $"{OetScoring.OetReportedScaledScore(score)}/500";
        }

        // A tutor-marked session never gets an AI result, so it is not "in progress" either.
        return resultsReady && card.Mode != SpeakingSessionMode.LiveTutor ? MarkingInProgress : null;
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

            return (ResolveSourceLabel(row), CreditsOf(row));
        }

        return null;
    }

    /// <summary>Every debit whose reference is exactly <paramref name="reference"/> or starts with
    /// "<paramref name="reference"/>:" (an exam holds two, "exam:{id}:cardA" and "exam:{id}:cardB"),
    /// summed, less what was refunded under "<paramref name="reference"/>:...:release" (a released hold).
    /// Speaking holds are keyed by exam/session id, never by the content id MatchDebit uses.</summary>
    private static (string? Source, int Credits)? MatchDebitByReference(
        List<DebitRow> debitRefs, List<DebitRow> refunds, string reference)
    {
        var matched = debitRefs
            .Where(row => row.ReferenceId is { } id
                && (string.Equals(id, reference, StringComparison.Ordinal)
                    || id.StartsWith(reference + ":", StringComparison.Ordinal)))
            .OrderBy(row => row.ReferenceId, StringComparer.Ordinal)
            .ToList();
        if (matched.Count == 0)
        {
            return null;
        }

        var refunded = refunds
            .Where(row => row.ReferenceId is { } id && id.StartsWith(reference + ":", StringComparison.Ordinal))
            .Sum(RefundedCreditsOf);
        var credits = Math.Max(0, matched.Sum(CreditsOf) - refunded);
        // Fully refunded: nothing was spent, so no balance is named either.
        return (credits == 0 ? null : ResolveSourceLabel(matched[0]), credits);
    }

    private static int RefundedCreditsOf(DebitRow row)
        => Math.Max(0, row.SharedCreditsDelta)
           + Math.Max(0, row.FlexibleCreditsDelta)
           + Math.Max(0, row.WritingOnlyCreditsDelta)
           + Math.Max(0, row.SpeakingOnlyCreditsDelta)
           + Math.Max(0, row.ListeningTestsDelta)
           + Math.Max(0, row.ReadingTestsDelta)
           + Math.Max(0, row.MockExamsDelta);

    private static int CreditsOf(DebitRow row)
        => Math.Abs(Math.Min(0, row.SharedCreditsDelta))
           + Math.Abs(Math.Min(0, row.FlexibleCreditsDelta))
           + Math.Abs(Math.Min(0, row.WritingOnlyCreditsDelta))
           + Math.Abs(Math.Min(0, row.SpeakingOnlyCreditsDelta))
           + Math.Abs(Math.Min(0, row.ListeningTestsDelta))
           + Math.Abs(Math.Min(0, row.ReadingTestsDelta))
           + Math.Abs(Math.Min(0, row.MockExamsDelta));

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

    private sealed record ExamRow(
        string Id,
        SpeakingExamMode Mode,
        SpeakingExamState State,
        DateTimeOffset CreatedAt,
        DateTimeOffset? IntroStartedAt,
        DateTimeOffset? CompletedAt,
        double? CombinedScaledSnapshot,
        string? SessionAId,
        string? SessionBId);

    private sealed record SessionRow(
        string Id,
        string? AttemptId,
        SpeakingSessionMode Mode,
        SpeakingSessionState State,
        DateTimeOffset? SubmittedAt);
}
