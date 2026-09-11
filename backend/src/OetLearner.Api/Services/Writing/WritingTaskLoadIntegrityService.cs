using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Per-task booleans of the 100% task-load integrity gate (Writing Addendum
/// Rev8 §17/§19.6). Every flag except <see cref="ModelAnswerVerified"/> must
/// be true for the task to count as loadable; the Model Answer is a separate
/// gate and is reported, never counted as a load failure.
/// </summary>
public sealed record WritingTaskLoadIntegrityChecks(
    bool LearnerProjection,
    bool TaskPrompt,
    bool CaseNotes,
    bool StimulusPdf,
    bool Recipient,
    bool Rulebook,
    bool LetterType,
    bool AssessmentPack,
    bool ModelAnswerVerified);

public sealed record WritingTaskLoadIntegrityRow(
    Guid ScenarioId,
    string? InternalCode,
    string Title,
    string Profession,
    string LetterType,
    bool LoadOk,
    WritingTaskLoadIntegrityChecks Checks,
    IReadOnlyList<string> Failures,
    string? Exception);

public sealed record WritingTaskLoadIntegrityProfessionSummary(int Total, int LoadPassed, int LoadFailed);

public sealed record WritingTaskLoadIntegrityReport(
    DateTimeOffset GeneratedAt,
    string ValidatorVersion,
    int Total,
    int LoadPassed,
    int LoadFailed,
    int ModelAnswerVerified,
    IReadOnlyDictionary<string, WritingTaskLoadIntegrityProfessionSummary> ByProfession,
    IReadOnlyList<WritingTaskLoadIntegrityRow> Rows);

public interface IWritingTaskLoadIntegrityService
{
    Task<WritingTaskLoadIntegrityReport> ScanPublishedAsync(CancellationToken ct = default);
}

/// <summary>
/// Proves every published Writing task can actually load for a candidate:
/// runs the exact learner projection (<see cref="WritingScenarioService.ToLearnerResponse"/>)
/// and every dependency a page load or grading start needs — prompt, case
/// notes, stimulus PDF row + storage object, recipient, rulebook, LT-* code,
/// approved assessment pack. Read-only; resolvers are the same ones the
/// publish gate and candidate grading use, so the three can never disagree.
/// </summary>
public sealed class WritingTaskLoadIntegrityService(
    LearnerDbContext db,
    IFileStorage storage,
    IRulebookLoader rulebooks,
    TimeProvider clock) : IWritingTaskLoadIntegrityService
{
    public async Task<WritingTaskLoadIntegrityReport> ScanPublishedAsync(CancellationToken ct = default)
    {
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published")
            .OrderBy(s => s.Profession)
            .ThenBy(s => s.Title)
            .ToListAsync(ct);
        var ids = scenarios.Select(s => s.Id).ToList();

        // Batched reads, grouped client-side (InMemory-safe, no N+1).
        var sentencesById = (await db.WritingScenarioStructuredSentences.AsNoTracking()
                .Where(x => ids.Contains(x.ScenarioId))
                .ToListAsync(ct))
            .GroupBy(x => x.ScenarioId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Ordinal).ToList());
        var answersById = (await db.WritingTaskModelAnswers.AsNoTracking()
                .Where(a => ids.Contains(a.ScenarioId))
                .ToListAsync(ct))
            .ToLookup(a => a.ScenarioId);
        var assetIds = scenarios
            .Select(s => s.StimulusPdfMediaAssetId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        var assets = await db.MediaAssets.AsNoTracking()
            .Where(m => assetIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        // ponytail: pack resolution cached per (profession, letter type) pair;
        // the catalogue has few distinct pairs, so this stays a handful of queries.
        var packCache = new Dictionary<(string, string), bool>();
        var rows = new List<WritingTaskLoadIntegrityRow>(scenarios.Count);
        foreach (var s in scenarios)
        {
            var sentences = sentencesById.TryGetValue(s.Id, out var found)
                ? found
                : new List<WritingScenarioStructuredSentence>();
            rows.Add(await CheckAsync(s, sentences, answersById[s.Id], assets, packCache, ct));
        }

        var byProfession = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Profession) ? "unknown" : r.Profession.Trim().ToLowerInvariant())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new WritingTaskLoadIntegrityProfessionSummary(g.Count(), g.Count(r => r.LoadOk), g.Count(r => !r.LoadOk)));

        return new WritingTaskLoadIntegrityReport(
            clock.GetUtcNow(),
            WritingRuleEngine.ValidatorVersion,
            rows.Count,
            rows.Count(r => r.LoadOk),
            rows.Count(r => !r.LoadOk),
            rows.Count(r => r.Checks.ModelAnswerVerified),
            byProfession,
            rows);
    }

    private async Task<WritingTaskLoadIntegrityRow> CheckAsync(
        WritingScenario s,
        List<WritingScenarioStructuredSentence> sentences,
        IEnumerable<WritingTaskModelAnswer> answers,
        IReadOnlyDictionary<string, MediaAsset> assets,
        Dictionary<(string, string), bool> packCache,
        CancellationToken ct)
    {
        var failures = new List<string>();
        string? exception = null;

        // (a) The exact learner projection a page load runs.
        var projectionOk = true;
        try
        {
            _ = WritingScenarioService.ToLearnerResponse(s, sentences);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            projectionOk = false;
            exception = Describe(ex);
            failures.Add("learner_projection_failed");
        }

        // (b) + (c) Something to read, and case notes to read it against.
        var taskPrompt = !string.IsNullOrWhiteSpace(s.TaskPromptMarkdown);
        if (!taskPrompt) failures.Add("task_prompt_missing");
        var caseNotes = sentences.Count >= 1;
        if (!caseNotes) failures.Add("case_notes_missing");

        // (d) An attached stimulus PDF must have its MediaAsset row AND its
        // storage object; a task without a PDF renders the prompt instead.
        var stimulusPdf = true;
        if (!string.IsNullOrWhiteSpace(s.StimulusPdfMediaAssetId))
        {
            if (!assets.TryGetValue(s.StimulusPdfMediaAssetId, out var asset))
            {
                stimulusPdf = false;
                failures.Add("stimulus_pdf_asset_missing");
            }
            else
            {
                try
                {
                    stimulusPdf = !string.IsNullOrWhiteSpace(asset.StoragePath)
                        && await storage.ExistsAsync(asset.StoragePath, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stimulusPdf = false;
                    exception ??= Describe(ex);
                }

                if (!stimulusPdf) failures.Add("stimulus_pdf_object_missing");
            }
        }

        // (e) Recipient: admin override wins, else the SAME heuristic the
        // publish gate uses (WritingTaskAuthoringService.ValidateAsync).
        var caseNotesText = string.Join("\n", sentences
            .Select(x => (x.SentenceText ?? string.Empty).Trim())
            .Where(x => x.Length > 0));
        var recipient = !string.IsNullOrWhiteSpace(s.RecipientRawText)
            || !string.Equals(
                WritingTaskUnderstandingService.Understand(
                    s.TaskPromptMarkdown ?? string.Empty,
                    caseNotesText,
                    WritingLetterTypeTaxonomy.ToPackLetterType(s.LetterType)).RecipientCategory,
                "unknown",
                StringComparison.Ordinal);
        if (!recipient) failures.Add("recipient_unresolved");

        // (f) Profession parses and its Writing rulebook loads.
        var rulebook = false;
        if (RulebookProfessionParser.TryParse(s.Profession, out var profession))
        {
            try
            {
                _ = rulebooks.Load(RuleKind.Writing, profession);
                rulebook = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                exception ??= Describe(ex);
                failures.Add("rulebook_unavailable");
            }
        }
        else
        {
            failures.Add("profession_unresolved");
        }

        // (g) Stored letter type is a valid LT-* catalogue code.
        var letterType = WritingLetterTypeTaxonomy.IsValidCatalogueLetterType(s.LetterType);
        if (!letterType) failures.Add("letter_type_invalid");

        // (h) Approved candidate-facing assessment pack resolves (same
        // resolver as runtime grading and the publish gate).
        var packKey = (s.Profession ?? string.Empty, s.LetterType ?? string.Empty);
        if (!packCache.TryGetValue(packKey, out var assessmentPack))
        {
            assessmentPack = await WritingAssessmentPreflightService.ResolvePackForCatalogueAsync(
                db, s.Profession, s.LetterType, ct) is not null;
            packCache[packKey] = assessmentPack;
        }
        if (!assessmentPack) failures.Add("assessment_pack_unresolved");

        // (i) Separate gate — reported, never a load failure.
        var modelAnswerVerified = answers.Any(WritingTaskModelAnswerService.IsVerifiedForCandidates);

        return new WritingTaskLoadIntegrityRow(
            s.Id,
            s.InternalCode,
            s.Title,
            s.Profession,
            s.LetterType,
            failures.Count == 0,
            new WritingTaskLoadIntegrityChecks(
                projectionOk,
                taskPrompt,
                caseNotes,
                stimulusPdf,
                recipient,
                rulebook,
                letterType,
                assessmentPack,
                modelAnswerVerified),
            failures,
            exception);
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
