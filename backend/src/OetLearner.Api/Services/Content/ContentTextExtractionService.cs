using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// PDF text extraction — Slice 7
//
// Design: thin IPdfTextExtractor interface so the concrete engine can be
// swapped without touching the service layer. The default implementation is
// a no-op (returns empty string) so the subsystem stays fully functional in
// test / CI without requiring a native PDF library.
//
// Production swap-in: implement this interface against PdfPig (UglyToad.PdfPig,
// MIT license, pure C#, no native deps). Adding the package is a separate,
// opt-in step that needs NuGet access during deploy — flagged in the
// operator handoff items.
// ═════════════════════════════════════════════════════════════════════════════

public interface IPdfTextExtractor
{
    /// <summary>Extract plain text from a PDF stream. Return empty string
    /// if the engine is a no-op or the PDF has no extractable text.</summary>
    Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct);

    /// <summary>
    /// Extract text one page at a time, index 0 = page 1.
    ///
    /// <para>
    /// Needed because a citation that cannot say which page it came from is not
    /// much of a citation: the companion is required to point at an exact
    /// location, and <see cref="ExtractAsync"/> concatenates the document into a
    /// single string where that information is gone. Splitting the flat result
    /// back apart afterwards is not possible — a blank line inside a page is
    /// indistinguishable from a page break.
    /// </para>
    ///
    /// <para>
    /// The default implementation returns the whole document as one page, which
    /// is honest for engines that genuinely cannot paginate; PdfPig overrides it.
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<string>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct)
    {
        var text = await ExtractAsync(pdfStream, ct);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }
}

/// <summary>No-op default. Replaced at DI time by a PdfPig-backed impl in
/// production deployments that have the package installed.</summary>
public sealed class NoOpPdfTextExtractor : IPdfTextExtractor
{
    public Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
        => Task.FromResult(string.Empty);
}

// ═════════════════════════════════════════════════════════════════════════════
// Service + hosted worker
// ═════════════════════════════════════════════════════════════════════════════

public interface IContentTextExtractionService
{
    /// <summary>Extract text for every PDF asset on a paper and persist it
    /// into <c>ContentPaper.ExtractedTextJson</c> as asset-id string entries.
    /// Existing structured authoring keys such as <c>listeningQuestions</c>
    /// are preserved.
    ///
    /// <para>
    /// An extractor that RETURNS (even an empty string: a scan with no text
    /// layer) is a result and is cached once. An extraction that THROWS (blob
    /// missing, storage or provider error) is not cached: caching it as empty
    /// text used to leave the paper permanently unreadable. It is recorded as a
    /// per-asset failure marker with a retry-after and an attempt cap instead,
    /// so a paid OCR tier is never re-billed on every pass.
    /// </para>
    ///
    /// <para>
    /// Nothing is written (no <c>UpdatedAt</c> touch, no <c>RowVersion</c>
    /// bump) when the pass changed nothing. A write bumps
    /// <c>ContentPaper.RowVersion</c>, so it can collide with a concurrent
    /// admin save: the loser gets <see cref="DbUpdateConcurrencyException"/>
    /// rather than silently overwriting the other side.
    /// </para></summary>
    /// <param name="force">Re-extract every PDF asset on the paper, replacing
    /// whatever is cached, ignoring failure back-off. A cached result is
    /// skipped by the normal pass forever, so a paper ingested before the PDF
    /// engine produced usable text stays permanently unreadable without this.
    /// A forced extraction that fails keeps the previously cached text.</param>
    Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct, bool force = false);
}

