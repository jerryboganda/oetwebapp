using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening authoring service — admin-side CRUD for the 42-item question map.
//
// Until the relational ListeningPart/ListeningQuestion entities ship (Phase 2),
// the canonical home of authored Listening structure is a JSON document under
//   ContentPaper.ExtractedTextJson["listeningQuestions"]
// which is exactly what ListeningLearnerService.ExtractQuestions consumes at
// runtime to deliver the player + grader. This service is the *only* admin-side
// way to read/write that document — it deserializes the surrounding JSON object,
// rewrites the listeningQuestions key in place (preserving any other keys an
// AI-extraction or import pipeline may have set), normalizes the items into the
// runtime shape, stamps an updated timestamp on the paper and writes an audit
// event.
//
// All admin callers (ListeningAuthoringAdminEndpoints) are gated by the
// AdminContentWrite policy and the PerUserWrite rate limit upstream.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningAuthoringService
{
    Task<ListeningAuthoredQuestionList> GetStructureAsync(string paperId, CancellationToken ct);

    Task<ListeningAuthoredQuestionList> ReplaceStructureAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredQuestion> questions,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// Phase 5 tail: read paper-level extract metadata
    /// (<c>listeningExtracts</c>) — accent code, speakers, extract title /
    /// kind / audio window. Empty list when not yet authored.
    /// </summary>
    Task<IReadOnlyList<ListeningAuthoredExtract>> GetExtractsAsync(string paperId, CancellationToken ct);

    /// <summary>
    /// Phase 5 tail: replace paper-level extract metadata
    /// (<c>listeningExtracts</c>) atomically. Preserves sibling JSON keys
    /// (e.g. <c>listeningQuestions</c>, <c>listeningTranscriptSegments</c>).
    /// </summary>
    Task<IReadOnlyList<ListeningAuthoredExtract>> ReplaceExtractsAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredExtract> extracts,
        string adminId,
        CancellationToken ct);
}

/// <summary>Server-side admin DTO for an authored Listening item. Carries the
/// correct answer + accepted synonyms, so it MUST NOT be returned by any
/// learner-facing endpoint.</summary>
public sealed record ListeningAuthoredQuestion(
    string Id,
    int Number,
    string PartCode,           // A1 | A2 | B | C1 | C2  (also accepts legacy A / C)
    string Type,               // "short_answer" | "multiple_choice_3"
    string Stem,
    IReadOnlyList<string>? Options,
    string CorrectAnswer,
    IReadOnlyList<string>? AcceptedAnswers,
    string? Explanation,
    string? SkillTag,
    string? TranscriptExcerpt,
    string? DistractorExplanation,
    int Points,
    // Phase 4: per-option (Part B/C) "why wrong" + distractor category enum
    // (too_strong | too_weak | wrong_speaker | opposite_meaning | reused_keyword).
    IReadOnlyList<string?>? OptionDistractorWhy = null,
    IReadOnlyList<string?>? OptionDistractorCategory = null,
    // Phase 4: speaker attitude tag for Part C
    // (concerned | optimistic | doubtful | critical | neutral | other).
    string? SpeakerAttitude = null,
    // Phase 5: time-coded transcript evidence (start/end ms in section audio).
    int? TranscriptEvidenceStartMs = null,
    int? TranscriptEvidenceEndMs = null);

public sealed record ListeningAuthoredQuestionList(
    IReadOnlyList<ListeningAuthoredQuestion> Questions,
    ListeningValidationCounts Counts);

/// <summary>
/// Phase 5 tail: paper-level extract metadata. One row per extract
/// (A1, A2, B clip, C1, C2). Drives accent / speakers / audio-window UI
/// surfaces in the player + review.
/// </summary>
public sealed record ListeningAuthoredExtract(
    string PartCode,                                   // A1 | A2 | B | C1 | C2
    int DisplayOrder,
    string Kind,                                       // consultation | workplace | presentation
    string Title,
    string? AccentCode,                                // e.g. en-GB | en-AU | en-IE | en-US
    IReadOnlyList<ListeningAuthoredSpeaker> Speakers,
    int? AudioStartMs,
    int? AudioEndMs);

