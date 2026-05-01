using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 3 — per-item analysis for a Mock Bundle.
///
/// Aggregates over <see cref="ReadingAnswer"/> for the reading section's
/// content paper. Listening / Writing / Speaking surface placeholder rows so
/// admins still see one row per published item; deeper aggregation lives in
/// the per-skill rulebook services and is layered on later.
///
/// Flagging heuristics:
///  - <c>too_easy</c>      when difficulty (proportion correct) ≥ 0.95 and N ≥ 30
///  - <c>too_hard</c>      when difficulty ≤ 0.20 and N ≥ 30
///  - <c>tempting_distractor</c> when any single distractor accounts for ≥ 40% of *incorrect* picks
///                         and incorrect-pick total ≥ 10
/// </summary>
public sealed class MockItemAnalysisService
{
    private readonly LearnerDbContext _db;

    public MockItemAnalysisService(LearnerDbContext db) { _db = db; }

    public async Task<object> GetForBundleAsync(string bundleId, CancellationToken ct)
    {
        var rows = await _db.MockItemAnalysisSnapshots.AsNoTracking()
            .Where(x => x.MockBundleId == bundleId)
            .OrderBy(x => x.SubtestCode)
            .ThenBy(x => x.Label)
            .ToListAsync(ct);

        var generatedAt = rows.Count == 0 ? (DateTimeOffset?)null : rows.Max(r => r.GeneratedAt);
        return new
        {
            bundleId,
            generatedAt,
            items = rows.Select(r => new
            {
                id = r.ItemId,
                subtest = r.SubtestCode,
                label = r.Label,
                totalAttempts = r.TotalAttempts,
                correctCount = r.CorrectCount,
                difficulty = Math.Round(r.Difficulty, 3),
                distractor = r.DistractorJson,
                flag = r.Flag,
                generatedAt = r.GeneratedAt,
            }).ToArray(),
        };
    }

    public async Task<object> GetDashboardAsync(string? bundleId, string? paperId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(bundleId))
        {
            return await GetForBundleAsync(bundleId, ct);
        }

        var rows = await _db.MockItemAnalysisSnapshots.AsNoTracking()
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
                subtest = r.SubtestCode,
                label = r.Label,
                totalAttempts = r.TotalAttempts,
                correctCount = r.CorrectCount,
                difficulty = Math.Round(r.Difficulty, 3),
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
            // Listening/Writing/Speaking aggregation deferred to dedicated services
            // (graded via different tables / human review). Wave 3 ships Reading
            // first because it has the highest signal density.
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

    private async Task<List<MockItemAnalysisSnapshot>> ComputeReadingAsync(
        string bundleId,
        string paperId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Pull all submitted reading attempts on this paper, with answers + question metadata.
        var attempts = await _db.Set<ReadingAttempt>().AsNoTracking()
            .Where(a => a.PaperId == paperId && a.Status == ReadingAttemptStatus.Submitted)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var questions = await (from q in _db.Set<ReadingQuestion>().AsNoTracking()
                               join p in _db.Set<ReadingPart>().AsNoTracking() on q.ReadingPartId equals p.Id
                               where p.PaperId == paperId
                               select new { q.Id, PartCode = p.PartCode, QuestionNumber = q.DisplayOrder })
                              .ToListAsync(ct);

        var answers = await _db.Set<ReadingAnswer>().AsNoTracking()
            .Where(a => attempts.Contains(a.ReadingAttemptId))
            .Select(a => new { a.ReadingQuestionId, a.IsCorrect, a.SelectedDistractorCategory })
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
                ItemId = q.Id,
                SubtestCode = "reading",
                Label = $"Reading {q.PartCode} · Q{q.QuestionNumber}",
                TotalAttempts = total,
                CorrectCount = correct,
                Difficulty = difficulty,
                DistractorJson = JsonSupport.Serialize(distractors),
                Flag = flag,
                GeneratedAt = now,
            });
        }

        return result;
    }
}
