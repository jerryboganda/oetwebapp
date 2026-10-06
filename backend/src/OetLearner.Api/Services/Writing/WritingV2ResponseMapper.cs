using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Who a grade response is for. <see cref="Candidate"/> (the default) never carries rule ids, model or canon
/// versions, provider tags or an unreviewed confidence flag; <see cref="Staff"/> (tutor review detail) keeps
/// the raw values tutors work with.
/// </summary>
public enum WritingGradeAudience
{
    Candidate,
    Staff,
}

/// <summary>
/// Translation layer between the WS5 service "view" record types and the
/// WS6 endpoint contract records (declared in
/// <see cref="WritingV2Contracts" />). Endpoints take the contract types
/// as their <c>.Produces&lt;T&gt;</c> declarations; services keep their
/// richer view types for internal call-sites. This mapper bridges the gap
/// in a single well-known place.
/// </summary>
public static class WritingV2ResponseMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WritingPathwayResponseV2 ToResponse(WritingPathwayV2View view)
    {
        return new WritingPathwayResponseV2(
            CurrentStage: view.CurrentStage,
            TotalWeeks: view.TotalWeeks,
            CurrentWeek: view.CurrentWeek,
            WeeksRemaining: view.WeeksRemaining,
            ReadinessScore: view.ReadinessScore,
            PredictedBand: view.PredictedBand,
            GeneratedAt: view.GeneratedAt,
            LastRecalculatedAt: view.LastRecalculatedAt,
            WeaknessVector: view.WeaknessVector,
            SubSkillMastery: view.SubSkillMastery,
            Items: view.Items.Select(i => new WritingPathwayItemResponse(
                Id: i.Id,
                OrderIndex: i.OrderIndex,
                Stage: i.Stage,
                Phase: i.Phase,
                WeekNumber: i.WeekNumber,
                FocusSkill: i.FocusSkill,
                FocusCriterion: i.FocusCriterion,
                ItemKind: i.ItemKind,
                ContentRefId: i.ContentRefId,
                Title: i.Title,
                Description: i.Description,
                EstimatedMinutes: i.EstimatedMinutes,
                IsCompleted: i.IsCompleted)).ToList());
    }

    public static WritingTodayPlanResponseV2 ToResponse(WritingDailyPlanView view)
    {
        return new WritingTodayPlanResponseV2(
            Date: view.Date.ToString("O"),
            Items: view.Items.Select(ToResponse).ToList(),
            TotalMinutes: view.TotalMinutes,
            CompletedCount: view.CompletedCount,
            RegenerationsRemaining: view.RegenerationsRemaining);
    }

    public static WritingTodayPlanItemResponseV2 ToResponse(WritingDailyPlanItemView item)
        => new(
            Id: item.Id,
            Ordinal: item.Ordinal,
            ItemKind: item.ItemKind,
            FocusSkill: item.FocusSkill,
            FocusCriterion: item.FocusCriterion,
            EstimatedMinutes: item.EstimatedMinutes,
            Title: item.Title,
            Description: item.Description,
            ActionHref: item.ActionHref,
            ContentId: item.ContentId,
            Status: item.Status);

    public static WritingScenarioResponse ToResponse(WritingScenarioView view)
    {
        var pdfId = view.StimulusPdfMediaAssetId;
        return new(
            Id: view.Id,
            Title: view.Title,
            LetterType: view.LetterType,
            Profession: view.Profession,
            SubDiscipline: view.SubDiscipline,
            Topics: view.Topics,
            Difficulty: view.Difficulty,
            CaseNotesStructured: view.CaseNotesStructured.Select(s => new WritingScenarioStructuredSentenceResponse(s.Ordinal, s.SentenceText, s.Relevance)).ToList(),
            IsDiagnostic: view.IsDiagnostic,
            Status: view.Status,
            CreatedAt: view.CreatedAt,
            UpdatedAt: view.CreatedAt,
            StimulusPdfMediaAssetId: pdfId,
            StimulusPdfDownloadPath: string.IsNullOrWhiteSpace(pdfId) ? null : $"/v1/media/{pdfId}/content",
            TaskPromptMarkdown: view.TaskPromptMarkdown,
            FixedInstructions: view.FixedInstructions,
            ReadingTimeSeconds: view.ReadingTimeSeconds,
            WritingTimeSeconds: view.WritingTimeSeconds,
            WordGuideMin: view.WordGuideMin,
            WordGuideMax: view.WordGuideMax,
            WriterRole: view.WriterRole,
            TodayDate: view.TodayDate);
    }

    public static WritingCanonRuleResponseV2 ToResponse(WritingCanonRuleView view)
        => new(
            Id: view.Id,
            Category: view.Category,
            AppliesToLetterTypes: view.AppliesToLetterTypes,
            AppliesToProfessions: view.AppliesToProfessions,
            Severity: view.Severity,
            RuleText: view.RuleText,
            CorrectExamples: view.CorrectExamples,
            IncorrectExamples: view.IncorrectExamples,
            DetectionType: view.DetectionType,
            LessonId: view.LessonId?.ToString(),
            Version: view.Version.ToString(),
            Active: view.Active);

    public static WritingCanonViolationResponse ToResponse(WritingCanonViolation v, string ruleText)
        => new(
            Id: v.Id,
            SubmissionId: v.SubmissionId,
            RuleId: v.RuleId,
            RuleText: ruleText,
            Severity: v.Severity,
            Snippet: v.Snippet ?? string.Empty,
            LineNumber: v.LineNumber ?? 0,
            CharStart: v.CharStart ?? 0,
            CharEnd: v.CharEnd ?? 0,
            SuggestedFix: v.SuggestedFix,
            Disputed: v.Disputed,
            DisputeResolution: v.DisputeResolution);

    public static WritingLessonResponseV2 ToResponse(WritingLessonV2View view)
        => new(
            Id: view.Id,
            SubSkill: view.SubSkill,
            OrderInCourse: view.OrderInCourse,
            Title: view.Title,
            BodyMarkdown: view.BodyMarkdown,
            VideoUrl: view.VideoUrl,
            EstimatedMinutes: view.EstimatedMinutes,
            QuizQuestions: view.QuizQuestions.Select(q => new WritingLessonQuizQuestionResponseV2(
                Id: q.Id,
                Question: q.Question,
                Options: q.Options,
                CorrectIndex: q.CorrectIndex,
                Explanation: q.Explanation ?? string.Empty)).ToList(),
            Status: view.Status);

    public static WritingMockResponse ToResponse(WritingMockTemplate template)
        => new(Id: template.Id, ScenarioId: template.ScenarioId, Title: template.Title, Status: template.Status);

    public static WritingMockSessionResponse ToResponse(WritingMockSessionView view)
        => new(
            Id: view.Id,
            MockId: view.MockId,
            ScenarioId: view.ScenarioId,
            Status: view.Status,
            StartedAt: view.StartedAt,
            ReadingPhaseEndedAt: view.ReadingPhaseEndedAt,
            SubmittedAt: view.SubmittedAt,
            SubmissionId: view.SubmissionId,
            ReadingSecondsRemaining: view.ReadingSecondsRemaining,
            WritingSecondsRemaining: view.WritingSecondsRemaining,
            IsPractice: view.IsPractice);

    public static WritingCommonMistakeResponse ToResponse(WritingCommonMistakeView view)
        => new(
            Id: view.Id,
            Category: view.Category,
            Summary: view.Summary,
            ExampleWrong: view.ExampleWrong,
            ExampleRight: view.ExampleRight,
            CanonRuleId: view.CanonRuleId,
            RelatedSubSkill: view.RelatedSubSkill);

    public static WritingShowcasePostResponse ToResponse(WritingShowcasePostView view)
        => new(
            Id: view.Id,
            SubmissionId: view.SubmissionId,
            AnonymizedLetterContent: view.AnonymizedLetterContent,
            Profession: view.Profession,
            LetterType: view.LetterType,
            Status: view.Status,
            PublishedAt: view.PublishedAt ?? view.CreatedAt,
            ReactionCount: 0);

    public static WritingTutorReviewResponse ToResponse(WritingTutorReviewView view)
    {
        IReadOnlyDictionary<string, string>? perCriterion = null;
        IReadOnlyDictionary<string, double>? scoreOverride = null;
        if (!string.IsNullOrWhiteSpace(view.PerCriterionCommentsJson))
        {
            try { perCriterion = JsonSerializer.Deserialize<Dictionary<string, string>>(view.PerCriterionCommentsJson, JsonOptions); }
            catch (JsonException) { perCriterion = null; }
        }
        if (!string.IsNullOrWhiteSpace(view.ScoreOverrideJson))
        {
            try { scoreOverride = JsonSerializer.Deserialize<Dictionary<string, double>>(view.ScoreOverrideJson, JsonOptions); }
            catch (JsonException) { scoreOverride = null; }
        }
        return new WritingTutorReviewResponse(
            Id: view.Id,
            SubmissionId: view.SubmissionId,
            TutorId: view.TutorId,
            TutorDisplayName: null,
            Status: view.Status,
            FreeTextFeedback: view.FreeTextFeedback,
            PerCriterionComments: perCriterion,
            ScoreOverride: scoreOverride,
            SubmittedAt: view.SubmittedAt);
    }

    public static WritingTutorQueueItemResponse ToResponse(WritingTutorQueueEntry entry)
        => new(
            SubmissionId: entry.SubmissionId,
            UserId: entry.LearnerId,
            Profession: entry.Profession,
            LetterType: entry.LetterType,
            WordCount: entry.WordCount,
            RequestedAt: entry.SubmittedAt,
            ClaimedAt: entry.ClaimedAt,
            ClaimedByTutorId: entry.ClaimedByTutorId,
            Status: entry.Status,
            ReviewReason: entry.ReviewReason);

    public static WritingOcrJobResponse ToResponse(WritingOcrJobView view)
        => new(
            Id: view.Id,
            SubmissionId: view.SubmissionId,
            Status: view.Status,
            Provider: view.Provider,
            ConfidenceScore: view.ConfidenceScore is null ? null : (int)Math.Round(view.ConfidenceScore.Value * 100),
            ExtractedText: view.ExtractedText,
            ImageUrls: view.ImageUrls,
            ErrorMessage: view.ErrorMessage,
            CreatedAt: view.CreatedAt,
            CompletedAt: view.CompletedAt);

    /// <summary>
    /// The legacy grade projection. For a candidate (the default) nothing internal leaves it: no cited rule
    /// ids, no model or canon version, no rule id or rule text on a canon violation, the Jev review flag reads
    /// "awaiting_review", the suggested fix and feedback are plain English and the priorities are clean and
    /// label-free. The candidate's own quoted wording stays verbatim. <see cref="WritingGradeAudience.Staff"/>
    /// (tutor review detail) keeps the raw stored values.
    /// </summary>
    public static WritingGradeResponseV2 ToGradeResponse(
        WritingGrade grade,
        IReadOnlyList<WritingCanonViolation> violations,
        IReadOnlyDictionary<string, string> ruleText,
        WritingGradeAudience audience = WritingGradeAudience.Candidate)
    {
        var candidate = audience == WritingGradeAudience.Candidate;
        Dictionary<string, WritingPerCriterionFeedbackResponse> perCriterion = new();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(grade.PerCriterionFeedbackJson, JsonOptions) ?? new();
            foreach (var (key, el) in raw)
            {
                int score = 0;
                string feedback = string.Empty;
                string? suggestedFix = null;
                string? quote = null;
                var cited = new List<string>();
                if (el.ValueKind == JsonValueKind.Object)
                {
                    if (el.TryGetProperty("score", out var sEl) && sEl.TryGetInt32(out var s)) score = s;
                    if (el.TryGetProperty("feedback", out var fEl) && fEl.ValueKind == JsonValueKind.String) feedback = WritingReportDigest.Clip(fEl.GetString(), WritingReportDigest.SummaryMaxChars);
                    // Stored as suggestedFix (new) or exemplarFix (older rows): the same suggested fix.
                    if (el.TryGetProperty("suggestedFix", out var sfEl) && sfEl.ValueKind == JsonValueKind.String) suggestedFix = sfEl.GetString();
                    else if (el.TryGetProperty("exemplarFix", out var eEl) && eEl.ValueKind == JsonValueKind.String) suggestedFix = eEl.GetString();
                    // Addendum Rev8 §19.4: the candidate's own wording the grader flagged.
                    if (el.TryGetProperty("quote", out var qEl) && qEl.ValueKind == JsonValueKind.String) quote = qEl.GetString();
                    if (!candidate && el.TryGetProperty("citedRuleIds", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in cEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var v = item.GetString();
                                if (!string.IsNullOrWhiteSpace(v)) cited.Add(v);
                            }
                        }
                    }
                }
                if (candidate) suggestedFix = WritingCandidateText.CleanOrNull(suggestedFix);
                perCriterion[key] = new WritingPerCriterionFeedbackResponse(score, feedback, suggestedFix, cited, quote, suggestedFix);
            }
        }
        catch (JsonException)
        {
            perCriterion = new();
        }

        var priorities = new List<string>();
        try
        {
            var raw = JsonSerializer.Deserialize<List<string>>(grade.TopThreePrioritiesJson, JsonOptions);
            if (raw is not null) priorities = raw;
        }
        catch (JsonException) { /* ignore */ }

        // The Jev review flag is an internal state: a candidate only learns that the result is awaiting review.
        var confidenceFlag = grade.ConfidenceFlag ?? "medium";
        if (candidate && string.Equals(confidenceFlag, "jev_review", StringComparison.OrdinalIgnoreCase))
            confidenceFlag = "awaiting_review";

        return new WritingGradeResponseV2(
            Id: grade.Id,
            SubmissionId: grade.SubmissionId,
            C1Purpose: grade.C1Purpose,
            C2Content: grade.C2Content,
            C3Conciseness: grade.C3Conciseness,
            C4Genre: grade.C4Genre,
            C5Organisation: grade.C5Organisation,
            C6Language: grade.C6Language,
            RawTotal: grade.RawTotal,
            EstimatedBand: grade.EstimatedBand,
            BandLabel: grade.BandLabel,
            PerCriterion: perCriterion,
            TopThreePriorities: candidate ? WritingReportDigest.CleanStoredPriorities(priorities) : priorities,
            ConfidenceFlag: confidenceFlag,
            ModelUsed: candidate ? string.Empty : grade.ModelUsed,
            CanonVersion: candidate ? string.Empty : grade.CanonVersion,
            CanonViolations: violations
                .Select(v => candidate
                    ? ToCandidateResponse(v)
                    : ToResponse(v, ruleText.TryGetValue(v.RuleId, out var t) ? t : string.Empty))
                .ToList(),
            GradedAt: grade.GradedAt);
    }

    /// <summary>
    /// A canon violation as the candidate sees it: the violation's own id (the dispute call needs it), the
    /// flagged wording and a plain suggested fix. The rule id and the rule text are internal and are not sent.
    /// </summary>
    public static WritingCanonViolationResponse ToCandidateResponse(WritingCanonViolation v)
        => new(
            Id: v.Id,
            SubmissionId: v.SubmissionId,
            RuleId: string.Empty,
            RuleText: string.Empty,
            Severity: v.Severity,
            Snippet: v.Snippet ?? string.Empty,
            LineNumber: v.LineNumber ?? 0,
            CharStart: v.CharStart ?? 0,
            CharEnd: v.CharEnd ?? 0,
            SuggestedFix: WritingCandidateText.CleanOrNull(v.SuggestedFix),
            Disputed: v.Disputed,
            // The stored value is "pending:" plus the learner's own reason; only the state is returned.
            DisputeResolution: v.DisputeResolution is { } resolution && resolution.StartsWith("pending", StringComparison.OrdinalIgnoreCase)
                ? "pending"
                : v.DisputeResolution);

    /// <summary>
    /// <c>CanRetry</c>: a failed run that Retry can help (legacy rows without a verdict count as
    /// retryable), a grading claim past the lease, or a queued row nobody picked up within it.
    /// <c>AutoRetrying</c>: the server re-queued a failed run by itself.
    /// <para/>
    /// Release window (owner handoff, 6 Oct 2026): <paramref name="learnerUnrestricted"/> is the learner's
    /// allowlist state. <c>null</c> means staff/raw mode, which is never held; <c>false</c> is a normal
    /// candidate, whose <c>graded</c> row reads <c>grading</c> until the 15-minute window has elapsed;
    /// <c>true</c> is an allowlisted account, which is released as soon as grading completes. The status is
    /// the EFFECTIVE status, so every existing "graded" consumer keeps waiting; the retry/failure fields
    /// still read the raw row (a held row is neither failed nor stale).
    /// </summary>
    public static WritingSubmissionResponse ToSubmissionResponse(
        WritingSubmission s,
        DateTimeOffset? now = null,
        bool? learnerUnrestricted = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var view = WritingResultRelease.Describe(s.Status, s.SubmittedAt, learnerUnrestricted ?? true, at);
        var failed = s.Status == WritingSubmissionStatuses.Failed;
        var autoRetrying = s.Status == WritingSubmissionStatuses.Queued && s.AutoRetryCount > 0;
        return new(
            Id: s.Id,
            UserId: s.UserId,
            ScenarioId: s.ScenarioId,
            Mode: s.Mode,
            LetterContent: s.LetterContent,
            ContentHash: s.LetterContentHash,
            WordCount: s.WordCount,
            TimeSpentSeconds: s.TimeSpentSeconds,
            StartedAt: s.StartedAt,
            SubmittedAt: s.SubmittedAt,
            IsRevision: s.IsRevision,
            OriginalSubmissionId: s.OriginalSubmissionId,
            Status: view.EffectiveStatus,
            GradingTier: s.GradingTier,
            InputSource: s.InputSource,
            FailureCode: failed || autoRetrying ? s.FailureCode : null,
            CanRetry: (failed && s.FailureRetryable != false)
                || WritingGradeRecovery.IsStaleGrading(s, at)
                || WritingGradeRecovery.IsStaleQueued(s, at),
            AutoRetrying: autoRetrying,
            AttemptCount: s.GradeEpoch,
            ReleaseState: view.State,
            ReleaseAt: view.ReleaseAt,
            ServerNow: at);
    }

    public static WritingDraftV2Response ToResponse(WritingDraftV2View view)
        => new(
            UserId: view.UserId,
            ScenarioId: view.ScenarioId,
            Mode: view.Mode,
            Content: view.Content,
            WordCount: view.WordCount,
            TimeSpentSeconds: view.TimeSpentSeconds,
            LastSavedAt: view.LastSavedAt,
            DraftId: view.DraftId,
            Version: view.Version,
            Status: view.Status,
            SubmissionId: view.SubmissionId,
            SubmissionStatus: view.SubmissionStatus,
            Phase: view.Phase,
            ReadingSecondsRemaining: view.ReadingSecondsRemaining,
            WritingSecondsRemaining: view.WritingSecondsRemaining,
            AttemptStartedAt: view.AttemptStartedAt);
}
