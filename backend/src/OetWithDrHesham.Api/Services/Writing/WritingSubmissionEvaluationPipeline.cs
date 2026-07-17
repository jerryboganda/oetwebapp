using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.Writing.Events;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingSubmissionGradeContext(
    string UserId,
    Guid ScenarioId,
    string Mode,
    string GradingTier,
    string InputSource,
    string LetterContent,
    int TimeSpentSeconds,
    DateTimeOffset StartedAt,
    bool IsRevision,
    Guid? OriginalSubmissionId);

public sealed record WritingSubmissionGradeOutcome(
    Guid SubmissionId,
    Guid GradeId,
    short RawTotal,
    string BandLabel,
    bool IdempotentReuse);

public interface IWritingSubmissionEvaluationPipeline
{
    Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct);
    Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct);
}

/// <summary>
/// Writing Module V2 grading pipeline.
///
/// 4 stages per spec §12.1:
///   1. Pre-flight (word count / verbatim-copy / format quick check)
///   2. AI rubric — <see cref="WritingEvaluationPipeline"/> via "writing.score.v1"
///   3. Canon engine — <see cref="IWritingCanonEngine"/> persists violations
///   4. Aggregation — top priorities + exemplar match + revision invite
///
/// Idempotency by <c>LetterContentHash</c> with 24h TTL — if a grade for this
/// hash already exists within the TTL window, the cached grade is reused and
/// no new AI call is made.
/// </summary>
public sealed class WritingSubmissionEvaluationPipeline(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    IWritingCanonEngine canonEngine,
    IWritingMistakeService mistakeService,
    IWritingEventBus events,
    TimeProvider clock,
    IRuntimeSettingsProvider settingsProvider,
    ILogger<WritingSubmissionEvaluationPipeline> logger) : IWritingSubmissionEvaluationPipeline
{
    public async Task<Guid> CreateSubmissionAsync(WritingSubmissionGradeContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var letter = context.LetterContent ?? string.Empty;
        if (letter.Trim().Length == 0)
        {
            throw ApiException.Validation("writing_submission_empty", "Letter content is required.");
        }

        var now = clock.GetUtcNow();
        var hash = ComputeHash(letter);
        var wordCount = CountWords(letter);
        var submission = new WritingSubmission
        {
            Id = Guid.NewGuid(),
            UserId = context.UserId,
            ScenarioId = context.ScenarioId,
            Mode = context.Mode ?? "practice",
            LetterContent = letter,
            LetterContentHash = hash,
            WordCount = wordCount,
            TimeSpentSeconds = context.TimeSpentSeconds,
            StartedAt = context.StartedAt,
            SubmittedAt = now,
            IsRevision = context.IsRevision,
            OriginalSubmissionId = context.OriginalSubmissionId,
            Status = "queued",
            GradingTier = string.IsNullOrWhiteSpace(context.GradingTier) ? "express" : context.GradingTier,
            InputSource = string.IsNullOrWhiteSpace(context.InputSource) ? "typed" : context.InputSource,
            CreatedAt = now,
        };
        db.WritingSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);

        await events.PublishAsync(new WritingSubmissionCreated(
            context.UserId,
            submission.Id,
            context.ScenarioId,
            submission.Mode,
            submission.GradingTier,
            submission.InputSource,
            now), ct);
        return submission.Id;
    }

    public async Task<WritingSubmissionGradeOutcome> EvaluateAsync(Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Submission was not found.");

        // ── No AI for mock Writing (defense-in-depth) ─────────────────────────
        // Mock Writing is graded by a human examiner — never by AI. The dedicated
        // mock submit path already parks the submission as "awaiting_review" and
        // routes it to a tutor; this guard ensures that even if some other caller
        // invokes the pipeline on a mock submission, it can never reach the AI
        // rubric (CallRubricAsync) NOR reuse a prior practice grade via the
        // idempotency cache below.
        if (submission.Mode == "mock")
        {
            if (submission.Status != WritingSubmissionStatuses.AwaitingReview)
            {
                submission.Status = WritingSubmissionStatuses.AwaitingReview;
                await db.SaveChangesAsync(ct);
            }
            return new WritingSubmissionGradeOutcome(submission.Id, Guid.Empty, 0, "pending", false);
        }

        var idempotency = await TryReuseExistingGradeAsync(submission, ct);
        if (idempotency is not null) return idempotency;

        submission.Status = "preflight";
        await db.SaveChangesAsync(ct);
        var preflight = PreflightChecks(submission);
        if (!preflight.Passed)
        {
            submission.Status = "failed";
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation(preflight.Reason!, preflight.Message!);
        }

        submission.Status = "grading";
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct);
        var rubric = await CallRubricAsync(submission, scenario, ct);

        var canon = await canonEngine.DetectViolationsAsync(
            new WritingCanonDetectionRequest(submission.UserId, submission.Id, submission.LetterContent,
                scenario?.LetterType ?? "routine_referral",
                scenario?.Profession ?? "medicine"), ct);

        var bandLabel = OetBandLabel(rubric.EstimatedBand);
        var grade = new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            C1Purpose = (short)rubric.C1,
            C2Content = (short)rubric.C2,
            C3Conciseness = (short)rubric.C3,
            C4Genre = (short)rubric.C4,
            C5Organisation = (short)rubric.C5,
            C6Language = (short)rubric.C6,
            RawTotal = (short)(rubric.C1 + rubric.C2 + rubric.C3 + rubric.C4 + rubric.C5 + rubric.C6),
            EstimatedBand = rubric.EstimatedBand,
            BandLabel = bandLabel,
            PerCriterionFeedbackJson = rubric.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = rubric.TopThreePrioritiesJson,
            ConfidenceFlag = rubric.ConfidenceFlag,
            ModelUsed = rubric.ModelUsed,
            CanonVersion = await ResolveCanonVersionAsync(ct),
            GradedAt = clock.GetUtcNow(),
            CreatedAt = clock.GetUtcNow(),
        };
        db.WritingGrades.Add(grade);
        submission.Status = "graded";
        await db.SaveChangesAsync(ct);

        try
        {
            await mistakeService.IncrementForCanonViolationsAsync(submission.UserId, canon.Violations, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mistake stat update failed for submission {SubmissionId}", submission.Id);
        }

        await events.PublishAsync(new WritingGradeReady(
            submission.UserId, submission.Id, grade.Id, grade.RawTotal, grade.EstimatedBand, grade.BandLabel, clock.GetUtcNow()), ct);

        foreach (var v in canon.Violations)
        {
            await events.PublishAsync(new WritingCanonViolationDetected(
                submission.UserId, submission.Id, v.Id, v.RuleId, v.Severity, v.DetectedAt), ct);
        }

        return new WritingSubmissionGradeOutcome(submission.Id, grade.Id, grade.RawTotal, grade.BandLabel, false);
    }

    private async Task<WritingSubmissionGradeOutcome?> TryReuseExistingGradeAsync(WritingSubmission submission, CancellationToken ct)
    {
        var ttl = TimeSpan.FromHours((await settingsProvider.GetAsync(ct)).Writing.GradeIdempotencyTtlHours);
        var cutoff = clock.GetUtcNow() - ttl;
        var existing = await db.WritingGrades.AsNoTracking()
            .Join(db.WritingSubmissions.AsNoTracking(), g => g.SubmissionId, s => s.Id, (g, s) => new { g, s })
            .Where(x => x.s.UserId == submission.UserId
                        && x.s.LetterContentHash == submission.LetterContentHash
                        && x.g.GradedAt >= cutoff
                        && x.s.Id != submission.Id)
            .OrderByDescending(x => x.g.GradedAt)
            .Select(x => x.g)
            .FirstOrDefaultAsync(ct);
        if (existing is null) return null;
        var materializedGrade = new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            C1Purpose = existing.C1Purpose,
            C2Content = existing.C2Content,
            C3Conciseness = existing.C3Conciseness,
            C4Genre = existing.C4Genre,
            C5Organisation = existing.C5Organisation,
            C6Language = existing.C6Language,
            RawTotal = existing.RawTotal,
            EstimatedBand = existing.EstimatedBand,
            BandLabel = existing.BandLabel,
            PerCriterionFeedbackJson = existing.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = existing.TopThreePrioritiesJson,
            ConfidenceFlag = existing.ConfidenceFlag,
            ModelUsed = existing.ModelUsed,
            CanonVersion = existing.CanonVersion,
            GradedAt = existing.GradedAt,
            CreatedAt = clock.GetUtcNow(),
        };
        db.WritingGrades.Add(materializedGrade);
        var reusedViolations = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == existing.SubmissionId)
            .ToListAsync(ct);
        foreach (var violation in reusedViolations)
        {
            db.WritingCanonViolations.Add(new WritingCanonViolation
            {
                Id = Guid.NewGuid(),
                SubmissionId = submission.Id,
                RuleId = violation.RuleId,
                Severity = violation.Severity,
                Snippet = violation.Snippet,
                LineNumber = violation.LineNumber,
                CharStart = violation.CharStart,
                CharEnd = violation.CharEnd,
                SuggestedFix = violation.SuggestedFix,
                Disputed = violation.Disputed,
                DisputeResolution = violation.DisputeResolution,
                DetectedAt = violation.DetectedAt,
            });
        }
        submission.Status = "graded";
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new WritingGradeReady(
            submission.UserId,
            submission.Id,
            materializedGrade.Id,
            materializedGrade.RawTotal,
            materializedGrade.EstimatedBand,
            materializedGrade.BandLabel,
            clock.GetUtcNow()), ct);
        return new WritingSubmissionGradeOutcome(submission.Id, materializedGrade.Id, materializedGrade.RawTotal, materializedGrade.BandLabel, true);
    }

    private static (bool Passed, string? Reason, string? Message) PreflightChecks(WritingSubmission submission)
    {
        if (submission.WordCount > 400) return (false, "writing_submission_too_long", "Letter exceeds the 400-word ceiling for OET writing.");
        if (string.IsNullOrWhiteSpace(submission.LetterContent)) return (false, "writing_submission_empty", "Letter content is required.");
        return (true, null, null);
    }

    private async Task<RubricResult> CallRubricAsync(WritingSubmission submission, WritingScenario? scenario, CancellationToken ct)
    {
        var letterType = scenario?.LetterType ?? "routine_referral";
        var profession = scenario?.Profession ?? "medicine";
        AiGroundedPrompt prompt;
        try
        {
            prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = NormaliseLetterTypeForRulebook(letterType),
                Profession = ParseProfession(profession),
                Task = AiTaskMode.Score,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Writing rubric grounded-prompt build failed for submission {SubmissionId}", submission.Id);
            throw ApiException.ServiceUnavailable("writing_rubric_unavailable", "Writing grading prompt is misconfigured.", retryable: true);
        }

        AiGatewayResult result;
        try
        {
            result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = BuildRubricInput(submission, scenario),
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingGrade,
                PromptTemplateId = "writing.score.v1",
                UserId = submission.UserId,
                // Arms the gateway backstop: a mock submission must never reach
                // the AI rubric. The mock paths branch away before here; if one
                // ever doesn't, the gateway hard-refuses (MockAssessmentForbidden).
                AssessmentContext = submission.Mode == "mock"
                    ? AiAssessmentContext.Mock
                    : AiAssessmentContext.Practice,
            }, ct);
        }
        catch (OetWithDrHesham.Api.Services.AiManagement.AiQuotaDeniedException quotaEx)
        {
            // No-charge-on-failure: the gateway throws before debiting, so no
            // credit was consumed. Surface a clean, modal-ready signal instead
            // of masking it as a generic service error (spec §9 — balance = 0).
            logger.LogInformation(
                "Writing grading blocked — AI grading credits exhausted for submission {SubmissionId} ({Code}).",
                submission.Id, quotaEx.ErrorCode);
            submission.Status = "failed";
            await db.SaveChangesAsync(ct);
            throw ApiException.PaymentRequired(
                "ai_credits_insufficient",
                "You have no AI grading credits remaining. Purchase an AI Credits package to continue.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing rubric AI call failed for submission {SubmissionId}", submission.Id);
            throw ApiException.ServiceUnavailable("writing_rubric_failed", "Writing grading service is temporarily unavailable. Please retry.", retryable: true);
        }

        var rubric = ParseRubric(result);
        if (rubric is null)
        {
            // The AI returned text we could not parse into a complete six-
            // criterion scoring contract. Never fabricate a grade — fail loud
            // and retryable so the learner can re-run rather than receive a
            // fake "all 3s" score.
            logger.LogWarning(
                "Writing rubric AI returned an incomplete or unreadable scoring contract for submission {SubmissionId}; refusing to fabricate a grade.",
                submission.Id);
            submission.Status = "failed";
            await db.SaveChangesAsync(ct);
            throw ApiException.ServiceUnavailable(
                "writing_rubric_failed",
                "Writing grading returned an unreadable response. Please retry.",
                retryable: true);
        }

        return rubric;
    }

    private static string BuildRubricInput(WritingSubmission submission, WritingScenario? scenario)
    {
        var sb = new StringBuilder();
        if (scenario is not null)
        {
            sb.AppendLine($"Scenario: {scenario.Title}");
            sb.AppendLine($"Profession: {scenario.Profession}");
            sb.AppendLine($"Letter type: {scenario.LetterType}");
            if (!string.IsNullOrWhiteSpace(scenario.TaskPromptMarkdown))
            {
                sb.AppendLine();
                sb.AppendLine("Task prompt:");
                sb.AppendLine("---");
                sb.AppendLine(scenario.TaskPromptMarkdown);
                sb.AppendLine("---");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"Word count: {submission.WordCount}");
        sb.AppendLine("Candidate letter:");
        sb.AppendLine("---");
        sb.AppendLine(submission.LetterContent);
        sb.AppendLine("---");
        sb.AppendLine();
        // Defer entirely to the grounded reply format in the system prompt.
        // Emitting a second, conflicting JSON shape here is what previously
        // desynced the model output from the parser and produced fabricated
        // fallback grades. The canonical contract is criteriaScores-based.
        sb.AppendLine(
            "Score this letter on the six OET Writing criteria and reply with the SINGLE JSON object "
            + "defined in the grounded reply format above (findings, criteriaScores, estimatedScaledScore, "
            + "estimatedGrade, passed, passRequires, advisory). Cite only rule IDs that appear in the grounded prompt.");
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions RubricParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Parse the canonical grounded scoring contract (criteriaScores-based, the
    /// single source of truth built by <see cref="RulebookPromptBuilder"/> /
    /// <c>lib/rulebook/ai-prompt.ts</c>) into a <see cref="RubricResult"/>.
    /// Returns <c>null</c> when the AI did not return a complete, in-range
    /// six-criterion contract — the caller then fails loud rather than
    /// fabricating a grade. Mirrors <see cref="WritingEvaluationPipeline"/>.
    /// </summary>
    private static RubricResult? ParseRubric(AiGatewayResult result)
    {
        if (!TryParseRubric(result.Completion, result.AppliedRuleIds, out var ai))
        {
            return null;
        }

        var scores = ai.CriteriaScores!; // non-null & complete per HasCompleteScoringContract
        var c1 = Math.Clamp(scores.Purpose ?? 0, 0, 3);
        var c2 = Math.Clamp(scores.Content ?? 0, 0, 7);
        var c3 = Math.Clamp(scores.ConcisenessClarity ?? 0, 0, 7);
        var c4 = Math.Clamp(scores.GenreStyle ?? 0, 0, 7);
        var c5 = Math.Clamp(scores.OrganisationLayout ?? 0, 0, 7);
        var c6 = Math.Clamp(scores.Language ?? 0, 0, 7);
        var rawTotal = c1 + c2 + c3 + c4 + c5 + c6;

        var findings = ai.Findings ?? new List<RubricAiFinding>();
        var perCriterion = BuildPerCriterionFeedbackJson(c1, c2, c3, c4, c5, c6, findings);
        var topThree = BuildTopThreePrioritiesJson(findings);
        var model = string.IsNullOrWhiteSpace(result.ResolvedModel) ? "claude-sonnet-5" : result.ResolvedModel;

        // EstimatedBand is stored in raw-total units (0–38) to match OetBandLabel
        // and the seed data; a complete contract is high confidence.
        return new RubricResult(c1, c2, c3, c4, c5, c6, rawTotal, perCriterion, topThree, "high", model);
    }

    private static bool TryParseRubric(string? completion, IReadOnlyList<string> allowedRuleIds, out RubricAiResponse response)
    {
        response = new RubricAiResponse();
        if (string.IsNullOrWhiteSpace(completion)) return false;

        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<RubricAiResponse>(completion[start..(end + 1)], RubricParseOptions);
            if (parsed is null || !HasCompleteScoringContract(parsed)) return false;

            // Grounding invariant: the AI must not cite rule IDs that are not in
            // the active rulebook. Drop any that are not in the applied set.
            if (parsed.Findings is { Count: > 0 } && allowedRuleIds.Count > 0)
            {
                parsed.Findings = parsed.Findings
                    .Where(f => !string.IsNullOrWhiteSpace(f.RuleId)
                                && allowedRuleIds.Contains(f.RuleId!, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                parsed.Findings ??= new List<RubricAiFinding>();
            }

            response = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasCompleteScoringContract(RubricAiResponse parsed)
    {
        if (parsed.EstimatedScaledScore is not { } scaled
            || scaled < OetScoring.ScaledMin
            || scaled > OetScoring.ScaledMax)
        {
            return false;
        }

        var s = parsed.CriteriaScores;
        if (s is null || s.ScoredCount != 6) return false;

        return InRange(s.Purpose, 0, 3)
            && InRange(s.Content, 0, 7)
            && InRange(s.ConcisenessClarity, 0, 7)
            && InRange(s.GenreStyle, 0, 7)
            && InRange(s.OrganisationLayout, 0, 7)
            && InRange(s.Language, 0, 7);
    }

    private static bool InRange(int? value, int min, int max)
        => value is { } v && v >= min && v <= max;

    private static string BuildPerCriterionFeedbackJson(
        int c1, int c2, int c3, int c4, int c5, int c6,
        IReadOnlyList<RubricAiFinding> findings)
    {
        (string Key, string Criterion, int Score)[] map =
        {
            ("c1", "purpose", c1),
            ("c2", "content", c2),
            ("c3", "conciseness_clarity", c3),
            ("c4", "genre_style", c4),
            ("c5", "organisation_layout", c5),
            ("c6", "language", c6),
        };

        var dict = new Dictionary<string, object>();
        foreach (var (key, criterion, score) in map)
        {
            var linked = findings.Where(f => CriterionForFinding(f) == criterion).ToList();
            var cited = linked
                .Select(f => f.RuleId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var feedback = string.Join(" ", linked
                .Select(f => f.Message)
                .Where(m => !string.IsNullOrWhiteSpace(m)));
            var exemplar = linked
                .Select(f => f.FixSuggestion)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            // Shape consumed by WritingV2ResponseMapper.ToGradeResponse —
            // keys c1..c6, each { score, feedback, exemplarFix, citedRuleIds }.
            dict[key] = new
            {
                score,
                feedback,
                exemplarFix = exemplar,
                citedRuleIds = cited,
            };
        }

        return JsonSerializer.Serialize(dict);
    }

    private static string BuildTopThreePrioritiesJson(IReadOnlyList<RubricAiFinding> findings)
    {
        var top = findings
            .OrderBy(f => SeverityRank(f.Severity))
            .Select(f => string.IsNullOrWhiteSpace(f.RuleId)
                ? (f.Message ?? string.Empty)
                : $"{f.RuleId}: {f.Message}")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(3)
            .ToList();
        return JsonSerializer.Serialize(top);
    }

    private static int SeverityRank(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "minor" => 2,
        _ => 3,
    };

    private static string CriterionForFinding(RubricAiFinding f)
        => !string.IsNullOrWhiteSpace(f.CriterionCode) ? f.CriterionCode! : CriterionFor(f.RuleId, f.Message);

    // Heuristic mapping from rule id / message to one of the six OET Writing
    // criteria; used only when the AI did not stamp criterionCode itself.
    private static string CriterionFor(string? ruleId, string? message)
    {
        var text = $"{ruleId} {message}".ToLowerInvariant();
        if (text.Contains("purpose") || text.Contains("intro_contains_purpose")) return "purpose";
        if (text.Contains("salutation") || text.Contains("yours") || text.Contains("address")
            || text.Contains("layout") || text.Contains("blank_line") || text.Contains("structure")
            || text.Contains("paragraph"))
            return "organisation_layout";
        if (text.Contains("conciseness") || text.Contains("concise") || text.Contains("length")
            || text.Contains("clarity") || text.Contains("priorit"))
            return "conciseness_clarity";
        if (text.Contains("genre") || text.Contains("register") || text.Contains("tone")
            || text.Contains("non_medical") || text.Contains("jargon") || text.Contains("style"))
            return "genre_style";
        if (text.Contains("grammar") || text.Contains("contraction") || text.Contains("present_perfect")
            || text.Contains("past_simple") || text.Contains("punctuation") || text.Contains("language")
            || text.Contains("date_format") || text.Contains("year") || text.Contains("abbrev"))
            return "language";
        return "content";
    }

    // -----------------------------------------------------------------
    // Tolerant DTOs for the canonical grounded scoring contract. Only the
    // fields this pipeline consumes are mapped; names match case-insensitively.
    // -----------------------------------------------------------------

    private sealed record RubricAiResponse
    {
        [JsonPropertyName("findings")]
        public List<RubricAiFinding>? Findings { get; set; }

        [JsonPropertyName("criteriaScores")]
        public RubricAiCriteriaScores? CriteriaScores { get; set; }

        [JsonPropertyName("estimatedScaledScore")]
        public int? EstimatedScaledScore { get; set; }

        [JsonPropertyName("estimatedGrade")]
        public string? EstimatedGrade { get; set; }
    }

    private sealed record RubricAiFinding
    {
        [JsonPropertyName("ruleId")]
        public string? RuleId { get; set; }

        [JsonPropertyName("severity")]
        public string? Severity { get; set; }

        [JsonPropertyName("quote")]
        public string? Quote { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("fixSuggestion")]
        public string? FixSuggestion { get; set; }

        [JsonPropertyName("criterionCode")]
        public string? CriterionCode { get; set; }
    }

    private sealed record RubricAiCriteriaScores
    {
        [JsonPropertyName("purpose")]
        public int? Purpose { get; set; }

        [JsonPropertyName("content")]
        public int? Content { get; set; }

        [JsonPropertyName("conciseness_clarity")]
        public int? ConcisenessClarity { get; set; }

        [JsonPropertyName("genre_style")]
        public int? GenreStyle { get; set; }

        [JsonPropertyName("organisation_layout")]
        public int? OrganisationLayout { get; set; }

        [JsonPropertyName("language")]
        public int? Language { get; set; }

        [JsonIgnore]
        public int ScoredCount =>
            (Purpose.HasValue ? 1 : 0)
            + (Content.HasValue ? 1 : 0)
            + (ConcisenessClarity.HasValue ? 1 : 0)
            + (GenreStyle.HasValue ? 1 : 0)
            + (OrganisationLayout.HasValue ? 1 : 0)
            + (Language.HasValue ? 1 : 0);
    }

    private async Task<string> ResolveCanonVersionAsync(CancellationToken ct)
    {
        var max = await db.WritingCanonRules.AsNoTracking().MaxAsync(r => (int?)r.Version, ct);
        return $"v{max ?? 1}";
    }

    private static string OetBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..40];
    }

    private static int CountWords(string content)
        => string.IsNullOrWhiteSpace(content) ? 0 : content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static ExamProfession ParseProfession(string raw)
        => RulebookProfessionParser.TryParse(raw, out var p) ? p : ExamProfession.Medicine;

    private static string NormaliseLetterTypeForRulebook(string v)
        => v.ToUpperInvariant() switch
        {
            "LT-RR" => "routine_referral",
            "LT-UR" => "urgent_referral",
            "LT-DG" => "discharge",
            "LT-TR" => "transfer",
            "LT-RP" => "advice_to_patient",
            "LT-NM" => "non_medical",
            _ => v.ToLowerInvariant(),
        };

    private sealed record RubricResult(int C1, int C2, int C3, int C4, int C5, int C6, int EstimatedBand,
        string PerCriterionFeedbackJson, string TopThreePrioritiesJson, string ConfidenceFlag, string ModelUsed);
}
