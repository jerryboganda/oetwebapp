using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

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
}

public sealed class WritingTaskCaseNotesService(LearnerDbContext db, TimeProvider clock) : IWritingTaskCaseNotesService
{
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
