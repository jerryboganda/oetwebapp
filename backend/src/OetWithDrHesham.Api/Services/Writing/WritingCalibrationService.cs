using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Services.Writing;

// ── DTOs surfaced to the admin endpoints ────────────────────────────────────

/// <summary>
/// Per-criterion + total grade Dr Ahmed assigned (the reference truth).
/// </summary>
public sealed record WritingCalibrationGradeView(
    int C1,
    int C2,
    int C3,
    int C4,
    int C5,
    int C6,
    int RawTotal,
    string BandLabel,
    string? Notes);

public sealed record WritingCalibrationLetterView(
    Guid Id,
    Guid ScenarioId,
    string LetterContent,
    string AuthorTier,
    WritingCalibrationGradeView DrAhmedGrade,
    DateTimeOffset AddedAt,
    string AddedById);

public sealed record WritingCalibrationLetterCreateRequest(
    Guid ScenarioId,
    string LetterContent,
    string AuthorTier,
    WritingCalibrationGradeView DrAhmedGrade);

public sealed record WritingCalibrationResultView(
    Guid Id,
    Guid CalibrationLetterId,
    WritingCalibrationGradeView Reference,
    WritingCalibrationGradeView? Ai,
    int AbsErrorRaw,
    bool BandMatch,
    string? AiError);

public sealed record WritingCalibrationRunView(
    Guid Id,
    DateTimeOffset RunDate,
    string ModelVersion,
    int TotalLetters,
    int Within2PointsCount,
    double MeanAbsError,
    int BandAgreementCount,
    string NotesMarkdown,
    IReadOnlyList<WritingCalibrationResultView> Results);

public sealed record WritingCalibrationRunSummaryView(
    Guid Id,
    DateTimeOffset RunDate,
    string ModelVersion,
    int TotalLetters,
    int Within2PointsCount,
    double MeanAbsError,
    int BandAgreementCount);

// ── Service ──────────────────────────────────────────────────────────────────

public interface IWritingCalibrationService
{
    Task<IReadOnlyList<WritingCalibrationLetterView>> ListLettersAsync(string adminId, CancellationToken ct);
    Task<WritingCalibrationLetterView> AddCalibrationLetterAsync(string adminId, WritingCalibrationLetterCreateRequest request, CancellationToken ct);
    Task<WritingCalibrationRunSummaryView> RunCalibrationAsync(string adminId, CancellationToken ct);
    Task<WritingCalibrationRunView?> GetLatestRunAsync(CancellationToken ct);
}

