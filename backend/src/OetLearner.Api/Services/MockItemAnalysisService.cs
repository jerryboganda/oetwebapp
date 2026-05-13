using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 3 — per-item analysis for a Mock Bundle.
///
/// Wave 4 (May 2026 closure addendum) extends this to compute listening
/// items from the <see cref="ListeningAttempt"/> + <see cref="ListeningAnswer"/>
/// relational graph (the same shape Reading uses). Writing / Speaking still
/// rely on rulebook-graded review tables; layered in by the dedicated
/// per-skill aggregation services.
///
/// Flagging heuristics (shared by Reading and Listening):
///  - <c>too_easy</c>      when difficulty (proportion correct) ≥ 0.95 and N ≥ 30
///  - <c>too_hard</c>      when difficulty ≤ 0.20 and N ≥ 30
///  - <c>tempting_distractor</c> when any single distractor accounts for ≥ 40% of *incorrect* picks
///                         and incorrect-pick total ≥ 10
/// </summary>
public sealed class MockItemAnalysisService
{
    private readonly LearnerDbContext _db;

    public MockItemAnalysisService(LearnerDbContext db) { _db = db; }

    public Task<object> GetForBundleAsync(string bundleId, CancellationToken ct)
        => GetForBundleAsync(bundleId, paperId: null, ct);

    private async Task<object> GetForBundleAsync(string bundleId, string? paperId, CancellationToken ct)
    {
        var normalizedPaperId = NormalizeOptionalFilter(paperId);
        var rows = await _db.MockItemAnalysisSnapshots.AsNoTracking()
            .Where(x => x.MockBundleId == bundleId
                && (normalizedPaperId == null || x.ContentPaperId == normalizedPaperId))
            .OrderBy(x => x.SubtestCode)
            .ThenBy(x => x.Label)
            .ToListAsync(ct);

        var generatedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(r => r.GeneratedAt);
        return new
        {
            bundleId,
            paperId = normalizedPaperId,
            generatedAt,
            items = rows.Select(r => new
            {
                id = r.ItemId,
                paperId = r.ContentPaperId,
                subtest = r.SubtestCode,
                label = r.Label,
                totalAttempts = r.TotalAttempts,
                correctCount = r.CorrectCount,
                difficulty = Math.Round(r.Difficulty, 3),
                discriminationIndex = r.DiscriminationIndex.HasValue ? Math.Round(r.DiscriminationIndex.Value, 3) : (double?)null,
                distractor = r.DistractorJson,
                flag = r.Flag,
                generatedAt = r.GeneratedAt,
            }).ToArray(),
        };
    }

    public async Task<object> GetDashboardAsync(string? bundleId, string? paperId, CancellationToken ct)
    {
        bundleId = NormalizeOptionalFilter(bundleId);
        paperId = NormalizeOptionalFilter(paperId);
        if (!string.IsNullOrWhiteSpace(bundleId))
        {
            return await GetForBundleAsync(bundleId, paperId, ct);
        }

        var rows = await _db.MockItemAnalysisSnapshots.AsNoTracking()
            .Where(x => paperId == null || x.ContentPaperId == paperId)
            .OrderByDescending(x => x.GeneratedAt)
            .ThenBy(x => x.MockBundleId)
            .Take(500)
            .ToListAsync(ct);

        return new
        {
            generatedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(x => x.GeneratedAt),
            bundleCount = rows.Select(x => x.MockBundleId).Distinct().Count(),
            flaggedCount = rows.Count(x => x.Flag != null),
            filters = new { bundleId, paperId },
            items = rows.Select(r => new
            {
                id = r.ItemId,
                bundleId = r.MockBundleId,
                paperId = r.ContentPaperId,
                subtest = r.SubtestCode,
                label = r.Label,
                totalAttempts = r.TotalAttempts,
                correctCount = r.CorrectCount,
                difficulty = Math.Round(r.Difficulty, 3),
                discriminationIndex = r.DiscriminationIndex.HasValue ? Math.Round(r.DiscriminationIndex.Value, 3) : (double?)null,
                distractor = r.DistractorJson,
                flag = r.Flag,
                generatedAt = r.GeneratedAt,
            }).ToArray(),
        };
    }

