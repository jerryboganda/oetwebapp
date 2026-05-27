using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingCaseNoteDrillSentenceView(Guid Id, int Ordinal, string SentenceText, string? Rationale);

public sealed record WritingCaseNoteDrillView(
    Guid Id,
    string Title,
    string Profession,
    string LetterType,
    string Format,
    string CaseNotesMarkdown,
    int Difficulty,
    IReadOnlyList<WritingCaseNoteDrillSentenceView> Sentences,
    string Status);

public sealed record WritingCaseNoteDrillAttemptRequest(IReadOnlyDictionary<Guid, string> Responses, int? TimeSpentSeconds);

public sealed record WritingCaseNoteDrillFeedbackEntry(Guid SentenceId, bool IsCorrect, string CorrectLabel, string? Rationale);

public sealed record WritingCaseNoteDrillAttemptResult(
    Guid AttemptId,
    int CorrectCount,
    int TotalCount,
    double ScorePercent,
    IReadOnlyList<WritingCaseNoteDrillFeedbackEntry> PerSentence);

public interface IWritingCaseNoteDrillService
{
    Task<IReadOnlyList<WritingCaseNoteDrillView>> ListAsync(string userId, string? profession, string? letterType, CancellationToken ct);
    Task<WritingCaseNoteDrillView?> GetAsync(string userId, Guid drillId, CancellationToken ct);
    Task<WritingCaseNoteDrillAttemptResult> SubmitAttemptAsync(string userId, Guid drillId, WritingCaseNoteDrillAttemptRequest request, CancellationToken ct);
}

public sealed class WritingCaseNoteDrillService(LearnerDbContext db, TimeProvider clock) : IWritingCaseNoteDrillService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingCaseNoteDrillView>> ListAsync(string userId, string? profession, string? letterType, CancellationToken ct)
    {
        _ = userId;
        var query = db.WritingCaseNoteDrills.AsNoTracking().Where(d => d.Status == "published");
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(d => d.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(d => d.LetterType == letterType);
        var drills = await query.OrderBy(d => d.Difficulty).ThenBy(d => d.Title).ToListAsync(ct);
        if (drills.Count == 0) return Array.Empty<WritingCaseNoteDrillView>();
        var ids = drills.Select(d => d.Id).ToList();
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => ids.Contains(s.DrillId))
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        return drills.Select(d => ToView(d, sentences.Where(s => s.DrillId == d.Id).ToList())).ToList();
    }

    public async Task<WritingCaseNoteDrillView?> GetAsync(string userId, Guid drillId, CancellationToken ct)
    {
        _ = userId;
        var drill = await db.WritingCaseNoteDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == drillId, ct);
        if (drill is null) return null;
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => s.DrillId == drillId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        return ToView(drill, sentences);
    }

    public async Task<WritingCaseNoteDrillAttemptResult> SubmitAttemptAsync(string userId, Guid drillId, WritingCaseNoteDrillAttemptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var drill = await db.WritingCaseNoteDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == drillId, ct)
            ?? throw ApiException.NotFound("writing_case_note_drill_not_found", "Case-note drill was not found.");
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => s.DrillId == drillId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        if (sentences.Count == 0)
        {
            throw ApiException.Conflict("writing_case_note_drill_empty", "Drill has no sentences.");
        }

        var responses = request.Responses ?? new Dictionary<Guid, string>();
        var feedback = new List<WritingCaseNoteDrillFeedbackEntry>(sentences.Count);
        var correct = 0;
        foreach (var s in sentences)
        {
            responses.TryGetValue(s.Id, out var raw);
            var learnerLabel = NormalizeLabel(raw);
            var correctLabel = NormalizeLabel(s.RelevanceLabel);
            // "maybe" tags are not penalized per spec §16.4: any answer is "correct".
            var isCorrect = correctLabel == "maybe"
                ? !string.IsNullOrEmpty(learnerLabel)
                : string.Equals(learnerLabel, correctLabel, StringComparison.OrdinalIgnoreCase);
            if (isCorrect) correct++;
            feedback.Add(new WritingCaseNoteDrillFeedbackEntry(s.Id, isCorrect, correctLabel, s.Rationale));
        }

        var score = Math.Round(correct * 100.0 / sentences.Count, 1);
        var attempt = new WritingCaseNoteDrillAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DrillId = drillId,
            ResponsesJson = JsonSerializer.Serialize(responses, JsonOptions),
            CorrectCount = correct,
            TotalCount = sentences.Count,
            ScorePercent = score,
            TimeSpentSeconds = request.TimeSpentSeconds,
            AttemptedAt = clock.GetUtcNow(),
        };
        db.WritingCaseNoteDrillAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return new WritingCaseNoteDrillAttemptResult(attempt.Id, correct, sentences.Count, score, feedback);
    }

    private static WritingCaseNoteDrillView ToView(WritingCaseNoteDrill drill, IReadOnlyList<WritingCaseNoteDrillSentence> sentences)
        => new(drill.Id, drill.Title, drill.Profession, drill.LetterType, drill.Format, drill.CaseNotesMarkdown, drill.Difficulty,
            sentences.Select(s => new WritingCaseNoteDrillSentenceView(s.Id, s.Ordinal, s.SentenceText, s.Rationale)).ToList(),
            drill.Status);

    private static string NormalizeLabel(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "relevant" or "essential" or "include" => "relevant",
            "maybe" or "optional" => "maybe",
            "irrelevant" or "omit" or "exclude" => "irrelevant",
            _ => string.Empty,
        };
}
