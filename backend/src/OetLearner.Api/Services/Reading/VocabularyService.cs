using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Vocabulary Service — WS4
//
// Manages per-learner spaced-repetition vocabulary cards for the Reading
// Module pathway. SM-2 scheduling is implemented inline (simplified variant
// matching the spec). AI-generated word cards are produced via the grounded
// AI gateway (Vocabulary rulebook, GenerateVocabularyGloss task) so every
// generated word card inherits the platform's standard guardrails.
//
// Separate from the legacy VocabularyService (Services/VocabularyService.cs)
// which manages VocabularyTerm entities — this service manages the newer
// VocabularyWord + LearnerVocabularyItem entities from ReadingPathwayEntities.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingVocabularyService
{
    /// <summary>Add a word to a learner's vocabulary deck. Creates the master
    /// <see cref="VocabularyWord"/> via AI if it does not yet exist.</summary>
    Task<LearnerVocabularyItem> AddWordAsync(string userId, string word, string source, CancellationToken ct);

    /// <summary>Return up to 30 items due for review today (NextReviewAt ≤ now).</summary>
    Task<List<(LearnerVocabularyItem Item, VocabularyWord Word)>> GetDueForReviewAsync(string userId, CancellationToken ct);

    /// <summary>Record a review outcome and advance the SM-2 schedule.
    /// Quality: 0=Forgot, 3=Hard, 4=Good, 5=Easy.</summary>
    Task<LearnerVocabularyItem> SubmitReviewAsync(Guid itemId, string userId, int quality, CancellationToken ct);

    /// <summary>Return aggregate retention statistics for the learner's deck.</summary>
    Task<VocabStatsDto> GetStatsAsync(string userId, CancellationToken ct);

    /// <summary>Subscribe the learner to every word in a curated <see cref="VocabularyList"/>.</summary>
    Task SubscribeToListAsync(string userId, Guid listId, CancellationToken ct);

    /// <summary>Idempotently return the master <see cref="VocabularyWord"/> for
    /// the given word, generating it via AI if no matching row exists.</summary>
    Task<VocabularyWord> EnsureWordExistsAsync(string word, CancellationToken ct);
}

public sealed record VocabStatsDto(
    int TotalWords,
    int MasteredCount,      // RetentionScore >= 90
    int LearningCount,      // RetentionScore 50–89
    int StrugglingCount,    // RetentionScore < 50
    int DueToday,
    double AverageRetention);

