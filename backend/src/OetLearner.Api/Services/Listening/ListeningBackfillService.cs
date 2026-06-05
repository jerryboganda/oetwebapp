using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening backfill service — Phase 2 follow-up.
//
// Reads the JSON-blob authored shape stored under
//   ContentPaper.ExtractedTextJson["listeningQuestions"]
//   ContentPaper.ExtractedTextJson["listeningExtracts"]
//   ContentPaper.ExtractedTextJson["listeningTranscriptSegments"]
//
// and projects it into the relational entities introduced in Phase 2:
//   ListeningPart (5 codes)
//   └── ListeningExtract (one row per extract; carries accent + speakers
//                         + audio window + per-extract transcript segments)
//         └── ListeningQuestion
//               └── ListeningQuestionOption (Part B / C only)
//
// Idempotent: each run wipes existing relational rows for the paper inside a
// transaction and rebuilds from JSON. The JSON blob remains the live runtime
// source of truth for grading until a separate "switch reads to relational"
// slice ships — this service exists so Ops can pre-populate the relational
// tables ahead of that switch.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningBackfillService
{
    Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, CancellationToken ct);

    /// <summary>
    /// Overload that allows a system_admin caller to bypass the attempts guard.
    /// H3: the endpoint enforces system_admin before passing <c>true</c>.
    /// </summary>
    Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, bool bypassAttemptsGuard, CancellationToken ct);

    Task<IReadOnlyList<ListeningBackfillReport>> BackfillAllAsync(string adminId, CancellationToken ct);
}

public sealed record ListeningBackfillReport(
    string PaperId,
    bool Success,
    int PartsCreated,
    int ExtractsCreated,
    int QuestionsCreated,
    int OptionsCreated,
    string? Reason);

public sealed class ListeningBackfillService(LearnerDbContext db) : IListeningBackfillService
{
    public async Task<IReadOnlyList<ListeningBackfillReport>> BackfillAllAsync(string adminId, CancellationToken ct)
    {
        var paperIds = await db.ContentPapers.AsNoTracking()
            .Where(p => p.SubtestCode == "listening")
            .Select(p => p.Id)
            .ToListAsync(ct);

        var reports = new List<ListeningBackfillReport>(paperIds.Count);
        foreach (var id in paperIds)
        {
            try
            {
                reports.Add(await BackfillPaperAsync(id, adminId, ct));
            }
            catch (Exception ex)
            {
                reports.Add(new ListeningBackfillReport(id, false, 0, 0, 0, 0, ex.Message));
            }
        }
        return reports;
    }

