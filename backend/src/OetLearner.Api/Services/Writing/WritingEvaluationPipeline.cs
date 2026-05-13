using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Mission-critical (per AGENTS.md): every Writing AI call MUST go through
/// <see cref="IAiGatewayService"/> with a grounded prompt built via
/// <see cref="IAiGatewayService.BuildGroundedPrompt"/>. The gateway physically
/// refuses ungrounded prompts. This pipeline is the single owner of the
/// Writing grading flow — invoked by <c>BackgroundJobProcessor</c> when a
/// <see cref="JobType.WritingEvaluation"/> job runs.
///
/// Architecture mirrors <c>SpeakingEvaluationPipeline</c>:
/// <list type="number">
///   <item>Pre-run <see cref="WritingRuleEngine.Lint"/> for deterministic findings.</item>
///   <item>Build grounded prompt and call gateway with <c>FeatureCode = AiFeatureCodes.WritingGrade</c>.</item>
///   <item>Parse JSON contract; merge AI findings with rule-engine findings (rule-engine wins).</item>
///   <item>Persist into existing Evaluation columns (ScoreRange, GradeRange, CriterionScoresJson, ...).</item>
///   <item>Mark Attempt Completed on success; on failure, keep rule-engine findings and mark Evaluation Failed.</item>
/// </list>
/// Side effects (analytics events, readiness refresh, study plan regen,
/// learner notifications) are emitted by
/// <c>BackgroundJobProcessor.CompleteWritingEvaluationSideEffectsAsync</c>
/// after this pipeline returns successfully — same pattern as Speaking.
/// </summary>
public interface IWritingEvaluationPipeline
{
    Task CompleteEvaluationAsync(BackgroundJobItem job, CancellationToken cancellationToken);
}

