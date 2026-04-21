using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

public interface ISpeakingEvaluationPipeline
{
    Task CompleteTranscriptionAsync(BackgroundJobItem job, CancellationToken cancellationToken);
    Task CompleteEvaluationAsync(BackgroundJobItem job, CancellationToken cancellationToken);
}

public sealed class SpeakingEvaluationPipeline(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    SpeakingRuleEngine ruleEngine,
    ILogger<SpeakingEvaluationPipeline> logger) : ISpeakingEvaluationPipeline
{
    public async Task CompleteTranscriptionAsync(BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var existingTranscript = JsonSupport.Deserialize<List<SpeakingTranscriptLine>>(attempt.TranscriptJson, []);
        if (existingTranscript.Count > 0)
        {
            MarkTranscriptionProvenance(attempt, provider: "existing", mock: false);
            return;
        }

        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var transcript = BuildMockDevelopmentTranscript(attempt, content);
        attempt.TranscriptJson = JsonSupport.Serialize(transcript);
        MarkTranscriptionProvenance(attempt, provider: "mock-dev", mock: true);
    }

    public async Task CompleteEvaluationAsync(BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);

        var transcript = JsonSupport.Deserialize<List<SpeakingTranscriptLine>>(attempt.TranscriptJson, []);
        if (transcript.Count == 0)
        {
            await CompleteTranscriptionAsync(job, cancellationToken);
            transcript = JsonSupport.Deserialize<List<SpeakingTranscriptLine>>(attempt.TranscriptJson, []);
        }

        var profession = ParseProfession(content.ProfessionId);
        var cardType = NormalizeCardType(content.ScenarioType, content.DetailJson);
        var turns = transcript
            .Select(line => new SpeakingTurn(
                line.Speaker,
                line.Text,
                SecondsToMilliseconds(line.StartTime),
                SecondsToMilliseconds(line.EndTime)))
            .ToList();

        var findings = ruleEngine.Audit(new SpeakingAuditInput(turns, cardType, profession));
        var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = profession,
            Task = AiTaskMode.Score,
            CardType = cardType,
        });

        AiGatewayResult? aiResult = null;
        int? aiEstimatedScore = null;
        IReadOnlyList<GatewayFinding> aiFindings = Array.Empty<GatewayFinding>();
        string aiProvenance = "gateway";

        try
        {
            aiResult = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = BuildEvaluationUserInput(content, attempt, transcript, findings),
                Model = "anthropic-claude-opus-4.7",
                Temperature = 0.1,
                MaxTokens = 4096,
                FeatureCode = AiFeatureCodes.SpeakingGrade,
                UserId = attempt.UserId,
                PromptTemplateId = "speaking.score.v1",
            }, cancellationToken);

            (aiEstimatedScore, aiFindings) = ParseGatewayScore(aiResult.Completion, prompt.Metadata.AppliedRuleIds);
            aiProvenance = "gateway:mock";
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Speaking grounded AI score call failed for attempt {AttemptId}; using rulebook fallback.", attempt.Id);
            aiProvenance = "gateway_error:rulebook_fallback";
        }

        var mergedFindings = MergeFindings(findings, aiFindings);
        var transcriptWithMarkers = AttachMarkers(transcript, mergedFindings);
        var scaledEstimate = ClampScaled(aiEstimatedScore ?? EstimateScaledScore(mergedFindings, transcriptWithMarkers, attempt));
        var speakingBand = OetScoring.GradeSpeaking(scaledEstimate);
        var scoreRange = BuildScoreRange(scaledEstimate);
        var confidence = ResolveConfidence(attempt, mergedFindings, aiResult);
        var phrasing = BuildPhrasingSegments(mergedFindings);

        attempt.TranscriptJson = JsonSupport.Serialize(transcriptWithMarkers);
        attempt.State = AttemptState.Completed;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        attempt.AnalysisJson = JsonSupport.Serialize(MergeAnalysis(attempt.AnalysisJson, new Dictionary<string, object?>
        {
            ["phrasing"] = phrasing,
            ["waveformPeaks"] = BuildWaveformPeaks(attempt.AudioMetadataJson),
            ["rulebookFindings"] = mergedFindings.Select(f => new
            {
                ruleId = f.RuleId,
                severity = f.Severity,
                message = f.Message,
                quote = f.Quote,
                fixSuggestion = f.FixSuggestion
            }).ToList(),
            ["aiProvenance"] = new
            {
                featureCode = AiFeatureCodes.SpeakingGrade,
                promptTemplateId = "speaking.score.v1",
                gateway = aiProvenance,
                rulebookVersion = aiResult?.RulebookVersion ?? prompt.Metadata.RulebookVersion,
                appliedRuleIds = aiResult?.AppliedRuleIds ?? prompt.Metadata.AppliedRuleIds,
                transcriptionProvider = ReadTranscriptionProvider(attempt.AnalysisJson),
                scoringPassAnchor = $"{OetScoring.ScaledPassGradeB}/500",
                advisoryOnly = true
            },
            ["speakingBand"] = new
            {
                scaledEstimate,
                requiredScaled = speakingBand.RequiredScaled,
                requiredGrade = speakingBand.RequiredGrade,
                grade = speakingBand.Grade,
                passed = speakingBand.Passed
            }
        }));

        evaluation.State = AsyncState.Completed;
        evaluation.ScoreRange = scoreRange;
        evaluation.GradeRange = speakingBand.Grade;
        evaluation.ConfidenceBand = confidence;
        evaluation.StrengthsJson = JsonSupport.Serialize(BuildStrengths(mergedFindings, transcriptWithMarkers));
        evaluation.IssuesJson = JsonSupport.Serialize(BuildIssues(mergedFindings, attempt.AnalysisJson));
        evaluation.CriterionScoresJson = JsonSupport.Serialize(BuildCriterionScores(mergedFindings, scaledEstimate));
        evaluation.FeedbackItemsJson = JsonSupport.Serialize(BuildFeedbackItems(evaluation.Id, transcriptWithMarkers, mergedFindings));
        evaluation.GeneratedAt = DateTimeOffset.UtcNow;
        evaluation.ModelExplanationSafe = "This advisory estimate uses the Speaking rulebook, rule-cited transcript markers, and the universal 350/500 Speaking pass anchor.";
        evaluation.LearnerDisclaimer = ReadTranscriptionProvider(attempt.AnalysisJson) == "mock-dev"
            ? "Training estimate only. This run used mock development ASR provenance, so treat transcript evidence as a workflow preview and request expert review for grading confidence."
            : "Training estimate only. This is not an official OET result; request expert review for grading confidence.";
        evaluation.StatusReasonCode = ReadTranscriptionProvider(attempt.AnalysisJson) == "mock-dev"
            ? "completed_mock_asr"
            : "completed";
        evaluation.StatusMessage = ReadTranscriptionProvider(attempt.AnalysisJson) == "mock-dev"
            ? "Speaking evaluation completed with mock development ASR provenance."
            : "Speaking evaluation completed.";
        evaluation.Retryable = false;
        evaluation.RetryAfterMs = null;
        evaluation.LastTransitionAt = DateTimeOffset.UtcNow;
    }

    private static List<SpeakingTranscriptLine> BuildMockDevelopmentTranscript(Attempt attempt, ContentItem? content)
    {
        var durationSeconds = ReadDurationSeconds(attempt.AudioMetadataJson);
        var title = content?.Title ?? "speaking role play";
        var setting = ReadString(content?.DetailJson, "setting") ?? "clinical setting";
        var end = Math.Max(6, Math.Min(durationSeconds ?? 18, 45));

        return
        [
            new SpeakingTranscriptLine
            {
                Id = "t1",
                Speaker = "candidate",
                Text = $"Mock development ASR transcript for {title} in a {setting}. Configure a production ASR provider before using transcript text as final learner evidence.",
                StartTime = 0,
                EndTime = end,
                Markers = []
            }
        ];
    }

    private void MarkTranscriptionProvenance(Attempt attempt, string provider, bool mock)
    {
        attempt.AnalysisJson = JsonSupport.Serialize(MergeAnalysis(attempt.AnalysisJson, new Dictionary<string, object?>
        {
            ["transcription"] = new
            {
                provider,
                provenance = mock ? "mock/dev" : "configured",
                mock,
                capturedAt = DateTimeOffset.UtcNow,
                warning = mock
                    ? "No production ASR provider was configured for this job; transcript text is explicit mock development evidence."
                    : null
            }
        }));
    }

    private static string BuildEvaluationUserInput(
        ContentItem content,
        Attempt attempt,
        IReadOnlyList<SpeakingTranscriptLine> transcript,
        IReadOnlyList<LintFinding> findings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Task title: {content.Title}");
        sb.AppendLine($"Scenario type: {content.ScenarioType ?? "general_roleplay"}");
        sb.AppendLine($"Profession: {content.ProfessionId ?? "medicine"}");
        sb.AppendLine($"Audio metadata: {attempt.AudioMetadataJson}");
        sb.AppendLine();
        sb.AppendLine("Transcript JSON:");
        sb.AppendLine(JsonSupport.Serialize(transcript.Select(line => new
        {
            line.Id,
            line.Speaker,
            line.Text,
            line.StartTime,
            line.EndTime
        })));
        sb.AppendLine();
        sb.AppendLine("Deterministic rulebook audit findings:");
        sb.AppendLine(JsonSupport.Serialize(findings.Select(f => new
        {
            f.RuleId,
            severity = f.Severity.ToString().ToLowerInvariant(),
            f.Message,
            f.Quote,
            f.FixSuggestion
        })));
        sb.AppendLine();
        sb.AppendLine("Score this attempt as advisory Speaking feedback only. Cite speaking rule IDs and preserve the universal 350/500 pass anchor.");
        return sb.ToString();
    }

    private static (int? scaledScore, IReadOnlyList<GatewayFinding> findings) ParseGatewayScore(
        string completion,
        IReadOnlyList<string> allowedRuleIds)
    {
        if (string.IsNullOrWhiteSpace(completion)) return (null, Array.Empty<GatewayFinding>());

        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return (null, Array.Empty<GatewayFinding>());

        try
        {
            using var document = JsonDocument.Parse(completion[start..(end + 1)]);
            var root = document.RootElement;
            int? score = null;
            if (root.TryGetProperty("estimatedScaledScore", out var scoreElement) && scoreElement.TryGetInt32(out var parsedScore))
            {
                score = parsedScore;
            }

            var findings = new List<GatewayFinding>();
            if (root.TryGetProperty("findings", out var findingsElement) && findingsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in findingsElement.EnumerateArray())
                {
                    var ruleId = ReadJsonString(item, "ruleId");
                    if (string.IsNullOrWhiteSpace(ruleId) || !allowedRuleIds.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    findings.Add(new GatewayFinding(
                        ruleId,
                        ReadJsonString(item, "severity") ?? "info",
                        ReadJsonString(item, "message") ?? "Rulebook finding.",
                        ReadJsonString(item, "quote"),
                        ReadJsonString(item, "fixSuggestion")));
                }
            }

            return (score, findings);
        }
        catch
        {
            return (null, Array.Empty<GatewayFinding>());
        }
    }

    private static List<UnifiedFinding> MergeFindings(
        IReadOnlyList<LintFinding> ruleFindings,
        IReadOnlyList<GatewayFinding> gatewayFindings)
    {
        var merged = new List<UnifiedFinding>();
        merged.AddRange(ruleFindings.Select(f => new UnifiedFinding(
            f.RuleId,
            f.Severity.ToString().ToLowerInvariant(),
            f.Message,
            f.Quote,
            f.FixSuggestion)));
        merged.AddRange(gatewayFindings.Select(f => new UnifiedFinding(
            f.RuleId,
            f.Severity,
            f.Message,
            f.Quote,
            f.FixSuggestion)));

        return merged
            .GroupBy(f => $"{f.RuleId}|{f.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => SeverityRank(f.Severity))
            .ThenBy(f => f.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SpeakingTranscriptLine> AttachMarkers(
        IReadOnlyList<SpeakingTranscriptLine> transcript,
        IReadOnlyList<UnifiedFinding> findings)
    {
        var cloned = transcript.Select(line => new SpeakingTranscriptLine
        {
            Id = line.Id,
            Speaker = line.Speaker,
            Text = line.Text,
            StartTime = line.StartTime,
            EndTime = line.EndTime,
            Markers = line.Markers?.ToList() ?? []
        }).ToList();

        var candidateLine = cloned.FirstOrDefault(line => string.Equals(line.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
                            ?? cloned.FirstOrDefault();
        if (candidateLine is null) return cloned;

        foreach (var (finding, index) in findings.Take(8).Select((finding, index) => (finding, index)))
        {
            candidateLine.Markers ??= [];
            candidateLine.Markers.Add(new Dictionary<string, object?>
            {
                ["id"] = $"tm-{index + 1}",
                ["type"] = CriterionFromFinding(finding),
                ["ruleId"] = finding.RuleId,
                ["severity"] = finding.Severity,
                ["startTime"] = candidateLine.StartTime,
                ["endTime"] = Math.Min(candidateLine.EndTime, candidateLine.StartTime + 6),
                ["text"] = finding.Quote ?? finding.RuleId,
                ["suggestion"] = finding.FixSuggestion ?? finding.Message
            });
        }

        return cloned;
    }

    private static int EstimateScaledScore(
        IReadOnlyList<UnifiedFinding> findings,
        IReadOnlyList<SpeakingTranscriptLine> transcript,
        Attempt attempt)
    {
        var score = 390;
        foreach (var finding in findings)
        {
            score -= SeverityRank(finding.Severity) switch
            {
                0 => 45,
                1 => 28,
                2 => 14,
                _ => 6
            };
        }

        var candidateWords = transcript
            .Where(line => string.Equals(line.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .Sum(line => Regex.Matches(line.Text ?? string.Empty, @"\b[\w']+\b").Count);

        if (candidateWords < 40) score -= 30;
        var duration = ReadDurationSeconds(attempt.AudioMetadataJson);
        if (duration is > 0 and < 90) score -= 15;

        return score;
    }

    private static string BuildScoreRange(int scaledEstimate)
    {
        var lower = ClampScaled(scaledEstimate - 18);
        var upper = ClampScaled(scaledEstimate + 12);
        if (lower == 330 && upper == 360)
        {
            lower = 332;
            upper = 362;
        }

        return $"{lower}-{upper}";
    }

    private static ConfidenceBand ResolveConfidence(
        Attempt attempt,
        IReadOnlyList<UnifiedFinding> findings,
        AiGatewayResult? aiResult)
    {
        if (ReadTranscriptionProvider(attempt.AnalysisJson) == "mock-dev") return ConfidenceBand.Low;
        if (aiResult is null) return ConfidenceBand.Low;
        if (findings.Any(f => SeverityRank(f.Severity) <= 1)) return ConfidenceBand.Medium;
        return ConfidenceBand.High;
    }

    private static List<string> BuildStrengths(
        IReadOnlyList<UnifiedFinding> findings,
        IReadOnlyList<SpeakingTranscriptLine> transcript)
    {
        var strengths = new List<string>
        {
            "The attempt has recorded audio, transcript evidence, and a rulebook-grounded review trail.",
            "The feedback is organised around OET Speaking criteria so follow-up practice can stay focused."
        };

        if (findings.Count == 0)
        {
            strengths.Add("No critical Speaking rulebook violations were detected in the transcript evidence.");
        }

        if (transcript.Any(line => string.Equals(line.Speaker, "candidate", StringComparison.OrdinalIgnoreCase)))
        {
            strengths.Add("Candidate speech was available for transcript-linked feedback.");
        }

        return strengths.Take(3).ToList();
    }

    private static List<string> BuildIssues(IReadOnlyList<UnifiedFinding> findings, string analysisJson)
    {
        var issues = findings
            .Take(4)
            .Select(f => $"{f.RuleId}: {f.Message}")
            .ToList();

        if (ReadTranscriptionProvider(analysisJson) == "mock-dev")
        {
            issues.Insert(0, "Transcript confidence is limited because this environment used the mock development ASR provider.");
        }

        if (issues.Count == 0)
        {
            issues.Add("No major rulebook issue was detected; use transcript replay or expert review for finer speaking nuance.");
        }

        return issues.Take(4).ToList();
    }

    private static List<object> BuildCriterionScores(IReadOnlyList<UnifiedFinding> findings, int scaledEstimate)
    {
        var baseline = scaledEstimate >= OetScoring.ScaledPassGradeB ? 4 : 3;
        var criteria = new[] { "intelligibility", "fluency", "appropriateness", "grammar_expression" };
        return criteria.Select(criterion =>
        {
            var related = findings.Count(f => CriterionFromFinding(f) == criterion);
            var score = Math.Clamp(baseline - related, 2, 5);
            return (object)new
            {
                criterionCode = criterion,
                scoreRange = $"{score}-{Math.Min(score + 1, 6)}/6",
                confidenceBand = related > 0 ? "medium" : "low",
                explanation = related > 0
                    ? "Rulebook findings are linked to this criterion in the transcript markers."
                    : "No direct rulebook marker was linked to this criterion in the current transcript evidence."
            };
        }).ToList();
    }

    private static List<object> BuildFeedbackItems(
        string evaluationId,
        IReadOnlyList<SpeakingTranscriptLine> transcript,
        IReadOnlyList<UnifiedFinding> findings)
    {
        var anchorLine = transcript.FirstOrDefault(line => string.Equals(line.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
                         ?? transcript.FirstOrDefault();
        return findings.Take(8).Select((finding, index) => (object)new
        {
            feedbackItemId = $"{evaluationId}-{index + 1}",
            criterionCode = CriterionFromFinding(finding),
            type = "rulebook_finding",
            ruleId = finding.RuleId,
            anchor = anchorLine is null
                ? null
                : new { lineId = anchorLine.Id, startTime = anchorLine.StartTime, endTime = anchorLine.EndTime },
            message = finding.Message,
            severity = finding.Severity,
            suggestedFix = finding.FixSuggestion ?? "Repeat the segment using a clearer, patient-safe clinical phrase."
        }).ToList();
    }

    private static List<object> BuildPhrasingSegments(IReadOnlyList<UnifiedFinding> findings)
    {
        var segments = findings
            .Where(f => !string.IsNullOrWhiteSpace(f.FixSuggestion) || !string.IsNullOrWhiteSpace(f.Quote))
            .Take(5)
            .Select((finding, index) => (object)new
            {
                id = $"phr-{index + 1}",
                ruleId = finding.RuleId,
                originalPhrase = finding.Quote ?? finding.RuleId,
                issueExplanation = finding.Message,
                strongerAlternative = finding.FixSuggestion ?? "Use a clearer patient-centred phrase.",
                drillPrompt = "Repeat this part once, keeping the meaning but improving clarity and tone."
            })
            .ToList();

        if (segments.Count == 0)
        {
            segments.Add(new
            {
                id = "phr-1",
                ruleId = "speaking-general",
                originalPhrase = "Transition between role-card tasks",
                issueExplanation = "Use phrasing drills to keep the role play structured without sounding scripted.",
                strongerAlternative = "Let me first check what matters most to you, then I will explain the next step.",
                drillPrompt = "Say this transition twice, then adapt it to the role card."
            });
        }

        return segments;
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

    private static int[] BuildWaveformPeaks(string audioMetadataJson)
    {
        var duration = ReadDurationSeconds(audioMetadataJson) ?? 120;
        var seed = Math.Clamp(duration, 30, 300);
        return Enumerable.Range(0, 12)
            .Select(i => 8 + (int)Math.Round(Math.Abs(Math.Sin((i + 1) * seed / 37.0)) * 18))
            .ToArray();
    }

    private static string CriterionFromFinding(UnifiedFinding finding)
    {
        var text = $"{finding.RuleId} {finding.Message}".ToLowerInvariant();
        if (text.Contains("jargon") || text.Contains("plain") || text.Contains("grammar") || text.Contains("expression"))
            return "grammar_expression";
        if (text.Contains("empathy") || text.Contains("sensitive") || text.Contains("patient") || text.Contains("tone"))
            return "appropriateness";
        if (text.Contains("monologue") || text.Contains("pause") || text.Contains("silence") || text.Contains("recap"))
            return "fluency";
        return "intelligibility";
    }

    private static int SeverityRank(string severity) => severity.Trim().ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "minor" => 2,
        _ => 3
    };

    private static int ClampScaled(int score) => Math.Clamp(score, OetScoring.ScaledMin, OetScoring.ScaledMax);

    private static int SecondsToMilliseconds(double seconds) => (int)Math.Round(seconds * 1000);

    private static int? ReadDurationSeconds(string audioMetadataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(audioMetadataJson) ? "{}" : audioMetadataJson);
            if (doc.RootElement.TryGetProperty("durationSeconds", out var duration) && duration.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? ReadString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeCardType(string? scenarioType, string detailJson)
    {
        var raw = scenarioType ?? ReadString(detailJson, "cardType") ?? ReadString(detailJson, "scenarioType") ?? "general_roleplay";
        var normalized = raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized.Contains("breaking") || normalized.Contains("bad_news")
            ? "breaking_bad_news"
            : normalized;
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        var normalized = raw.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse<ExamProfession>(normalized, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    private static string? ReadJsonString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadTranscriptionProvider(string analysisJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(analysisJson) ? "{}" : analysisJson);
            if (doc.RootElement.TryGetProperty("transcription", out var transcription)
                && transcription.TryGetProperty("provider", out var provider)
                && provider.ValueKind == JsonValueKind.String)
            {
                return provider.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private sealed class SpeakingTranscriptLine
    {
        public string Id { get; set; } = "";
        public string Speaker { get; set; } = "candidate";
        public string Text { get; set; } = "";
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public List<Dictionary<string, object?>>? Markers { get; set; }
    }

    private sealed record GatewayFinding(
        string RuleId,
        string Severity,
        string Message,
        string? Quote,
        string? FixSuggestion);

    private sealed record UnifiedFinding(
        string RuleId,
        string Severity,
        string Message,
        string? Quote,
        string? FixSuggestion);
}
