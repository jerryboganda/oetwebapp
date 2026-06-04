using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

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
            CaseNotesMarkdown: view.CaseNotesMarkdown,
            CaseNotesStructured: view.CaseNotesStructured.Select(s => new WritingScenarioStructuredSentenceResponse(s.Ordinal, s.SentenceText, s.Relevance)).ToList(),
            IsDiagnostic: view.IsDiagnostic,
            Status: view.Status,
            CreatedAt: view.CreatedAt,
            UpdatedAt: view.CreatedAt,
            StimulusPdfMediaAssetId: pdfId,
            StimulusPdfDownloadPath: string.IsNullOrWhiteSpace(pdfId) ? null : $"/v1/media/{pdfId}/content");
    }

    public static WritingExemplarResponse ToResponse(WritingExemplarView view)
        => new(
            Id: view.Id,
            ScenarioId: view.ScenarioId,
            Profession: view.Profession,
            LetterType: view.LetterType,
            Difficulty: 3,
            TargetBand: view.TargetBand,
            LetterContent: view.LetterContent,
            Annotations: view.Annotations.Select(a => new WritingExemplarAnnotationResponse(
                Id: Guid.NewGuid(),
                CharStart: a.CharStart ?? 0,
                CharEnd: a.CharEnd ?? 0,
                RuleId: a.RuleId,
                Note: a.Note)).ToList(),
            AuthorNote: null,
            Status: view.Status);

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
            WritingSecondsRemaining: view.WritingSecondsRemaining);

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
            Status: entry.Status);

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

    public static WritingScoreAppealResponse ToResponse(WritingAppealResult view)
        => new(
            Id: view.AppealId,
            SubmissionId: view.SubmissionId,
            Status: view.Status,
            OriginalRawTotal: view.OriginalRawTotal,
            SecondOpinionRawTotal: view.SecondOpinionRawTotal,
            FinalRawTotal: view.FinalRawTotal,
            Reasoning: view.Reasoning,
            RequestedAt: view.RequestedAt,
            ResolvedAt: view.ResolvedAt);

    public static WritingGradeResponseV2 ToGradeResponse(
        WritingGrade grade,
        IReadOnlyList<WritingCanonViolation> violations,
        IReadOnlyDictionary<string, string> ruleText,
        WritingExemplarComparisonResponse? comparison)
    {
        Dictionary<string, WritingPerCriterionFeedbackResponse> perCriterion = new();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(grade.PerCriterionFeedbackJson, JsonOptions) ?? new();
            foreach (var (key, el) in raw)
            {
                int score = 0;
                string feedback = string.Empty;
                string? exemplar = null;
                var cited = new List<string>();
                if (el.ValueKind == JsonValueKind.Object)
                {
                    if (el.TryGetProperty("score", out var sEl) && sEl.TryGetInt32(out var s)) score = s;
                    if (el.TryGetProperty("feedback", out var fEl) && fEl.ValueKind == JsonValueKind.String) feedback = fEl.GetString() ?? string.Empty;
                    if (el.TryGetProperty("exemplarFix", out var eEl) && eEl.ValueKind == JsonValueKind.String) exemplar = eEl.GetString();
                    if (el.TryGetProperty("citedRuleIds", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
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
                perCriterion[key] = new WritingPerCriterionFeedbackResponse(score, feedback, exemplar, cited);
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

        var revisionInvite = grade.RawTotal < 30
            ? new WritingRevisionInviteResponse(true, "Significant gains likely on a focused revision.")
            : new WritingRevisionInviteResponse(false, "Score within target — revision optional.");

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
            TopThreePriorities: priorities,
            ConfidenceFlag: grade.ConfidenceFlag ?? "medium",
            ModelUsed: grade.ModelUsed,
            CanonVersion: grade.CanonVersion,
            CanonViolations: violations.Select(v => ToResponse(v, ruleText.TryGetValue(v.RuleId, out var t) ? t : string.Empty)).ToList(),
            ExemplarComparison: comparison,
            RevisionInvite: revisionInvite,
            GradedAt: grade.GradedAt);
    }

    public static WritingSubmissionResponse ToSubmissionResponse(WritingSubmission s)
        => new(
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
            Status: s.Status,
            GradingTier: s.GradingTier,
            InputSource: s.InputSource);

    public static WritingDraftV2Response ToResponse(WritingDraftV2View view)
        => new(
            UserId: view.UserId,
            ScenarioId: view.ScenarioId,
            Mode: view.Mode,
            Content: view.Content,
            WordCount: view.WordCount,
            TimeSpentSeconds: view.TimeSpentSeconds,
            LastSavedAt: view.LastSavedAt);
}
