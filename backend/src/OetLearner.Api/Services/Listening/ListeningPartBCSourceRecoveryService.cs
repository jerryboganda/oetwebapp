using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part B / Part C — restore printed stems from the paper's own source.
//
// Migration 20261128000000 overwrote every sentinel Part B/C stem with ONE
// generic heading, and 20261129000000 then blanked that heading for every paper
// except a single hand-restored one. Candidates are therefore shown a Part B/C
// item with no question above its three options.
//
// The printed wording still exists: ContentTextExtractionService caches the
// extracted text of every PDF asset on ContentPaper.ExtractedTextJson, keyed by
// asset id. This service re-derives each item's stem and options from that text
// via ListeningPartBCSourceParser — the paper's own source, never a guess — and
// writes back only what the source unambiguously supports.
//
// Deliberate design decisions:
//   * It only ever FILLS a value that is currently unusable (blank, sentinel,
//     metadata heading, generic placeholder). A stem or option that a human
//     authored is never overwritten by a machine re-read.
//   * It runs outside the authoring attempts guard on purpose. Every affected
//     paper is published and has learner attempts, so the normal authoring
//     routes refuse the write; this path exists precisely to repair source
//     fidelity on those papers. It cannot change an answer key, an option's
//     correctness, question numbering, or scoring.
//   * Anything the source cannot support is REPORTED, so an operator can enter
//     the remaining items from the question paper by hand.
// ═════════════════════════════════════════════════════════════════════════════

public sealed record ListeningPartBCRecoveryItem(
    int Number,
    string Status,
    string? PreviousStem,
    string? RecoveredStem,
    int OptionsUpdated,
    string? Detail);

public sealed record ListeningPartBCRecoveryReport(
    string PaperId,
    string PaperTitle,
    string PaperSlug,
    string Status,
    bool DryRun,
    bool SourceTextAvailable,
    int PartBCQuestionCount,
    int AlreadyUsable,
    int Recovered,
    int Unrecoverable,
    IReadOnlyList<ListeningPartBCRecoveryItem> Items,
    /// <summary>Diagnostics for an operator working out WHY a paper did not
    /// recover: how much cached text the paper has at all, how long the text the
    /// parser actually chose is, and a short excerpt of it. Without these, a
    /// paper that fails is indistinguishable from one with no source.</summary>
    int CachedTextEntries = 0,
    int LargestCachedTextChars = 0,
    int SelectedSourceChars = 0,
    string? SelectedSourceExcerpt = null)
{
    /// <summary>True when no Part B/C item is left without a printed question.</summary>
    public bool IsClean => Unrecoverable == 0 && PartBCQuestionCount > 0;
}

public interface IListeningPartBCSourceRecoveryService
{
    /// <summary>Re-derive missing Part B/C stems and options for one paper from
    /// its own question-paper text. <paramref name="dryRun"/> computes the full
    /// report without writing.</summary>
    Task<ListeningPartBCRecoveryReport> RecoverPaperAsync(
        string paperId, bool dryRun, string adminId, CancellationToken ct);

    /// <summary>Dry-run every Listening paper so operators can see, per paper and
    /// per printed number, which items still have no candidate-facing question.</summary>
    Task<IReadOnlyList<ListeningPartBCRecoveryReport>> AuditAllAsync(
        bool publishedOnly, CancellationToken ct);

    /// <summary>Run recovery across every Listening paper in one pass. Papers are
    /// independent: one failing never aborts the sweep.</summary>
    Task<ListeningPartBCSweepReport> RecoverAllAsync(
        bool publishedOnly, bool dryRun, string adminId, CancellationToken ct);
}

/// <summary>Fleet-wide result for the whole Listening catalogue.</summary>
public sealed record ListeningPartBCSweepReport(
    bool DryRun,
    int PapersScanned,
    int PapersChanged,
    int PapersFullyClean,
    int PapersNeedingManualEntry,
    int PapersWithoutSourceText,
    int TotalRecovered,
    int TotalStillUnrecoverable,
    IReadOnlyList<ListeningPartBCRecoveryReport> Papers,
    IReadOnlyList<string> Failures);