    public async Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, CancellationToken ct)
        => await BackfillPaperAsync(paperId, adminId, bypassAttemptsGuard: false, ct);

    public async Task<ListeningBackfillReport> BackfillPaperAsync(string paperId, string adminId, bool bypassAttemptsGuard, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        if (!string.Equals(paper.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase))
        {
            return new ListeningBackfillReport(paperId, false, 0, 0, 0, 0, "Paper is not a Listening paper.");
        }

        var (questions, extracts, transcript) = ParseAuthoredJson(paper.ExtractedTextJson);
        if (questions.Count == 0)
        {
            return new ListeningBackfillReport(paperId, false, 0, 0, 0, 0,
                "ExtractedTextJson.listeningQuestions is empty — nothing to backfill.");
        }

        var hasLearnerAttempts = await db.ListeningAttempts.AsNoTracking()
            .AnyAsync(a => a.PaperId == paperId, ct);
        if (hasLearnerAttempts && !bypassAttemptsGuard)
        {
            // Selective blocking: only refuse if the answer key would change.
            // Cosmetic edits (stem text, explanations, transcript evidence,
            // extract metadata, distractor wording) are safe to apply.
            var existingQuestionsForCheck = await db.ListeningQuestions
                .AsNoTracking()
                .Where(q => q.PaperId == paperId)
                .Include(q => q.Options)
                .ToListAsync(ct);

            if (DetectAnswerKeyChanges(existingQuestionsForCheck, questions))
            {
                return new ListeningBackfillReport(paperId, false, 0, 0, 0, 0,
                    "Cannot modify answer keys while in-flight attempts exist. " +
                    "Create a new paper revision or wait for all attempts to complete.");
            }
        }

        // Idempotent rebuild: wipe existing relational rows for this paper
        // before re-inserting. The guard above blocks answer-key changes when
        // learner attempts exist; non-destructive (cosmetic) changes proceed
        // through the same wipe-and-rebuild path safely.
        var now = DateTimeOffset.UtcNow;

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var existingPartIds = await db.ListeningParts
            .Where(p => p.PaperId == paperId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (existingPartIds.Count > 0)
        {
            var existingQuestions = await db.ListeningQuestions
                .Where(q => q.PaperId == paperId)
                .ToListAsync(ct);
            var existingQuestionIds = existingQuestions.Select(q => q.Id).ToList();

            var existingOptions = await db.ListeningQuestionOptions
                .Where(o => existingQuestionIds.Contains(o.ListeningQuestionId))
                .ToListAsync(ct);
            db.ListeningQuestionOptions.RemoveRange(existingOptions);

            db.ListeningQuestions.RemoveRange(existingQuestions);

            var existingExtracts = await db.ListeningExtracts
                .Where(e => existingPartIds.Contains(e.ListeningPartId))
                .ToListAsync(ct);
            db.ListeningExtracts.RemoveRange(existingExtracts);

            var existingParts = await db.ListeningParts
                .Where(p => p.PaperId == paperId)
                .ToListAsync(ct);
            db.ListeningParts.RemoveRange(existingParts);

            await db.SaveChangesAsync(ct);
        }

        // Legacy split: papers authored before the Part B sub-section split
        // carry all six Part B items under the single "B" part code. Distribute
        // them to B1..B6 by question-number order so the projection produces six
        // independent sub-sections (each its own extract + timer + question),
        // matching the post-migration relational shape. Items already coded
        // B1..B6 are left untouched.
        questions = SplitLegacyPartB(questions);

        // Build parts (one per partCode that has at least one question)
        var grouped = questions
            .GroupBy(q => NormalizePartCodeEnum(q.PartCode))
            .OrderBy(g => g.Key)
            .ToList();

        var partRows = new List<ListeningPart>();
        var partIdByCode = new Dictionary<ListeningPartCode, string>();
        foreach (var g in grouped)
        {
            var partId = $"lp_{Guid.NewGuid():N}";
            partIdByCode[g.Key] = partId;
            partRows.Add(new ListeningPart
            {
                Id = partId,
                PaperId = paperId,
                PartCode = g.Key,
                MaxRawScore = g.Sum(q => Math.Max(1, q.Points)),
                Instructions = null,
                TimeLimitSeconds = extracts
                    .FirstOrDefault(e => NormalizePartCodeEnum(e.PartCode) == g.Key)?.TimeLimitSeconds,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        db.ListeningParts.AddRange(partRows);

        // Build extracts. Phase 5-tail metadata (accent + speakers + audio
        // window + extract title) is keyed by partCode. We default to one
        // extract per part when no metadata is authored, matching the
        // canonical OET shape (A1, A2, B [single workplace block in JSON],
        // C1, C2). Sentence-level transcript segments are partitioned by
        // partCode where authored.
        var extractRowByPart = new Dictionary<ListeningPartCode, string>();
        var extractCount = 0;
        foreach (var partCode in partIdByCode.Keys)
        {
            var meta = extracts.FirstOrDefault(e =>
                NormalizePartCodeEnum(e.PartCode) == partCode);
            var partSegments = transcript
                .Where(s => string.IsNullOrEmpty(s.PartCode)
                            || NormalizePartCodeEnum(s.PartCode!) == partCode)
                .Select(s => new
                {
                    startMs = s.StartMs,
                    endMs = s.EndMs,
                    speakerId = s.SpeakerId,
                    text = s.Text,
                })
                .ToList();

            var extractId = $"le_{Guid.NewGuid():N}";
            extractRowByPart[partCode] = extractId;
            db.ListeningExtracts.Add(new ListeningExtract
            {
                Id = extractId,
                ListeningPartId = partIdByCode[partCode],
                DisplayOrder = (int)partCode,
                Kind = ResolveExtractKind(partCode, meta?.Kind),
                Title = meta?.Title ?? DefaultExtractTitle(partCode),
                AccentCode = meta?.AccentCode,
                SpeakersJson = JsonSerializer.Serialize(meta?.Speakers ?? new List<JsonElement>()),
                AudioStartMs = meta?.AudioStartMs,
                AudioEndMs = meta?.AudioEndMs,
                ReplayInLearningOnly = true,
                TranscriptSegmentsJson = JsonSerializer.Serialize(partSegments),
                CreatedAt = now,
                UpdatedAt = now,
            });
            extractCount++;
        }

        var optionsCreated = 0;
        var questionRows = new List<ListeningQuestion>(questions.Count);
        foreach (var q in questions)
        {
            var partCode = NormalizePartCodeEnum(q.PartCode);
            var partId = partIdByCode[partCode];
            var extractId = extractRowByPart[partCode];

            var canonicalAnswerJson = JsonSerializer.Serialize(q.CorrectAnswer ?? string.Empty);
            string? acceptedJson = q.AcceptedAnswers is { Count: > 0 }
                ? JsonSerializer.Serialize(q.AcceptedAnswers)
                : null;

            var qid = $"lq_{Guid.NewGuid():N}";
            questionRows.Add(new ListeningQuestion
            {
                Id = qid,
                PaperId = paperId,
                ListeningPartId = partId,
                ListeningExtractId = extractId,
                QuestionNumber = q.Number,
                DisplayOrder = q.Number,
                Points = Math.Max(1, q.Points),
                QuestionType = q.Type switch
                {
                    "multiple_choice_3" => ListeningQuestionType.MultipleChoice3,
                    "fill_in_blank" => ListeningQuestionType.FillInBlank,
                    _ => ListeningQuestionType.ShortAnswer,
                },
                Stem = q.Stem,
                CorrectAnswerJson = canonicalAnswerJson,
                AcceptedSynonymsJson = acceptedJson,
                CaseSensitive = false,
                ExplanationMarkdown = q.Explanation,
                SkillTag = q.SkillTag,
                TranscriptEvidenceText = q.TranscriptExcerpt,
                TranscriptEvidenceStartMs = q.TranscriptEvidenceStartMs,
                TranscriptEvidenceEndMs = q.TranscriptEvidenceEndMs,
                SpeakerAttitude = ParseSpeakerAttitude(q.SpeakerAttitude),
                CreatedAt = now,
                UpdatedAt = now,
            });

            // Options exist for Part B/C MCQ items.
            if (q.Type == "multiple_choice_3" && q.Options is { Count: > 0 })
            {
                var optionLabels = new[] { "A", "B", "C" };
                for (var i = 0; i < q.Options.Count && i < 3; i++)
                {
                    var optionKey = optionLabels[i];
                    var optionText = q.Options[i] ?? string.Empty;
                    var isCorrect = string.Equals(
                        q.CorrectAnswer?.Trim(),
                        optionText.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                        || string.Equals(q.CorrectAnswer?.Trim(), optionKey, StringComparison.OrdinalIgnoreCase);
                    db.ListeningQuestionOptions.Add(new ListeningQuestionOption
                    {
                        Id = $"lo_{Guid.NewGuid():N}",
                        ListeningQuestionId = qid,
                        OptionKey = optionKey,
                        DisplayOrder = i,
                        Text = optionText,
                        IsCorrect = isCorrect,
                        DistractorCategory = ParseDistractorCategoryAt(q.OptionDistractorCategory, i),
                        WhyWrongMarkdown = ReadAt(q.OptionDistractorWhy, i),
                    });
                    optionsCreated++;
                }
            }
        }
        db.ListeningQuestions.AddRange(questionRows);

        // Resolve the audit FK once for both events below: ActorAuthAccountId is
        // FK'd to ApplicationUserAccounts.Id, so a caller id with no account row
        // (dev-auth header, seeded/legacy admin) must be stored as null to avoid
        // a SaveChanges 500. The raw id is still kept in ActorId for traceability.
        var actorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct);

        // Audit
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorAuthAccountId = actorAuthAccountId,
            ActorName = adminId,
            Action = "ListeningRelationalBackfill",
            ResourceType = "ContentPaper",
            ResourceId = paperId,
            Details = JsonSerializer.Serialize(new
            {
                parts = partRows.Count,
                extracts = extractCount,
                questions = questionRows.Count,
                options = optionsCreated,
            }),
        });

        if (hasLearnerAttempts)
        {
            var attemptCount = await db.ListeningAttempts.AsNoTracking()
                .CountAsync(a => a.PaperId == paperId, ct);
            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"audit_{Guid.NewGuid():N}",
                OccurredAt = now,
                ActorId = adminId,
                ActorAuthAccountId = actorAuthAccountId,
                ActorName = "ListeningBackfillService",
                Action = "listening.backfill.non_destructive_update",
                ResourceType = "ContentPaper",
                ResourceId = paperId,
                Details = $"Non-destructive update applied with {attemptCount} existing attempts",
            });
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return new ListeningBackfillReport(
            PaperId: paperId,
            Success: true,
            PartsCreated: partRows.Count,
            ExtractsCreated: extractCount,
            QuestionsCreated: questionRows.Count,
            OptionsCreated: optionsCreated,
            Reason: null);
    }

    // ── JSON parsing ────────────────────────────────────────────────────

    private static (List<AuthoredQuestion> questions, List<AuthoredExtract> extracts, List<AuthoredSegment> transcript)
        ParseAuthoredJson(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson))
            return ([], [], []);

        Dictionary<string, JsonElement> root;
        try
        {
            root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extractedTextJson)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException) { return ([], [], []); }

        var questions = ParseQuestionsArray(root.GetValueOrDefault("listeningQuestions"));
        var extracts = ParseExtractsArray(root.GetValueOrDefault("listeningExtracts"));
        var transcript = ParseTranscriptArray(root.GetValueOrDefault("listeningTranscriptSegments"));
        return (questions, extracts, transcript);
    }

    private static List<AuthoredQuestion> ParseQuestionsArray(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array) return [];
        var output = new List<AuthoredQuestion>(raw.GetArrayLength());
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            // Preserve a bare legacy "B" here (do NOT floor it to B1 yet) so
            // SplitLegacyPartB can distribute the six items across B1..B6. Any
            // other code is normalized to its canonical form.
            var partCode = NormalizePartCodeRaw(GetString(item, "partCode") ?? GetString(item, "part") ?? "A");
            var type = GetString(item, "type") ?? GetString(item, "questionType") ?? "short_answer";
            var stem = GetString(item, "text") ?? GetString(item, "stem") ?? string.Empty;
            var correct = GetString(item, "correctAnswer") ?? GetString(item, "answer") ?? string.Empty;
            var number = GetInt(item, "number") ?? 0;
            var points = GetInt(item, "points") ?? 1;

            var options = item.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array
                ? optsEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
                : new List<string>();
            var accepted = item.TryGetProperty("acceptedAnswers", out var accEl) && accEl.ValueKind == JsonValueKind.Array
                ? accEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList()
                : new List<string>();
            var optionDistractorCategory = item.TryGetProperty("optionDistractorCategory", out var odcEl) && odcEl.ValueKind == JsonValueKind.Array
                ? odcEl.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToList()
                : new List<string?>();
            var optionDistractorWhy = item.TryGetProperty("optionDistractorWhy", out var odwEl) && odwEl.ValueKind == JsonValueKind.Array
                ? odwEl.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToList()
                : new List<string?>();

            output.Add(new AuthoredQuestion(
                Number: number,
                PartCode: partCode,
                Type: type,
                Stem: stem,
                Options: options,
                CorrectAnswer: correct,
                AcceptedAnswers: accepted,
                Explanation: GetString(item, "explanation"),
                SkillTag: GetString(item, "skillTag"),
                TranscriptExcerpt: GetString(item, "transcriptExcerpt"),
                Points: Math.Max(1, points),
                OptionDistractorCategory: optionDistractorCategory,
                OptionDistractorWhy: optionDistractorWhy,
                SpeakerAttitude: GetString(item, "speakerAttitude"),
                TranscriptEvidenceStartMs: GetInt(item, "transcriptEvidenceStartMs"),
                TranscriptEvidenceEndMs: GetInt(item, "transcriptEvidenceEndMs")));
        }
        return output;
    }

    private static List<AuthoredExtract> ParseExtractsArray(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array) return [];
        var output = new List<AuthoredExtract>(raw.GetArrayLength());
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var speakers = item.TryGetProperty("speakers", out var spEl) && spEl.ValueKind == JsonValueKind.Array
                ? spEl.EnumerateArray().Select(e => e.Clone()).ToList()
                : new List<JsonElement>();
            output.Add(new AuthoredExtract(
                PartCode: NormalizePartCodeString(GetString(item, "partCode") ?? "A1"),
                Kind: GetString(item, "kind"),
                Title: GetString(item, "title"),
                AccentCode: GetString(item, "accentCode"),
                AudioStartMs: GetInt(item, "audioStartMs"),
                AudioEndMs: GetInt(item, "audioEndMs"),
                Speakers: speakers,
                TimeLimitSeconds: GetInt(item, "timeLimitSeconds")));
        }
        return output;
    }

    private static List<AuthoredSegment> ParseTranscriptArray(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array) return [];
        var output = new List<AuthoredSegment>(raw.GetArrayLength());
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var startMs = GetInt(item, "startMs") ?? -1;
            var endMs = GetInt(item, "endMs") ?? -1;
            var text = GetString(item, "text") ?? string.Empty;
            if (startMs < 0 || endMs < startMs || string.IsNullOrWhiteSpace(text)) continue;
            output.Add(new AuthoredSegment(
                StartMs: startMs,
                EndMs: endMs,
                PartCode: GetString(item, "partCode"),
                SpeakerId: GetString(item, "speakerId"),
                Text: text));
        }
        return output;
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : (int?)null,
            JsonValueKind.String => int.TryParse(v.GetString(), out var i) ? i : (int?)null,
            _ => null,
        };
    }

    private static bool IsBareLegacyB(string? raw)
        => string.Equals((raw ?? string.Empty).Trim(), "B", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Distribute legacy single-"B" Part B items across B1..B6 by question
    /// number order. Items already coded B1..B6 (or any non-B code) pass
    /// through unchanged. Numbers are unique per the contiguous-1..42 rule, so
    /// keying the assignment by question number is safe.
    /// </summary>
    private static List<AuthoredQuestion> SplitLegacyPartB(List<AuthoredQuestion> questions)
    {
        var bareB = questions.Where(q => IsBareLegacyB(q.PartCode)).OrderBy(q => q.Number).ToList();
        if (bareB.Count == 0) return questions;

        var assignment = new Dictionary<int, string>(bareB.Count);
        for (var i = 0; i < bareB.Count; i++)
        {
            // Cap at B6 — well-formed Part B has exactly six items.
            assignment[bareB[i].Number] = $"B{Math.Min(i, 5) + 1}";
        }

        return questions
            .Select(q => IsBareLegacyB(q.PartCode) && assignment.TryGetValue(q.Number, out var code)
                ? q with { PartCode = code }
                : q)
            .ToList();
    }

    private static string NormalizePartCodeString(string raw)
    {
        var n = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return n switch
        {
            "A1" or "A2" or "B1" or "B2" or "B3" or "B4" or "B5" or "B6" or "C1" or "C2" => n,
            "A" => "A1",
            // Legacy bare-B floor. SplitLegacyPartB distributes the six items to
            // B1..B6 before this is reached for grouping; the floor only guards
            // any stray bare-B that slipped past (maps to the first B window).
            "B" => "B1",
            "C" => "C1",
            _ => "A1",
        };
    }

    /// <summary>Like <see cref="NormalizePartCodeString"/> but PRESERVES a bare
    /// legacy "B" so <see cref="SplitLegacyPartB"/> can distribute the six items
    /// across B1..B6 before grouping. Used for question parsing only.</summary>
    private static string NormalizePartCodeRaw(string raw)
    {
        var n = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return n switch
        {
            "A1" or "A2" or "B" or "B1" or "B2" or "B3" or "B4" or "B5" or "B6" or "C1" or "C2" => n,
            "A" => "A1",
            "C" => "C1",
            _ => "A1",
        };
    }

    private static ListeningPartCode NormalizePartCodeEnum(string raw) => NormalizePartCodeString(raw) switch
    {
        "A1" => ListeningPartCode.A1,
        "A2" => ListeningPartCode.A2,
        "B1" => ListeningPartCode.B1,
        "B2" => ListeningPartCode.B2,
        "B3" => ListeningPartCode.B3,
        "B4" => ListeningPartCode.B4,
        "B5" => ListeningPartCode.B5,
        "B6" => ListeningPartCode.B6,
        "C1" => ListeningPartCode.C1,
        "C2" => ListeningPartCode.C2,
        _ => ListeningPartCode.A1,
    };

    private static bool IsPartB(ListeningPartCode partCode) => partCode
        is ListeningPartCode.B1 or ListeningPartCode.B2 or ListeningPartCode.B3
        or ListeningPartCode.B4 or ListeningPartCode.B5 or ListeningPartCode.B6;

    private static ListeningExtractKind ResolveExtractKind(ListeningPartCode partCode, string? rawKind)
    {
        var n = (rawKind ?? string.Empty).Trim().ToLowerInvariant();
        if (n == "consultation") return ListeningExtractKind.Consultation;
        if (n == "workplace") return ListeningExtractKind.Workplace;
        if (n == "presentation") return ListeningExtractKind.Presentation;
        if (IsPartB(partCode)) return ListeningExtractKind.Workplace;
        return partCode switch
        {
            ListeningPartCode.C1 or ListeningPartCode.C2 => ListeningExtractKind.Presentation,
            _ => ListeningExtractKind.Consultation,
        };
    }

    private static string DefaultExtractTitle(ListeningPartCode partCode) => partCode switch
    {
        ListeningPartCode.A1 => "Consultation 1",
        ListeningPartCode.A2 => "Consultation 2",
        ListeningPartCode.B1 => "Workplace extract 1",
        ListeningPartCode.B2 => "Workplace extract 2",
        ListeningPartCode.B3 => "Workplace extract 3",
        ListeningPartCode.B4 => "Workplace extract 4",
        ListeningPartCode.B5 => "Workplace extract 5",
        ListeningPartCode.B6 => "Workplace extract 6",
        ListeningPartCode.C1 => "Presentation 1",
        ListeningPartCode.C2 => "Presentation 2",
        _ => "Extract",
    };

    private static ListeningSpeakerAttitude? ParseSpeakerAttitude(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "concerned" => ListeningSpeakerAttitude.Concerned,
            "optimistic" => ListeningSpeakerAttitude.Optimistic,
            "doubtful" => ListeningSpeakerAttitude.Doubtful,
            "critical" => ListeningSpeakerAttitude.Critical,
            "neutral" => ListeningSpeakerAttitude.Neutral,
            "other" => ListeningSpeakerAttitude.Other,
            _ => null,
        };
    }

    private static ListeningDistractorCategory? ParseDistractorCategoryAt(IReadOnlyList<string?>? list, int index)
    {
        if (list is null || index >= list.Count) return null;
        var raw = list[index];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "too_strong" => ListeningDistractorCategory.TooStrong,
            "too_weak" => ListeningDistractorCategory.TooWeak,
            "wrong_speaker" => ListeningDistractorCategory.WrongSpeaker,
            "opposite_meaning" => ListeningDistractorCategory.OppositeMeaning,
            "reused_keyword" => ListeningDistractorCategory.ReusedKeyword,
            "out_of_scope" => ListeningDistractorCategory.OutOfScope,
            _ => null,
        };
    }

    private static string? ReadAt(IReadOnlyList<string?>? list, int index)
    {
        if (list is null || index >= list.Count) return null;
        var raw = list[index];
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    // ── Answer-key change detection ─────────────────────────────────────
    //
    // Compares existing relational questions against the incoming authored
    // JSON. Returns true if any answer-key-affecting change is detected:
    //   • CorrectAnswer changed (Part A short-answer)
    //   • Which options are marked correct changed (Part B/C MCQ)
    //   • A question that had attempts was deleted
    //   • Question numbering was reordered (would mis-grade existing answers)
    //
    // Cosmetic fields (stem text, explanation, distractor wording, transcript
    // evidence, extract metadata) are intentionally NOT checked here — those
    // may change freely without affecting grades.

    private static bool DetectAnswerKeyChanges(
        List<ListeningQuestion> existing,
        List<AuthoredQuestion> incoming)
    {
        // Match by QuestionNumber — that's the stable authored identity.
        var existingByNumber = existing.ToDictionary(q => q.QuestionNumber);

        foreach (var incomingQ in incoming)
        {
            if (!existingByNumber.TryGetValue(incomingQ.Number, out var existingQ))
                continue; // New question being added — safe, doesn't affect existing grades.

            // Part A (short_answer): compare canonical correct answer.
            var incomingAnswerJson = JsonSerializer.Serialize(incomingQ.CorrectAnswer ?? string.Empty);
            if (!string.Equals(existingQ.CorrectAnswerJson, incomingAnswerJson, StringComparison.Ordinal))
                return true;

            // Part B/C (MCQ): compare which option keys are marked correct.
            if (existingQ.Options is { Count: > 0 })
            {
                var existingCorrectKeys = existingQ.Options
                    .Where(o => o.IsCorrect)
                    .Select(o => o.OptionKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Recompute what isCorrect would be for incoming options
                // using the same logic as the main projection loop.
                var incomingCorrectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (incomingQ.Type == "multiple_choice_3" && incomingQ.Options is { Count: > 0 })
                {
                    var optionLabels = new[] { "A", "B", "C" };
                    for (var i = 0; i < incomingQ.Options.Count && i < 3; i++)
                    {
                        var optionKey = optionLabels[i];
                        var optionText = incomingQ.Options[i] ?? string.Empty;
                        var isCorrect = string.Equals(
                            incomingQ.CorrectAnswer?.Trim(),
                            optionText.Trim(),
                            StringComparison.OrdinalIgnoreCase)
                            || string.Equals(incomingQ.CorrectAnswer?.Trim(), optionKey, StringComparison.OrdinalIgnoreCase);
                        if (isCorrect)
                            incomingCorrectKeys.Add(optionKey);
                    }
                }

                if (!existingCorrectKeys.SetEquals(incomingCorrectKeys))
                    return true;
            }
        }

        // Check for deleted questions: existing question numbers missing from incoming set.
        var incomingNumbers = incoming.Select(q => q.Number).ToHashSet();
        if (existing.Any(q => !incomingNumbers.Contains(q.QuestionNumber)))
            return true; // Deletion affects scoring.

        return false;
    }

    // ── Internal projection records ─────────────────────────────────────

    private sealed record AuthoredQuestion(
        int Number,
        string PartCode,
        string Type,
        string Stem,
        IReadOnlyList<string> Options,
        string CorrectAnswer,
        IReadOnlyList<string> AcceptedAnswers,
        string? Explanation,
        string? SkillTag,
        string? TranscriptExcerpt,
        int Points,
        IReadOnlyList<string?> OptionDistractorCategory,
        IReadOnlyList<string?> OptionDistractorWhy,
        string? SpeakerAttitude,
        int? TranscriptEvidenceStartMs,
        int? TranscriptEvidenceEndMs);

    private sealed record AuthoredExtract(
        string PartCode,
        string? Kind,
        string? Title,
        string? AccentCode,
        int? AudioStartMs,
        int? AudioEndMs,
        IReadOnlyList<JsonElement> Speakers,
        int? TimeLimitSeconds = null);

    private sealed record AuthoredSegment(
        int StartMs,
        int EndMs,
        string? PartCode,
        string? SpeakerId,
        string Text);
}
