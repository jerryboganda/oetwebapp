using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Phase 2 (D.1) of the OET Speaking module roadmap.
///
/// Synchronously scores a finished <see cref="SpeakingSession"/> via the
/// rulebook-grounded <see cref="IAiGatewayService"/>. The output is an
/// advisory <see cref="SpeakingAiAssessment"/> row — the canonical
/// scaled score is ALWAYS recomputed via
/// <see cref="OetScoring.SpeakingProjectedScaled(OetScoring.SpeakingCriterionScores)"/>
/// rather than trusting the AI's own number. Per-criterion scores are
/// clamped to the OET rubric (linguistic 0–6, clinical 0–3).
///
/// Evidence quotes from the AI are best-effort verified against the
/// transcript so the per-criterion drawer can render highlighted
/// segments without trusting the AI to fabricate substrings.
/// </summary>
public sealed class SpeakingAiAssessmentService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    ILogger<SpeakingAiAssessmentService> logger)
{
    private const string PromptTemplateId = "speaking.score.v2";
    private const string ProviderName = "ai_gateway";
    private const string ModelId = "gateway-default";

    // ---------------------------------------------------------------------
    // Prompt template — appended to the rulebook-grounded system prompt
    // as the user/task content. The gateway itself supplies the rulebook
    // header so this template focuses on the JSON contract the AI must
    // return for the speaking-grade feature.
    // ---------------------------------------------------------------------
    private const string PROMPT_TEMPLATE_V2 = """
You are an OET Speaking examiner scoring a single role-play session.
Return ONLY a strict JSON object with this exact shape (no markdown, no
prose, no code fences):

{
  "criterionScores": {
    "intelligibility":      { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "fluency":              { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "appropriateness":      { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "grammarExpression":    { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "relationshipBuilding": { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "patientPerspective":   { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "structure":            { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "informationGathering": { "score": 0, "rationale": "", "evidenceQuotes": [] },
    "informationGiving":    { "score": 0, "rationale": "", "evidenceQuotes": [] }
  },
  "readinessBand": "not_ready|developing|borderline|exam_ready|strong",
  "overallSummary": "",
  "confidenceBand": "low|medium|high",
  "strengths": [],
  "improvements": [],
  "recommendedDrillKinds": []
}

Scoring rules:
  * Linguistic criteria (intelligibility, fluency, appropriateness,
    grammarExpression) use the OET 0–6 band scale.
  * Clinical communication criteria (relationshipBuilding,
    patientPerspective, structure, informationGathering,
    informationGiving) use the OET 0–3 band scale.
  * Each `evidenceQuotes` entry MUST be a verbatim substring of the
    candidate's transcript turns. Quote 3–10 words.
  * `rationale` must explain WHY the score was awarded, citing the
    relevant criterion descriptor.
  * `overallSummary` is 2–4 sentences of advisory feedback. Never claim
    this is an official OET score.
""";

    public async Task<SpeakingAiAssessmentProjection> RunAssessmentAsync(
        string sessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw ApiException.Validation("SPEAKING_SESSION_ID_REQUIRED",
                "Speaking session id is required.");
        }

        // ── Load session + card + interlocutor + latest transcript ──
        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("speaking_session_not_found",
                "That Speaking session does not exist.");