public sealed class WritingEvaluationPipeline(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    WritingRuleEngine ruleEngine,
    ILogger<WritingEvaluationPipeline> logger,
    IWritingOptionsProvider? optionsProvider = null) : IWritingEvaluationPipeline
{
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task CompleteEvaluationAsync(BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == attempt.UserId, cancellationToken);

        // Admin kill-switch: if AI grading is disabled, mark Failed with a
        // dedicated reason code and short-circuit before any prompt build
        // / gateway call. Deterministic rule-engine findings are still
        // attached so the learner sees something useful.
        if (optionsProvider is not null)
        {
            var opts = await optionsProvider.GetAsync(cancellationToken);
            if (!opts.AiGradingEnabled)
            {
                IReadOnlyList<LintFinding> killSwitchFindings;
                try
                {
                    killSwitchFindings = ruleEngine.Lint(new WritingLintInput(
                        LetterText: attempt.DraftContent ?? string.Empty,
                        LetterType: ResolveLetterType(attempt, content),
                        CaseNotesMarkers: WritingCaseNotesMarkerExtractor.Derive(content?.CaseNotes),
                        Profession: ParseProfession(content?.ProfessionId ?? user?.ActiveProfessionId)));
                }
                catch
                {
                    killSwitchFindings = Array.Empty<LintFinding>();
                }
                MarkFailed(evaluation, killSwitchFindings, "kill_switch",
                    opts.KillSwitchReason ?? "AI Writing grading is temporarily disabled by an administrator.",
                    retryable: false, retryAfterMs: null);
                attempt.State = AttemptState.Submitted;
                logger.LogWarning("Writing AI grading kill-switch active; attempt {AttemptId} marked Failed.", attempt.Id);
                return;
            }
        }

        // Profession: prefer content authoring metadata, then learner active
        // profession. Falls back to medicine for backward compatibility with
        // legacy seeded attempts that have no profession id.
        var professionRaw = content?.ProfessionId ?? user?.ActiveProfessionId;
        var profession = ParseProfession(professionRaw);

        // Candidate target country for the country-aware Writing pass mark
        // (UK/IE/AU/NZ/CA → 350, US/QA → 300). Read the latest LearnerGoal
        // submission for this user. SQLite-friendly: simple Where + ToList.
        var goalCountry = string.IsNullOrWhiteSpace(attempt.UserId)
            ? null
            : (await db.Set<LearnerGoal>()
                .Where(g => g.UserId == attempt.UserId)
                .ToListAsync(cancellationToken))
                .OrderByDescending(g => g.UpdatedAt)
                .Select(g => g.TargetCountry)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        var candidateCountry = goalCountry;

        var letterType = ResolveLetterType(attempt, content);

        // 1. Deterministic rule-engine pre-run.
        IReadOnlyList<LintFinding> ruleFindings;
        try
        {
            ruleFindings = ruleEngine.Lint(new WritingLintInput(
                LetterText: attempt.DraftContent ?? string.Empty,
                LetterType: letterType,
                CaseNotesMarkers: WritingCaseNotesMarkerExtractor.Derive(content?.CaseNotes),
                Profession: profession));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing rule engine failed for attempt {AttemptId}; continuing with empty findings.", attempt.Id);
            ruleFindings = Array.Empty<LintFinding>();
        }

        // 2. Build grounded prompt.
        AiGroundedPrompt prompt;
        try
        {
            prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = letterType,
                CandidateCountry = candidateCountry,
                Task = AiTaskMode.Score,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Writing grounded prompt build failed for attempt {AttemptId}.", attempt.Id);
            MarkFailed(evaluation, ruleFindings, "prompt_build_error",
                "Could not assemble the grounded Writing prompt. Please retry.",
                retryable: true, retryAfterMs: 60_000);
            attempt.State = AttemptState.Submitted;
            return;
        }

        // 3. Call the AI gateway.
        AiGatewayResult aiResult;
        try
        {
            aiResult = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = BuildUserInput(attempt, content, letterType, candidateCountry, ruleFindings),
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingGrade,
                UserId = attempt.UserId,
                PromptTemplateId = "writing.score.v1",
            }, cancellationToken);
        }
        catch (PromptNotGroundedException ex)
        {
            logger.LogError(ex, "Writing AI call refused (ungrounded) for attempt {AttemptId}.", attempt.Id);
            MarkFailed(evaluation, ruleFindings, "ai_ungrounded",
                "We couldn't grade this attempt because the AI grading prompt was not properly grounded. Our team has been notified.",
                retryable: false, retryAfterMs: null);
            attempt.State = AttemptState.Submitted;
            return;
        }
        catch (AiQuotaDeniedException ex)
        {
            logger.LogWarning(ex, "Writing AI call denied by quota for attempt {AttemptId} ({Code}).", attempt.Id, ex.Message);
            MarkFailed(evaluation, ruleFindings, "ai_quota_denied",
                "AI grading is unavailable for this attempt due to quota limits. Please try again later or upgrade your plan.",
                retryable: false, retryAfterMs: null);
            attempt.State = AttemptState.Submitted;
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing AI provider error for attempt {AttemptId}; marking Failed (Retryable=true).", attempt.Id);
            MarkFailed(evaluation, ruleFindings, "ai_provider_error",
                "We couldn't reach the AI grading service. Please retry — your free-tier counter has not been consumed.",
                retryable: true, retryAfterMs: 60_000);
            attempt.State = AttemptState.Submitted;
            return;
        }

        // 4. Parse JSON reply (tolerant). On malformed JSON, fall back to
        //    rule-engine-only findings.
        var allowedAiRuleIds = aiResult.AppliedRuleIds.Count > 0
            ? aiResult.AppliedRuleIds
            : prompt.Metadata.AppliedRuleIds;
        if (!TryParse(aiResult.Completion, allowedAiRuleIds, out var aiResponse))
        {
            logger.LogWarning("Writing AI returned malformed JSON for attempt {AttemptId}; falling back to rule-engine-only.", attempt.Id);
            MarkFailed(evaluation, ruleFindings, "ai_malformed_response",
                "AI grading returned an unreadable response. Please retry.",
                retryable: true, retryAfterMs: 60_000);
            attempt.State = AttemptState.Submitted;
            return;
        }

        // 5. Merge findings; rule-engine wins on duplicate ruleId.
        var mergedFindings = MergeFindings(ruleFindings, aiResponse.Findings);

        // 6. Build score, grade, and per-criterion contract.
        var scaled = ClampScaled(aiResponse.EstimatedScaledScore ?? OetScoring.ScaledPassGradeB);
        var scoreRange = $"{ClampScaled(scaled - 10)}-{ClampScaled(scaled + 10)}";
        var grade = !string.IsNullOrWhiteSpace(aiResponse.EstimatedGrade)
            ? aiResponse.EstimatedGrade!
            : OetScoring.OetGradeLetterFromScaled(scaled);

        var scoredCriteriaCount = aiResponse.CriteriaScores?.ScoredCount ?? 0;
        var confidence = scoredCriteriaCount switch
        {
            6 => ConfidenceBand.High,
            >= 4 => ConfidenceBand.Medium,
            _ => ConfidenceBand.Low,
        };

        var criterionScores = BuildCriterionScores(aiResponse.CriteriaScores, mergedFindings);
        var feedbackItems = BuildFeedbackItems(evaluation.Id, mergedFindings);
        var strengths = BuildStrengths(aiResponse, criterionScores);
        var issues = BuildIssues(mergedFindings);
        var passInfo = OetScoring.GradeWriting(scaled, candidateCountry);

        // 7. Persist.
        var now = DateTimeOffset.UtcNow;
        evaluation.State = AsyncState.Completed;
        evaluation.ScoreRange = scoreRange;
        evaluation.GradeRange = grade;
        evaluation.ConfidenceBand = confidence;
        evaluation.StrengthsJson = JsonSupport.Serialize(strengths);
        evaluation.IssuesJson = JsonSupport.Serialize(issues);
        evaluation.CriterionScoresJson = JsonSupport.Serialize(criterionScores);
        evaluation.FeedbackItemsJson = JsonSupport.Serialize(feedbackItems);
        evaluation.GeneratedAt = now;
        evaluation.ModelExplanationSafe = SanitiseExplanation(aiResult.Completion, aiResponse);
        evaluation.LearnerDisclaimer = "AI-generated estimate. Pending optional tutor review.";
        evaluation.StatusReasonCode = "completed";
        evaluation.StatusMessage = "Writing evaluation completed.";
        evaluation.Retryable = false;
        evaluation.RetryAfterMs = null;
        evaluation.LastTransitionAt = now;

        // Stamp AI provenance + pass info into the attempt analysis JSON so
        // downstream UIs (and audit) can reconstruct how the score was made.
        attempt.AnalysisJson = JsonSupport.Serialize(MergeAnalysis(attempt.AnalysisJson, new Dictionary<string, object?>
        {
            ["aiProvenance"] = new
            {
                featureCode = AiFeatureCodes.WritingGrade,
                promptTemplateId = "writing.score.v1",
                rulebookVersion = aiResult.RulebookVersion ?? prompt.Metadata.RulebookVersion,
                appliedRuleIds = aiResult.AppliedRuleIds ?? prompt.Metadata.AppliedRuleIds,
                advisoryOnly = true,
            },
            ["writingBand"] = new
            {
                scaledEstimate = scaled,
                grade,
                passed = passInfo.Passed,
                requiredScaled = passInfo.RequiredScaled,
                requiredGrade = passInfo.RequiredGrade,
                country = candidateCountry,
                criteriaSource = scoredCriteriaCount > 0 ? "ai_grounded" : "rulebook_fallback",
            },
            ["rulebookFindings"] = mergedFindings.Select(f => new
            {
                ruleId = f.RuleId,
                severity = f.Severity,
                message = f.Message,
                quote = f.Quote,
                fixSuggestion = f.FixSuggestion,
                source = f.Source,
            }).ToList(),
        }));

        attempt.State = AttemptState.Completed;
        attempt.CompletedAt = now;

        // Audit P2-2 — persist one WritingRuleViolation row per merged
        // finding for the admin analytics dashboard. The attempt's
        // AnalysisJson.rulebookFindings remains the learner-facing copy;
        // this table is admin-only and indexed on (RuleId, GeneratedAt),
        // (Profession, GeneratedAt), and AttemptId for the dashboard
        // pivots. Trim long messages / quotes to the column limits to
        // avoid silent truncation surprises.
        foreach (var finding in mergedFindings)
        {
            db.Set<WritingRuleViolation>().Add(new WritingRuleViolation
            {
                Id = Guid.NewGuid().ToString("N"),
                AttemptId = attempt.Id,
                EvaluationId = evaluation.Id,
                UserId = attempt.UserId,
                Profession = profession.ToString().ToLowerInvariant(),
                LetterType = letterType,
                RuleId = TruncateAscii(finding.RuleId, 128),
                Severity = TruncateAscii(finding.Severity, 16),
                Source = TruncateAscii(finding.Source, 16),
                Message = TruncateAscii(finding.Message, 1024),
                Quote = string.IsNullOrEmpty(finding.Quote) ? null : TruncateAscii(finding.Quote!, 1024),
                GeneratedAt = now,
            });
        }
    }

    private static string TruncateAscii(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    // -----------------------------------------------------------------
    // Failure helper
    // -----------------------------------------------------------------

    private static void MarkFailed(
        Evaluation evaluation,
        IReadOnlyList<LintFinding> ruleFindings,
        string reasonCode,
        string message,
        bool retryable,
        int? retryAfterMs)
    {
        var now = DateTimeOffset.UtcNow;
        evaluation.State = AsyncState.Failed;
        evaluation.ScoreRange = "pending";
        evaluation.GradeRange = null;
        evaluation.ConfidenceBand = ConfidenceBand.Low;
        evaluation.StrengthsJson = JsonSupport.Serialize(Array.Empty<string>());
        // Even on failure, keep deterministic rulebook findings: they are
        // self-sufficient feedback that does not depend on the AI.
        var unified = ruleFindings.Select(f => new UnifiedFinding(
            f.RuleId,
            f.Severity.ToString().ToLowerInvariant(),
            f.Message,
            f.Quote,
            f.FixSuggestion,
            CriterionFor(f.RuleId, f.Message),
            "rulebook")).ToList();
        evaluation.IssuesJson = JsonSupport.Serialize(unified.Take(4).Select(u => $"{u.RuleId}: {u.Message}").ToList());
        evaluation.CriterionScoresJson = JsonSupport.Serialize(Array.Empty<object>());
        evaluation.FeedbackItemsJson = JsonSupport.Serialize(unified.Take(8).Select((u, i) => (object)new
        {
            feedbackItemId = $"{evaluation.Id}-{i + 1}",
            criterionCode = u.CriterionCode,
            type = "anchored_comment",
            ruleId = u.RuleId,
            anchor = new { snippet = u.Quote ?? string.Empty },
            message = u.Message,
            severity = u.Severity,
            suggestedFix = u.FixSuggestion,
        }).ToList());
        evaluation.GeneratedAt = now;
        evaluation.ModelExplanationSafe = "Grading was not completed. Deterministic rulebook findings are preserved.";
        evaluation.LearnerDisclaimer = message;
        evaluation.StatusReasonCode = reasonCode;
        evaluation.StatusMessage = message;
        evaluation.Retryable = retryable;
        evaluation.RetryAfterMs = retryAfterMs;
        evaluation.LastTransitionAt = now;
    }

    // -----------------------------------------------------------------
    // Letter type / profession resolution
    // -----------------------------------------------------------------

    private static string ResolveLetterType(Attempt attempt, ContentItem? content)
    {
        // 1. Attempt context column — set by the writing endpoint when a
        //    letter type was selected. Non-JSON values are tolerated.
        var ctx = attempt.Context;
        if (!string.IsNullOrWhiteSpace(ctx))
        {
            // try JSON
            try
            {
                using var doc = JsonDocument.Parse(ctx!);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("letterType", out var lt)
                    && lt.ValueKind == JsonValueKind.String)
                {
                    var v = lt.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return Normalise(v);
                }
            }
            catch
            {
                // not JSON — fall through to plain-string handling
            }

            // plain-string context like "discharge" or "routine_referral"
            var trimmed = ctx.Trim();
            if (LooksLikeLetterTypeToken(trimmed)) return Normalise(trimmed);
        }

        // 2. Content metadata.
        if (content is not null && !string.IsNullOrWhiteSpace(content.DetailJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(content.DetailJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("letterType", out var lt)
                    && lt.ValueKind == JsonValueKind.String)
                {
                    var v = lt.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return Normalise(v);
                }
            }
            catch
            {
                // fall through
            }
        }

        if (content is not null && !string.IsNullOrWhiteSpace(content.ScenarioType)
            && LooksLikeLetterTypeToken(content.ScenarioType!))
        {
            return Normalise(content.ScenarioType!);
        }

        return "routine_referral";
    }

    private static bool LooksLikeLetterTypeToken(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        return v is "routine_referral"
            or "urgent_referral"
            or "discharge"
            or "transfer"
            or "request_for_information"
            or "advice"
            or "advice_to_patient"
            or "non_medical"
            or "routine"
            or "urgent";
    }

    private static string Normalise(string value)
        => value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return RulebookProfessionParser.TryParse(raw, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    // -----------------------------------------------------------------
    // User input builder
    // -----------------------------------------------------------------

    private static string BuildUserInput(
        Attempt attempt,
        ContentItem? content,
        string letterType,
        string? candidateCountry,
        IReadOnlyList<LintFinding> ruleFindings)
    {
        var sb = new StringBuilder();
        if (content is not null)
        {
            sb.AppendLine($"Task title: {content.Title}");
            sb.AppendLine($"Profession: {content.ProfessionId ?? "medicine"}");
            if (!string.IsNullOrWhiteSpace(content.ScenarioType))
            {
                sb.AppendLine($"Scenario type: {content.ScenarioType}");
            }
        }
        sb.AppendLine($"Letter type: {letterType}");
        if (!string.IsNullOrWhiteSpace(candidateCountry))
        {
            sb.AppendLine($"Candidate target country: {candidateCountry}");
        }
        if (content is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Official task source and case notes:");
            sb.AppendLine("---");
            sb.AppendLine(string.IsNullOrWhiteSpace(content.CaseNotes)
                ? "No case notes were available for this content item."
                : content.CaseNotes);
            if (!string.IsNullOrWhiteSpace(content.DetailJson) && content.DetailJson != "{}")
            {
                sb.AppendLine();
                sb.AppendLine("Task metadata JSON:");
                sb.AppendLine(content.DetailJson);
            }
            sb.AppendLine("---");
        }
        sb.AppendLine();
        sb.AppendLine("Candidate letter draft:");
        sb.AppendLine("---");
        sb.AppendLine(attempt.DraftContent ?? string.Empty);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Deterministic rulebook audit findings (already detected — do not duplicate):");
        sb.AppendLine(JsonSupport.Serialize(ruleFindings.Select(f => new
        {
            ruleId = f.RuleId,
            severity = f.Severity.ToString().ToLowerInvariant(),
            message = f.Message,
            quote = f.Quote,
            fixSuggestion = f.FixSuggestion,
        })));
        sb.AppendLine();
        sb.AppendLine("Score this attempt as advisory Writing feedback only. " +
                      "Cite Writing rule IDs from the grounded prompt and preserve the country-aware pass anchor.");
        return sb.ToString();
    }

    // -----------------------------------------------------------------
    // JSON parsing
    // -----------------------------------------------------------------

    private static bool TryParse(string completion, IReadOnlyList<string> allowedRuleIds, out WritingAiResponse response)
    {
        response = new WritingAiResponse();
        if (string.IsNullOrWhiteSpace(completion)) return false;

        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        try
        {
            var json = completion[start..(end + 1)];
            var parsed = JsonSerializer.Deserialize<WritingAiResponse>(json, ParseOptions);
            if (parsed is null) return false;
            if (!HasCompleteScoringContract(parsed)) return false;

            // Filter findings to allowed rule IDs (grounding invariant — the
            // AI must not invent rule IDs that are not in the rulebook).
            if (parsed.Findings is { Count: > 0 } && allowedRuleIds.Count > 0)
            {
                parsed.Findings = parsed.Findings
                    .Where(f => !string.IsNullOrWhiteSpace(f.RuleId)
                                && allowedRuleIds.Contains(f.RuleId, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (parsed.Findings is null)
            {
                parsed.Findings = new List<WritingAiFinding>();
            }

            response = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCompleteScoringContract(WritingAiResponse parsed)
    {
        if (parsed.EstimatedScaledScore is not { } scaled
            || scaled < OetScoring.ScaledMin
            || scaled > OetScoring.ScaledMax)
        {
            return false;
        }

        var scores = parsed.CriteriaScores;
        if (scores is null || scores.ScoredCount != 6) return false;

        return IsInRange(scores.Purpose, 0, 3)
            && IsInRange(scores.Content, 0, 7)
            && IsInRange(scores.ConcisenessClarity, 0, 7)
            && IsInRange(scores.GenreStyle, 0, 7)
            && IsInRange(scores.OrganisationLayout, 0, 7)
            && IsInRange(scores.Language, 0, 7);
    }

    private static bool IsInRange(int? value, int min, int max)
        => value is { } score && score >= min && score <= max;

    // -----------------------------------------------------------------
    // Findings merge
    // -----------------------------------------------------------------

    private static List<UnifiedFinding> MergeFindings(
        IReadOnlyList<LintFinding> ruleFindings,
        IReadOnlyList<WritingAiFinding>? aiFindings)
    {
        var merged = new List<UnifiedFinding>();
        var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Rule-engine findings are authoritative — added first and dedupe by ruleId.
        foreach (var f in ruleFindings)
        {
            if (!seenRuleIds.Add(f.RuleId)) continue;
            merged.Add(new UnifiedFinding(
                f.RuleId,
                f.Severity.ToString().ToLowerInvariant(),
                f.Message,
                f.Quote,
                f.FixSuggestion,
                CriterionFor(f.RuleId, f.Message),
                "rulebook"));
        }

        if (aiFindings is not null)
        {
            foreach (var f in aiFindings)
            {
                if (string.IsNullOrWhiteSpace(f.RuleId)) continue;
                if (!seenRuleIds.Add(f.RuleId)) continue;
                merged.Add(new UnifiedFinding(
                    f.RuleId,
                    string.IsNullOrWhiteSpace(f.Severity) ? "info" : f.Severity!.ToLowerInvariant(),
                    f.Message ?? "Rulebook finding.",
                    f.Quote,
                    f.FixSuggestion,
                    !string.IsNullOrWhiteSpace(f.CriterionCode) ? f.CriterionCode! : CriterionFor(f.RuleId, f.Message),
                    "ai"));
            }
        }

        merged.Sort((a, b) =>
        {
            var s = SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity));
            if (s != 0) return s;
            return string.Compare(a.RuleId, b.RuleId, StringComparison.OrdinalIgnoreCase);
        });
        return merged;
    }

    private static int SeverityRank(string severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "minor" => 2,
        _ => 3,
    };

    // Heuristic mapping from rule id / message to one of the 6 OET Writing
    // criteria. Used only when the AI did not stamp criterionCode itself.
    private static string CriterionFor(string ruleId, string? message)
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
    // Per-criterion scores (6 OET Writing criteria)
    // -----------------------------------------------------------------

    private static List<object> BuildCriterionScores(
        WritingAiCriteriaScores? ai,
        IReadOnlyList<UnifiedFinding> findings)
    {
        var (purposeMax, contentMax) = (3, 7);
        var sevenMax = 7;

        object Build(string code, int? aiScore, int max)
        {
            var clamped = aiScore.HasValue ? Math.Clamp(aiScore.Value, 0, max) : (int?)null;
            var linked = findings.Where(f => f.CriterionCode == code).Select(f => f.RuleId).Distinct().ToArray();
            return new
            {
                criterionCode = code,
                scoreRange = clamped.HasValue ? $"{clamped}/{max}" : $"-/{max}",
                score = clamped,
                max,
                confidenceBand = clamped.HasValue ? "high" : (linked.Length > 0 ? "medium" : "low"),
                source = clamped.HasValue ? "ai_grounded" : "rulebook_fallback",
                linkedRuleIds = linked,
                explanation = clamped.HasValue
                    ? "Score derived from the grounded AI evaluation."
                    : (linked.Length > 0
                        ? "Rulebook findings are linked to this criterion; no AI score was provided."
                        : "No direct evidence was linked to this criterion in the current draft."),
            };
        }

        return new List<object>
        {
            Build("purpose",              ai?.Purpose,              purposeMax),
            Build("content",              ai?.Content,              contentMax),
            Build("conciseness_clarity",  ai?.ConcisenessClarity,   sevenMax),
            Build("genre_style",          ai?.GenreStyle,           sevenMax),
            Build("organisation_layout",  ai?.OrganisationLayout,   sevenMax),
            Build("language",             ai?.Language,             sevenMax),
        };
    }

    private static List<object> BuildFeedbackItems(string evaluationId, IReadOnlyList<UnifiedFinding> findings)
    {
        return findings.Take(8).Select((f, i) => (object)new
        {
            feedbackItemId = $"{evaluationId}-{i + 1}",
            criterionCode = f.CriterionCode,
            type = "anchored_comment",
            ruleId = f.RuleId,
            severity = f.Severity,
            anchor = new { snippet = f.Quote ?? string.Empty },
            message = f.Message,
            suggestedFix = f.FixSuggestion ?? "Revise this segment to address the cited rule.",
            source = f.Source,
        }).ToList();
    }

    private static List<string> BuildStrengths(WritingAiResponse ai, IReadOnlyList<object> criterionScores)
    {
        if (ai.Strengths is { Count: > 0 })
        {
            return ai.Strengths.Where(s => !string.IsNullOrWhiteSpace(s)).Take(3).ToList()!;
        }

        return new List<string>
        {
            "Letter structure is grounded in deterministic Writing rulebook checks.",
            "Feedback is organised around the six OET Writing criteria for focused practice.",
        };
    }

    private static List<string> BuildIssues(IReadOnlyList<UnifiedFinding> findings)
    {
        if (findings.Count == 0)
        {
            return new List<string>
            {
                "No rulebook violations detected; tutor review can confirm finer Writing nuance.",
            };
        }

        return findings.Take(4).Select(f => $"{f.RuleId}: {f.Message}").ToList();
    }

    // -----------------------------------------------------------------
    // Misc helpers
    // -----------------------------------------------------------------

    private static int ClampScaled(int score) => Math.Clamp(score, OetScoring.ScaledMin, OetScoring.ScaledMax);

    private static string SanitiseExplanation(string completion, WritingAiResponse parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.Advisory)) return parsed.Advisory!;
        // Keep at most 600 chars of the raw completion, trimmed of obvious
        // JSON braces — purely a safety net so the column is never huge.
        var trimmed = (completion ?? string.Empty).Trim();
        if (trimmed.Length > 600) trimmed = trimmed[..600] + "…";
        return string.IsNullOrWhiteSpace(trimmed)
            ? "AI-generated advisory estimate based on the OET Writing rulebook."
            : trimmed;
    }

    private static Dictionary<string, object?> MergeAnalysis(string? currentJson, Dictionary<string, object?> updates)
    {
        var current = JsonSupport.Deserialize<Dictionary<string, object?>>(currentJson, new Dictionary<string, object?>());
        foreach (var update in updates)
        {
            current[update.Key] = update.Value;
        }
        return current;
    }

    // -----------------------------------------------------------------
    // Internal types
    // -----------------------------------------------------------------

    private sealed record UnifiedFinding(
        string RuleId,
        string Severity,
        string Message,
        string? Quote,
        string? FixSuggestion,
        string CriterionCode,
        string Source);

    /// <summary>
    /// Tolerant DTO for the AI JSON contract (blueprint §B.4). Only the
    /// fields this pipeline actually consumes are mapped; everything else
    /// is ignored. JSON property names are matched case-insensitively.
    /// </summary>
    private sealed record WritingAiResponse
    {
        public List<WritingAiFinding>? Findings { get; set; }

        [JsonPropertyName("criteriaScores")]
        public WritingAiCriteriaScores? CriteriaScores { get; set; }

        [JsonPropertyName("estimatedScaledScore")]
        public int? EstimatedScaledScore { get; set; }

        [JsonPropertyName("estimatedGrade")]
        public string? EstimatedGrade { get; set; }

        [JsonPropertyName("passed")]
        public bool? Passed { get; set; }

        [JsonPropertyName("passRequires")]
        public JsonElement? PassRequires { get; set; }

        [JsonPropertyName("advisory")]
        public string? Advisory { get; set; }

        [JsonPropertyName("strengths")]
        public List<string>? Strengths { get; set; }
    }

    private sealed record WritingAiFinding
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

    private sealed record WritingAiCriteriaScores
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
}
