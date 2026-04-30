using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using System.Text.Json;

namespace OetLearner.Api.Services;

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md.
//
// Admin surface for tutor calibration. Two responsibilities:
//   1. CRUD over `SpeakingCalibrationSample` (gold-marked recordings the
//      tutor pool calibrates against).
//   2. The drift report — for every tutor x sample where they submitted
//      a rubric, compute their per-criterion absolute error vs the gold
//      score, then aggregate to per-tutor mean absolute error so the
//      admin can spot outliers without paging through every score row.
//
// All gold rubrics are validated against the canonical criterion limits
// (linguistic 0-6, clinical 0-3) at write time so storage never holds an
// invalid rubric. Mirrors `AdminContentRead` / `AdminContentWrite` /
// `AdminContentPublish` permission groupings used elsewhere — we do not
// invent a new admin permission for calibration; tutor calibration is a
// quality-control extension of the speaking content pipeline.
public partial class AdminService
{
    public static readonly IReadOnlyList<string> SpeakingCriterionCodes = new[]
    {
        "intelligibility",
        "fluency",
        "appropriateness",
        "grammarExpression",
        "relationshipBuilding",
        "patientPerspective",
        "structure",
        "informationGathering",
        "informationGiving",
    };

    private static readonly Dictionary<string, int> SpeakingCriterionMaxByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["intelligibility"] = 6,
        ["fluency"] = 6,
        ["appropriateness"] = 6,
        ["grammarExpression"] = 6,
        ["relationshipBuilding"] = 3,
        ["patientPerspective"] = 3,
        ["structure"] = 3,
        ["informationGathering"] = 3,
        ["informationGiving"] = 3,
    };

    public async Task<object> ListSpeakingCalibrationSamplesAsync(string? status, CancellationToken ct)
    {
        var q = db.SpeakingCalibrationSamples.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<SpeakingCalibrationSampleStatus>(status, ignoreCase: true, out var parsed))
        {
            q = q.Where(x => x.Status == parsed);
        }
        var rows = await q
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .ToListAsync(ct);

        var sampleIds = rows.Select(r => r.Id).ToArray();
        var scoreCounts = await db.SpeakingCalibrationScores
            .AsNoTracking()
            .Where(s => sampleIds.Contains(s.SampleId))
            .GroupBy(s => s.SampleId)
            .Select(g => new { SampleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SampleId, x => x.Count, ct);

        return new
        {
            samples = rows.Select(r => new
            {
                sampleId = r.Id,
                title = r.Title,
                description = r.Description,
                sourceAttemptId = r.SourceAttemptId,
                professionId = r.ProfessionId,
                difficulty = r.Difficulty,
                status = r.Status.ToString().ToLowerInvariant(),
                goldScores = ParseScoresOrEmpty(r.GoldScoresJson),
                tutorSubmissionCount = scoreCounts.TryGetValue(r.Id, out var c) ? c : 0,
                createdAt = r.CreatedAt,
                publishedAt = r.PublishedAt,
            }).ToArray(),
        };
    }

    public async Task<object> GetSpeakingCalibrationSampleAsync(string sampleId, CancellationToken ct)
    {
        var row = await db.SpeakingCalibrationSamples.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        return ProjectSampleDetail(row);
    }

    public async Task<object> CreateSpeakingCalibrationSampleAsync(
        string adminId,
        string adminName,
        AdminSpeakingCalibrationSampleCreateRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw ApiException.Validation("SPEAKING_CALIBRATION_TITLE_REQUIRED", "Title is required.");
        }
        if (string.IsNullOrWhiteSpace(req.SourceAttemptId))
        {
            throw ApiException.Validation("SPEAKING_CALIBRATION_SOURCE_REQUIRED", "Source attempt id is required.");
        }
        ValidateRubricPayload(req.GoldScores);
        var attemptOk = await db.Attempts.AnyAsync(a => a.Id == req.SourceAttemptId, ct);
        if (!attemptOk)
        {
            throw ApiException.Validation("SPEAKING_CALIBRATION_SOURCE_NOT_FOUND", "Source attempt does not exist.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new SpeakingCalibrationSample
        {
            Id = $"scs-{Guid.NewGuid():N}",
            CreatedByAdminId = adminId,
            Title = req.Title.Trim(),
            Description = (req.Description ?? string.Empty).Trim(),
            SourceAttemptId = req.SourceAttemptId,
            ProfessionId = string.IsNullOrWhiteSpace(req.ProfessionId) ? "nursing" : req.ProfessionId.Trim(),
            Difficulty = string.IsNullOrWhiteSpace(req.Difficulty) ? "core" : req.Difficulty.Trim(),
            GoldScoresJson = SerialiseRubric(req.GoldScores),
            CalibrationNotes = (req.CalibrationNotes ?? string.Empty).Trim(),
            Status = SpeakingCalibrationSampleStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.SpeakingCalibrationSamples.Add(entity);
        await LogAuditAsync(adminId, adminName, "Created", "SpeakingCalibrationSample", entity.Id,
            $"Created calibration sample '{entity.Title}'.", ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ProjectSampleDetail(entity);
    }

    public async Task<object> UpdateSpeakingCalibrationSampleAsync(
        string adminId,
        string adminName,
        string sampleId,
        AdminSpeakingCalibrationSampleUpdateRequest req,
        CancellationToken ct)
    {
        var entity = await db.SpeakingCalibrationSamples
            .FirstOrDefaultAsync(x => x.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        if (entity.Status == SpeakingCalibrationSampleStatus.Archived)
        {
            throw ApiException.Conflict("speaking_calibration_archived",
                "Archived calibration samples are immutable.");
        }
        if (!string.IsNullOrWhiteSpace(req.Title)) entity.Title = req.Title.Trim();
        if (req.Description is not null) entity.Description = req.Description.Trim();
        if (!string.IsNullOrWhiteSpace(req.ProfessionId)) entity.ProfessionId = req.ProfessionId.Trim();
        if (!string.IsNullOrWhiteSpace(req.Difficulty)) entity.Difficulty = req.Difficulty.Trim();
        if (req.CalibrationNotes is not null) entity.CalibrationNotes = req.CalibrationNotes.Trim();
        if (req.GoldScores is not null)
        {
            ValidateRubricPayload(req.GoldScores);
            entity.GoldScoresJson = SerialiseRubric(req.GoldScores);
            // Recompute every existing tutor's drift against the new gold.
            await RecomputeDriftForSampleAsync(sampleId, req.GoldScores, ct);
        }
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SpeakingCalibrationSample", sampleId,
            $"Updated calibration sample '{entity.Title}'.", ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ProjectSampleDetail(entity);
    }

    public async Task<object> PublishSpeakingCalibrationSampleAsync(
        string adminId, string adminName, string sampleId, CancellationToken ct)
    {
        var entity = await db.SpeakingCalibrationSamples
            .FirstOrDefaultAsync(x => x.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        if (entity.Status == SpeakingCalibrationSampleStatus.Archived)
        {
            throw ApiException.Conflict("speaking_calibration_archived",
                "Archived calibration samples cannot be published.");
        }
        entity.Status = SpeakingCalibrationSampleStatus.Published;
        var now = DateTimeOffset.UtcNow;
        entity.PublishedAt ??= now;
        entity.UpdatedAt = now;
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await LogAuditAsync(adminId, adminName, "Published", "SpeakingCalibrationSample", sampleId,
            $"Published calibration sample '{entity.Title}'.", ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ProjectSampleDetail(entity);
    }

    public async Task<object> ArchiveSpeakingCalibrationSampleAsync(
        string adminId, string adminName, string sampleId, CancellationToken ct)
    {
        var entity = await db.SpeakingCalibrationSamples
            .FirstOrDefaultAsync(x => x.Id == sampleId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_sample_not_found", "That calibration sample does not exist.");
        entity.Status = SpeakingCalibrationSampleStatus.Archived;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "SpeakingCalibrationSample", sampleId,
            $"Archived calibration sample '{entity.Title}'.", ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ProjectSampleDetail(entity);
    }

    /// <summary>
    /// Drift report: for each tutor who submitted ≥ minSubmissionsPerTutor
    /// calibration scores, compute the mean absolute error across all
    /// criteria of all submitted samples. Higher MAE = larger drift.
    /// </summary>
    public async Task<object> GetSpeakingCalibrationDriftAsync(int minSubmissionsPerTutor, CancellationToken ct)
    {
        var min = Math.Max(1, minSubmissionsPerTutor);
        // Single query: pull every score for published samples.
        var scores = await db.SpeakingCalibrationScores
            .AsNoTracking()
            .Join(db.SpeakingCalibrationSamples.AsNoTracking(),
                s => s.SampleId,
                sa => sa.Id,
                (s, sa) => new { s, sa })
            .Where(x => x.sa.Status == SpeakingCalibrationSampleStatus.Published)
            .Select(x => new
            {
                x.s.TutorId,
                x.s.SampleId,
                x.s.SubmittedAt,
                x.s.TotalAbsoluteError,
            })
            .ToListAsync(ct);

        // Per-tutor user lookup so the drift table can render names.
        // Tutors live in `ExpertUsers` (not `Users`) — see SeedData.cs.
        var tutorIds = scores.Select(x => x.TutorId).Distinct().ToArray();
        var tutorNames = await db.ExpertUsers
            .AsNoTracking()
            .Where(u => tutorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        const int rubricSize = 9;
        var rows = scores
            .GroupBy(x => x.TutorId)
            .Where(g => g.Count() >= min)
            .Select(g =>
            {
                var n = g.Count();
                var totalAbs = g.Sum(x => x.TotalAbsoluteError);
                // Mean absolute error per criterion = totalAbs / (n samples * 9 criteria).
                var meanAbs = totalAbs / (n * rubricSize);
                return new
                {
                    tutorId = g.Key,
                    tutorName = tutorNames.TryGetValue(g.Key, out var name) ? name : g.Key,
                    submissionCount = n,
                    meanAbsoluteError = Math.Round(meanAbs, 3),
                    totalAbsoluteError = Math.Round(totalAbs, 3),
                    lastSubmittedAt = g.Max(x => x.SubmittedAt),
                };
            })
            .OrderByDescending(x => x.meanAbsoluteError)
            .ToArray();

        return new
        {
            tutors = rows,
            sampleSize = scores.Count,
            samplesPublished = scores.Select(x => x.SampleId).Distinct().Count(),
        };
    }

    private async Task RecomputeDriftForSampleAsync(
        string sampleId, SpeakingCriterionScoresPayload newGold, CancellationToken ct)
    {
        var rows = await db.SpeakingCalibrationScores
            .Where(s => s.SampleId == sampleId)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            var tutor = ParseRubric(row.ScoresJson);
            if (tutor is null) continue;
            row.TotalAbsoluteError = ComputeTotalAbsoluteError(tutor, newGold);
        }
    }

    private static SpeakingCriterionScoresPayload? ParseRubric(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            int Get(string key)
            {
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                {
                    return v.GetInt32();
                }
                return 0;
            }
            return new SpeakingCriterionScoresPayload(
                Intelligibility: Get("intelligibility"),
                Fluency: Get("fluency"),
                Appropriateness: Get("appropriateness"),
                GrammarExpression: Get("grammarExpression"),
                RelationshipBuilding: Get("relationshipBuilding"),
                PatientPerspective: Get("patientPerspective"),
                Structure: Get("structure"),
                InformationGathering: Get("informationGathering"),
                InformationGiving: Get("informationGiving"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static void ValidateRubricPayload(SpeakingCriterionScoresPayload p)
    {
        void Check(string code, int value)
        {
            var max = SpeakingCriterionMaxByCode[code];
            if (value < 0 || value > max)
            {
                throw ApiException.Validation(
                    "SPEAKING_CALIBRATION_RUBRIC_OUT_OF_RANGE",
                    $"{code} must be between 0 and {max}.");
            }
        }
        Check("intelligibility", p.Intelligibility);
        Check("fluency", p.Fluency);
        Check("appropriateness", p.Appropriateness);
        Check("grammarExpression", p.GrammarExpression);
        Check("relationshipBuilding", p.RelationshipBuilding);
        Check("patientPerspective", p.PatientPerspective);
        Check("structure", p.Structure);
        Check("informationGathering", p.InformationGathering);
        Check("informationGiving", p.InformationGiving);
    }

    internal static string SerialiseRubric(SpeakingCriterionScoresPayload p)
        => JsonSerializer.Serialize(new
        {
            intelligibility = p.Intelligibility,
            fluency = p.Fluency,
            appropriateness = p.Appropriateness,
            grammarExpression = p.GrammarExpression,
            relationshipBuilding = p.RelationshipBuilding,
            patientPerspective = p.PatientPerspective,
            structure = p.Structure,
            informationGathering = p.InformationGathering,
            informationGiving = p.InformationGiving,
        });

    internal static double ComputeTotalAbsoluteError(
        SpeakingCriterionScoresPayload tutor,
        SpeakingCriterionScoresPayload gold)
    {
        return Math.Abs(tutor.Intelligibility - gold.Intelligibility)
             + Math.Abs(tutor.Fluency - gold.Fluency)
             + Math.Abs(tutor.Appropriateness - gold.Appropriateness)
             + Math.Abs(tutor.GrammarExpression - gold.GrammarExpression)
             + Math.Abs(tutor.RelationshipBuilding - gold.RelationshipBuilding)
             + Math.Abs(tutor.PatientPerspective - gold.PatientPerspective)
             + Math.Abs(tutor.Structure - gold.Structure)
             + Math.Abs(tutor.InformationGathering - gold.InformationGathering)
             + Math.Abs(tutor.InformationGiving - gold.InformationGiving);
    }

    private static object ProjectSampleDetail(SpeakingCalibrationSample s) => new
    {
        sampleId = s.Id,
        title = s.Title,
        description = s.Description,
        sourceAttemptId = s.SourceAttemptId,
        professionId = s.ProfessionId,
        difficulty = s.Difficulty,
        status = s.Status.ToString().ToLowerInvariant(),
        goldScores = ParseScoresOrEmpty(s.GoldScoresJson),
        calibrationNotes = s.CalibrationNotes,
        createdByAdminId = s.CreatedByAdminId,
        createdAt = s.CreatedAt,
        updatedAt = s.UpdatedAt,
        publishedAt = s.PublishedAt,
    };

    private static object ParseScoresOrEmpty(string json)
    {
        var parsed = ParseRubric(json);
        if (parsed is null) return new { };
        return new
        {
            intelligibility = parsed.Intelligibility,
            fluency = parsed.Fluency,
            appropriateness = parsed.Appropriateness,
            grammarExpression = parsed.GrammarExpression,
            relationshipBuilding = parsed.RelationshipBuilding,
            patientPerspective = parsed.PatientPerspective,
            structure = parsed.Structure,
            informationGathering = parsed.InformationGathering,
            informationGiving = parsed.InformationGiving,
        };
    }
}