public sealed class ListeningPartBCSourceRecoveryService(
    LearnerDbContext db,
    Content.IContentTextExtractionService? textExtraction = null)
    : IListeningPartBCSourceRecoveryService
{
    private const string QuestionsKey = "listeningQuestions";

    public async Task<IReadOnlyList<ListeningPartBCRecoveryReport>> AuditAllAsync(
        bool publishedOnly, CancellationToken ct)
    {
        var query = db.ContentPapers.AsNoTracking().Where(p => p.SubtestCode == "listening");
        if (publishedOnly) query = query.Where(p => p.Status == ContentStatus.Published);

        var paperIds = await query
            .OrderBy(p => p.Title)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var reports = new List<ListeningPartBCRecoveryReport>(paperIds.Count);
        foreach (var paperId in paperIds)
        {
            reports.Add(await RecoverPaperAsync(paperId, dryRun: true, adminId: "system:audit", ct));
        }
        return reports;
    }

    public async Task<ListeningPartBCSweepReport> RecoverAllAsync(
        bool publishedOnly, bool dryRun, string adminId, CancellationToken ct)
    {
        var query = db.ContentPapers.AsNoTracking().Where(p => p.SubtestCode == "listening");
        if (publishedOnly) query = query.Where(p => p.Status == ContentStatus.Published);

        var papers = await query
            .OrderBy(p => p.Title)
            .Select(p => new { p.Id, p.Title })
            .ToListAsync(ct);

        var reports = new List<ListeningPartBCRecoveryReport>(papers.Count);
        var failures = new List<string>();

        foreach (var paper in papers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                reports.Add(await RecoverPaperAsync(paper.Id, dryRun, adminId, ct));
            }
            catch (Exception ex)
            {
                // One bad paper must never abort the sweep — the whole point is
                // to get every other paper readable in a single pass.
                failures.Add($"{paper.Title} ({paper.Id}): {ex.Message}");
            }
        }

        return new ListeningPartBCSweepReport(
            DryRun: dryRun,
            PapersScanned: reports.Count,
            PapersChanged: reports.Count(r => r.Recovered > 0),
            PapersFullyClean: reports.Count(r => r.IsClean),
            PapersNeedingManualEntry: reports.Count(r => r.Unrecoverable > 0),
            PapersWithoutSourceText: reports.Count(r => !r.SourceTextAvailable && r.PartBCQuestionCount > 0),
            TotalRecovered: reports.Sum(r => r.Recovered),
            TotalStillUnrecoverable: reports.Sum(r => r.Unrecoverable),
            Papers: reports,
            Failures: failures);
    }

    public async Task<ListeningPartBCRecoveryReport> RecoverPaperAsync(
        string paperId, bool dryRun, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Listening paper not found.");

        if (!string.Equals(paper.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "listening_paper_expected",
                "Part B/C source recovery only applies to Listening papers.");
        }

        var questions = await db.ListeningQuestions
            .Where(q => q.PaperId == paperId
                && q.QuestionNumber >= ListeningPartBCSourceParser.FirstNumber
                && q.QuestionNumber <= ListeningPartBCSourceParser.LastNumber)
            .OrderBy(q => q.QuestionNumber)
            .ToListAsync(ct);

        var questionIds = questions.Select(q => q.Id).ToList();
        var optionsByQuestion = (await db.ListeningQuestionOptions
                .Where(option => questionIds.Contains(option.ListeningQuestionId))
                .ToListAsync(ct))
            .GroupBy(option => option.ListeningQuestionId)
            .ToDictionary(group => group.Key, group => group.OrderBy(o => o.DisplayOrder).ToList(), StringComparer.Ordinal);

        // Only items the candidate cannot currently read need the source.
        var needsStem = questions
            .Where(q => !ListeningLearnerService.IsUsablePartBCStem(q.Stem))
            .Select(q => q.QuestionNumber)
            .ToHashSet();
        var needsOptions = questions
            .Where(q => !HasUsableOptions(optionsByQuestion.GetValueOrDefault(q.Id)))
            .Select(q => q.QuestionNumber)
            .ToHashSet();
        var wanted = needsStem.Union(needsOptions).ToHashSet();

        var sourceText = ListeningPartBCSourceParser.SelectQuestionPaperText(
            ReadAssetTexts(paper), wanted);

        // A paper ingested before the PDF engine was configured has an empty
        // cached extraction, and the normal extraction pass skips any asset that
        // already has an entry — so it would stay blank forever and recovery
        // would silently find nothing. Force one re-extraction and retry before
        // giving up. This runs BEFORE any stem edit is staged so the extraction
        // service's own SaveChanges cannot flush a half-finished repair, and it
        // only rewrites cache entries that are unusable.
        if (sourceText is null && wanted.Count > 0 && textExtraction is not null)
        {
            try
            {
                await textExtraction.ExtractForPaperAsync(paper.Id, ct, force: true);
                sourceText = ListeningPartBCSourceParser.SelectQuestionPaperText(ReadAssetTexts(paper), wanted);
            }
            catch (Exception)
            {
                // Extraction is best-effort; a failure is reported as "no source
                // text" below rather than failing the whole recovery.
            }
        }

        var parsed = wanted.Count == 0
            ? ListeningPartBCSourceParseResult.Empty
            : ListeningPartBCSourceParser.Parse(sourceText, wanted);
        var recoveredByNumber = parsed.Items.ToDictionary(item => item.Number);
        var skipByNumber = parsed.Skipped.ToDictionary(skip => skip.Number);

        var items = new List<ListeningPartBCRecoveryItem>(questions.Count);
        var recoveredCount = 0;
        var alreadyUsableCount = 0;
        var unrecoverableCount = 0;
        var changedNumbers = new List<int>();

        foreach (var question in questions)
        {
            var number = question.QuestionNumber;
            var stemUsable = !needsStem.Contains(number);
            var optionsUsable = !needsOptions.Contains(number);

            if (stemUsable && optionsUsable)
            {
                alreadyUsableCount++;
                items.Add(new(number, "already-usable", question.Stem, null, 0, null));
                continue;
            }

            if (!recoveredByNumber.TryGetValue(number, out var source))
            {
                unrecoverableCount++;
                var detail = skipByNumber.TryGetValue(number, out var skip)
                    ? skip.Detail
                    : sourceText is null
                        ? "This paper has no extracted question-paper text to recover from. Re-run PDF text extraction for the paper, or enter the item from the source paper."
                        : $"Q{number} could not be recovered from the question-paper text.";
                items.Add(new(number, "unrecoverable", question.Stem, null, 0, detail));
                continue;
            }

            var previousStem = question.Stem;
            if (!stemUsable) question.Stem = source.Stem;

            var optionsUpdated = 0;
            if (!optionsUsable)
            {
                optionsUpdated = ApplyOptions(optionsByQuestion.GetValueOrDefault(question.Id), source);
            }

            recoveredCount++;
            changedNumbers.Add(number);
            items.Add(new(
                number,
                "recovered",
                previousStem,
                stemUsable ? null : source.Stem,
                optionsUpdated,
                null));
        }

        var cachedTexts = ReadAssetTexts(paper).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var report = new ListeningPartBCRecoveryReport(
            PaperId: paper.Id,
            PaperTitle: paper.Title,
            PaperSlug: paper.Slug,
            Status: paper.Status.ToString(),
            DryRun: dryRun,
            SourceTextAvailable: sourceText is not null,
            PartBCQuestionCount: questions.Count,
            AlreadyUsable: alreadyUsableCount,
            Recovered: recoveredCount,
            Unrecoverable: unrecoverableCount,
            Items: items,
            CachedTextEntries: cachedTexts.Count,
            LargestCachedTextChars: cachedTexts.Count == 0 ? 0 : cachedTexts.Max(t => t!.Length),
            SelectedSourceChars: sourceText?.Length ?? 0,
            SelectedSourceExcerpt: sourceText is null
                ? null
                : sourceText[..Math.Min(1500, sourceText.Length)]);

        if (dryRun || changedNumbers.Count == 0)
        {
            // Nothing is persisted on an audit pass; drop the in-memory edits so a
            // later SaveChanges on this scope cannot flush a dry run.
            if (dryRun) foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Unchanged;
            return report;
        }

        SyncAuthoredJson(paper, recoveredByNumber, changedNumbers);

        paper.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "ListeningPartBCSourceStemsRecovered",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            Details = JsonSerializer.Serialize(new
            {
                recovered = recoveredCount,
                unrecoverable = unrecoverableCount,
                alreadyUsable = alreadyUsableCount,
                numbers = changedNumbers,
                summary = ListeningPartBCSourceParser.Describe(parsed),
            }),
        });

        await db.SaveChangesAsync(ct);
        return report;
    }

    /// <summary>
    /// Every cached per-asset text entry on the paper. The entry keys are asset
    /// ids; structured authoring keys such as <c>listeningQuestions</c> are
    /// arrays/objects and are skipped by the string-kind test.
    /// </summary>
    private static IEnumerable<string?> ReadAssetTexts(ContentPaper paper)
    {
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson)) yield break;

        Dictionary<string, JsonElement>? root;
        try
        {
            root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson);
        }
        catch (JsonException)
        {
            yield break;
        }
        if (root is null) yield break;

        // Prefer the question-paper asset when the paper carries per-role assets;
        // fall back to every cached text entry, because the bulk importer stores
        // one whole-booklet PDF whose asset row may carry no part label at all.
        var questionPaperAssetIds = paper.Assets
            .Where(asset => asset.Role == PaperAssetRole.QuestionPaper)
            .Select(asset => asset.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var assetId in questionPaperAssetIds)
        {
            if (root.TryGetValue(assetId, out var element) && element.ValueKind == JsonValueKind.String)
            {
                yield return element.GetString();
            }
        }

        foreach (var (key, element) in root)
        {
            if (questionPaperAssetIds.Contains(key)) continue;
            if (element.ValueKind != JsonValueKind.String) continue;
            yield return element.GetString();
        }
    }

    private static bool HasUsableOptions(IReadOnlyList<ListeningQuestionOption>? options)
    {
        if (options is null || options.Count < 3) return false;
        return options
            .Take(3)
            .All(option => !string.IsNullOrWhiteSpace(ListeningLearnerService.SanitizeOptionText(option.Text)));
    }

    /// <summary>
    /// Fill blank/placeholder option prose from the source. Option KEYS and
    /// <c>IsCorrect</c> are never touched, so the answer key and every stored
    /// learner answer stay valid; only the displayed text changes.
    /// </summary>
    private static int ApplyOptions(IReadOnlyList<ListeningQuestionOption>? options, ListeningPartBCSourceItem source)
    {
        if (options is null || options.Count == 0) return 0;

        var byKey = options
            .Where(option => !string.IsNullOrWhiteSpace(option.OptionKey))
            .GroupBy(option => option.OptionKey.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var updated = 0;
        foreach (var (key, text) in new[] { ("A", source.OptionA), ("B", source.OptionB), ("C", source.OptionC) })
        {
            if (!byKey.TryGetValue(key, out var option)) continue;
            if (!string.IsNullOrWhiteSpace(ListeningLearnerService.SanitizeOptionText(option.Text))) continue;
            if (string.Equals(option.Text, text, StringComparison.Ordinal)) continue;
            option.Text = text;
            option.Version += 1;
            updated++;
        }
        return updated;
    }

    /// <summary>
    /// Mirror the recovered wording into <c>ExtractedTextJson.listeningQuestions</c>.
    /// The authored JSON is a live fallback and the source a relational backfill
    /// re-projects from, so leaving it stale would let the blank stem come back.
    /// Only the recovered numbers' <c>stem</c>/<c>options</c> are touched; every
    /// other key and the array order are preserved.
    /// </summary>
    private static void SyncAuthoredJson(
        ContentPaper paper,
        IReadOnlyDictionary<int, ListeningPartBCSourceItem> recovered,
        IReadOnlyList<int> changedNumbers)
    {
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson)) return;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(paper.ExtractedTextJson);
        }
        catch (JsonException)
        {
            return;
        }
        if (root is not JsonObject rootObject) return;
        if (rootObject[QuestionsKey] is not JsonArray authored) return;

        var changed = changedNumbers.ToHashSet();
        var mutated = false;

        foreach (var entry in authored)
        {
            if (entry is not JsonObject item) continue;
            if (!TryReadNumber(item, out var number)) continue;
            if (!changed.Contains(number)) continue;
            if (!recovered.TryGetValue(number, out var source)) continue;

            var currentStem = item["stem"]?.GetValue<string>()
                ?? (item["text"]?.GetValueKind() == JsonValueKind.String ? item["text"]!.GetValue<string>() : null);
            if (!ListeningLearnerService.IsUsablePartBCStem(currentStem))
            {
                item["stem"] = source.Stem;
                // `text` is the legacy alias the learner projection also reads.
                if (item.ContainsKey("text")) item["text"] = source.Stem;
                mutated = true;
            }

            if (item["options"] is JsonArray optionArray && optionArray.Count >= 3)
            {
                for (var index = 0; index < 3; index++)
                {
                    var existing = optionArray[index]?.GetValueKind() == JsonValueKind.String
                        ? optionArray[index]!.GetValue<string>()
                        : null;
                    if (!string.IsNullOrWhiteSpace(ListeningLearnerService.SanitizeOptionText(existing))) continue;
                    optionArray[index] = source.Options[index];
                    mutated = true;
                }
            }
        }

        if (mutated) paper.ExtractedTextJson = rootObject.ToJsonString();
    }

    private static bool TryReadNumber(JsonObject item, out int number)
    {
        number = 0;
        var raw = item["number"];
        if (raw is null) return false;
        return raw.GetValueKind() switch
        {
            JsonValueKind.Number => raw.AsValue().TryGetValue(out number),
            JsonValueKind.String => int.TryParse(raw.GetValue<string>(), out number),
            _ => false,
        };
    }
}
