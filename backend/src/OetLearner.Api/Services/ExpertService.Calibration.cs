using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public partial class ExpertService
{
    public async Task<IReadOnlyList<ExpertCalibrationCaseSummaryResponse>> GetCalibrationCasesAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var cases = await ToOrderedListDescendingAsync(
            db.ExpertCalibrationCases
                .AsNoTracking(),
            calibrationCase => calibrationCase.CreatedAt,
            ct);

        var results = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(result => result.ReviewerId == reviewerId)
            .ToDictionaryAsync(result => result.CalibrationCaseId, ct);

        return cases.Select(calibrationCase =>
        {
            results.TryGetValue(calibrationCase.Id, out var result);
            var status = result is null
                ? "pending"
                : result.IsDraft ? "draft" : "completed";
            var alignmentScore = result is null || result.IsDraft
                ? null
                : (double?)ResolveCalibrationAlignment(calibrationCase, result);
            return new ExpertCalibrationCaseSummaryResponse(
                calibrationCase.Id,
                calibrationCase.Title,
                calibrationCase.ProfessionId,
                calibrationCase.SubtestCode,
                calibrationCase.SubtestCode,
                calibrationCase.BenchmarkScore,
                result is null || result.IsDraft ? null : result.ReviewerScore,
                alignmentScore,
                status,
                calibrationCase.CreatedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<ExpertCalibrationNoteResponse>> GetCalibrationNotesAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var notes = await ToOrderedListDescendingAsync(
            db.ExpertCalibrationNotes
                .AsNoTracking()
                .Where(note => note.ReviewerId == reviewerId || note.ReviewerId == null),
            note => note.CreatedAt,
            ct,
            take: 50);

        return notes.Select(note => new ExpertCalibrationNoteResponse(
            note.Id,
            note.Type.ToString().ToLowerInvariant(),
            note.Message,
            note.CaseId,
            note.CreatedAt)).ToList();
    }

    public async Task<ExpertCalibrationCaseDetailResponse> GetCalibrationCaseDetailAsync(string caseId, string reviewerId, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        var calibrationCase = await db.ExpertCalibrationCases
            .AsNoTracking()
            .FirstOrDefaultAsync(existingCase => existingCase.Id == caseId, ct)
            ?? throw ApiException.NotFound("calibration_case_not_found", "The requested calibration case does not exist.");

        var existingSubmission = await db.ExpertCalibrationResults
            .AsNoTracking()
            .FirstOrDefaultAsync(result => result.CalibrationCaseId == caseId && result.ReviewerId == reviewerId, ct);

        var artifacts = DeserializeCalibrationArtifacts(calibrationCase);
        var benchmarkRubric = DeserializeCalibrationRubric(calibrationCase);
        var referenceNotes = DeserializeCalibrationReferenceNotes(calibrationCase);

        return new ExpertCalibrationCaseDetailResponse(
            calibrationCase.Id,
            calibrationCase.Title,
            calibrationCase.ProfessionId,
            calibrationCase.SubtestCode,
            calibrationCase.SubtestCode,
            calibrationCase.BenchmarkLabel,
            calibrationCase.BenchmarkScore,
            calibrationCase.Difficulty,
            existingSubmission is not null ? (existingSubmission.IsDraft ? "draft" : "completed") : "pending",
            calibrationCase.CreatedAt,
            artifacts,
            benchmarkRubric,
            referenceNotes,
            existingSubmission is null
                ? null
                : new ExpertCalibrationSubmissionResponse(
                    reviewerId,
                    expert.DisplayName,
                    existingSubmission.ReviewerScore,
                    existingSubmission.IsDraft ? 0 : ResolveCalibrationAlignment(calibrationCase, existingSubmission),
                    existingSubmission.DisagreementSummary,
                    existingSubmission.Notes,
                    JsonSupport.Deserialize(existingSubmission.SubmittedRubricJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
                    existingSubmission.SubmittedAt,
                    existingSubmission.IsDraft,
                    existingSubmission.UpdatedAt));
    }

    public async Task<object> SubmitCalibrationAsync(string caseId, string reviewerId, ExpertCalibrationSubmitRequest request, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        if (request.Scores.Count == 0)
        {
            throw ApiException.Validation(
                "calibration_scores_required",
                "Provide at least one calibration score before submitting.",
                [new ApiFieldError("scores", "required", "Add one or more scores before submitting this calibration case.")]);
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > MaxCalibrationNotesLength)
        {
            throw ApiException.Validation(
                "calibration_notes_too_long",
                "Calibration notes are too long.",
                [new ApiFieldError("notes", "too_long", $"Calibration notes cannot exceed {MaxCalibrationNotesLength} characters.")]);
        }

        var calibrationCase = await db.ExpertCalibrationCases.FirstOrDefaultAsync(existingCase => existingCase.Id == caseId, ct)
            ?? throw ApiException.NotFound("calibration_case_not_found", "The requested calibration case does not exist.");

        var existingResult = await db.ExpertCalibrationResults
            .FirstOrDefaultAsync(result => result.CalibrationCaseId == caseId && result.ReviewerId == reviewerId, ct);
        if (existingResult is not null && !existingResult.IsDraft)
        {
            throw ApiException.Conflict("calibration_already_submitted", "This calibration case has already been submitted.");
        }

        var normalizedScores = NormalizeScores(request.Scores, calibrationCase.SubtestCode);
        var benchmarkLookup = NormalizeCalibrationBenchmarkScores(
            DeserializeCalibrationRubric(calibrationCase),
            calibrationCase.SubtestCode);
        ValidateCompleteCalibrationScores(normalizedScores, benchmarkLookup);

        var reviewerScore = CalculateCalibrationReviewerScore(normalizedScores);
        var alignment = CalculateCalibrationAlignment(normalizedScores, benchmarkLookup, calibrationCase.SubtestCode);

        var largestDelta = benchmarkLookup.Keys
            .Select(criterion => new
            {
                Criterion = criterion,
                Gap = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion]),
                NormalizedGap = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion]) /
                    Math.Max(1.0, MaxScoreForCriterion(calibrationCase.SubtestCode, criterion))
            })
            .OrderByDescending(item => item.NormalizedGap)
            .FirstOrDefault();

        var disagreementSummary = largestDelta is null || largestDelta.Gap == 0
            ? "Aligned with benchmark."
            : $"{ToLabel(largestDelta.Criterion)} differs from benchmark by {largestDelta.Gap} point(s).";

        if (existingResult is null)
        {
            db.ExpertCalibrationResults.Add(new ExpertCalibrationResult
            {
                Id = $"ecr-{Guid.NewGuid():N}",
                CalibrationCaseId = caseId,
                ReviewerId = reviewerId,
                SubmittedRubricJson = JsonSupport.Serialize(normalizedScores),
                ReviewerScore = reviewerScore,
                AlignmentScore = alignment,
                DisagreementSummary = disagreementSummary,
                Notes = request.Notes?.Trim() ?? string.Empty,
                SubmittedAt = DateTimeOffset.UtcNow,
                IsDraft = false,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            // Upgrade an existing draft into a final submission.
            existingResult.SubmittedRubricJson = JsonSupport.Serialize(normalizedScores);
            existingResult.ReviewerScore = reviewerScore;
            existingResult.AlignmentScore = alignment;
            existingResult.DisagreementSummary = disagreementSummary;
            existingResult.Notes = request.Notes?.Trim() ?? string.Empty;
            existingResult.SubmittedAt = DateTimeOffset.UtcNow;
            existingResult.UpdatedAt = DateTimeOffset.UtcNow;
            existingResult.IsDraft = false;
        }

        db.ExpertCalibrationNotes.Add(new ExpertCalibrationNote
        {
            Id = $"ecn-{Guid.NewGuid():N}",
            Type = CalibrationNoteType.Completed,
            Message = $"Completed {calibrationCase.Title}. Alignment: {alignment}%.",
            CaseId = caseId,
            ReviewerId = reviewerId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Submitted Calibration", caseId, disagreementSummary, ct);
        await RecordExpertEventAsync(reviewerId, "expert_calibration_submitted", new { caseId, alignment }, ct);
        await db.SaveChangesAsync(ct);

        return new { success = true, caseId, alignment };
    }

    /// <summary>
    /// Saves a calibration submission as a draft so the reviewer can resume later without losing work.
    /// Supplement §4.8: preserves reviewer work where possible. Drafts never contribute to
    /// alignment/history aggregates and are replaced (not duplicated) on subsequent saves.
    /// Supplement: <c>POST /v1/expert/calibration/cases/{caseId}/draft</c>.
    /// </summary>
    public async Task<object> SaveCalibrationDraftAsync(string caseId, string reviewerId, ExpertCalibrationSubmitRequest request, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(reviewerId, ct);

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Trim().Length > MaxCalibrationNotesLength)
        {
            throw ApiException.Validation(
                "calibration_notes_too_long",
                "Calibration notes are too long.",
                [new ApiFieldError("notes", "too_long", $"Calibration notes cannot exceed {MaxCalibrationNotesLength} characters.")]);
        }

        var calibrationCase = await db.ExpertCalibrationCases
            .FirstOrDefaultAsync(existingCase => existingCase.Id == caseId, ct)
            ?? throw ApiException.NotFound("calibration_case_not_found", "The requested calibration case does not exist.");

        var existing = await db.ExpertCalibrationResults
            .FirstOrDefaultAsync(result => result.CalibrationCaseId == caseId && result.ReviewerId == reviewerId, ct);
        if (existing is not null && !existing.IsDraft)
        {
            throw ApiException.Conflict(
                "calibration_already_submitted",
                "This calibration case has already been submitted and cannot be saved as a draft.");
        }

        var normalizedScores = request.Scores.Count == 0
            ? new Dictionary<string, int>()
            : NormalizeScores(request.Scores, calibrationCase.SubtestCode);

        var reviewerScore = normalizedScores.Count == 0
            ? 0
            : (int)Math.Round(normalizedScores.Values.Average(), MidpointRounding.AwayFromZero);

        var normalizedNotes = request.Notes?.Trim() ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            existing = new ExpertCalibrationResult
            {
                Id = $"ecr-{Guid.NewGuid():N}",
                CalibrationCaseId = caseId,
                ReviewerId = reviewerId,
                SubmittedAt = now
            };
            db.ExpertCalibrationResults.Add(existing);
        }

        existing.SubmittedRubricJson = JsonSupport.Serialize(normalizedScores);
        existing.ReviewerScore = reviewerScore;
        existing.AlignmentScore = 0;
        existing.DisagreementSummary = string.Empty;
        existing.Notes = normalizedNotes;
        existing.IsDraft = true;
        existing.UpdatedAt = now;

        await LogExpertAuditAsync(reviewerId, expert.DisplayName, "Saved Calibration Draft", caseId, "Calibration draft saved.", ct);
        await RecordExpertEventAsync(reviewerId, "expert_calibration_draft_saved", new { caseId, scoreCount = normalizedScores.Count }, ct);
        await db.SaveChangesAsync(ct);

        return new
        {
            success = true,
            caseId,
            isDraft = true,
            scores = normalizedScores,
            notes = normalizedNotes,
            updatedAt = existing.UpdatedAt
        };
    }

    /// <summary>
    /// Returns the reviewer's calibration submission history ordered newest first.
    /// Supplement: <c>GET /v1/expert/calibration/history</c>.
    /// </summary>
    public async Task<ExpertCalibrationHistoryResponse> GetCalibrationHistoryAsync(string reviewerId, int limit, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);
        var effectiveLimit = Math.Clamp(limit, 1, 200);

        var results = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(r => r.ReviewerId == reviewerId && !r.IsDraft)
            .OrderByDescending(r => r.SubmittedAt)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        var total = await db.ExpertCalibrationResults
            .AsNoTracking()
            .CountAsync(r => r.ReviewerId == reviewerId && !r.IsDraft, ct);

        if (results.Count == 0)
        {
            return new ExpertCalibrationHistoryResponse(Array.Empty<ExpertCalibrationHistoryEntryResponse>(), 0, DateTimeOffset.UtcNow);
        }

        var caseIds = results.Select(r => r.CalibrationCaseId).Distinct().ToList();
        var cases = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(c => caseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var professionIds = cases.Values.Select(c => c.ProfessionId).Distinct().ToList();
        var professionNames = await db.Professions
            .AsNoTracking()
            .Where(p => professionIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Label, ct);

        var entries = results.Select(r =>
        {
            cases.TryGetValue(r.CalibrationCaseId, out var @case);
            var professionName = @case is not null && professionNames.TryGetValue(@case.ProfessionId, out var pn)
                ? pn
                : @case?.ProfessionId ?? string.Empty;

            return new ExpertCalibrationHistoryEntryResponse(
                Id: r.Id,
                CaseId: r.CalibrationCaseId,
                CaseTitle: @case?.Title ?? "(deleted case)",
                Profession: professionName,
                SubTest: @case?.SubtestCode ?? string.Empty,
                BenchmarkScore: @case?.BenchmarkScore ?? 0,
                ReviewerScore: r.ReviewerScore,
                AlignmentScore: @case is null ? r.AlignmentScore : ResolveCalibrationAlignment(@case, r),
                DisagreementSummary: r.DisagreementSummary ?? string.Empty,
                SubmittedAt: r.SubmittedAt);
        }).ToList();

        return new ExpertCalibrationHistoryResponse(entries, total, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns aggregate alignment statistics across the reviewer's calibration submissions,
    /// plus per-sub-test breakdown and a 12-point trend. Supplement:
    /// <c>GET /v1/expert/calibration/alignment</c>.
    /// </summary>
    public async Task<ExpertCalibrationAlignmentResponse> GetCalibrationAlignmentAsync(string reviewerId, CancellationToken ct)
    {
        await EnsureExpertAsync(reviewerId, ct);

        var results = await db.ExpertCalibrationResults
            .AsNoTracking()
            .Where(r => r.ReviewerId == reviewerId && !r.IsDraft)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

        if (results.Count == 0)
        {
            return new ExpertCalibrationAlignmentResponse(
                TotalSubmissions: 0,
                OverallAverageAlignment: 0,
                LatestAlignment: null,
                PreviousAlignment: null,
                DeltaFromPrevious: null,
                PerSubTest: Array.Empty<ExpertCalibrationAlignmentBreakdownResponse>(),
                Trend: Array.Empty<ExpertCalibrationAlignmentTrendPointResponse>(),
                GeneratedAt: DateTimeOffset.UtcNow);
        }

        var caseIds = results.Select(r => r.CalibrationCaseId).Distinct().ToList();
        var cases = await db.ExpertCalibrationCases
            .AsNoTracking()
            .Where(c => caseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var scoredResults = results
            .Select(r =>
            {
                cases.TryGetValue(r.CalibrationCaseId, out var calibrationCase);
                var alignment = calibrationCase is null ? r.AlignmentScore : ResolveCalibrationAlignment(calibrationCase, r);
                return new
                {
                    Result = r,
                    Alignment = alignment,
                    SubTest = calibrationCase?.SubtestCode ?? "unknown"
                };
            })
            .ToList();

        var overallAverage = Math.Round(scoredResults.Average(r => r.Alignment), 1);
        var latest = scoredResults[0].Alignment;
        double? previous = scoredResults.Count > 1 ? scoredResults[1].Alignment : null;
        double? delta = previous is null ? null : Math.Round(latest - previous.Value, 1);

        var perSubTest = scoredResults
            .GroupBy(r => r.SubTest)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(r => r.Result.SubmittedAt).ToList();
                return new ExpertCalibrationAlignmentBreakdownResponse(
                    SubTest: g.Key,
                    SubmissionCount: ordered.Count,
                    AverageAlignment: Math.Round(ordered.Average(r => r.Alignment), 1),
                    LatestAlignment: ordered[0].Alignment);
            })
            .OrderBy(b => b.SubTest, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trend = scoredResults
            .OrderBy(r => r.Result.SubmittedAt)
            .TakeLast(12)
            .Select(r => new ExpertCalibrationAlignmentTrendPointResponse(r.Result.SubmittedAt, r.Alignment))
            .ToList();

        return new ExpertCalibrationAlignmentResponse(
            TotalSubmissions: results.Count,
            OverallAverageAlignment: overallAverage,
            LatestAlignment: latest,
            PreviousAlignment: previous,
            DeltaFromPrevious: delta,
            PerSubTest: perSubTest,
            Trend: trend,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    private static List<ExpertCalibrationArtifactResponse> DeserializeCalibrationArtifacts(ExpertCalibrationCase calibrationCase)
    {
        var artifacts = JsonSupport.Deserialize<List<ExpertCalibrationArtifactResponse>>(calibrationCase.CaseArtifactsJson, []);
        if (artifacts.Count > 0)
        {
            return artifacts;
        }

        return string.Equals(calibrationCase.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? new List<ExpertCalibrationArtifactResponse>
            {
                new("role_card", "Role Card", "You are the ward doctor handing over a patient with post-operative pain escalation and new abnormal observations."),
                new("transcript", "Candidate Transcript", "Doctor, I am calling about a patient whose pain has worsened despite the current analgesia plan. We need to review the escalation steps and safety-net advice."),
                new("benchmark_focus", "Benchmark Focus", "Benchmark case tests structured handover, prioritisation, and safe escalation language under time pressure.")
            }
            : new List<ExpertCalibrationArtifactResponse>
            {
                new("case_notes", "Case Notes", "Mrs Khan requires a referral following post-operative complications after laparoscopic cholecystectomy. Include wound concerns, analgesia response, and follow-up plan."),
                new("learner_response", "Learner Response", "Dear Dr Patel, thank you for seeing Mrs Khan, who has persistent abdominal pain, mild wound ooze, and difficulty mobilising after surgery."),
                new("benchmark_focus", "Benchmark Focus", "Benchmark case tests clear purpose, clinical relevance filtering, and concise sequencing of referral information.")
            };
    }

    private static List<ExpertCalibrationRubricEntryResponse> DeserializeCalibrationRubric(ExpertCalibrationCase calibrationCase)
    {
        var rubric = JsonSupport.Deserialize<List<ExpertCalibrationRubricEntryResponse>>(calibrationCase.ReferenceRubricJson, []);
        if (rubric.Count > 0)
        {
            return rubric;
        }

        return string.Equals(calibrationCase.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? new List<ExpertCalibrationRubricEntryResponse>
            {
                // Linguistic criteria (0–6 scale)
                new("intelligibility", 5, "Speech remains easy to follow with only minor stress-related hesitation."),
                new("fluency", 5, "Delivery is steady and recovers quickly after clarification moments."),
                new("appropriateness", 4, "Register is professional but one reassurance phrase is slightly abrupt."),
                new("grammar", 5, "Grammar and expression are controlled throughout the handover."),
                // Clinical Communication criteria (0–3 scale)
                new("relationshipBuilding", 2, "Respectful attitude and empathy are evident; introductions are complete."),
                new("patientPerspective", 2, "The candidate acknowledges the patient's concerns but misses one cue."),
                new("providingStructure", 3, "Clear signposting and logical sequencing of the handover."),
                new("informationGathering", 2, "Uses open-then-closed questioning; one compound question observed."),
                new("informationGiving", 2, "Pauses to check understanding; one safety-net checkback missed.")
            }
            : new List<ExpertCalibrationRubricEntryResponse>
            {
                new("purpose", 4, "Purpose is established immediately and sustained throughout the letter."),
                new("content", 5, "Relevant post-operative facts are selected accurately for referral."),
                new("conciseness", 3, "A few low-value details reduce efficiency."),
                new("genre", 4, "Register and format match a professional referral letter."),
                new("organization", 4, "Information flows logically from reason for referral to current concerns."),
                new("language", 4, "Language is mostly controlled with minor slips that do not impede meaning.")
            };
    }

    private static List<string> DeserializeCalibrationReferenceNotes(ExpertCalibrationCase calibrationCase)
    {
        var notes = JsonSupport.Deserialize<List<string>>(calibrationCase.ReferenceNotesJson, []);
        if (notes.Count > 0)
        {
            return notes;
        }

        return string.Equals(calibrationCase.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            ? new List<string>
            {
                "Benchmark expects a concise opening summary before detailed escalation points.",
                "Full marks require explicit clinical prioritisation and a clear follow-up request.",
                "Minor alignment loss is acceptable when reassurance language is warm but slightly repetitive."
            }
            : new List<string>
            {
                "Benchmark prioritises referral purpose, current complication, and follow-up request in the opening half of the letter.",
                "Low-value surgical background should be compressed unless it changes the referral decision.",
                "Language control is important, but information selection remains the main separator in this case."
            };
    }

    private static Dictionary<string, int> NormalizeCalibrationBenchmarkScores(
        IReadOnlyCollection<ExpertCalibrationRubricEntryResponse> benchmarkRubric,
        string subtestCode)
    {
        var criteria = string.Equals(subtestCode, "writing", StringComparison.OrdinalIgnoreCase) ? WritingCriteria : SpeakingCriteria;
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in benchmarkRubric)
        {
            var normalizedKey = NormalizeCriterionKey(entry.Criterion, criteria);
            if (normalizedKey is null)
            {
                continue;
            }

            var maxScore = MaxScoreForCriterion(subtestCode, normalizedKey);
            normalized[normalizedKey] = Math.Clamp(entry.BenchmarkScore, 0, maxScore);
        }

        return normalized;
    }

    private static void ValidateCompleteCalibrationScores(
        IReadOnlyDictionary<string, int> normalizedScores,
        IReadOnlyDictionary<string, int> benchmarkLookup)
    {
        if (benchmarkLookup.Count == 0)
        {
            throw ApiException.Validation(
                "calibration_rubric_missing",
                "This calibration case does not have a benchmark rubric.",
                [new ApiFieldError("scores", "missing_benchmark", "A benchmark rubric is required before this calibration case can be submitted.")]);
        }

        var missing = benchmarkLookup.Keys
            .Where(criterion => !normalizedScores.ContainsKey(criterion))
            .ToArray();
        if (missing.Length > 0)
        {
            throw ApiException.Validation(
                "calibration_scores_incomplete",
                "Complete every benchmark criterion before submitting.",
                missing.Select(criterion => new ApiFieldError($"scores.{criterion}", "required", $"A score for {criterion} is required before final submission.")));
        }
    }

    private static int CalculateCalibrationReviewerScore(IReadOnlyDictionary<string, int> normalizedScores)
    {
        return normalizedScores.Count == 0
            ? 0
            : (int)Math.Round(normalizedScores.Values.Average(), MidpointRounding.AwayFromZero);
    }

    private static double CalculateCalibrationAlignment(
        IReadOnlyDictionary<string, int> normalizedScores,
        IReadOnlyDictionary<string, int> benchmarkLookup,
        string subtestCode)
    {
        var comparableCriteria = benchmarkLookup.Keys
            .Where(normalizedScores.ContainsKey)
            .ToList();
        if (comparableCriteria.Count == 0)
        {
            return 0;
        }

        var averageSimilarity = comparableCriteria.Average(criterion =>
        {
            var criterionMax = Math.Max(1.0, MaxScoreForCriterion(subtestCode, criterion));
            var delta = Math.Abs(normalizedScores[criterion] - benchmarkLookup[criterion]);
            return Math.Max(0.0, 1.0 - delta / criterionMax);
        });

        return Math.Round(averageSimilarity * 100.0, 1);
    }

    private static double ResolveCalibrationAlignment(ExpertCalibrationCase calibrationCase, ExpertCalibrationResult result)
    {
        if (result.IsDraft)
        {
            return 0;
        }

        try
        {
            var criteria = string.Equals(calibrationCase.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase)
                ? WritingCriteria
                : SpeakingCriteria;
            var rawScores = JsonSupport.Deserialize(result.SubmittedRubricJson, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            var normalizedScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in rawScores)
            {
                var normalizedKey = NormalizeCriterionKey(key, criteria);
                if (normalizedKey is null)
                {
                    continue;
                }

                var maxScore = MaxScoreForCriterion(calibrationCase.SubtestCode, normalizedKey);
                normalizedScores[normalizedKey] = Math.Clamp(value, 0, maxScore);
            }

            var benchmarkLookup = NormalizeCalibrationBenchmarkScores(
                DeserializeCalibrationRubric(calibrationCase),
                calibrationCase.SubtestCode);

            return CalculateCalibrationAlignment(normalizedScores, benchmarkLookup, calibrationCase.SubtestCode);
        }
        catch
        {
            return result.AlignmentScore;
        }
    }
}
