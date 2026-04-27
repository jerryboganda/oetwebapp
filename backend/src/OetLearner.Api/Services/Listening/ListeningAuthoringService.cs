using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

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
    int Points);

public sealed record ListeningAuthoredQuestionList(
    IReadOnlyList<ListeningAuthoredQuestion> Questions,
    ListeningValidationCounts Counts);

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
            Points: points);
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
        };
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