public sealed record ListeningAuthoredSpeaker(
    string Id,
    string Role,
    string? Gender,                                    // m | f | nb | null
    string? Accent);

public sealed class ListeningAuthoringService(LearnerDbContext db) : IListeningAuthoringService
{
    private const string QuestionsKey = "listeningQuestions";

    public async Task<ListeningAuthoredQuestionList> GetStructureAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        var raw = ReadQuestionsArray(paper.ExtractedTextJson);
        var items = raw.Select(NormalizeFromStorage).ToList();
        return new ListeningAuthoredQuestionList(items, Tally(items));
    }

    public async Task<ListeningAuthoredQuestionList> ReplaceStructureAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredQuestion> questions,
        string adminId,
        CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        // Deserialize whatever ExtractedTextJson currently holds so we don't
        // clobber sibling keys (e.g. an AI-extraction pipeline may have stored
        // metadata, transcripts, audio offsets etc. alongside listeningQuestions).
        Dictionary<string, JsonElement> root;
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson))
        {
            root = new Dictionary<string, JsonElement>();
        }
        else
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException)
            {
                // Existing payload is corrupt — start clean rather than refuse the save.
                root = new Dictionary<string, JsonElement>();
            }
        }

        var normalized = questions
            .Select(NormalizeForStorage)
            .OrderBy(q => q.Number)
            .ToList();

        var serialized = JsonSerializer.SerializeToElement(normalized);
        root[QuestionsKey] = serialized;

        paper.ExtractedTextJson = JsonSerializer.Serialize(root);
        paper.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = adminId,
            ActorName = adminId,
            Action = "ListeningStructureUpdated",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            Details = JsonSerializer.Serialize(new
            {
                count = normalized.Count,
                partA = normalized.Count(q => NormalizePartCode(q.PartCode).StartsWith('A')),
                partB = normalized.Count(q => NormalizePartCode(q.PartCode).StartsWith('B')),
                partC = normalized.Count(q => NormalizePartCode(q.PartCode).StartsWith('C')),
            }),
        });

        await db.SaveChangesAsync(ct);

        return new ListeningAuthoredQuestionList(normalized, Tally(normalized));
    }

    // ── Phase 5 tail: extract metadata (accent + speakers) ──────────────

    private const string ExtractsKey = "listeningExtracts";

    public async Task<IReadOnlyList<ListeningAuthoredExtract>> GetExtractsAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        return ReadExtractsArray(paper.ExtractedTextJson)
            .Select((e, i) => NormalizeExtractFromStorage(e, i))
            .OrderBy(e => PartCodeOrder(e.PartCode))
            .ThenBy(e => e.DisplayOrder)
            .ToList();
    }

    public async Task<IReadOnlyList<ListeningAuthoredExtract>> ReplaceExtractsAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredExtract> extracts,
        string adminId,
        CancellationToken ct)
    {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        Dictionary<string, JsonElement> root;
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson))
        {
            root = new Dictionary<string, JsonElement>();
        }
        else
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException)
            {
                root = new Dictionary<string, JsonElement>();
            }
        }

        var normalized = extracts
            .Select(NormalizeExtractForStorage)
            .OrderBy(e => PartCodeOrder(e.PartCode))
            .ThenBy(e => e.DisplayOrder)
            .ToList();
        var duplicateParts = normalized
            .GroupBy(e => e.PartCode, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateParts.Count > 0)
        {
            throw ApiException.Validation(
                "listening_extract_duplicate_part",
                $"Listening extract metadata must contain one row per part. Duplicate parts: {string.Join(", ", duplicateParts)}.");
        }

        // Persist with camelCase JSON property names so the read-back path
        // (which inspects keys like "partCode", "accentCode", etc.) sees the
        // same shape an external author or AI extractor would write.
        var camelOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var serialized = JsonSerializer.SerializeToElement(normalized, camelOptions);
        root[ExtractsKey] = serialized;

        paper.ExtractedTextJson = JsonSerializer.Serialize(root);
        paper.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = adminId,
            ActorName = adminId,
            Action = "ListeningExtractsUpdated",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            Details = JsonSerializer.Serialize(new
            {
                count = normalized.Count,
                accents = normalized.Where(e => !string.IsNullOrWhiteSpace(e.AccentCode))
                                    .Select(e => e.AccentCode!).Distinct().ToList(),
            }),
        });

        await db.SaveChangesAsync(ct);
        return normalized;
    }

    private static List<Dictionary<string, object?>> ReadExtractsArray(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(extractedTextJson);
            var raw = root?.GetValueOrDefault(ExtractsKey);
            if (raw is null) return [];
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ListeningAuthoredExtract NormalizeExtractFromStorage(Dictionary<string, object?> e, int index)
    {
        string? Read(string key) => e.GetValueOrDefault(key)?.ToString();
        int? ReadInt(string key)
        {
            var s = Read(key);
            return int.TryParse(s, out var v) && v >= 0 ? v : (int?)null;
        }

        var partCode = NormalizeExtractPartCode(Read("partCode")) ?? "A1";
        var displayOrder = ReadInt("displayOrder") ?? index;
        var kind = NormalizeExtractKind(Read("kind"), partCode);

        var speakersRaw = e.GetValueOrDefault("speakers");
        var speakers = ParseAuthoredSpeakers(speakersRaw);

        return new ListeningAuthoredExtract(
            PartCode: partCode,
            DisplayOrder: displayOrder,
            Kind: kind,
            Title: Read("title") ?? $"Extract {index + 1}",
            AccentCode: Read("accentCode"),
            Speakers: speakers,
            AudioStartMs: ReadInt("audioStartMs"),
            AudioEndMs: ReadInt("audioEndMs"));
    }

    private static ListeningAuthoredExtract NormalizeExtractForStorage(ListeningAuthoredExtract e)
    {
        var partCode = NormalizeExtractPartCode(e.PartCode) ?? "A1";
        var kind = NormalizeExtractKind(e.Kind, partCode);
        var displayOrder = Math.Max(0, e.DisplayOrder);
        var title = (e.Title ?? string.Empty).Trim();
        if (title.Length == 0) title = $"{partCode} extract";
        var accentCode = string.IsNullOrWhiteSpace(e.AccentCode) ? null : e.AccentCode.Trim();

        var speakers = (e.Speakers ?? [])
            .Select((s, idx) => new ListeningAuthoredSpeaker(
                Id: string.IsNullOrWhiteSpace(s.Id) ? $"s{idx + 1}" : s.Id.Trim(),
                Role: string.IsNullOrWhiteSpace(s.Role) ? "speaker" : s.Role.Trim(),
                Gender: NormalizeSpeakerGender(s.Gender),
                Accent: string.IsNullOrWhiteSpace(s.Accent) ? null : s.Accent.Trim()))
            .ToList();

        var audioStart = e.AudioStartMs is int sv && sv >= 0 ? sv : (int?)null;
        var audioEnd = e.AudioEndMs is int ev && ev >= 0 ? ev : (int?)null;
        if (audioStart is int s && audioEnd is int en && en < s)
        {
            audioEnd = null;
        }

        return new ListeningAuthoredExtract(
            PartCode: partCode,
            DisplayOrder: displayOrder,
            Kind: kind,
            Title: title,
            AccentCode: accentCode,
            Speakers: speakers,
            AudioStartMs: audioStart,
            AudioEndMs: audioEnd);
    }

    private static IReadOnlyList<ListeningAuthoredSpeaker> ParseAuthoredSpeakers(object? raw)
    {
        if (raw is null) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw));
            if (list is null) return [];
            var output = new List<ListeningAuthoredSpeaker>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                output.Add(new ListeningAuthoredSpeaker(
                    Id: s.GetValueOrDefault("id")?.ToString() ?? $"s{i + 1}",
                    Role: s.GetValueOrDefault("role")?.ToString() ?? "speaker",
                    Gender: NormalizeSpeakerGender(s.GetValueOrDefault("gender")?.ToString()),
                    Accent: s.GetValueOrDefault("accent")?.ToString()));
            }
            return output;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeSpeakerGender(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw.Trim().ToLowerInvariant();
        return n is "m" or "f" or "nb" ? n : null;
    }

    private static string NormalizeExtractKind(string? raw, string partCode)
    {
        var n = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (n is "consultation" or "workplace" or "presentation") return n;
        return partCode switch
        {
            "B" => "workplace",
            "C1" or "C2" => "presentation",
            _ => "consultation",
        };
    }

    private static string? NormalizeExtractPartCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw.Trim().ToUpperInvariant();
        return n switch
        {
            "A1" or "A2" or "B" or "C1" or "C2" => n,
            "A" => "A1",
            "C" => "C1",
            _ => null,
        };
    }

    private static int PartCodeOrder(string partCode) => partCode switch
    {
        "A1" => 1,
        "A2" => 2,
        "B" => 3,
        "C1" => 4,
        "C2" => 5,
        _ => 99,
    };

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> ReadQuestionsArray(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(extractedTextJson);
            var raw = root?.GetValueOrDefault(QuestionsKey);
            if (raw is null) return [];
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ListeningAuthoredQuestion NormalizeFromStorage(Dictionary<string, object?> q)
    {
        string? Read(string key) => q.GetValueOrDefault(key)?.ToString();
        IReadOnlyList<string> ReadList(string key)
        {
            var raw = q.GetValueOrDefault(key);
            if (raw is null) return [];
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(raw));
                return list ?? [];
            }
            catch (JsonException) { return []; }
        }
        IReadOnlyList<string?> ReadNullableList(string key)
        {
            var raw = q.GetValueOrDefault(key);
            if (raw is null) return [];
            try
            {
                var list = JsonSerializer.Deserialize<List<string?>>(JsonSerializer.Serialize(raw));
                return list ?? [];
            }
            catch (JsonException) { return []; }
        }
        int? ReadInt(string key)
        {
            var s = Read(key);
            return int.TryParse(s, out var v) ? v : (int?)null;
        }

        var number = int.TryParse(Read("number"), out var n) ? n : 0;
        var points = int.TryParse(Read("points"), out var p) ? Math.Max(1, p) : 1;

        return new ListeningAuthoredQuestion(
            Id: Read("id") ?? $"lq-{number}",
            Number: number,
            PartCode: NormalizePartCode(Read("partCode") ?? Read("part") ?? "A"),
            Type: Read("type") ?? Read("questionType") ?? "short_answer",
            Stem: Read("text") ?? Read("stem") ?? string.Empty,
            Options: ReadList("options"),
            CorrectAnswer: Read("correctAnswer") ?? Read("answer") ?? string.Empty,
            AcceptedAnswers: ReadList("acceptedAnswers"),
            Explanation: Read("explanation"),
            SkillTag: Read("skillTag"),
            TranscriptExcerpt: Read("transcriptExcerpt"),
            DistractorExplanation: Read("distractorExplanation"),
            Points: points,
            OptionDistractorWhy: ReadNullableList("optionDistractorWhy"),
            OptionDistractorCategory: ReadNullableList("optionDistractorCategory"),
            SpeakerAttitude: Read("speakerAttitude"),
            TranscriptEvidenceStartMs: ReadInt("transcriptEvidenceStartMs"),
            TranscriptEvidenceEndMs: ReadInt("transcriptEvidenceEndMs"));
    }

    private static ListeningAuthoredQuestion NormalizeForStorage(ListeningAuthoredQuestion q)
    {
        var partCode = NormalizePartCode(q.PartCode);
        var type = string.IsNullOrWhiteSpace(q.Type)
            ? (partCode.StartsWith('A') ? "short_answer" : "multiple_choice_3")
            : q.Type.Trim();

        // Part B/C are MCQ-3; force exactly 3 options if any were provided.
        var options = (q.Options ?? []).Select(o => o ?? string.Empty).ToList();
        if (type == "multiple_choice_3" && options.Count > 3) options = options.Take(3).ToList();

        var accepted = (q.AcceptedAnswers ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return q with
        {
            Id = string.IsNullOrWhiteSpace(q.Id) ? $"lq-{q.Number}" : q.Id.Trim(),
            PartCode = partCode,
            Type = type,
            Stem = (q.Stem ?? string.Empty).Trim(),
            Options = options,
            CorrectAnswer = (q.CorrectAnswer ?? string.Empty).Trim(),
            AcceptedAnswers = accepted,
            Explanation = string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation.Trim(),
            SkillTag = string.IsNullOrWhiteSpace(q.SkillTag) ? null : q.SkillTag.Trim(),
            TranscriptExcerpt = string.IsNullOrWhiteSpace(q.TranscriptExcerpt) ? null : q.TranscriptExcerpt.Trim(),
            DistractorExplanation = string.IsNullOrWhiteSpace(q.DistractorExplanation) ? null : q.DistractorExplanation.Trim(),
            Points = Math.Max(1, q.Points),
            OptionDistractorWhy = NormalizeNullableStringList(q.OptionDistractorWhy, options.Count),
            OptionDistractorCategory = NormalizeDistractorCategoryList(q.OptionDistractorCategory, options.Count),
            SpeakerAttitude = NormalizeSpeakerAttitude(q.SpeakerAttitude),
            TranscriptEvidenceStartMs = q.TranscriptEvidenceStartMs is int s && s >= 0 ? s : null,
            TranscriptEvidenceEndMs = q.TranscriptEvidenceEndMs is int e && e >= 0 ? e : null,
        };
    }

    private static readonly HashSet<string> AllowedDistractorCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "too_strong", "too_weak", "wrong_speaker", "opposite_meaning", "reused_keyword",
    };

    private static readonly HashSet<string> AllowedSpeakerAttitudes = new(StringComparer.OrdinalIgnoreCase)
    {
        "concerned", "optimistic", "doubtful", "critical", "neutral", "other",
    };

    private static IReadOnlyList<string?> NormalizeNullableStringList(IReadOnlyList<string?>? list, int targetLength)
    {
        if (list is null || list.Count == 0) return [];
        var result = new List<string?>(targetLength);
        for (var i = 0; i < targetLength; i++)
        {
            var v = i < list.Count ? list[i] : null;
            result.Add(string.IsNullOrWhiteSpace(v) ? null : v!.Trim());
        }
        return result;
    }

    private static IReadOnlyList<string?> NormalizeDistractorCategoryList(IReadOnlyList<string?>? list, int targetLength)
    {
        if (list is null || list.Count == 0) return [];
        var result = new List<string?>(targetLength);
        for (var i = 0; i < targetLength; i++)
        {
            var v = i < list.Count ? list[i] : null;
            if (string.IsNullOrWhiteSpace(v)) { result.Add(null); continue; }
            var normalized = v!.Trim().ToLowerInvariant();
            result.Add(AllowedDistractorCategories.Contains(normalized) ? normalized : null);
        }
        return result;
    }

    private static string? NormalizeSpeakerAttitude(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLowerInvariant();
        return AllowedSpeakerAttitudes.Contains(normalized) ? normalized : null;
    }

    private static string NormalizePartCode(string raw)
    {
        var v = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return v switch
        {
            "A" or "A1" or "A2" or "B" or "C" or "C1" or "C2" => v,
            _ => "A",
        };
    }

    private static ListeningValidationCounts Tally(IReadOnlyList<ListeningAuthoredQuestion> items)
    {
        var a = items.Count(q => q.PartCode.StartsWith('A'));
        var b = items.Count(q => q.PartCode.StartsWith('B'));
        var c = items.Count(q => q.PartCode.StartsWith('C'));
        return new ListeningValidationCounts(a, b, c, a + b + c);
    }
}