    public async Task<object> RecomputeAsync(string bundleId, string adminId, CancellationToken ct)
    {
        var bundle = await _db.MockBundles.AsNoTracking()
            .Include(x => x.Sections.OrderBy(s => s.SectionOrder))
            .FirstOrDefaultAsync(x => x.Id == bundleId, ct)
            ?? throw ApiException.NotFound("bundle_not_found", "Mock bundle not found.");

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.MockItemAnalysisSnapshots
            .Where(x => x.MockBundleId == bundle.Id)
            .ToListAsync(ct);
        _db.MockItemAnalysisSnapshots.RemoveRange(existing);

        var snapshots = new List<MockItemAnalysisSnapshot>();

        foreach (var section in bundle.Sections)
        {
            if (string.Equals(section.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            {
                snapshots.AddRange(await ComputeReadingAsync(bundle.Id, section.ContentPaperId, now, ct));
            }
            else if (string.Equals(section.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase))
            {
                // Wave 4: listening items follow the same Submitted-attempts +
                // ListeningAnswer (IsCorrect / SelectedDistractorCategory)
                // signal pattern as Reading. Writing / Speaking aggregation
                // still lives in the per-skill review services because they
                // are graded against rulebook criteria, not single-key answers.
                snapshots.AddRange(await ComputeListeningAsync(bundle.Id, section.ContentPaperId, now, ct));
            }
        }

        if (snapshots.Count > 0)
        {
            _db.MockItemAnalysisSnapshots.AddRange(snapshots);
        }
        await _db.SaveChangesAsync(ct);

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminId,
            Action = "mock_item_analysis_recomputed",
            ResourceType = "MockBundle",
            ResourceId = bundle.Id,
            Details = JsonSupport.Serialize(new { itemCount = snapshots.Count }),
        });
        await _db.SaveChangesAsync(ct);

        return await GetForBundleAsync(bundle.Id, ct);
    }

    /// <summary>
    /// Wave 4: returns only listening item-analysis rows for a bundle.
    /// Mirrors <see cref="GetForBundleAsync"/> but pre-filters to
    /// <c>SubtestCode == "listening"</c> for the dedicated admin endpoint
    /// <c>GET /v1/admin/mocks/{bundleId}/listening-item-analysis</c>.
    /// </summary>
    public async Task<object> GetForBundleListeningAsync(string bundleId, CancellationToken ct)
    {
        var rows = await _db.MockItemAnalysisSnapshots.AsNoTracking()
            .Where(x => x.MockBundleId == bundleId && x.SubtestCode == "listening")
            .OrderBy(x => x.Label)
            .ToListAsync(ct);

        var generatedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(r => r.GeneratedAt);
        return new
        {
            bundleId,
            subtest = "listening",
            generatedAt,
            items = rows.Select(r => new
            {
                id = r.ItemId,
                paperId = r.ContentPaperId,
                label = r.Label,
                totalAttempts = r.TotalAttempts,
                correctCount = r.CorrectCount,
                difficulty = Math.Round(r.Difficulty, 3),
                discriminationIndex = r.DiscriminationIndex.HasValue ? Math.Round(r.DiscriminationIndex.Value, 3) : (double?)null,
                distractor = r.DistractorJson,
                flag = r.Flag,
                generatedAt = r.GeneratedAt,
            }).ToArray(),
        };
    }

    /// <summary>
    /// Wave 4 — Listening counterpart to <see cref="ComputeReadingAsync"/>.
    /// Builds one <see cref="MockItemAnalysisSnapshot"/> per
    /// <see cref="ListeningQuestion"/> belonging to the section's content
    /// paper. Difficulty and distractor counts come from
    /// <see cref="ListeningAnswer"/> rows attached to Submitted attempts.
    /// </summary>
    private async Task<List<MockItemAnalysisSnapshot>> ComputeListeningAsync(
        string bundleId,
        string paperId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var attemptScores = await _db.Set<ListeningAttempt>().AsNoTracking()
            .Where(a => a.PaperId == paperId && a.Status == ListeningAttemptStatus.Submitted)
            .Select(a => new { a.Id, Score = a.RawScore ?? 0 })
            .ToDictionaryAsync(a => a.Id, a => a.Score, ct);

        var questions = await (from q in _db.Set<ListeningQuestion>().AsNoTracking()
                               join p in _db.Set<ListeningPart>().AsNoTracking() on q.ListeningPartId equals p.Id
                               where p.PaperId == paperId
                               orderby q.QuestionNumber
                               select new { q.Id, p.PartCode, q.QuestionNumber })
                              .ToListAsync(ct);

            var submittedAttemptIds = attemptScores.Keys.ToArray();
            var answers = submittedAttemptIds.Length == 0
            ? new List<ListeningAnswer>()
            : await _db.Set<ListeningAnswer>().AsNoTracking()
                .Where(a => submittedAttemptIds.Contains(a.ListeningAttemptId))
                .ToListAsync(ct);

        var byQuestion = answers.GroupBy(a => a.ListeningQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<MockItemAnalysisSnapshot>();
        foreach (var q in questions)
        {
            byQuestion.TryGetValue(q.Id, out var rows);
            var total = rows?.Count ?? 0;
            var correct = rows?.Count(r => r.IsCorrect == true) ?? 0;
            var difficulty = total == 0 ? 0d : (double)correct / total;

            var distractors = rows is null
                ? new Dictionary<string, int>()
                : rows.Where(r => r.IsCorrect == false && r.SelectedDistractorCategory.HasValue)
                      .GroupBy(r => r.SelectedDistractorCategory!.Value.ToString())
                      .ToDictionary(g => g.Key, g => g.Count());

            string? flag = null;
            if (total >= 30 && difficulty >= 0.95) flag = "too_easy";
            else if (total >= 30 && difficulty <= 0.20) flag = "too_hard";
            else
            {
                var incorrect = total - correct;
                if (incorrect >= 10 && distractors.Count > 0)
                {
                    var top = distractors.Values.Max();
                    if ((double)top / incorrect >= 0.40) flag = "tempting_distractor";
                }
            }

            result.Add(new MockItemAnalysisSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                MockBundleId = bundleId,
                ContentPaperId = paperId,
                ItemId = q.Id,
                SubtestCode = "listening",
                Label = $"Listening {q.PartCode} · Q{q.QuestionNumber}",
                TotalAttempts = total,
                CorrectCount = correct,
                Difficulty = difficulty,
                DiscriminationIndex = CalculateDiscrimination(rows?.Select(r => new ItemResponse(r.ListeningAttemptId, r.IsCorrect == true)), attemptScores),
                DistractorJson = JsonSupport.Serialize(distractors),
                Flag = flag,
                GeneratedAt = now,
            });
        }

        return result;
    }