public sealed class ContentTextExtractionService(
    LearnerDbContext db,
    IFileStorage storage,
    IPdfTextExtractor extractor,
    ILogger<ContentTextExtractionService> logger) : IContentTextExtractionService
{
    /// <summary>
    /// Reserved <c>ExtractedTextJson</c> key holding the per-asset failure
    /// markers: an ARRAY of <c>{ assetId, attempts, lastAttemptAt, retryAfter,
    /// error }</c>. Its value is an array, so every reader that only takes
    /// string-valued entries (asset texts) skips it, exactly as they skip the
    /// authoring keys (<c>listeningQuestions</c>, <c>writingStructure</c>, …).
    /// An array rather than an object keyed by asset id on purpose: the worker's
    /// SQL pre-filter decides "this asset has a cached entry" by looking for the
    /// asset id as a JSON key (<c>"&lt;id&gt;":</c>), and a failure marker keyed
    /// by the id would make a failed asset look cached, so it would never be
    /// retried.
    /// </summary>
    internal const string FailuresKey = "extractionFailures";

    /// <summary>After this many failed automatic attempts the asset is left
    /// alone until a forced extraction (<c>force: true</c>) succeeds.</summary>
    internal const int MaxAutomaticAttempts = 5;

    private static readonly JsonSerializerOptions FailureJson = new(JsonSerializerDefaults.Web);

    /// <summary>Back-off after the Nth consecutive failed attempt.</summary>
    internal static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(15),
        2 => TimeSpan.FromHours(1),
        3 => TimeSpan.FromHours(6),
        _ => TimeSpan.FromHours(24),
    };

    internal sealed class ExtractionFailure
    {
        public string AssetId { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public DateTimeOffset LastAttemptAt { get; set; }
        public DateTimeOffset RetryAfter { get; set; }
        public string? Error { get; set; }
    }

    public async Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct, bool force = false)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null) return 0;

        var existing = ReadExistingPayload(paper.ExtractedTextJson);
        var failures = ReadFailures(existing);

        int processed = 0;
        var dirty = false;
        foreach (var asset in paper.Assets)
        {
            if (asset.MediaAsset is null) continue;
            if (!string.Equals(asset.MediaAsset.Format, "pdf", StringComparison.OrdinalIgnoreCase)) continue;
            // `force` re-extracts unconditionally. Its only caller is Part B/C
            // stem recovery, which asks for it precisely when nothing in the
            // cache could be parsed — a length check would refuse there, because
            // the pathological case is a LONG but structureless extraction
            // (PdfPig's page.Text ran ~25 000 characters together with no word
            // spacing or line breaks).
            if (!force && existing.ContainsKey(asset.Id)) continue;
            // A recent or exhausted failure is not retried by the normal pass.
            if (!force
                && failures.TryGetValue(asset.Id, out var marker)
                && (marker.Attempts >= MaxAutomaticAttempts || marker.RetryAfter > DateTimeOffset.UtcNow))
            {
                continue;
            }

            var timer = Stopwatch.StartNew();
            var facts = PdfExtractionFacts.Begin();
            try
            {
                string text;
                long sizeBytes;
                await using (var s = await storage.OpenReadAsync(asset.MediaAsset.StoragePath, ct))
                {
                    sizeBytes = s.CanSeek ? s.Length : -1;
                    text = await extractor.ExtractAsync(s, ct) ?? string.Empty;
                }

                existing[asset.Id] = JsonSerializer.SerializeToElement(text);
                failures.Remove(asset.Id);
                processed++;
                dirty = true;
                logger.LogInformation(
                    "{Event} assetId={AssetId} sizeBytes={SizeBytes} pages={Pages} embeddedChars={EmbeddedChars} chars={Chars} tier={Tier} elapsedMs={ElapsedMs}",
                    "pdf.extract.done", asset.Id, sizeBytes, facts.Pages, facts.EmbeddedChars, text.Length,
                    PdfExtractionFacts.DescribeTier(text.Length, facts.EmbeddedChars), timer.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown or a caller that went away: not a property of the
                // asset, so neither cached nor recorded as a failure.
                throw;
            }
            catch (Exception ex)
            {
                var attempts = (failures.TryGetValue(asset.Id, out var prior) ? prior.Attempts : 0) + 1;
                var failedAt = DateTimeOffset.UtcNow;
                var failure = new ExtractionFailure
                {
                    AssetId = asset.Id,
                    Attempts = attempts,
                    LastAttemptAt = failedAt,
                    RetryAfter = failedAt + RetryDelay(attempts),
                    Error = ex.GetType().Name,
                };
                failures[asset.Id] = failure;
                dirty = true;
                logger.LogWarning(
                    ex,
                    "{Event} assetId={AssetId} attempt={Attempt} exhausted={Exhausted} retryAfter={RetryAfter:o} elapsedMs={ElapsedMs}",
                    "pdf.extract.failed", asset.Id, attempts, attempts >= MaxAutomaticAttempts,
                    failure.RetryAfter, timer.ElapsedMilliseconds);
            }
            finally
            {
                PdfExtractionFacts.End();
            }
        }

        // Nothing extracted and no failure to record: leave the row alone. The
        // old pass rewrote the whole blob and bumped UpdatedAt for every paper
        // it looked at, which is also what kept rotating the worker's window.
        if (!dirty) return processed;

        if (failures.Count > 0)
        {
            existing[FailuresKey] = JsonSerializer.SerializeToElement(
                failures.Values.OrderBy(failure => failure.AssetId, StringComparer.Ordinal).ToList(),
                FailureJson);
        }
        else
        {
            existing.Remove(FailuresKey);
        }

        paper.ExtractedTextJson = JsonSerializer.Serialize(existing);
        // ContentPaper.RowVersion is the concurrency token Listening authoring
        // already bumps. Bumping it here makes the two writers lose to each
        // other instead of one silently dropping the other's keys.
        paper.RowVersion++;
        if (processed > 0) paper.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return processed;
    }

    private static Dictionary<string, JsonElement> ReadExistingPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, JsonElement>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    /// <summary>The stored marker array as a lookup by asset id; empty when absent or unreadable.</summary>
    private static Dictionary<string, ExtractionFailure> ReadFailures(Dictionary<string, JsonElement> payload)
    {
        var byAsset = new Dictionary<string, ExtractionFailure>(StringComparer.Ordinal);
        if (!payload.TryGetValue(FailuresKey, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return byAsset;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<ExtractionFailure>>(element, FailureJson);
            foreach (var failure in stored ?? [])
            {
                if (!string.IsNullOrEmpty(failure.AssetId))
                {
                    byAsset[failure.AssetId] = failure;
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable marker is the same as none: the asset is tried again.
            byAsset.Clear();
        }

        return byAsset;
    }
}

/// <summary>
/// Background worker that periodically walks non-archived papers and extracts
/// text for PDF assets that don't yet have a cached extraction. Cheap idle loop;
/// the heavy lifting only happens after a new asset is attached.
///
/// <para>
/// Registered through <see cref="Ai.AiCostBearingHostedServiceRegistration"/>,
/// so in production only the <c>ai-worker</c> runs it (it used to run in the
/// blue slot, the green slot and the ai-worker at once, each rewriting the same
/// rows). NOTE the gate's name is a misnomer for this worker: PdfPig itself is
/// free, but <c>AutoPdfTextExtractor</c> can fall through to a paid OCR tier,
/// which is what puts it on the same switch. That switch is
/// <c>Ai:HostedWorkers:Enabled</c> (default off in Production for API slots);
/// forcing it on for an API slot restores the triplicated extraction.
/// </para>
///
/// <para>
/// Selection is by predicate ("has a PDF asset with no cached entry"), then an
/// in-memory id cursor walks the candidates 20 at a time and wraps. The cursor
/// is what keeps papers whose only uncached asset is in failure back-off from
/// pinning the batch; the previous window (oldest 20 by <c>UpdatedAt</c>)
/// rotated only because every visit bumped <c>UpdatedAt</c>.
/// </para>
/// </summary>
public sealed class ContentTextExtractionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ContentTextExtractionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    internal const int BatchSize = 20;

    /// <summary>Last paper id of the previous full batch; empty = start from
    /// the beginning. Per process: the worker is a single sequential loop.</summary>
    private string _cursor = string.Empty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(30, 90)), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Text extraction worker tick failed."); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var paperIds = await NextBatchAsync(ct);

        int total = 0;
        foreach (var id in paperIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A scope (and so a DbContext) per paper: a paper that fails
                // to save must not leave its dirty entity behind to fail the
                // next paper's SaveChanges too.
                using var paperScope = scopeFactory.CreateScope();
                var svc = paperScope.ServiceProvider.GetRequiredService<IContentTextExtractionService>();
                total += await svc.ExtractForPaperAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // An admin saved the paper while this pass was extracting. The
                // asset is still uncached, so a later pass picks it up.
                logger.LogWarning(ex, "{Event} paperId={PaperId}", "pdf.extract.conflict", id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Event} paperId={PaperId}", "pdf.extract.paper_failed", id);
            }
        }

        if (paperIds.Count > 0)
        {
            logger.LogInformation(
                "{Event} papers={Papers} extractedAssets={Extracted}", "pdf.extract.tick", paperIds.Count, total);
        }

        return total;
    }

    /// <summary>
    /// Papers with at least one PDF asset that has no cached entry, after the
    /// cursor, in id order. The cached-entry test is a substring match on the
    /// asset id used as a JSON key (<c>"&lt;id&gt;":</c>); a string value cannot
    /// contain that sequence unescaped, so it only matches a real key. The
    /// service re-checks every asset authoritatively, so this is only a
    /// pre-filter.
    /// </summary>
    internal static IQueryable<string> CandidatePaperIds(LearnerDbContext db, string cursor)
        => db.ContentPapers.AsNoTracking()
            .Where(p => p.Status != ContentStatus.Archived)
            .Where(p => p.Id.CompareTo(cursor) > 0)
            .Where(p => p.Assets.Any(a => a.MediaAsset != null
                && a.MediaAsset.Format.ToLower() == "pdf"
                && !p.ExtractedTextJson.Contains("\"" + a.Id + "\":")))
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .Take(BatchSize);

    private async Task<List<string>> NextBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var ids = await CandidatePaperIds(db, _cursor).ToListAsync(ct);
        // A short batch means the end of the candidates: start over next time.
        _cursor = ids.Count < BatchSize ? string.Empty : ids[^1];
        return ids;
    }
}
