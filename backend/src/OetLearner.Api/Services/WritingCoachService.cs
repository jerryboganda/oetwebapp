using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

/// <summary>
/// AI Writing Coach — provides real-time, rule-cited suggestions while a
/// learner drafts an OET letter. The service combines two sources:
///
///   1. <see cref="WritingRuleEngine"/> — deterministic, rulebook-backed
///      findings that always run and never fail the request. These are the
///      authoritative baseline; they execute before the AI call so learners
///      always see rule-engine feedback even if the upstream model is down.
///   2. <see cref="IAiGatewayService"/> — model-generated suggestions that
///      are forced through the rulebook-grounded gateway (the gateway
///      physically refuses ungrounded prompts). Failures are logged and
///      swallowed so coach is "best-effort additive" rather than fatal.
///
/// All AI calls use <see cref="AiTaskMode.Coach"/> + the
/// <see cref="AiFeatureCodes.WritingCoachSuggest"/> feature code, so usage
/// metering and BYOK eligibility flow through the standard pipeline.
/// </summary>
public class WritingCoachService(
    LearnerDbContext db,
    IAiGatewayService gateway,
    WritingRuleEngine ruleEngine,
    ILogger<WritingCoachService> logger,
    OetLearner.Api.Services.Writing.IWritingOptionsProvider? optionsProvider = null)
{
    public async Task<object> CheckTextAsync(string userId, string attemptId, WritingCoachCheckRequest request, CancellationToken ct)
    {
        var attemptExists = await db.Attempts.AsNoTracking()
            .AnyAsync(attempt => attempt.Id == attemptId
                && attempt.UserId == userId
                && attempt.SubtestCode == "writing", ct);
        if (!attemptExists)
        {
            throw ApiException.NotFound("WRITING_ATTEMPT_NOT_FOUND", "Writing attempt not found.");
        }

        // Ensure or create coach session
        var session = await db.WritingCoachSessions
            .FirstOrDefaultAsync(s => s.AttemptId == attemptId && s.UserId == userId, ct);

        if (session == null)
        {
            session = new WritingCoachSession
            {
                Id = $"wcs-{Guid.NewGuid():N}",
                AttemptId = attemptId,
                UserId = userId,
                SuggestionsGenerated = 0,
                SuggestionsAccepted = 0,
                SuggestionsDismissed = 0,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.WritingCoachSessions.Add(session);
        }

        var text = request.CurrentText ?? "";
        if (text.Length == 0)
        {
            await db.SaveChangesAsync(ct);
            return BuildResponse(session, Array.Empty<WritingCoachSuggestion>());
        }

        // Admin kill-switch: when AI coach is disabled, persist the session
        // (so accept/dismiss telemetry still works) but skip both rule-engine
        // and AI suggestion generation. Returns an empty suggestion list so
        // the UI degrades gracefully.
        if (optionsProvider is not null)
        {
            var opts = await optionsProvider.GetAsync(ct);
            if (!opts.AiCoachEnabled)
            {
                await db.SaveChangesAsync(ct);
                return BuildResponse(session, Array.Empty<WritingCoachSuggestion>());
            }
        }

        var letterType = string.IsNullOrWhiteSpace(request.LetterType)
            ? "routine_referral"
            : request.LetterType.Trim().ToLowerInvariant();
        var profession = ParseProfession(request.Profession);
        var country = string.IsNullOrWhiteSpace(request.CandidateCountry) ? null : request.CandidateCountry.Trim();
        var caseNotesMarkers = await DeriveCaseNotesMarkersAsync(userId, attemptId, ct);

        var emitted = new List<WritingCoachSuggestion>();
        // Dedupe key combines ruleId + character anchor so the rule engine and
        // AI never persist duplicate findings for the same span.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 1. Deterministic rule-engine findings — always attempted, errors
        //    here are non-fatal so coach degrades gracefully.
        try
        {
            var lintInput = new WritingLintInput(
                LetterText: text,
                LetterType: letterType,
                CaseNotesMarkers: caseNotesMarkers,
                Profession: profession);
            var findings = ruleEngine.Lint(lintInput);
            foreach (var f in findings)
            {
                var s = MaterialiseRuleFinding(f, session.Id, attemptId, text);
                if (!seen.Add(SuggestionKey(f.RuleId, s.StartOffset, s.EndOffset))) continue;
                db.WritingCoachSuggestions.Add(s);
                session.SuggestionsGenerated++;
                emitted.Add(s);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Writing rule engine failed for attempt {AttemptId} (letterType={LetterType}, profession={Profession})",
                attemptId, letterType, profession);
        }

        // 2. AI suggestions — best-effort additive layer. Any failure
        //    (gateway refusal, provider error, malformed JSON) downgrades to
        //    rule-engine-only without bubbling to the caller.
        try
        {
            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = letterType,
                CandidateCountry = country,
                Task = AiTaskMode.Coach,
            });

            var aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = text,
                FeatureCode = AiFeatureCodes.WritingCoachSuggest,
                UserId = userId,
                Temperature = 0.3,
            }, ct);

            var allowedAiRuleIds = new HashSet<string>(
                aiResult.AppliedRuleIds.Count > 0 ? aiResult.AppliedRuleIds : prompt.Metadata.AppliedRuleIds,
                StringComparer.OrdinalIgnoreCase);

            foreach (var ai in ParseAiSuggestions(aiResult.Completion, text))
            {
                if (!allowedAiRuleIds.Contains(ai.RuleId)) continue;
                if (!seen.Add(SuggestionKey(ai.RuleId, ai.StartOffset, ai.EndOffset))) continue;
                var s = new WritingCoachSuggestion
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    AttemptId = attemptId,
                    SuggestionType = ai.Category,
                    OriginalText = ai.OriginalText,
                    SuggestedText = ai.SuggestedText,
                    Explanation = ComposeExplanation(ai.RuleId, ai.Severity, ai.Message, ai.Rationale),
                    StartOffset = ai.StartOffset,
                    EndOffset = ai.EndOffset,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.WritingCoachSuggestions.Add(s);
                session.SuggestionsGenerated++;
                emitted.Add(s);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AI gateway call failed for writing coach attempt {AttemptId}; persisting rule-engine findings only",
                attemptId);
        }

        await db.SaveChangesAsync(ct);
        return BuildResponse(session, emitted);
    }

    public async Task<object> ResolveSuggestionAsync(string userId, Guid suggestionId, string resolution, CancellationToken ct)
    {
        if (resolution is not ("accepted" or "dismissed"))
            throw ApiException.Validation("INVALID_RESOLUTION", "Resolution must be 'accepted' or 'dismissed'.");

        var suggestion = await db.WritingCoachSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId, ct)
            ?? throw ApiException.NotFound("SUGGESTION_NOT_FOUND", "Coach suggestion not found.");

        // Verify ownership via session
        var session = await db.WritingCoachSessions
            .FirstOrDefaultAsync(s => s.Id == suggestion.SessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Coach session not found.");

        if (suggestion.Resolution != null)
            throw ApiException.Validation("ALREADY_RESOLVED", "Suggestion has already been resolved.");

        suggestion.Resolution = resolution;
        if (resolution == "accepted") session.SuggestionsAccepted++;
        else session.SuggestionsDismissed++;

        await db.SaveChangesAsync(ct);

        return new
        {
            id = suggestion.Id,
            resolution = suggestion.Resolution,
            stats = new
            {
                totalGenerated = session.SuggestionsGenerated,
                accepted = session.SuggestionsAccepted,
                dismissed = session.SuggestionsDismissed
            }
        };
    }

    public async Task<object> GetStatsAsync(string userId, string attemptId, CancellationToken ct)
    {
        var session = await db.WritingCoachSessions
            .FirstOrDefaultAsync(s => s.AttemptId == attemptId && s.UserId == userId, ct);

        if (session == null)
        {
            return new
            {
                active = false,
                totalGenerated = 0,
                accepted = 0,
                dismissed = 0,
                pending = 0,
                acceptanceRate = 0.0,
                suggestionBreakdown = Array.Empty<object>()
            };
        }

        var suggestions = await db.WritingCoachSuggestions
            .Where(s => s.SessionId == session.Id)
            .ToListAsync(ct);

        var breakdown = suggestions
            .GroupBy(s => s.SuggestionType)
            .Select(g => new
            {
                type = g.Key,
                total = g.Count(),
                accepted = g.Count(s => s.Resolution == "accepted"),
                dismissed = g.Count(s => s.Resolution == "dismissed"),
                pending = g.Count(s => s.Resolution == null)
            })
            .ToList();

        return new
        {
            active = true,
            totalGenerated = session.SuggestionsGenerated,
            accepted = session.SuggestionsAccepted,
            dismissed = session.SuggestionsDismissed,
            pending = session.SuggestionsGenerated - session.SuggestionsAccepted - session.SuggestionsDismissed,
            acceptanceRate = session.SuggestionsGenerated > 0
                ? Math.Round(100.0 * session.SuggestionsAccepted / session.SuggestionsGenerated, 1)
                : 0.0,
            suggestionBreakdown = breakdown
        };
    }

    private async Task<WritingCaseNotesMarkers> DeriveCaseNotesMarkersAsync(string userId, string attemptId, CancellationToken ct)
    {
        var caseNotes = await (from attempt in db.Attempts.AsNoTracking()
                               join content in db.ContentItems.AsNoTracking() on attempt.ContentId equals content.Id
                               where attempt.Id == attemptId
                                   && attempt.UserId == userId
                                   && attempt.SubtestCode == "writing"
                                   && content.SubtestCode == "writing"
                               select content.CaseNotes)
            .FirstOrDefaultAsync(ct);

        return WritingCaseNotesMarkerExtractor.Derive(caseNotes);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static object BuildResponse(WritingCoachSession session, IReadOnlyList<WritingCoachSuggestion> suggestions)
        => new
        {
            sessionId = session.Id,
            suggestions = suggestions.Select(s => new
            {
                id = s.Id,
                type = s.SuggestionType,
                originalText = s.OriginalText,
                suggestedText = s.SuggestedText,
                explanation = s.Explanation,
                startOffset = s.StartOffset,
                endOffset = s.EndOffset,
                resolution = s.Resolution,
            }),
            stats = new
            {
                totalGenerated = session.SuggestionsGenerated,
                accepted = session.SuggestionsAccepted,
                dismissed = session.SuggestionsDismissed,
                pending = session.SuggestionsGenerated - session.SuggestionsAccepted - session.SuggestionsDismissed,
            }
        };

    private static string SuggestionKey(string ruleId, int start, int end)
        => $"{ruleId}|{start}|{end}";

    private static WritingCoachSuggestion MaterialiseRuleFinding(LintFinding f, string sessionId, string attemptId, string text)
    {
        var start = Math.Max(0, f.Start ?? 0);
        var end = Math.Max(start, f.End ?? (start + (f.Quote?.Length ?? 0)));
        if (end > text.Length) end = text.Length;
        if (start > text.Length) start = text.Length;

        var category = MapSeverityToCategory(f.RuleId, f.Severity);
        var quote = f.Quote ?? (start < end ? text[start..end] : "");

        return new WritingCoachSuggestion
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            AttemptId = attemptId,
            SuggestionType = category,
            OriginalText = quote,
            SuggestedText = f.FixSuggestion ?? "",
            Explanation = ComposeExplanation(f.RuleId, f.Severity.ToString().ToLowerInvariant(), f.Message, rationale: null),
            StartOffset = start,
            EndOffset = end,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// The persisted <c>SuggestionType</c> column is constrained to 32 chars
    /// and used as the public-facing category. Rule findings don't carry a
    /// category natively, so we project severity → a coarse bucket. This
    /// keeps the existing API contract (which expects "grammar"/"structure"/
    /// etc.) compatible with deterministic rule-engine output.
    /// </summary>
    private static string MapSeverityToCategory(string ruleId, RuleSeverity severity)
    {
        _ = ruleId;
        return severity switch
        {
            RuleSeverity.Critical => "structure",
            RuleSeverity.Major => "grammar",
            RuleSeverity.Minor => "tone",
            _ => "structure",
        };
    }

    private static string ComposeExplanation(string ruleId, string? severity, string? message, string? rationale)
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(ruleId))
        {
            parts.Add(string.IsNullOrWhiteSpace(severity)
                ? $"[{ruleId}]"
                : $"[{ruleId} · {severity}]");
        }
        if (!string.IsNullOrWhiteSpace(message)) parts.Add(message!.Trim());
        if (!string.IsNullOrWhiteSpace(rationale)) parts.Add(rationale!.Trim());

        var text = string.Join(" ", parts);
        return text.Length <= 512 ? text : text[..512];
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return RulebookProfessionParser.TryParse(raw.Trim(), out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    // ------------------------------------------------------------------
    // AI response parser. Permissive of either {suggestions:[…]} or
    // {findings:[…]} payload shapes — the gateway's Coach prompt template
    // currently emits "findings" but the W3A contract calls for
    // "suggestions". Tolerating both means a future prompt template change
    // won't silently break the coach.
    // ------------------------------------------------------------------

    private readonly record struct ParsedAiSuggestion(
        string RuleId,
        string Category,
        string Severity,
        string OriginalText,
        string SuggestedText,
        string Message,
        string Rationale,
        int StartOffset,
        int EndOffset);

    private static IEnumerable<ParsedAiSuggestion> ParseAiSuggestions(string completion, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(completion)) yield break;

        var json = ExtractJsonBlock(completion);
        if (json is null) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) yield break;

            JsonElement arr;
            if (!(TryGetArray(doc.RootElement, "suggestions", out arr)
                  || TryGetArray(doc.RootElement, "findings", out arr)))
            {
                yield break;
            }

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var ruleId = GetString(item, "ruleId") ?? GetString(item, "rule_id") ?? "";
                if (string.IsNullOrWhiteSpace(ruleId)) continue;

                var category = NormaliseCategory(GetString(item, "category") ?? GuessCategoryFromSeverity(GetString(item, "severity")));
                var severity = (GetString(item, "severity") ?? "minor").Trim().ToLowerInvariant();
                var message = (GetString(item, "message") ?? "").Trim();
                var rationale = (GetString(item, "rationale") ?? "").Trim();

                var snippet = GetString(item, "snippet")
                    ?? GetString(item, "quote")
                    ?? GetString(item, "originalText")
                    ?? "";
                var replacement = GetString(item, "suggestedReplacement")
                    ?? GetString(item, "suggestedText")
                    ?? GetString(item, "fixSuggestion")
                    ?? "";

                var (start, end) = ResolveAnchor(item, snippet, sourceText);
                if (string.IsNullOrEmpty(snippet) && start < end)
                {
                    snippet = sourceText[start..end];
                }

                yield return new ParsedAiSuggestion(
                    RuleId: ruleId.Trim(),
                    Category: category,
                    Severity: severity,
                    OriginalText: snippet,
                    SuggestedText: replacement,
                    Message: message,
                    Rationale: rationale,
                    StartOffset: start,
                    EndOffset: end);
            }
        }
    }

    private static (int start, int end) ResolveAnchor(JsonElement item, string snippet, string sourceText)
    {
        if (item.TryGetProperty("anchor", out var anchor) && anchor.ValueKind == JsonValueKind.Object)
        {
            var s = TryReadInt(anchor, "start");
            var e = TryReadInt(anchor, "end");
            if (s is not null && e is not null && s.Value >= 0 && e.Value >= s.Value)
            {
                return (Math.Min(s.Value, sourceText.Length), Math.Min(e.Value, sourceText.Length));
            }
        }

        var startTop = TryReadInt(item, "start");
        var endTop = TryReadInt(item, "end");
        if (startTop is not null && endTop is not null && startTop.Value >= 0 && endTop.Value >= startTop.Value)
        {
            return (Math.Min(startTop.Value, sourceText.Length), Math.Min(endTop.Value, sourceText.Length));
        }

        if (!string.IsNullOrEmpty(snippet))
        {
            var idx = sourceText.IndexOf(snippet, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return (idx, idx + snippet.Length);
        }

        return (0, 0);
    }

    private static int? TryReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement array)
    {
        if (element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            array = v;
            return true;
        }
        array = default;
        return false;
    }

    private static string? ExtractJsonBlock(string text)
    {
        // The coach prompt asks for raw JSON, but providers occasionally wrap
        // it in ```json … ``` fences. Strip the fence, then locate the first
        // balanced top-level object.
        var stripped = text.Trim();
        if (stripped.StartsWith("```"))
        {
            var firstNewline = stripped.IndexOf('\n');
            if (firstNewline >= 0) stripped = stripped[(firstNewline + 1)..];
            var fenceEnd = stripped.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0) stripped = stripped[..fenceEnd];
            stripped = stripped.Trim();
        }

        var openIdx = stripped.IndexOf('{');
        if (openIdx < 0) return null;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = openIdx; i < stripped.Length; i++)
        {
            var ch = stripped[i];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\') { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) return stripped[openIdx..(i + 1)];
            }
        }
        return null;
    }

    private static string NormaliseCategory(string? raw)
    {
        var category = (raw ?? "").Trim().ToLowerInvariant();
        return category switch
        {
            "grammar" or "vocabulary" or "structure" or "conciseness" or "tone" or "format" => category,
            _ => "structure",
        };
    }

    private static string GuessCategoryFromSeverity(string? severity)
        => (severity ?? "").Trim().ToLowerInvariant() switch
        {
            "critical" => "structure",
            "major" => "grammar",
            _ => "tone",
        };
}

public record WritingCoachCheckRequest(
    string CurrentText,
    int? CursorPosition,
    string? LetterType = null,
    string? Profession = null,
    string? CandidateCountry = null);
