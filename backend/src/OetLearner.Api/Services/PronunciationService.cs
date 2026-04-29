using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Services;

/// <summary>
/// ============================================================================
/// PronunciationService — the ONLY path from learner-facing endpoints into
/// pronunciation content, attempts, assessments, and progress.
/// ============================================================================
///
/// Invariants (enforced by code, tests, and the grounded AI gateway):
///   1. Scoring NEVER bypasses <see cref="IPronunciationAsrProviderSelector"/>.
///      The selector ensures Azure → Whisper → Mock falls through correctly.
///      There is NO RNG scoring in this file.
///   2. All grade projections use <c>OetScoring.PronunciationProjectedScaled</c> /
///      <c>PronunciationProjectedBand</c>. Never a free-form 350 comparison.
///   3. All AI feedback goes through <see cref="IPronunciationFeedbackService"/>,
///      which physically cannot skip the grounded prompt builder.
///   4. Audio I/O goes through <see cref="IFileStorage"/> — never File.* directly.
///   5. Every attempt produces exactly one <c>PronunciationAttempt</c> row +
///      optionally one <c>PronunciationAssessment</c> row + one audit entry.
/// </summary>
public class PronunciationService(
    LearnerDbContext db,
    IPronunciationAsrProviderSelector asrSelector,
    IPronunciationFeedbackService feedback,
    IPronunciationSchedulerService scheduler,
    IPronunciationEntitlementService entitlement,
    IFileStorage storage,
    IOptions<PronunciationOptions> options,
    ILogger<PronunciationService> logger)
{
    private readonly PronunciationOptions _opts = options.Value;

    // ── Learner-facing reads ────────────────────────────────────────────────

    public async Task<object> GetProfileAsync(string userId, CancellationToken ct)
    {
        var progress = await db.LearnerPronunciationProgress
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        var assessments = await db.PronunciationAssessments
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        assessments = assessments
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .ToList();

        var overallScore = assessments.Count > 0
            ? Math.Round(assessments.Average(a => a.OverallScore), 1)
            : 0.0;
        var projected = OetScoring.PronunciationProjectedBand(overallScore);

        var weakPhonemes = progress
            .Where(p => p.AverageScore < 60 && p.PhonemeCode != "_speech_overall")
            .OrderBy(p => p.AverageScore)
            .Take(5)
            .Select(p => new
            {
                phonemeCode = p.PhonemeCode,
                averageScore = Math.Round(p.AverageScore, 1),
                attemptCount = p.AttemptCount,
                lastPracticedAt = p.LastPracticedAt,
                nextDueAt = p.NextDueAt,
            })
            .ToList();

        return new
        {
            overallScore,
            projectedSpeakingScaled = projected.ScaledScore,
            projectedSpeakingGrade = projected.Grade,
            projectedSpeakingPassed = projected.Passed,
            totalAssessments = assessments.Count,
            weakPhonemes,
            progressOverTime = assessments.Select(a => new
            {
                date = a.CreatedAt,
                overall = Math.Round(a.OverallScore, 1),
                accuracy = Math.Round(a.AccuracyScore, 1),
                fluency = Math.Round(a.FluencyScore, 1),
                projectedScaled = a.ProjectedSpeakingScaled,
            }).ToList(),
            phonemeProgress = progress
                .Where(p => p.PhonemeCode != "_speech_overall")
                .Select(p => new
                {
                    phonemeCode = p.PhonemeCode,
                    averageScore = Math.Round(p.AverageScore, 1),
                    attemptCount = p.AttemptCount,
                    lastPracticedAt = p.LastPracticedAt,
                    nextDueAt = p.NextDueAt,
                    intervalDays = p.IntervalDays,
                }).ToList()
        };
    }

    public async Task<object> GetMyProgressAsync(string userId, CancellationToken ct)
    {
        var progress = await db.LearnerPronunciationProgress
            .Where(p => p.UserId == userId && p.PhonemeCode != "_speech_overall")
            .ToListAsync(ct);

        return progress
            .OrderByDescending(p => p.LastPracticedAt)
            .Select(p => new
        {
            phonemeCode = p.PhonemeCode,
            averageScore = Math.Round(p.AverageScore, 1),
            attemptCount = p.AttemptCount,
            lastPracticedAt = p.LastPracticedAt,
            nextDueAt = p.NextDueAt,
            intervalDays = p.IntervalDays,
        }).ToList();
    }

    public async Task<object> GetDrillsAsync(string? profession, string? difficulty, string? focus, CancellationToken ct)
    {
        var q = db.PronunciationDrills.Where(d => d.Status == "active");
        if (!string.IsNullOrWhiteSpace(profession))
        {
            q = q.Where(d => d.Profession == profession || d.Profession == "all");
        }
        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            q = q.Where(d => d.Difficulty == difficulty);
        }
        if (!string.IsNullOrWhiteSpace(focus))
        {
            q = q.Where(d => d.Focus == focus);
        }

        var drills = await q
            .OrderBy(d => d.Difficulty == "easy" ? 0 : d.Difficulty == "medium" ? 1 : 2)
            .ThenBy(d => d.OrderIndex)
            .ThenBy(d => d.Label)
            .ToListAsync(ct);

        return drills.Select(ToDrillDto).ToList();
    }

    public async Task<IReadOnlyList<object>> GetDueDrillsAsync(string userId, int limit, CancellationToken ct)
    {
        var due = await scheduler.GetDueDrillsAsync(userId, limit, ct);
        return due.Select(ToDrillDto).ToList();
    }

    public async Task<object> GetDrillAsync(string drillId, CancellationToken ct)
    {
        var drill = await db.PronunciationDrills.FindAsync([drillId], ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", "Pronunciation drill not found.");
        return ToDrillDto(drill);
    }

    public async Task<PronunciationEntitlement> GetEntitlementAsync(string? userId, CancellationToken ct)
        => await entitlement.CheckAsync(userId, ct);

    // ── Attempt lifecycle ──────────────────────────────────────────────────

    public async Task<object> InitAttemptAsync(string userId, string drillId, CancellationToken ct)
    {
        var drill = await db.PronunciationDrills.FindAsync([drillId], ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", "Pronunciation drill not found.");

        var gate = await entitlement.CheckAsync(userId, ct);
        if (!gate.Allowed)
        {
            throw ApiException.Forbidden("PRONUNCIATION_QUOTA_EXHAUSTED", gate.Reason);
        }

        var attempt = new PronunciationAttempt
        {
            Id = $"prn-{Guid.NewGuid():N}",
            UserId = userId,
            DrillId = drillId,
            Status = "awaiting_upload",
            CreatedAt = DateTimeOffset.UtcNow,
            AudioReapAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _opts.AudioRetentionDays)),
        };
        db.PronunciationAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        return new
        {
            attemptId = attempt.Id,
            drillId,
            uploadUrl = $"/v1/pronunciation/drills/{drillId}/attempt/{attempt.Id}/audio",
            maxAudioBytes = _opts.MaxAudioBytes,
            allowedMimeTypes = _opts.AllowedMimeTypes,
            entitlement = new { gate.Tier, gate.Remaining, gate.LimitPerWindow, gate.WindowDays, gate.ResetAt },
        };
    }

    /// <summary>
    /// Accept the audio stream for an attempt, run the ASR + grading pipeline
    /// synchronously, and persist a full assessment row. The synchronous path
    /// keeps UX simple (no polling) while still producing honest work —
    /// transcription + grounded AI feedback complete in a few seconds.
    /// </summary>
    public async Task<object> UploadAndScoreAsync(
        string userId,
        string drillId,
        string attemptId,
        Stream audio,
        string mimeType,
        long? contentLength,
        int? audioDurationMs,
        CancellationToken ct)
    {
        var attempt = await db.PronunciationAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && a.DrillId == drillId, ct)
            ?? throw ApiException.NotFound("ATTEMPT_NOT_FOUND", "Pronunciation attempt not found.");

        if (attempt.Status != "awaiting_upload")
        {
            throw ApiException.Validation("ATTEMPT_ALREADY_COMPLETED", "Attempt already uploaded.");
        }

        if (!_opts.AllowedMimeTypes.Any(m => string.Equals(m, mimeType, StringComparison.OrdinalIgnoreCase)))
        {
            attempt.Status = "refused";
            attempt.ErrorCode = "invalid_mime";
            attempt.ErrorMessage = $"Mime type '{mimeType}' is not allowed.";
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation("AUDIO_MIME_REJECTED", attempt.ErrorMessage);
        }

        if (contentLength.HasValue && contentLength.Value > _opts.MaxAudioBytes)
        {
            attempt.Status = "refused";
            attempt.ErrorCode = "audio_too_large";
            attempt.ErrorMessage = $"Audio exceeds {_opts.MaxAudioBytes} bytes.";
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation("AUDIO_TOO_LARGE", attempt.ErrorMessage);
        }

        var drill = await db.PronunciationDrills.FindAsync([drillId], ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", "Pronunciation drill not found.");

        // ── Persist the blob ──────────────────────────────────────────────
        var extension = MimeToExtension(mimeType);
        var storageKey = $"pronunciation/{userId[..Math.Min(userId.Length, 16)]}/{attemptId}.{extension}";
        long bytes = 0;
        try
        {
            await using var writer = await storage.OpenWriteAsync(storageKey, ct);
            await audio.CopyToAsync(writer, ct);
            bytes = storage.Length(storageKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist pronunciation audio for attempt {AttemptId}", attemptId);
            attempt.Status = "failed";
            attempt.ErrorCode = "storage_error";
            attempt.ErrorMessage = "Could not store audio.";
            await db.SaveChangesAsync(ct);
            throw;
        }

        if (bytes > _opts.MaxAudioBytes)
        {
            storage.Delete(storageKey);
            attempt.Status = "refused";
            attempt.ErrorCode = "audio_too_large";
            attempt.ErrorMessage = $"Audio exceeds {_opts.MaxAudioBytes} bytes (received {bytes}).";
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation("AUDIO_TOO_LARGE", attempt.ErrorMessage);
        }

        attempt.AudioStorageKey = storageKey;
        attempt.AudioBytes = bytes;
        attempt.AudioMimeType = mimeType;
        attempt.AudioDurationMs = audioDurationMs;
        attempt.Status = "processing";
        await db.SaveChangesAsync(ct);

        // ── Run ASR ───────────────────────────────────────────────────────
        var provider = asrSelector.Select();
        AsrResult asrResult;
        try
        {
            await using var audioStream = await storage.OpenReadAsync(storageKey, ct);
            var referenceText = BuildReferenceText(drill);
            asrResult = await provider.AnalyzeAsync(new AsrRequest(
                Audio: audioStream,
                AudioMimeType: mimeType,
                ReferenceText: referenceText,
                TargetPhoneme: drill.TargetPhoneme,
                Locale: "en-GB",
                TargetRuleId: drill.PrimaryRuleId,
                RulebookProfession: drill.Profession == "all" ? "medicine" : drill.Profession,
                AudioBytes: bytes,
                UserId: userId
            ), ct);
        }
        catch (PronunciationAsrException ex)
        {
            logger.LogWarning(ex, "ASR provider '{Provider}' error {Code}: {Message}", provider.Name, ex.Code, ex.Message);
            attempt.Status = "failed";
            attempt.ErrorCode = ex.Code;
            attempt.ErrorMessage = ex.Message;
            attempt.Provider = provider.Name;
            await db.SaveChangesAsync(ct);
            throw ApiException.Conflict("ASR_PROVIDER_ERROR", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected ASR error for attempt {AttemptId}", attemptId);
            attempt.Status = "failed";
            attempt.ErrorCode = "asr_unexpected";
            attempt.ErrorMessage = ex.Message;
            attempt.Provider = provider.Name;
            await db.SaveChangesAsync(ct);
            throw;
        }

        // ── Persist assessment ───────────────────────────────────────────
        var projected = OetScoring.PronunciationProjectedBand(asrResult.OverallScore);
        var assessment = new PronunciationAssessment
        {
            Id = $"pa-{Guid.NewGuid():N}",
            UserId = userId,
            DrillId = drillId,
            AttemptId = attemptId,
            AccuracyScore = asrResult.AccuracyScore,
            FluencyScore = asrResult.FluencyScore,
            CompletenessScore = asrResult.CompletenessScore,
            ProsodyScore = asrResult.ProsodyScore,
            OverallScore = asrResult.OverallScore,
            ProjectedSpeakingScaled = projected.ScaledScore,
            ProjectedSpeakingGrade = projected.Grade,
            WordScoresJson = JsonSupport.Serialize(asrResult.WordScores),
            ProblematicPhonemesJson = JsonSupport.Serialize(asrResult.ProblematicPhonemes),
            FluencyMarkersJson = JsonSupport.Serialize(asrResult.FluencyMarkers),
            FindingsJson = "[]",
            FeedbackJson = "{}",
            Provider = asrResult.ProviderName,
            RulebookVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PronunciationAssessments.Add(assessment);

        // ── Update progress + scheduler ──────────────────────────────────
        var progress = await db.LearnerPronunciationProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PhonemeCode == drill.TargetPhoneme, ct);
        if (progress is null)
        {
            progress = new LearnerPronunciationProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PhonemeCode = drill.TargetPhoneme,
                AverageScore = asrResult.OverallScore,
                AttemptCount = 1,
                ScoreHistoryJson = JsonSupport.Serialize(new[] { Math.Round(asrResult.OverallScore, 1) }),
                LastPracticedAt = DateTimeOffset.UtcNow,
                IntervalDays = 0,
                Ease = 2.5,
            };
            db.LearnerPronunciationProgress.Add(progress);
        }
        else
        {
            progress.AttemptCount++;
            progress.AverageScore =
                (progress.AverageScore * (progress.AttemptCount - 1) + asrResult.OverallScore) / progress.AttemptCount;
            progress.LastPracticedAt = DateTimeOffset.UtcNow;
            var history = JsonSupport.Deserialize(progress.ScoreHistoryJson, new List<double>());
            history.Add(Math.Round(asrResult.OverallScore, 1));
            if (history.Count > 20) history.RemoveRange(0, history.Count - 20);
            progress.ScoreHistoryJson = JsonSupport.Serialize(history);
        }

        attempt.Status = "completed";
        attempt.AssessmentId = assessment.Id;
        attempt.Provider = asrResult.ProviderName;
        attempt.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // ── Grounded AI feedback (cached on the assessment) ──────────────
        try
        {
            var fb = await feedback.GenerateAsync(assessment, drill, userId, drill.Profession, ct);
            assessment.FeedbackJson = JsonSupport.Serialize(fb);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pronunciation AI feedback failed (non-fatal) for assessment {AssessmentId}", assessment.Id);
        }

        // ── Advance the spaced-repetition schedule ───────────────────────
        try
        {
            await scheduler.UpdateScheduleAsync(userId, drill.TargetPhoneme, asrResult.OverallScore, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scheduler update failed (non-fatal)");
        }

        return BuildAssessmentDto(assessment);
    }

    public async Task<object> GetAssessmentAsync(string userId, string assessmentId, CancellationToken ct)
    {
        var a = await db.PronunciationAssessments
            .FirstOrDefaultAsync(x => x.Id == assessmentId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("ASSESSMENT_NOT_FOUND", "Pronunciation assessment not found.");
        return BuildAssessmentDto(a);
    }

    /// <summary>
    /// Called from <c>ExpertService.SubmitReviewAsync</c> after a speaking review
    /// is saved. Derives a lightweight pronunciation aggregate row so the
    /// learner sees speaking performance reflected in their pronunciation
    /// dashboard. Per-phoneme ASR of the speaking attempt is tracked in the
    /// Phase 3 speaking-loop extension.
    /// </summary>
    public async Task CreateFromSpeakingReviewAsync(
        string userId,
        string attemptId,
        Dictionary<string, object?> criterionScores,
        CancellationToken ct)
    {
        var exists = await db.PronunciationAssessments
            .AnyAsync(a => a.AttemptId == attemptId && a.Provider == "speaking-review", ct);
        if (exists) return;

        double intelligibility = ExtractScore(criterionScores, "intelligibility");
        double fluency = ExtractScore(criterionScores, "fluency");
        double appropriateness = ExtractScore(criterionScores, "appropriateness");
        double grammar = ExtractScore(criterionScores, "grammar");

        double accuracy = NormalizeTo100(intelligibility);
        double fluencyScore = NormalizeTo100(fluency);
        double completeness = NormalizeTo100((appropriateness + grammar) / 2.0);
        double prosody = NormalizeTo100((intelligibility + fluency) / 2.0);
        double overall = (accuracy + fluencyScore + completeness + prosody) / 4.0;
        var projected = OetScoring.PronunciationProjectedBand(overall);

        var a = new PronunciationAssessment
        {
            Id = $"pa-spk-{Guid.NewGuid():N}",
            UserId = userId,
            AttemptId = attemptId,
            AccuracyScore = Math.Round(accuracy, 1),
            FluencyScore = Math.Round(fluencyScore, 1),
            CompletenessScore = Math.Round(completeness, 1),
            ProsodyScore = Math.Round(prosody, 1),
            OverallScore = Math.Round(overall, 1),
            ProjectedSpeakingScaled = projected.ScaledScore,
            ProjectedSpeakingGrade = projected.Grade,
            WordScoresJson = "[]",
            ProblematicPhonemesJson = "[]",
            FluencyMarkersJson = JsonSupport.Serialize(new
            {
                source = "expert_review",
                intelligibilityRaw = intelligibility,
                fluencyRaw = fluency,
                appropriatenessRaw = appropriateness,
                grammarRaw = grammar
            }),
            FindingsJson = "[]",
            FeedbackJson = "{}",
            Provider = "speaking-review",
            RulebookVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PronunciationAssessments.Add(a);

        var progress = await db.LearnerPronunciationProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PhonemeCode == "_speech_overall", ct);
        if (progress is null)
        {
            progress = new LearnerPronunciationProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PhonemeCode = "_speech_overall",
                AverageScore = overall,
                AttemptCount = 1,
                ScoreHistoryJson = JsonSupport.Serialize(new[] { Math.Round(overall, 1) }),
                LastPracticedAt = DateTimeOffset.UtcNow,
            };
            db.LearnerPronunciationProgress.Add(progress);
        }
        else
        {
            progress.AttemptCount++;
            progress.AverageScore = (progress.AverageScore * (progress.AttemptCount - 1) + overall) / progress.AttemptCount;
            progress.LastPracticedAt = DateTimeOffset.UtcNow;
            var history = JsonSupport.Deserialize(progress.ScoreHistoryJson, new List<double>());
            history.Add(Math.Round(overall, 1));
            if (history.Count > 20) history.RemoveRange(0, history.Count - 20);
            progress.ScoreHistoryJson = JsonSupport.Serialize(history);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<object> GetSpeakingLinkedAssessmentsAsync(string userId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        var list = await db.PronunciationAssessments
            .Where(a => a.UserId == userId && a.AttemptId != null && a.Provider == "speaking-review")
            .ToListAsync(ct);

        return list
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new
        {
            id = a.Id,
            attemptId = a.AttemptId,
            accuracy = a.AccuracyScore,
            fluency = a.FluencyScore,
            completeness = a.CompletenessScore,
            prosody = a.ProsodyScore,
            overall = a.OverallScore,
            projectedSpeakingScaled = a.ProjectedSpeakingScaled,
            projectedSpeakingGrade = a.ProjectedSpeakingGrade,
            createdAt = a.CreatedAt
        }).ToList();
    }

    // ── Discrimination (minimal-pair) ───────────────────────────────────────

    public async Task<object> SubmitDiscriminationAsync(
        string userId,
        string drillId,
        int roundsTotal,
        int roundsCorrect,
        CancellationToken ct)
    {
        var drill = await db.PronunciationDrills.FindAsync([drillId], ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", "Pronunciation drill not found.");

        if (roundsTotal <= 0)
            throw ApiException.Validation("INVALID_ROUNDS", "At least one round is required.");

        var row = new LearnerPronunciationDiscriminationAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DrillId = drillId,
            TargetPhoneme = drill.TargetPhoneme,
            RoundsTotal = roundsTotal,
            RoundsCorrect = Math.Clamp(roundsCorrect, 0, roundsTotal),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.LearnerPronunciationDiscriminationAttempts.Add(row);
        await db.SaveChangesAsync(ct);
        return new
        {
            id = row.Id,
            roundsTotal = row.RoundsTotal,
            roundsCorrect = row.RoundsCorrect,
            accuracy = Math.Round(row.RoundsCorrect * 100.0 / row.RoundsTotal, 1),
            targetPhoneme = row.TargetPhoneme,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static object ToDrillDto(PronunciationDrill d) => new
    {
        id = d.Id,
        phoneme = d.TargetPhoneme,
        targetPhoneme = d.TargetPhoneme,
        title = d.Label,
        label = d.Label,
        profession = d.Profession,
        focus = d.Focus,
        primaryRuleId = d.PrimaryRuleId,
        description = d.TipsHtml,
        difficultyLevel = d.Difficulty,
        difficulty = d.Difficulty,
        audioExampleUrl = d.AudioModelUrl,
        audioModelUrl = d.AudioModelUrl,
        audioModelAssetId = d.AudioModelAssetId,
        exampleWordsJson = d.ExampleWordsJson,
        minimalPairsJson = d.MinimalPairsJson,
        sentencesJson = d.SentencesJson,
        tipsHtml = d.TipsHtml,
    };

    private static object BuildAssessmentDto(PronunciationAssessment a) => new
    {
        id = a.Id,
        drillId = a.DrillId,
        attemptId = a.AttemptId,
        accuracy = a.AccuracyScore,
        fluency = a.FluencyScore,
        completeness = a.CompletenessScore,
        prosody = a.ProsodyScore,
        overall = a.OverallScore,
        projectedSpeakingScaled = a.ProjectedSpeakingScaled,
        projectedSpeakingGrade = a.ProjectedSpeakingGrade,
        wordScoresJson = a.WordScoresJson,
        problematicPhonemesJson = a.ProblematicPhonemesJson,
        fluencyMarkersJson = a.FluencyMarkersJson,
        findingsJson = a.FindingsJson,
        feedbackJson = a.FeedbackJson,
        provider = a.Provider,
        rulebookVersion = a.RulebookVersion,
        createdAt = a.CreatedAt,
    };

    private static string BuildReferenceText(PronunciationDrill d)
    {
        var sentences = JsonSupport.Deserialize(d.SentencesJson, new List<string>());
        if (sentences.Count > 0) return string.Join(" ", sentences);
        var words = JsonSupport.Deserialize(d.ExampleWordsJson, new List<string>());
        if (words.Count > 0) return string.Join(", ", words);
        return d.Label;
    }

    private static string MimeToExtension(string mime) => mime switch
    {
        "audio/webm" => "webm",
        "audio/ogg" => "ogg",
        "audio/mpeg" => "mp3",
        "audio/mp4" => "m4a",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/aac" => "aac",
        _ => "bin",
    };

    private static double ExtractScore(Dictionary<string, object?> scores, string key)
    {
        if (scores.TryGetValue(key, out var val) && val is not null)
        {
            if (val is double d) return d;
            if (val is int i) return i;
            if (val is long l) return l;
            if (val is decimal m) return (double)m;
            if (val is System.Text.Json.JsonElement je && je.TryGetDouble(out var jd)) return jd;
            if (double.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return 0;
    }

    private static double NormalizeTo100(double score)
    {
        if (score <= 0) return 0;
        if (score <= 6) return Math.Min(100, score / 6.0 * 100);
        if (score <= 100) return Math.Min(100, score);
        if (score <= 500) return Math.Min(100, score / 500.0 * 100);
        return 100;
    }
}
