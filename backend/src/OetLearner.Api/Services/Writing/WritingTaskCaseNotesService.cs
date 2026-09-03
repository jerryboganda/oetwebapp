using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingCaseNotesExtractionResult(
    Guid ScenarioId,
    int SentenceCount,
    int CharsExtracted,
    bool Truncated,
    string? MediaAssetId);

/// <summary>
/// Narrow, surgical write path for a writing task's structured case-note
/// sentences (<see cref="WritingScenarioStructuredSentence"/>). Backs the admin
/// case-note backfill flow: many tasks were authored via
/// <see cref="WritingTaskAuthoringService"/> (PDF-driven, spec §3/§19.2), which
/// never touches this table, so their case notes are stored only as an
/// unreadable stimulus PDF and grading preflight fails with
/// <c>case_note_pages_unreadable</c> (<see cref="WritingAssessmentPreflightService"/>).
/// Deliberately separate from <see cref="WritingScenarioService"/>'s scenario
/// upsert (which also rewrites Title/LetterType/Profession/Topics/Difficulty)
/// so a batch backfill can safely replace just the case-note sentences for many
/// tasks with zero risk of clobbering unrelated authored fields.
/// </summary>
public interface IWritingTaskCaseNotesService
{
    Task<IReadOnlyList<WritingScenarioStructuredSentenceDto>?> ReplaceAsync(
        Guid scenarioId,
        IReadOnlyList<WritingScenarioStructuredSentenceDto> sentences,
        CancellationToken ct = default);

    /// <summary>
    /// Preparation-time canonicalization: extracts the task's stimulus PDF
    /// (embedded text via PdfPig, OCR via Azure/Mistral fallback for scanned
    /// pages) and stores the result as structured case-note sentences.
    /// Admin/backfill use ONLY — never called from the candidate grading path,
    /// which reads the stored sentences. Returns null when the task or its
    /// stimulus PDF is missing. Throws <see cref="ApiException"/> with
    /// <c>case_note_extraction_empty</c> when the PDF yields no usable text
    /// (admin must transcribe or attach a readable PDF instead).
    /// </summary>
    Task<WritingCaseNotesExtractionResult?> ExtractFromStimulusPdfAsync(
        Guid scenarioId,
        CancellationToken ct = default);
}

public sealed class WritingTaskCaseNotesService(
    LearnerDbContext db,
    TimeProvider clock,
    IFileStorage storage,
    IPdfTextExtractor extractor,
    ILogger<WritingTaskCaseNotesService>? logger = null) : IWritingTaskCaseNotesService
{
    /// <summary>Upper bound on sentences stored per extraction (table hygiene).</summary>
    private const int MaxSentencesPerExtraction = 500;
    public async Task<IReadOnlyList<WritingScenarioStructuredSentenceDto>?> ReplaceAsync(
        Guid scenarioId,
        IReadOnlyList<WritingScenarioStructuredSentenceDto> sentences,
        CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        if (scenario is null) return null;

        var existing = await db.WritingScenarioStructuredSentences
            .Where(s => s.ScenarioId == scenarioId)
            .ToListAsync(ct);
        db.WritingScenarioStructuredSentences.RemoveRange(existing);

        var now = clock.GetUtcNow();
        var ordinal = 0;
        var saved = new List<WritingScenarioStructuredSentenceDto>();
        foreach (var s in sentences.OrderBy(s => s.Ordinal))
        {
            var text = (s.SentenceText ?? string.Empty).Trim();
            if (text.Length == 0) continue;

            ordinal++;
            var relevance = NormalizeRelevance(s.Relevance);
            var notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes!.Trim();

            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenarioId,
                Ordinal = ordinal,
                SentenceText = text,
                RelevanceLabel = relevance,
                Notes = notes,
                CreatedAt = now,
            });
            saved.Add(new WritingScenarioStructuredSentenceDto(ordinal, text, relevance, notes));
        }

        scenario.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return saved;
    }

    public async Task<WritingCaseNotesExtractionResult?> ExtractFromStimulusPdfAsync(
        Guid scenarioId,
        CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        if (scenario is null) return null;
        if (string.IsNullOrWhiteSpace(scenario.StimulusPdfMediaAssetId)) return null;

        var asset = await db.MediaAssets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == scenario.StimulusPdfMediaAssetId, ct);
        if (asset is null)
        {
            logger?.LogWarning(
                "Case-note extraction: stimulus asset {AssetId} missing for scenario {ScenarioId}.",
                scenario.StimulusPdfMediaAssetId, scenarioId);
            return null;
        }

        string text;
        await using (var stream = await storage.OpenReadAsync(asset.StoragePath, ct))
        {
            text = await extractor.ExtractAsync(stream, ct) ?? string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length >= 2)
            .ToList();
        var truncated = lines.Count > MaxSentencesPerExtraction;
        var kept = lines.Take(MaxSentencesPerExtraction).ToList();

        if (kept.Count == 0)
        {
            throw ApiException.Validation(
                "case_note_extraction_empty",
                "The stimulus PDF yielded no usable text. Transcribe the case notes via the case-notes editor or attach a readable PDF.");
        }

        var dtos = kept
            .Select((line, i) => new WritingScenarioStructuredSentenceDto(i + 1, line, "relevant", null))
            .ToList();
        await ReplaceAsync(scenarioId, dtos, ct);

        logger?.LogInformation(
            "Case-note extraction: scenario {ScenarioId} stored {Count} sentences from asset {AssetId} (chars {Chars}, truncated {Truncated}).",
            scenarioId, kept.Count, scenario.StimulusPdfMediaAssetId, text.Length, truncated);

        return new WritingCaseNotesExtractionResult(
            scenarioId, kept.Count, text.Length, truncated, scenario.StimulusPdfMediaAssetId);
    }

    private static string NormalizeRelevance(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "relevant" => "relevant",
            "maybe" => "maybe",
            "irrelevant" => "irrelevant",
            "omit" => "irrelevant",
            "essential" => "relevant",
            _ => "relevant",
        };
}
