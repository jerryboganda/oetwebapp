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

    /// <summary>Find every published Part B/C stem/option carrying a stray
    /// watermark letter and re-derive it from a fresh extraction of the printed
    /// paper. Writes ONLY the items whose <c>paperId:number</c> key is in
    /// <paramref name="approvedKeys"/>; null or empty = dry run.</summary>
    Task<ListeningPartBCWatermarkReport> RepairWatermarkResidueAsync(
        IReadOnlyCollection<string>? approvedKeys, string adminId, CancellationToken ct);
}

/// <summary>One stored Part B/C field (stem or option) of an item with watermark residue.</summary>
public sealed record ListeningPartBCWatermarkField(string Field, string Current, string? Proposed);

/// <summary>
/// One Part B/C item carrying a stray watermark letter. <c>verified</c>: the
/// fresh re-read of the printed paper equals the stored text with the stray
/// letters removed, in every field. <c>needs-review</c>: the re-read differs
/// in some other way (e.g. a stray "A" moved an option boundary), so a human
/// must compare. <c>no-source</c>: the paper text could not re-derive the item.
/// </summary>
public sealed record ListeningPartBCWatermarkItem(
    string Key,
    string PaperId,
    string PaperTitle,
    int Number,
    string Status,
    IReadOnlyList<ListeningPartBCWatermarkField> Fields,
    string? Detail);