public sealed class WritingCalibrationService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    TimeProvider clock,
    ILogger<WritingCalibrationService> logger) : IWritingCalibrationService
{
    private const string ModelVersionConst = "writing.score.v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────
    // LETTERS — list + create
    // ─────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WritingCalibrationLetterView>> ListLettersAsync(string adminId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminId);
        var rows = await db.WritingCalibrationLetters.AsNoTracking()
            .OrderByDescending(x => x.AddedAt)
            .ToListAsync(ct);
        return rows.Select(ToLetterView).ToList();
    }

    public async Task<WritingCalibrationLetterView> AddCalibrationLetterAsync(string adminId, WritingCalibrationLetterCreateRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminId);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LetterContent))
        {
            throw ApiException.Validation("writing_calibration_empty_letter", "Letter content is required.");
        }
        if (request.ScenarioId == Guid.Empty)
        {
            throw ApiException.Validation("writing_calibration_missing_scenario", "ScenarioId is required.");
        }
        var tier = string.IsNullOrWhiteSpace(request.AuthorTier) ? "learner" : request.AuthorTier.Trim().ToLowerInvariant();
        if (tier is not ("exemplar" or "learner" or "synthetic"))
        {
            throw ApiException.Validation("writing_calibration_bad_tier",
                "AuthorTier must be one of: exemplar, learner, synthetic.");
        }
        if (request.DrAhmedGrade is null)
        {
            throw ApiException.Validation("writing_calibration_missing_grade", "Dr Ahmed's grade is required.");
        }
        ValidateGrade(request.DrAhmedGrade);

        var entity = new WritingCalibrationLetter
        {
            Id = Guid.NewGuid(),
            ScenarioId = request.ScenarioId,
            LetterContent = request.LetterContent,
            AuthorTier = tier,
            DrAhmedGradeJson = JsonSerializer.Serialize(request.DrAhmedGrade, JsonOpts),
            AddedAt = clock.GetUtcNow(),
            AddedById = adminId,
        };
        db.WritingCalibrationLetters.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToLetterView(entity);
    }

    // ─────────────────────────────────────────────────────────────────────
    // RUN — grade every letter through the V2 pipeline + persist comparison
    // ─────────────────────────────────────────────────────────────────────

    public async Task<WritingCalibrationRunSummaryView> RunCalibrationAsync(string adminId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminId);

        var letters = await db.WritingCalibrationLetters.AsNoTracking().ToListAsync(ct);
        if (letters.Count == 0)
        {
            throw ApiException.Conflict("writing_calibration_empty_corpus",
                "Add at least one calibration letter before running the harness.");
        }

        var runId = Guid.NewGuid();
        var startedAt = clock.GetUtcNow();
        var results = new List<WritingCalibrationResult>(letters.Count);
        var notes = new List<string>();

        foreach (var letter in letters)
        {
            ct.ThrowIfCancellationRequested();
            var reference = ParseGrade(letter.DrAhmedGradeJson)
                ?? new WritingCalibrationGradeView(0, 0, 0, 0, 0, 0, 0, "?", null);
            try
            {
                var aiGrade = await GradeWithAiAsync(letter, adminId, ct);
                var absErr = Math.Abs(reference.RawTotal - aiGrade.RawTotal);
                var bandMatch = string.Equals(reference.BandLabel, aiGrade.BandLabel, StringComparison.OrdinalIgnoreCase);
                results.Add(new WritingCalibrationResult
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    CalibrationLetterId = letter.Id,
                    AiGradeJson = JsonSerializer.Serialize(aiGrade, JsonOpts),
                    AbsErrorRaw = absErr,
                    BandMatch = bandMatch,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Calibration AI call failed for letter {LetterId}", letter.Id);
                notes.Add($"- Letter `{letter.Id}` failed: {ex.Message}");
                // Record an error row so the run reflects coverage gaps.
                results.Add(new WritingCalibrationResult
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    CalibrationLetterId = letter.Id,
                    AiGradeJson = JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts),
                    AbsErrorRaw = 99,
                    BandMatch = false,
                });
            }
        }

        var within2 = results.Count(r => r.AbsErrorRaw <= 2);
        var bandHits = results.Count(r => r.BandMatch);
        var meanAbsErr = results.Count > 0 ? results.Average(r => (double)r.AbsErrorRaw) : 0;
        var run = new WritingCalibrationRun
        {
            Id = runId,
            RunDate = startedAt,
            ModelVersion = ModelVersionConst,
            TotalLetters = letters.Count,
            Within2PointsCount = within2,
            MeanAbsError = meanAbsErr,
            BandAgreementCount = bandHits,
            NotesMarkdown = string.Join("\n", notes),
        };
        db.WritingCalibrationRuns.Add(run);
        db.WritingCalibrationResults.AddRange(results);
        await db.SaveChangesAsync(ct);

        return new WritingCalibrationRunSummaryView(
            run.Id, run.RunDate, run.ModelVersion, run.TotalLetters,
            run.Within2PointsCount, run.MeanAbsError, run.BandAgreementCount);
    }

    // ─────────────────────────────────────────────────────────────────────
    // LATEST RUN + per-letter detail
    // ─────────────────────────────────────────────────────────────────────

    public async Task<WritingCalibrationRunView?> GetLatestRunAsync(CancellationToken ct)
    {
        var run = await db.WritingCalibrationRuns.AsNoTracking()
            .OrderByDescending(r => r.RunDate)
            .FirstOrDefaultAsync(ct);
        if (run is null) return null;

        var results = await db.WritingCalibrationResults.AsNoTracking()
            .Where(r => r.RunId == run.Id)
            .OrderByDescending(r => r.AbsErrorRaw)
            .ToListAsync(ct);
        var letterIds = results.Select(r => r.CalibrationLetterId).Distinct().ToList();
        var letters = await db.WritingCalibrationLetters.AsNoTracking()
            .Where(l => letterIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l, ct);

        var detail = results.Select(r =>
        {
            letters.TryGetValue(r.CalibrationLetterId, out var letter);
            var reference = letter is not null ? ParseGrade(letter.DrAhmedGradeJson) : null;
            var ai = ParseGrade(r.AiGradeJson);
            string? aiError = null;
            if (ai is null)
            {
                aiError = TryReadError(r.AiGradeJson) ?? "AI grade unavailable.";
            }
            return new WritingCalibrationResultView(
                r.Id,
                r.CalibrationLetterId,
                reference ?? new WritingCalibrationGradeView(0, 0, 0, 0, 0, 0, 0, "?", null),
                ai,
                r.AbsErrorRaw,
                r.BandMatch,
                aiError);
        }).ToList();

        return new WritingCalibrationRunView(
            run.Id,
            run.RunDate,
            run.ModelVersion,
            run.TotalLetters,
            run.Within2PointsCount,
            run.MeanAbsError,
            run.BandAgreementCount,
            run.NotesMarkdown,
            detail);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Grade one calibration letter via the same Writing V2 grading
    /// template (<c>writing.score.v1</c>). All AI calls go through
    /// <see cref="IAiGatewayService"/> per AGENTS.md.
    /// </summary>
    private async Task<WritingCalibrationGradeView> GradeWithAiAsync(WritingCalibrationLetter letter, string adminId, CancellationToken ct)
    {
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Task = AiTaskMode.Score,
        });
        var input = BuildGradeInput(letter);
        var result = await aiGateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = input,
            Temperature = 0.1,
            FeatureCode = AiFeatureCodes.WritingGrade,
            PromptTemplateId = "writing.score.v1",
            UserId = adminId,
        }, ct);
        var parsed = ParseAiCompletion(result.Completion);
        return parsed ?? throw new InvalidOperationException("AI grading returned an unparseable response.");
    }

    private static string BuildGradeInput(WritingCalibrationLetter letter)
        => string.Join('\n',
            "Calibration letter to grade.",
            $"Letter content:\n---\n{letter.LetterContent}\n---",
            "Return JSON: { c1, c2, c3, c4, c5, c6, rawTotal, bandLabel }.");

    private static WritingCalibrationGradeView? ParseAiCompletion(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            int Get(string name, int max)
            {
                if (!doc.RootElement.TryGetProperty(name, out var el)) return 0;
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)) return Math.Clamp(v, 0, max);
                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return Math.Clamp(s, 0, max);
                return 0;
            }
            var scores = new[]
            {
                Get("c1", 3),
                Get("c2", 7),
                Get("c3", 7),
                Get("c4", 7),
                Get("c5", 7),
                Get("c6", 7),
            };
            int rawTotal = scores.Sum();
            if (doc.RootElement.TryGetProperty("rawTotal", out var rt) && rt.ValueKind == JsonValueKind.Number && rt.TryGetInt32(out var rv))
            {
                rawTotal = Math.Clamp(rv, 0, 38);
            }
            var bandLabel = doc.RootElement.TryGetProperty("bandLabel", out var b) && b.ValueKind == JsonValueKind.String
                ? b.GetString() ?? RawBandLabel(rawTotal)
                : RawBandLabel(rawTotal);
            return new WritingCalibrationGradeView(scores[0], scores[1], scores[2], scores[3], scores[4], scores[5], rawTotal, bandLabel, null);
        }
        catch (JsonException) { return null; }
    }

    private static WritingCalibrationGradeView? ParseGrade(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<WritingCalibrationGradeView>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadError(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static WritingCalibrationLetterView ToLetterView(WritingCalibrationLetter row)
        => new(
            row.Id,
            row.ScenarioId,
            row.LetterContent,
            row.AuthorTier,
            ParseGrade(row.DrAhmedGradeJson) ?? new WritingCalibrationGradeView(0, 0, 0, 0, 0, 0, 0, "?", null),
            row.AddedAt,
            row.AddedById);

    private static void ValidateGrade(WritingCalibrationGradeView grade)
    {
        if (grade.C1 is < 0 or > 3) throw ApiException.Validation("writing_calibration_c1_range", "C1 must be 0..3.");
        if (grade.C2 is < 0 or > 7) throw ApiException.Validation("writing_calibration_c2_range", "C2 must be 0..7.");
        if (grade.C3 is < 0 or > 7) throw ApiException.Validation("writing_calibration_c3_range", "C3 must be 0..7.");
        if (grade.C4 is < 0 or > 7) throw ApiException.Validation("writing_calibration_c4_range", "C4 must be 0..7.");
        if (grade.C5 is < 0 or > 7) throw ApiException.Validation("writing_calibration_c5_range", "C5 must be 0..7.");
        if (grade.C6 is < 0 or > 7) throw ApiException.Validation("writing_calibration_c6_range", "C6 must be 0..7.");
        if (grade.RawTotal is < 0 or > 38) throw ApiException.Validation("writing_calibration_raw_range", "RawTotal must be 0..38.");
        if (string.IsNullOrWhiteSpace(grade.BandLabel)) throw ApiException.Validation("writing_calibration_band_required", "BandLabel is required.");
    }

    // Mirrors WritingAppealService.RawBandLabel — same thresholds.
    private static string RawBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }
}