        // ── No AI for MOCK or LIVE-TUTOR Speaking ──────────────────────────
        // Two ways a Speaking session is human-marked, never AI:
        //   1. Live-tutor booking (Mode = LiveTutor) — the booked tutor plays the
        //      patient and marks the exam.
        //   2. A MOCK (2026-06-29 owner rule) — a two-card exam launched from a
        //      curated Mock Set (or full mock bundle) carries a MockSetId /
        //      MockSessionId. Mock Speaking is forced to a live-tutor booking at
        //      creation, so in practice mocks are already LiveTutor; this second
        //      clause also catches any legacy AI-mode mock row.
        // In both cases the finished session is visible in the tutor review queue
        // (TutorReviewQueueService.ListQueueAsync) and the tutor's
        // SpeakingTutorAssessment becomes the released band. Do NOT call the AI
        // gateway or write a SpeakingAiAssessment row.
        //
        // Non-mock AI sessions (AiSelfPractice / random AiExam) fall through and
        // ARE AI-scored. For AiExam the assessment is OFFICIAL (IsAdvisory=false);
        // for practice it is advisory (IsAdvisory=true).
        if (session.Mode == SpeakingSessionMode.LiveTutor
            || !string.IsNullOrWhiteSpace(session.MockSetId)
            || !string.IsNullOrWhiteSpace(session.MockSessionId))
        {
            logger.LogInformation(
                "Speaking session {SessionId} is mock/live-tutor — AI assessment skipped; routed to human examiner marking.",
                sessionId);
            return new SpeakingAiAssessmentProjection(
                AssessmentId: string.Empty,
                Provider: "human_examiner",
                ModelId: string.Empty,
                PromptTemplateId: string.Empty,
                CriterionScores: new Dictionary<string, CriterionScore>(),
                EstimatedScaledScore: 0,
                ReadinessBand: "awaiting_human_review",
                OverallSummary: "Mock Speaking is marked by a human examiner. Your result is released after marking.",
                ConfidenceBand: "pending",
                GeneratedAt: DateTimeOffset.UtcNow,
                IsAdvisory: false);
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, ct)
            ?? throw ApiException.NotFound("role_play_card_not_found",
                "That role-play card does not exist.");