public sealed class ReadingVocabularyService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<ReadingVocabularyService>? logger = null)
    : IReadingVocabularyService
{
    private const int DailyReviewCap = 30;

    // ── AddWordAsync ────────────────────────────────────────────────────────

    public async Task<LearnerVocabularyItem> AddWordAsync(
        string userId, string word, string source, CancellationToken ct)
    {
        var masterWord = await EnsureWordExistsAsync(word, ct);

        // Idempotent — return existing card if the learner already has this word.
        var existing = await db.LearnerVocabularyItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.VocabularyWordId == masterWord.Id, ct);
        if (existing is not null)
            return existing;

        var item = new LearnerVocabularyItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VocabularyWordId = masterWord.Id,
            Source = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim(),
            Easiness = 2.5m,
            IntervalDays = 1,
            Repetitions = 0,
            RetentionScore = 0,
            NextReviewAt = DateTimeOffset.UtcNow.AddDays(1),
            LastReviewedAt = null,
            AddedAt = DateTimeOffset.UtcNow,
        };

        db.LearnerVocabularyItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    // ── GetDueForReviewAsync ────────────────────────────────────────────────

    public async Task<List<(LearnerVocabularyItem Item, VocabularyWord Word)>> GetDueForReviewAsync(
        string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var items = await db.LearnerVocabularyItems
            .Where(x => x.UserId == userId && x.NextReviewAt <= now)
            .OrderBy(x => x.NextReviewAt)
            .Take(DailyReviewCap)
            .ToListAsync(ct);

        if (items.Count == 0)
            return new List<(LearnerVocabularyItem, VocabularyWord)>();

        var wordIds = items.Select(i => i.VocabularyWordId).Distinct().ToList();
        var words = await db.VocabularyWords
            .Where(w => wordIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, ct);

        return items
            .Where(i => words.ContainsKey(i.VocabularyWordId))
            .Select(i => (i, words[i.VocabularyWordId]))
            .ToList();
    }

    // ── SubmitReviewAsync ───────────────────────────────────────────────────

    public async Task<LearnerVocabularyItem> SubmitReviewAsync(
        Guid itemId, string userId, int quality, CancellationToken ct)
    {
        if (quality < 0 || quality > 5)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be 0–5.");

        var item = await db.LearnerVocabularyItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"LearnerVocabularyItem {itemId} not found for user {userId}.");

        var now = DateTimeOffset.UtcNow;

        // SM-2 algorithm (simplified — spec variant)
        if (quality < 3)
        {
            item.IntervalDays = 1;
            item.Repetitions = 0;
        }
        else
        {
            // Update ease factor (clamp ≥ 1.3)
            var delta = 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02);
            item.Easiness = Math.Max(1.3m, item.Easiness + (decimal)delta);

            item.IntervalDays = item.Repetitions switch
            {
                0 => 1,
                1 => 6,
                _ => (int)(item.IntervalDays * (double)item.Easiness),
            };
            item.Repetitions++;
        }

        item.NextReviewAt = now.AddDays(item.IntervalDays);
        item.LastReviewedAt = now;
        item.RetentionScore = (int)(quality / 5.0 * 100);

        await db.SaveChangesAsync(ct);
        return item;
    }

    // ── GetStatsAsync ───────────────────────────────────────────────────────

    public async Task<VocabStatsDto> GetStatsAsync(string userId, CancellationToken ct)
    {
        var items = await db.LearnerVocabularyItems
            .Where(x => x.UserId == userId)
            .Select(x => new { x.RetentionScore, x.NextReviewAt })
            .ToListAsync(ct);

        if (items.Count == 0)
            return new VocabStatsDto(0, 0, 0, 0, 0, 0.0);

        var now = DateTimeOffset.UtcNow;
        var mastered = items.Count(i => i.RetentionScore >= 90);
        var learning = items.Count(i => i.RetentionScore is >= 50 and < 90);
        var struggling = items.Count(i => i.RetentionScore < 50);
        var dueToday = items.Count(i => i.NextReviewAt <= now);
        var avgRetention = items.Average(i => (double)i.RetentionScore);

        return new VocabStatsDto(
            TotalWords: items.Count,
            MasteredCount: mastered,
            LearningCount: learning,
            StrugglingCount: struggling,
            DueToday: dueToday,
            AverageRetention: Math.Round(avgRetention, 2));
    }

    // ── SubscribeToListAsync ────────────────────────────────────────────────

    public async Task SubscribeToListAsync(string userId, Guid listId, CancellationToken ct)
    {
        var list = await db.VocabularyLists.FindAsync(new object[] { listId }, ct)
            ?? throw new KeyNotFoundException($"VocabularyList {listId} not found.");

        List<Guid> wordIds;
        try
        {
            wordIds = JsonSerializer.Deserialize<List<Guid>>(list.WordIdsJson) ?? new();
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "VocabularyList {ListId} has malformed WordIdsJson; treating as empty.", listId);
            wordIds = new();
        }

        if (wordIds.Count == 0)
            return;

        // Determine which word IDs the user already has cards for.
        var existingWordIds = await db.LearnerVocabularyItems
            .Where(x => x.UserId == userId && wordIds.Contains(x.VocabularyWordId))
            .Select(x => x.VocabularyWordId)
            .ToHashSetAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var newItems = new List<LearnerVocabularyItem>();

        foreach (var wordId in wordIds)
        {
            if (existingWordIds.Contains(wordId))
                continue;

            // Verify the word exists in the master table.
            var wordExists = await db.VocabularyWords.AnyAsync(w => w.Id == wordId, ct);
            if (!wordExists)
            {
                logger?.LogWarning("VocabularyList {ListId} references unknown word ID {WordId}; skipping.", listId, wordId);
                continue;
            }

            newItems.Add(new LearnerVocabularyItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VocabularyWordId = wordId,
                Source = "curated_list",
                Easiness = 2.5m,
                IntervalDays = 1,
                Repetitions = 0,
                RetentionScore = 0,
                NextReviewAt = now.AddDays(1),
                LastReviewedAt = null,
                AddedAt = now,
            });
        }

        if (newItems.Count > 0)
        {
            db.LearnerVocabularyItems.AddRange(newItems);
            await db.SaveChangesAsync(ct);
        }
    }

    // ── EnsureWordExistsAsync ───────────────────────────────────────────────

    public async Task<VocabularyWord> EnsureWordExistsAsync(string word, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(word))
            throw new ArgumentException("Word must not be empty.", nameof(word));

        var normalised = word.Trim();
        var lower = normalised.ToLowerInvariant();

        // Case-insensitive lookup.
        var existing = await db.VocabularyWords
            .FirstOrDefaultAsync(w => w.Word.ToLower() == lower, ct);
        if (existing is not null)
            return existing;

        // Generate via AI gateway (Vocabulary rulebook + VocabularyGloss task).
        OetRulebook rulebook;
        try
        {
            rulebook = rulebookLoader.Load(RuleKind.Vocabulary, ExamProfession.Medicine);
        }
        catch (RulebookNotFoundException)
        {
            rulebook = new OetRulebook { Version = "fallback", Kind = RuleKind.Vocabulary };
        }

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Vocabulary,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateVocabularyGloss,
        });

        var userMessage = BuildWordCardPrompt(normalised);

        GeneratedWordCard? card = null;
        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = string.Empty,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.VocabularyGloss,
                UserId = null,
            }, ct);

            card = TryParseWordCard(result.Completion);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "EnsureWordExistsAsync — AI generation failed for '{Word}'; using stub card.", normalised);
        }

        // Create stub if AI call failed or returned unparseable JSON.
        var now = DateTimeOffset.UtcNow;
        var vocabWord = new VocabularyWord
        {
            Id = Guid.NewGuid(),
            Word = normalised,
            PartOfSpeech = card?.PartOfSpeech ?? "",
            DefinitionEn = card?.DefinitionEn ?? $"Definition of {normalised} (AI unavailable).",
            DefinitionAr = card?.DefinitionAr ?? "",
            PronunciationIpa = card?.PronunciationIpa ?? "",
            AudioUrl = null,
            ExampleEn = card?.ExampleEn ?? $"The patient's notes referenced {normalised}.",
            ExampleAr = card?.ExampleAr ?? "",
            HealthcareContext = card?.HealthcareContext ?? "general",
            ProfessionRelevanceJson = card?.ProfessionRelevanceJson ?? "[]",
            Difficulty = card?.Difficulty ?? 5,
            CreatedAt = now,
        };

        db.VocabularyWords.Add(vocabWord);
        await db.SaveChangesAsync(ct);
        return vocabWord;
    }

    // ── Prompt + parsing helpers ────────────────────────────────────────────

    private static string BuildWordCardPrompt(string word)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generate a healthcare vocabulary word card for use in OET exam preparation.");
        sb.AppendLine();
        sb.AppendLine($"Word: {word}");
        sb.AppendLine();
        sb.AppendLine("Return a SINGLE JSON object with the following fields:");
        sb.AppendLine("{");
        sb.AppendLine("  \"partOfSpeech\": \"noun|verb|adjective|adverb|phrase\",");
        sb.AppendLine("  \"definitionEn\": \"concise English definition (≤ 30 words)\",");
        sb.AppendLine("  \"definitionAr\": \"Arabic translation of the definition\",");
        sb.AppendLine("  \"pronunciationIpa\": \"/ˈ.../\",");
        sb.AppendLine("  \"exampleEn\": \"example sentence in OET medical register\",");
        sb.AppendLine("  \"exampleAr\": \"Arabic translation of the example sentence\",");
        sb.AppendLine("  \"healthcareContext\": \"brief note on clinical usage (≤ 20 words)\",");
        sb.AppendLine("  \"professionRelevance\": [\"Medicine\",\"Nursing\"],");
        sb.AppendLine("  \"difficulty\": 5");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static GeneratedWordCard? TryParseWordCard(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var json = ExtractJsonBlock(completion);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var professions = new List<string>();
            if (root.TryGetProperty("professionRelevance", out var profEl) && profEl.ValueKind == JsonValueKind.Array)
                foreach (var e in profEl.EnumerateArray())
                    if (e.GetString() is string s) professions.Add(s);

            return new GeneratedWordCard(
                PartOfSpeech: SafeString(root, "partOfSpeech") ?? "",
                DefinitionEn: SafeString(root, "definitionEn") ?? "",
                DefinitionAr: SafeString(root, "definitionAr") ?? "",
                PronunciationIpa: SafeString(root, "pronunciationIpa") ?? "",
                ExampleEn: SafeString(root, "exampleEn") ?? "",
                ExampleAr: SafeString(root, "exampleAr") ?? "",
                HealthcareContext: SafeString(root, "healthcareContext") ?? "",
                ProfessionRelevanceJson: JsonSerializer.Serialize(professions),
                Difficulty: root.TryGetProperty("difficulty", out var d) && d.TryGetInt32(out var dv) ? Math.Clamp(dv, 1, 10) : 5);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record GeneratedWordCard(
        string PartOfSpeech,
        string DefinitionEn,
        string DefinitionAr,
        string PronunciationIpa,
        string ExampleEn,
        string ExampleAr,
        string HealthcareContext,
        string ProfessionRelevanceJson,
        int Difficulty);

    private static string? ExtractJsonBlock(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) return trimmed;
        var fenceStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart < 0) fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart < 0) return null;
        var afterFence = trimmed.IndexOf('\n', fenceStart);
        if (afterFence < 0) return null;
        var closeFence = trimmed.IndexOf("```", afterFence + 1, StringComparison.Ordinal);
        if (closeFence < 0) return null;
        var inner = trimmed[(afterFence + 1)..closeFence].Trim();
        return inner.StartsWith("{") && inner.EndsWith("}") ? inner : null;
    }

    private static string? SafeString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