    private async Task<List<MockItemAnalysisSnapshot>> ComputeReadingAsync(
        string bundleId,
        string paperId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Pull all submitted reading attempts on this paper, with answers + question metadata.
        var attemptScores = await _db.Set<ReadingAttempt>().AsNoTracking()
            .Where(a => a.PaperId == paperId && a.Status == ReadingAttemptStatus.Submitted)
            .Select(a => new { a.Id, Score = a.RawScore ?? 0 })
            .ToDictionaryAsync(a => a.Id, a => a.Score, ct);

        var questions = await (from q in _db.Set<ReadingQuestion>().AsNoTracking()
                               join p in _db.Set<ReadingPart>().AsNoTracking() on q.ReadingPartId equals p.Id
                               where p.PaperId == paperId
                               select new { q.Id, PartCode = p.PartCode, QuestionNumber = q.DisplayOrder })
                              .ToListAsync(ct);

            var submittedAttemptIds = attemptScores.Keys.ToArray();
            var answers = await _db.Set<ReadingAnswer>().AsNoTracking()
                .Where(a => submittedAttemptIds.Contains(a.ReadingAttemptId))
                .Select(a => new { a.ReadingAttemptId, a.ReadingQuestionId, a.IsCorrect, a.SelectedDistractorCategory })
                .ToListAsync(ct);

        var byQuestion = answers.GroupBy(a => a.ReadingQuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<MockItemAnalysisSnapshot>();
        foreach (var q in questions)
        {
            byQuestion.TryGetValue(q.Id, out var rows);
            var total = rows?.Count ?? 0;
            var correct = rows?.Count(r => r.IsCorrect == true) ?? 0;
            var difficulty = total == 0 ? 0d : (double)correct / total;

            var distractors = rows is null
                ? new Dictionary<string, int>()
                : rows.Where(r => r.IsCorrect == false && r.SelectedDistractorCategory.HasValue)
                      .GroupBy(r => r.SelectedDistractorCategory!.Value.ToString())
                      .ToDictionary(g => g.Key, g => g.Count());

            string? flag = null;
            if (total >= 30 && difficulty >= 0.95) flag = "too_easy";
            else if (total >= 30 && difficulty <= 0.20) flag = "too_hard";
            else
            {
                var incorrect = total - correct;
                if (incorrect >= 10 && distractors.Count > 0)
                {
                    var top = distractors.Values.Max();
                    if ((double)top / incorrect >= 0.40) flag = "tempting_distractor";
                }
            }

            result.Add(new MockItemAnalysisSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                MockBundleId = bundleId,
                ContentPaperId = paperId,
                ItemId = q.Id,
                SubtestCode = "reading",
                Label = $"Reading {q.PartCode} · Q{q.QuestionNumber}",
                TotalAttempts = total,
                CorrectCount = correct,
                Difficulty = difficulty,
                DiscriminationIndex = CalculateDiscrimination(rows?.Select(r => new ItemResponse(r.ReadingAttemptId, r.IsCorrect == true)), attemptScores),
                DistractorJson = JsonSupport.Serialize(distractors),
                Flag = flag,
                GeneratedAt = now,
            });
        }

        return result;
    }

    private static double? CalculateDiscrimination(IEnumerable<ItemResponse>? responses, IReadOnlyDictionary<string, int> attemptScores)
    {
        var scored = responses?
            .Where(response => attemptScores.ContainsKey(response.AttemptId))
            .OrderByDescending(response => attemptScores[response.AttemptId])
            .ToList() ?? [];
        if (scored.Count < 10) return null;

        var cohortSize = Math.Max(1, (int)Math.Ceiling(scored.Count * 0.27));
        var top = scored.Take(cohortSize).Count(response => response.IsCorrect) / (double)cohortSize;
        var bottom = scored.TakeLast(cohortSize).Count(response => response.IsCorrect) / (double)cohortSize;
        return top - bottom;
    }

    private static string? NormalizeOptionalFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct ItemResponse(string AttemptId, bool IsCorrect);
}