        var script = await db.InterlocutorScripts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, ct);

        // Hidden card type — fed to the scorer as marking guidance, never to
        // the learner. Null when the card is untyped.
        SpeakingCardType? cardTypeRow = null;
        if (!string.IsNullOrWhiteSpace(card.CardTypeId))
        {
            cardTypeRow = await db.SpeakingCardTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == card.CardTypeId, ct);
        }

        var transcript = await db.SpeakingTranscripts.AsNoTracking()
            .Where(t => t.SpeakingSessionId == sessionId && t.IsLatest)
            .OrderByDescending(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (transcript is null)
        {
            throw ApiException.Conflict("speaking_session_no_transcript",
                "An AI assessment requires a transcript. Wait for transcription to complete and try again.");
        }

        // ── Build grounded prompt via the canonical gateway ──
        var profession = ParseProfession(card.ProfessionId);
        AiGroundedPrompt prompt;
        try
        {
            prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Speaking,
                Profession = profession,
                Task = AiTaskMode.Score,
                CardType = "role_play",
            });
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }

        var userInput = BuildUserInput(card, script, transcript, cardTypeRow);

        // ── Invoke gateway (mirror SpeakingEvaluationPipeline pattern) ──
        AiGatewayResult aiResult;
        try
        {
            aiResult = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userInput,
                Model = string.Empty,
                Temperature = 0.1,
                MaxTokens = 4096,
                FeatureCode = AiFeatureCodes.SpeakingGrade,
                UserId = session.UserId,
                PromptTemplateId = PromptTemplateId,
                // Tag the assessment context for the audit trail + gateway
                // backstop. ONLY a genuine mock (curated Mock Set / full mock
                // bundle — MockSetId/MockSessionId set) is Mock context; a plain
                // ExamSessionId does NOT make a session a mock (every two-card
                // exam card has one), so random AI exams are Practice and keep AI
                // marking. Mock Speaking never reaches here (it is human-marked
                // above) — this tag just arms the gateway's mock_assessment_
                // forbidden backstop if a future caller ever bypasses the guard.
                AssessmentContext =
                    (!string.IsNullOrWhiteSpace(session.MockSetId)
                        || !string.IsNullOrWhiteSpace(session.MockSessionId))
                        ? AiAssessmentContext.Mock
                        : AiAssessmentContext.Practice,
            }, ct);
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Speaking AI assessment failed for session {SessionId}; surfacing retryable error.",
                sessionId);
            // Fail loud so the caller can retry. The free-tier counter is
            // not consumed because the AI gateway records the failure as
            // AiUsageRecord.Outcome=ProviderError (mirrors Q3 fail-loud in
            // SpeakingEvaluationPipeline). DO NOT swallow.
            throw ApiException.Conflict("speaking_ai_unavailable",
                "We couldn't reach the AI scoring service. Please retry shortly — your free-tier counter has not been consumed.");
        }

        // ── Parse, clamp, and validate evidence quotes ──
        var parsed = ParseAssessment(aiResult.Completion);
        if (parsed is null)
        {
            logger.LogWarning(
                "Speaking AI assessment returned an unparseable payload for session {SessionId}.",
                sessionId);
            throw ApiException.Conflict("speaking_ai_unparseable",
                "The AI scoring service returned an invalid response. Please retry.");
        }

        var transcriptText = ExtractTranscriptText(transcript.SegmentsJson);
        foreach (var (code, criterion) in parsed.CriterionScores)
        {
            foreach (var quote in criterion.EvidenceQuotes)
            {
                if (!ContainsNormalised(transcriptText, quote))
                {
                    logger.LogWarning(
                        "Speaking AI assessment quote not found in transcript for session {SessionId} criterion {Criterion}: {Quote}",
                        sessionId, code, quote);
                }
            }
        }

        // ── Canonical scaled score: ALWAYS recomputed via OetScoring ──
        var rubricScores = new OetScoring.SpeakingCriterionScores(
            Intelligibility:      ScoreOf(parsed, "intelligibility",      0, 6),
            Fluency:              ScoreOf(parsed, "fluency",              0, 6),
            Appropriateness:      ScoreOf(parsed, "appropriateness",      0, 6),
            GrammarExpression:    ScoreOf(parsed, "grammarExpression",    0, 6),
            RelationshipBuilding: ScoreOf(parsed, "relationshipBuilding", 0, 3),
            PatientPerspective:   ScoreOf(parsed, "patientPerspective",   0, 3),
            Structure:            ScoreOf(parsed, "structure",            0, 3),
            InformationGathering: ScoreOf(parsed, "informationGathering", 0, 3),
            InformationGiving:    ScoreOf(parsed, "informationGiving",    0, 3));

        var scaled = OetScoring.SpeakingProjectedScaled(rubricScores);
        var readinessBand = OetScoring.SpeakingReadinessBandCode(
            OetScoring.SpeakingReadinessBandFromScaled(scaled));

        // ── Persist ──
        var now = DateTimeOffset.UtcNow;
        var assessmentId = $"spa_{Guid.NewGuid():N}";

        var rationalesPayload = new Dictionary<string, object?>();
        foreach (var (code, criterion) in parsed.CriterionScores)
        {
            rationalesPayload[code] = new
            {
                rationale = criterion.Rationale,
                evidenceQuotes = criterion.EvidenceQuotes,
            };
        }

        var row = new SpeakingAiAssessment
        {
            Id = assessmentId,
            SpeakingSessionId = sessionId,
            TranscriptId = transcript.Id,
            Provider = ProviderName,
            ModelId = ModelId,
            PromptTemplateId = PromptTemplateId,
            Intelligibility = rubricScores.Intelligibility,
            Fluency = rubricScores.Fluency,
            Appropriateness = rubricScores.Appropriateness,
            GrammarExpression = rubricScores.GrammarExpression,
            RelationshipBuilding = rubricScores.RelationshipBuilding,
            PatientPerspective = rubricScores.PatientPerspective,
            Structure = rubricScores.Structure,
            InformationGathering = rubricScores.InformationGathering,
            InformationGiving = rubricScores.InformationGiving,
            EstimatedScaledScore = scaled,
            ReadinessBand = readinessBand,
            PerCriterionRationalesJson = JsonSerializer.Serialize(rationalesPayload),
            OverallSummary = parsed.OverallSummary ?? string.Empty,
            ConfidenceBand = NormaliseConfidenceBand(parsed.ConfidenceBand),
            GeneratedAt = now,
            RulebookFindingsJson = "[]",
            // OFFICIAL when this is an AI exam card; advisory for practice.
            IsAdvisory = session.Mode != SpeakingSessionMode.AiExam,
        };

        db.SpeakingAiAssessments.Add(row);
        await db.SaveChangesAsync(ct);

        return ProjectAssessment(row, parsed.CriterionScores);
    }

    public async Task<SpeakingAiAssessmentProjection?> GetLatestAsync(
        string sessionId,
        CancellationToken ct)
    {
        var row = await db.SpeakingAiAssessments.AsNoTracking()
            .Where(a => a.SpeakingSessionId == sessionId)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return ProjectAssessment(row, RehydrateCriterionScores(row));
    }

    // ─────────────────────────────────────────────────────────────────
    // Projection
    // ─────────────────────────────────────────────────────────────────

    private static SpeakingAiAssessmentProjection ProjectAssessment(
        SpeakingAiAssessment row,
        IDictionary<string, CriterionScore> criterionScores)
    {
        return new SpeakingAiAssessmentProjection(
            AssessmentId: row.Id,
            Provider: row.Provider,
            ModelId: row.ModelId,
            PromptTemplateId: row.PromptTemplateId,
            CriterionScores: criterionScores,
            EstimatedScaledScore: row.EstimatedScaledScore,
            ReadinessBand: row.ReadinessBand,
            OverallSummary: row.OverallSummary,
            ConfidenceBand: row.ConfidenceBand,
            GeneratedAt: row.GeneratedAt,
            IsAdvisory: row.IsAdvisory);
    }

    private static IDictionary<string, CriterionScore> RehydrateCriterionScores(SpeakingAiAssessment row)
    {
        var rationales = ReadRationales(row.PerCriterionRationalesJson);
        IDictionary<string, CriterionScore> result = new Dictionary<string, CriterionScore>(StringComparer.OrdinalIgnoreCase)
        {
            ["intelligibility"]      = Build(row.Intelligibility,      6, "intelligibility"),
            ["fluency"]              = Build(row.Fluency,              6, "fluency"),
            ["appropriateness"]      = Build(row.Appropriateness,      6, "appropriateness"),
            ["grammarExpression"]    = Build(row.GrammarExpression,    6, "grammarExpression"),
            ["relationshipBuilding"] = Build(row.RelationshipBuilding, 3, "relationshipBuilding"),
            ["patientPerspective"]   = Build(row.PatientPerspective,   3, "patientPerspective"),
            ["structure"]            = Build(row.Structure,            3, "structure"),
            ["informationGathering"] = Build(row.InformationGathering, 3, "informationGathering"),
            ["informationGiving"]    = Build(row.InformationGiving,    3, "informationGiving"),
        };
        return result;

        CriterionScore Build(int score, int max, string code)
        {
            var found = rationales.TryGetValue(code, out var packed);
            return new CriterionScore(
                Score: score,
                MaxScore: max,
                Rationale: found ? (packed.Rationale ?? string.Empty) : string.Empty,
                EvidenceQuotes: found ? (packed.EvidenceQuotes ?? Array.Empty<string>()) : Array.Empty<string>());
        }
    }

    private static Dictionary<string, (string Rationale, string[] EvidenceQuotes)> ReadRationales(string? json)
    {
        var map = new Dictionary<string, (string, string[])>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var rationale = prop.Value.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() ?? string.Empty
                    : string.Empty;
                var quotes = new List<string>();
                if (prop.Value.TryGetProperty("evidenceQuotes", out var quotesEl)
                    && quotesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in quotesEl.EnumerateArray())
                    {
                        if (q.ValueKind == JsonValueKind.String)
                        {
                            var s = q.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) quotes.Add(s!);
                        }
                    }
                }
                map[prop.Name] = (rationale, quotes.ToArray());
            }
        }
        catch
        {
            // Tolerate corrupt JSON — fall back to empty rationales.
        }

        return map;
    }

    // ─────────────────────────────────────────────────────────────────
    // Prompt assembly
    // ─────────────────────────────────────────────────────────────────

    private static string BuildUserInput(
        RolePlayCard card,
        InterlocutorScript? script,
        SpeakingTranscript transcript,
        SpeakingCardType? cardType)
    {
        var sb = new StringBuilder();
        sb.AppendLine(PROMPT_TEMPLATE_V2);
        sb.AppendLine();
        // Hidden card type — marking guidance only. NEVER shown to the learner.
        if (cardType is not null)
        {
            sb.AppendLine("---- CARD TYPE (hidden marking guidance) ----");
            sb.AppendLine(JsonSerializer.Serialize(new
            {
                name = cardType.Name,
                description = cardType.Description,
            }));
            sb.AppendLine();
        }
        sb.AppendLine("---- ROLE PLAY CARD (candidate-facing) ----");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            cardId = card.Id,
            professionId = card.ProfessionId,
            scenarioTitle = card.ScenarioTitle,
            setting = card.Setting,
            candidateRole = card.CandidateRole,
            interlocutorRole = card.InterlocutorRole,
            patientName = card.PatientName,
            patientAge = card.PatientAge,
            background = card.Background,
            tasks = new[] { card.Task1, card.Task2, card.Task3, card.Task4, card.Task5 }
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToArray(),
            patientEmotion = card.PatientEmotion,
            communicationGoal = card.CommunicationGoal,
            clinicalTopic = card.ClinicalTopic,
        }));
        sb.AppendLine();
        sb.AppendLine("---- INTERLOCUTOR SCRIPT (hidden patient persona) ----");
        sb.AppendLine(script is null
            ? "{}"
            : JsonSerializer.Serialize(new
            {
                patientBackground = script.PatientBackground,
                patientTasks = new[]
                    {
                        script.PatientTask1, script.PatientTask2, script.PatientTask3,
                        script.PatientTask4, script.PatientTask5,
                    }
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToArray(),
                openingResponse = script.OpeningResponse,
                prompts = new[] { script.Prompt1, script.Prompt2, script.Prompt3 }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray(),
                hiddenInformation = script.HiddenInformation,
                resistanceLevel = ResistanceLevels.ToCode(script.ResistanceLevel),
                emotionalState = script.EmotionalState,
                closingCue = script.ClosingCue,
            }));
        sb.AppendLine();
        sb.AppendLine("---- TRANSCRIPT (latest revision) ----");
        sb.AppendLine(transcript.SegmentsJson);
        sb.AppendLine();
        sb.AppendLine("Now produce the strict JSON object specified above.");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    // Response parsing
    // ─────────────────────────────────────────────────────────────────

    private sealed class ParsedAssessment
    {
        public Dictionary<string, CriterionScore> CriterionScores { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string? OverallSummary { get; init; }
        public string? ConfidenceBand { get; init; }
    }

    private static ParsedAssessment? ParseAssessment(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            var root = doc.RootElement;

            var scores = new Dictionary<string, CriterionScore>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("criterionScores", out var critEl)
                && critEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in critEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var rawScore = TryReadInt(prop.Value, "score") ?? 0;
                    var rationale = TryReadString(prop.Value, "rationale") ?? string.Empty;
                    var quotes = ReadStringArray(prop.Value, "evidenceQuotes");
                    var max = IsLinguisticCriterion(prop.Name) ? 6 : 3;
                    scores[prop.Name] = new CriterionScore(rawScore, max, rationale, quotes);
                }
            }

            return new ParsedAssessment
            {
                CriterionScores = scores,
                OverallSummary = TryReadString(root, "overallSummary"),
                ConfidenceBand = TryReadString(root, "confidenceBand"),
            };
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreOf(ParsedAssessment parsed, string code, int min, int max)
    {
        if (!parsed.CriterionScores.TryGetValue(code, out var cs)) return 0;
        return Math.Clamp(cs.Score, min, max);
    }

    private static string NormaliseConfidenceBand(string? raw) => (raw ?? "medium").Trim().ToLowerInvariant() switch
    {
        "low" => "low",
        "high" => "high",
        _ => "medium",
    };

    private static bool IsLinguisticCriterion(string code) => code switch
    {
        "intelligibility" or "fluency" or "appropriateness" or "grammarExpression" => true,
        _ => false,
    };

    private static int? TryReadInt(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)Math.Round(v.GetDouble()),
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null,
        };
    }

    private static string? TryReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string[] ReadStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
            }
        }
        return list.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────
    // Quote verification
    // ─────────────────────────────────────────────────────────────────

    private static string ExtractTranscriptText(string segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;
            var sb = new StringBuilder();
            foreach (var segment in doc.RootElement.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object) continue;
                if (segment.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    sb.Append(textEl.GetString());
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ContainsNormalised(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        var h = NormaliseWhitespace(haystack);
        var n = NormaliseWhitespace(needle);
        return h.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormaliseWhitespace(string value)
        => Regex.Replace(value.Trim(), @"\s+", " ");

    // ─────────────────────────────────────────────────────────────────
    // Profession parsing (mirrors SpeakingEvaluationPipeline)
    // ─────────────────────────────────────────────────────────────────

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        var normalised = raw
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse<ExamProfession>(normalised, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }
}