public sealed record ListeningPartBCWatermarkReport(
    bool DryRun,
    int PapersScanned,
    int PapersAffected,
    int ItemsWithResidue,
    int Verified,
    int NeedsReview,
    int NoSource,
    int Applied,
    IReadOnlyList<ListeningPartBCWatermarkItem> Items,
    IReadOnlyList<string> Failures);

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

        // A paper whose Part B/C content lives only in the authored JSON
        // projection — no relational rows at all, the shape some bulk imports
        // took — is invisible to the relational recovery below (there is
        // nothing in ListeningQuestions to iterate). Recover its stems/options
        // directly in ExtractedTextJson.listeningQuestions instead: same
        // parser, same cached source text, same precision-first guards: only
        // the write target differs.
        if (questions.Count == 0)
        {
            return await RecoverFromAuthoredJsonAsync(paper, dryRun, adminId, ct);
        }

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

        var (sourceText, parsed) = await ResolveSourceTextAsync(paper, wanted, ct);
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
    /// Resolve the question-paper text to parse Part B/C stems/options from, and
    /// parse it. The cached extraction is not necessarily usable: a paper
    /// ingested before the PDF engine produced real layout has either nothing
    /// cached or a long structureless run, and the normal extraction pass skips
    /// any asset that already has an entry — so it would stay unreadable
    /// forever. Re-extract once whenever the cache cannot supply every wanted
    /// number, and keep whichever pass reads more. This must run BEFORE any
    /// edit is staged on <paramref name="paper"/>, so the extraction service's
    /// own SaveChanges cannot flush a half-finished repair.
    /// </summary>
    private async Task<(string? SourceText, ListeningPartBCSourceParseResult Parsed)> ResolveSourceTextAsync(
        ContentPaper paper, HashSet<int> wanted, CancellationToken ct)
    {
        var sourceText = ListeningPartBCSourceParser.SelectQuestionPaperText(
            ReadAssetTexts(paper), wanted);

        var parsed = wanted.Count == 0
            ? ListeningPartBCSourceParseResult.Empty
            : ListeningPartBCSourceParser.Parse(sourceText, wanted);

        if (wanted.Count > 0 && parsed.Items.Count < wanted.Count && textExtraction is not null)
        {
            try
            {
                await textExtraction.ExtractForPaperAsync(paper.Id, ct, force: true);
                var refreshedText = ListeningPartBCSourceParser.SelectQuestionPaperText(ReadAssetTexts(paper), wanted);
                var refreshed = ListeningPartBCSourceParser.Parse(refreshedText, wanted);
                if (refreshed.Items.Count > parsed.Items.Count)
                {
                    sourceText = refreshedText;
                    parsed = refreshed;
                }
            }
            catch (Exception)
            {
                // Extraction is best-effort; a failure leaves the cached-text
                // result in place and is reported per item below.
            }
        }

        return (sourceText, parsed);
    }

    /// <summary>
    /// Recovery path for a paper whose Part B/C content has no relational
    /// <c>ListeningQuestions</c> rows at all — only an authored JSON projection
    /// on <c>ExtractedTextJson.listeningQuestions</c>, the shape some bulk
    /// imports took. <see cref="RecoverPaperAsync"/> reads/writes stems and
    /// options against that array directly instead of the relational table.
    /// Every guard is identical to the relational path: only an unusable
    /// stem/option is ever replaced, and only with text the paper's own
    /// question-paper source unambiguously supports.
    /// </summary>
    private async Task<ListeningPartBCRecoveryReport> RecoverFromAuthoredJsonAsync(
        ContentPaper paper, bool dryRun, string adminId, CancellationToken ct)
    {
        JsonNode? root;
        try
        {
            root = string.IsNullOrWhiteSpace(paper.ExtractedTextJson)
                ? null
                : JsonNode.Parse(paper.ExtractedTextJson);
        }
        catch (JsonException)
        {
            root = null;
        }

        var authored = (root as JsonObject)?[QuestionsKey] as JsonArray;
        var bcEntries = new List<(int Number, JsonObject Item)>();
        if (authored is not null)
        {
            foreach (var entry in authored)
            {
                if (entry is not JsonObject item) continue;
                if (!TryReadNumber(item, out var number)) continue;
                if (number is < ListeningPartBCSourceParser.FirstNumber or > ListeningPartBCSourceParser.LastNumber) continue;
                bcEntries.Add((number, item));
            }
        }

        if (bcEntries.Count == 0)
        {
            // No JSON-authored Part B/C content either — nothing this path can
            // do. Matches the relational path's shape for a paper with no
            // Part B/C items at all.
            return new ListeningPartBCRecoveryReport(
                PaperId: paper.Id,
                PaperTitle: paper.Title,
                PaperSlug: paper.Slug,
                Status: paper.Status.ToString(),
                DryRun: dryRun,
                SourceTextAvailable: false,
                PartBCQuestionCount: 0,
                AlreadyUsable: 0,
                Recovered: 0,
                Unrecoverable: 0,
                Items: []);
        }

        static string? CurrentStem(JsonObject item) =>
            item["stem"]?.GetValueKind() == JsonValueKind.String ? item["stem"]!.GetValue<string>()
            : item["text"]?.GetValueKind() == JsonValueKind.String ? item["text"]!.GetValue<string>()
            : null;

        var needsStem = bcEntries
            .Where(entry => !ListeningLearnerService.IsUsablePartBCStem(CurrentStem(entry.Item)))
            .Select(entry => entry.Number)
            .ToHashSet();
        var needsOptions = bcEntries
            .Where(entry => !HasUsableOptionsJson(entry.Item["options"] as JsonArray))
            .Select(entry => entry.Number)
            .ToHashSet();
        var wanted = needsStem.Union(needsOptions).ToHashSet();

        var (sourceText, parsed) = await ResolveSourceTextAsync(paper, wanted, ct);
        var recoveredByNumber = parsed.Items.ToDictionary(item => item.Number);
        var skipByNumber = parsed.Skipped.ToDictionary(skip => skip.Number);

        var items = new List<ListeningPartBCRecoveryItem>(bcEntries.Count);
        var recoveredCount = 0;
        var alreadyUsableCount = 0;
        var unrecoverableCount = 0;
        var mutated = false;

        foreach (var (number, item) in bcEntries.OrderBy(entry => entry.Number))
        {
            var stemUsable = !needsStem.Contains(number);
            var optionsUsable = !needsOptions.Contains(number);
            var previousStem = CurrentStem(item);

            if (stemUsable && optionsUsable)
            {
                alreadyUsableCount++;
                items.Add(new(number, "already-usable", previousStem, null, 0, null));
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
                items.Add(new(number, "unrecoverable", previousStem, null, 0, detail));
                continue;
            }

            var optionsUpdated = 0;
            if (!dryRun)
            {
                if (!stemUsable)
                {
                    item["stem"] = source.Stem;
                    // `text` is the legacy alias the learner projection also reads.
                    if (item.ContainsKey("text")) item["text"] = source.Stem;
                    mutated = true;
                }

                if (!optionsUsable && item["options"] is JsonArray optionArray && optionArray.Count >= 3)
                {
                    for (var index = 0; index < 3; index++)
                    {
                        var existing = optionArray[index]?.GetValueKind() == JsonValueKind.String
                            ? optionArray[index]!.GetValue<string>()
                            : null;
                        if (!string.IsNullOrWhiteSpace(ListeningLearnerService.SanitizeOptionText(existing))) continue;
                        optionArray[index] = source.Options[index];
                        optionsUpdated++;
                        mutated = true;
                    }
                }
            }

            recoveredCount++;
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
            PartBCQuestionCount: bcEntries.Count,
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

        if (!dryRun && mutated && root is JsonObject rootObject)
        {
            paper.ExtractedTextJson = rootObject.ToJsonString();
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
                    source = "authored-json",
                    summary = ListeningPartBCSourceParser.Describe(parsed),
                }),
            });

            await db.SaveChangesAsync(ct);
        }

        return report;
    }

    // -- Watermark residue repair ---------------------------------------------
    // The August recovery sweeps parsed text from the word-box extractor before
    // it filtered watermark glyphs, so lone S/A/M/P/L/E/B/N/K letters were
    // written into stems and options that are otherwise perfectly "usable" --
    // RecoverPaperAsync never looks at them again. This pass targets exactly
    // those items, in BOTH stores (relational rows and the authored JSON the
    // learner projection prefers). Option keys, correctness, numbering and
    // scoring are never touched.

    public async Task<ListeningPartBCWatermarkReport> RepairWatermarkResidueAsync(
        IReadOnlyCollection<string>? approvedKeys, string adminId, CancellationToken ct)
    {
        var approved = (approvedKeys ?? []).ToHashSet(StringComparer.Ordinal);
        var dryRun = approved.Count == 0;

        var paperIds = await db.ContentPapers.AsNoTracking()
            .Where(p => p.SubtestCode == "listening" && p.Status == ContentStatus.Published)
            .OrderBy(p => p.Title)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var items = new List<ListeningPartBCWatermarkItem>();
        var failures = new List<string>();
        var affected = 0;
        var applied = 0;
        foreach (var paperId in paperIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (paperItems, paperApplied) = await RepairPaperWatermarkAsync(paperId, approved, adminId, ct);
                if (paperItems.Count > 0) affected++;
                items.AddRange(paperItems);
                applied += paperApplied;
            }
            catch (Exception ex)
            {
                failures.Add($"{paperId}: {ex.Message}");
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return new ListeningPartBCWatermarkReport(
            DryRun: dryRun,
            PapersScanned: paperIds.Count,
            PapersAffected: affected,
            ItemsWithResidue: items.Count,
            Verified: items.Count(i => i.Status == "verified"),
            NeedsReview: items.Count(i => i.Status == "needs-review"),
            NoSource: items.Count(i => i.Status == "no-source"),
            Applied: applied,
            Items: items,
            Failures: failures);
    }

    private async Task<(List<ListeningPartBCWatermarkItem> Items, int Applied)> RepairPaperWatermarkAsync(
        string paperId, HashSet<string> approved, string adminId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.Include(p => p.Assets).FirstAsync(p => p.Id == paperId, ct);

        var questions = await db.ListeningQuestions
            .Where(q => q.PaperId == paperId
                && q.QuestionNumber >= ListeningPartBCSourceParser.FirstNumber
                && q.QuestionNumber <= ListeningPartBCSourceParser.LastNumber)
            .ToListAsync(ct);
        var questionIds = questions.Select(q => q.Id).ToList();
        var optionsByQuestion = (await db.ListeningQuestionOptions
                .Where(o => questionIds.Contains(o.ListeningQuestionId))
                .ToListAsync(ct))
            .GroupBy(o => o.ListeningQuestionId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(o => o.OptionKey.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                    .ToDictionary(k => k.Key, k => k.First(), StringComparer.Ordinal),
                StringComparer.Ordinal);

        // Every stored field per printed number, from both stores.
        var fields = new Dictionary<int, List<(string Field, string Current)>>();
        void Add(int number, string field, string? value)
        {
            if (value is null) return;
            if (!fields.TryGetValue(number, out var list)) fields[number] = list = [];
            list.Add((field, value));
        }
        foreach (var question in questions)
        {
            Add(question.QuestionNumber, "stem", question.Stem);
            var options = optionsByQuestion.GetValueOrDefault(question.Id);
            foreach (var key in new[] { "A", "B", "C" })
                Add(question.QuestionNumber, $"option{key}", options?.GetValueOrDefault(key)?.Text);
        }
        foreach (var (number, item) in ReadAuthoredPartBC(paper.ExtractedTextJson))
        {
            Add(number, "json.stem", JsonString(item["stem"]) ?? JsonString(item["text"]));
            if (item["options"] is JsonArray optionArray)
                for (var index = 0; index < Math.Min(3, optionArray.Count); index++)
                    Add(number, "json.option" + (char)('A' + index), JsonString(optionArray[index]));
        }

        bool HasResidue(int number) => fields[number].Any(f => ListeningPartBCSourceParser.HasWatermarkResidue(f.Current));
        if (!fields.Keys.Any(HasResidue)) return ([], 0);

        // The cached text is the polluted pre-fix extraction: always re-read the
        // PDFs with the watermark-filtering extractor before comparing.
        if (textExtraction is not null)
        {
            try { await textExtraction.ExtractForPaperAsync(paper.Id, ct, force: true); }
            catch (Exception) { /* reported per item as no-source below */ }
        }
        // Compare EVERY Part B/C item on an affected paper, not only the ones
        // with a visible stray letter: when the stray letter was an "A" the
        // parser took it as the option marker, so the stem swallowed the real
        // "A <words>" (which reads as a sentence-initial article) and option A
        // lost its opening words -- no lone letter is left to detect.
        var numbers = fields.Keys.ToHashSet();
        var parsed = ListeningPartBCSourceParser.Parse(
            ListeningPartBCSourceParser.SelectQuestionPaperText(ReadAssetTexts(paper), numbers), numbers);
        var sourceByNumber = parsed.Items.ToDictionary(item => item.Number);
        var skipByNumber = parsed.Skipped.ToDictionary(skip => skip.Number);

        var report = new List<ListeningPartBCWatermarkItem>();
        var toApply = new List<ListeningPartBCSourceItem>();
        foreach (var number in numbers.Order())
        {
            var key = $"{paper.Id}:{number}";
            var source = sourceByNumber.GetValueOrDefault(number);
            if (source is null)
            {
                if (!HasResidue(number)) continue;
                report.Add(new(key, paper.Id, paper.Title, number, "no-source",
                    fields[number]
                        .Where(f => ListeningPartBCSourceParser.HasWatermarkResidue(f.Current))
                        .Select(f => new ListeningPartBCWatermarkField(f.Field, f.Current, null))
                        .ToList(),
                    skipByNumber.GetValueOrDefault(number)?.Detail
                        ?? $"Q{number} could not be re-derived from the question-paper text."));
                continue;
            }

            var changed = fields[number]
                .Select(f => new ListeningPartBCWatermarkField(f.Field, f.Current, SourceValue(source, f.Field)))
                .Where(f => Normalise(f.Current) != Normalise(f.Proposed))
                .ToList();
            if (changed.Count == 0) continue;

            var status = changed.All(f => Normalise(ListeningPartBCSourceParser.StripWatermarkResidue(f.Current)) == Normalise(f.Proposed))
                ? "verified"
                : "needs-review";
            report.Add(new(key, paper.Id, paper.Title, number, status, changed, null));

            if (approved.Contains(key)) toApply.Add(source);
        }

        if (toApply.Count == 0) return (report, 0);

        foreach (var source in toApply)
        {
            var question = questions.FirstOrDefault(q => q.QuestionNumber == source.Number);
            if (question is null) continue;
            question.Stem = source.Stem;
            var options = optionsByQuestion.GetValueOrDefault(question.Id);
            foreach (var (key, text) in new[] { ("A", source.OptionA), ("B", source.OptionB), ("C", source.OptionC) })
            {
                if (options?.GetValueOrDefault(key) is not { } option || option.Text == text) continue;
                option.Text = text;
                option.Version += 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(paper.ExtractedTextJson)
            && JsonNode.Parse(paper.ExtractedTextJson) is JsonObject root
            && root[QuestionsKey] is JsonArray authored)
        {
            var byNumber = toApply.ToDictionary(item => item.Number);
            foreach (var entry in authored)
            {
                if (entry is not JsonObject item || !TryReadNumber(item, out var number)) continue;
                if (!byNumber.TryGetValue(number, out var source)) continue;
                item["stem"] = source.Stem;
                // `text` is the legacy alias the learner projection also reads.
                if (item.ContainsKey("text")) item["text"] = source.Stem;
                if (item["options"] is JsonArray optionArray)
                    for (var index = 0; index < Math.Min(3, optionArray.Count); index++)
                        optionArray[index] = source.Options[index];
            }
            paper.ExtractedTextJson = root.ToJsonString();
        }

        var appliedNumbers = toApply.Select(item => item.Number).ToHashSet();
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "ListeningPartBCWatermarkResidueRepaired",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            // The full before/after for every applied item: this IS the rollback record.
            Details = JsonSerializer.Serialize(new
            {
                numbers = appliedNumbers.Order().ToList(),
                items = report.Where(item => appliedNumbers.Contains(item.Number)).ToList(),
            }),
        });
        await db.SaveChangesAsync(ct);
        return (report, toApply.Count);
    }

    private static string SourceValue(ListeningPartBCSourceItem source, string field) => field switch
    {
        "stem" or "json.stem" => source.Stem,
        "optionA" or "json.optionA" => source.OptionA,
        "optionB" or "json.optionB" => source.OptionB,
        _ => source.OptionC,
    };

    private static string Normalise(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? JsonString(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static IEnumerable<(int Number, JsonObject Item)> ReadAuthoredPartBC(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson)) yield break;
        JsonNode? root;
        try { root = JsonNode.Parse(extractedTextJson); }
        catch (JsonException) { yield break; }
        if ((root as JsonObject)?[QuestionsKey] is not JsonArray authored) yield break;
        foreach (var entry in authored)
        {
            if (entry is JsonObject item && TryReadNumber(item, out var number)
                && number is >= ListeningPartBCSourceParser.FirstNumber and <= ListeningPartBCSourceParser.LastNumber)
                yield return (number, item);
        }
    }

    private static bool HasUsableOptionsJson(JsonArray? options)
    {
        if (options is null || options.Count < 3) return false;
        for (var index = 0; index < 3; index++)
        {
            var text = options[index]?.GetValueKind() == JsonValueKind.String
                ? options[index]!.GetValue<string>()
                : null;
            if (string.IsNullOrWhiteSpace(ListeningLearnerService.SanitizeOptionText(text))) return false;
        }
        return true;
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
