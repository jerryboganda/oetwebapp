using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

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
    /// bump) when the pass changed nothing. A pass can run for minutes (OCR), so
    /// the result is never written back over the blob as it was at the start:
    /// the row is re-read immediately before the write and ONLY this pass's keys
    /// (asset texts, the failure markers) are merged into it, so a save that
    /// landed meanwhile (an admin structure save, Listening authoring) survives
    /// whether or not that writer bumps <c>RowVersion</c>. The write itself bumps
    /// <c>ContentPaper.RowVersion</c> against the value just read; a writer that
    /// slips in during those few milliseconds costs a re-merge (up to
    /// <c>MaxWriteAttempts</c>), not the extraction already paid for, and only
    /// then <see cref="DbUpdateConcurrencyException"/>.
    /// </para>
    ///
    /// <para>
    /// Cancellation (the ai-worker stops on every deploy) keeps the assets already
    /// extracted earlier in the same pass: they are saved, within a short bound,
    /// before the cancellation propagates.
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

    /// <summary>
    /// Reserved <c>ExtractedTextJson</c> key: an OBJECT <c>{ "&lt;assetId&gt;": attempts }</c>
    /// of the assets whose automatic attempts are used up. Here, unlike in
    /// <see cref="FailuresKey"/>, the asset id IS a JSON key on purpose: the
    /// worker's SQL pre-filter reads "id appears as a key" as "nothing left to
    /// do", so an exhausted asset drops out of the candidate set instead of
    /// being reloaded on every full cycle for the rest of its life. Nothing reads
    /// it as text (the service itself still decides from the failure marker), and
    /// a forced extraction that succeeds removes it with the marker. Assets still
    /// in back-off stay candidates: that is bounded (a few attempts over about a day).
    /// </summary>
    internal const string ExhaustedKey = "extractionExhausted";

    /// <summary>After this many failed automatic attempts the asset is left
    /// alone until a forced extraction (<c>force: true</c>) succeeds.</summary>
    internal const int MaxAutomaticAttempts = 5;

    /// <summary>How often the final write re-merges onto a row a concurrent writer
    /// changed in the milliseconds between the re-read and the save.</summary>
    internal const int MaxWriteAttempts = 3;

    /// <summary>Bound on the save that keeps already-extracted assets when the pass is
    /// cancelled: it must not hold a stopping host (the ai-worker has a 90 s grace).</summary>
    private static readonly TimeSpan CancelledWriteBudget = TimeSpan.FromSeconds(15);

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

    /// <summary>What one pass produced, held apart from the stored blob until the
    /// write, so the blob is merged onto the row as it is THEN, not as it was when
    /// the pass started (minutes earlier, across OCR calls).</summary>
    private sealed class PassOutcome
    {
        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ExtractionFailure> Failures { get; } = new(StringComparer.Ordinal);

        public bool HasChanges => Texts.Count > 0 || Failures.Count > 0;
    }

    public async Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct, bool force = false)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null) return 0;

        // These snapshots only decide WHAT to extract; the write re-reads the blob.
        var existing = ReadExistingPayload(paper.ExtractedTextJson);
        var failures = ReadFailures(existing);
        var outcome = new PassOutcome();

        try
        {
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

                    outcome.Texts[asset.Id] = text;
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
                    outcome.Failures[asset.Id] = failure;
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
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping (every deploy stops the ai-worker, and a paper
            // can hold several PDFs, each possibly through a paid OCR tier). What
            // earlier assets of this paper already produced is real, paid-for work:
            // keep it, within a short bound, then let the cancellation propagate.
            await PersistOnCancellationAsync(paper, outcome, force);
            throw;
        }

        return await WriteOutcomeAsync(paper, outcome, force, ct);
    }

    /// <summary>Best effort: a failure here must never replace the cancellation.</summary>
    private async Task PersistOnCancellationAsync(ContentPaper paper, PassOutcome outcome, bool force)
    {
        if (!outcome.HasChanges) return;

        using var grace = new CancellationTokenSource(CancelledWriteBudget);
        try
        {
            var kept = await WriteOutcomeAsync(paper, outcome, force, grace.Token);
            logger.LogInformation(
                "{Event} paperId={PaperId} keptAssets={Kept}", "pdf.extract.cancel_persisted", paper.Id, kept);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Event} paperId={PaperId}", "pdf.extract.cancel_persist_failed", paper.Id);
        }
    }

    /// <summary>
    /// Merges the pass's results into the paper's blob as it is NOW and saves,
    /// returning how many asset texts were written. Only this pass's keys are
    /// touched (asset texts and the failure markers), so a save that landed while
    /// the pass was extracting (an admin Speaking/Writing structure save, which does
    /// not bump <c>RowVersion</c> on its own, or Listening authoring) survives; the
    /// write then fences on the <c>RowVersion</c> read together with that blob.
    /// </summary>
    private async Task<int> WriteOutcomeAsync(ContentPaper paper, PassOutcome outcome, bool force, CancellationToken ct)
    {
        // Nothing extracted and no failure to record: leave the row alone. The
        // old pass rewrote the whole blob and bumped UpdatedAt for every paper
        // it looked at, which is also what kept rotating the worker's window.
        if (!outcome.HasChanges) return 0;

        for (var attempt = 1; ; attempt++)
        {
            var current = await db.ContentPapers.AsNoTracking()
                .Where(p => p.Id == paper.Id)
                .Select(p => new { p.ExtractedTextJson, p.RowVersion })
                .FirstOrDefaultAsync(ct);
            if (current is null) return 0; // deleted while the pass ran

            var payload = ReadExistingPayload(current.ExtractedTextJson);
            var stored = ReadFailures(payload);
            var written = 0;
            var changed = false;

            foreach (var (assetId, text) in outcome.Texts)
            {
                // Cached meanwhile by another writer: theirs stands (a forced pass replaces it by design).
                if (!force && payload.ContainsKey(assetId)) continue;
                payload[assetId] = JsonSerializer.SerializeToElement(text);
                stored.Remove(assetId);
                written++;
                changed = true;
            }

            foreach (var (assetId, failure) in outcome.Failures)
            {
                // A failed forced pass keeps the text it did not replace and still records
                // the marker; for a normal pass, an entry that appeared meanwhile makes the
                // marker pointless.
                if (!force && payload.ContainsKey(assetId)) continue;
                stored[assetId] = failure;
                changed = true;
            }

            if (!changed) return 0;

            ApplyFailureKeys(payload, stored);

            // ContentPaper.RowVersion is the concurrency token Listening authoring
            // already bumps. Fencing on the value read together with the blob makes a
            // writer that slips in after this read lose the compare instead of being
            // overwritten; only the properties set here are written, so any other
            // pending change on a caller-tracked row is left alone.
            db.Entry(paper).Property(p => p.RowVersion).OriginalValue = current.RowVersion;
            paper.RowVersion = current.RowVersion + 1;
            paper.ExtractedTextJson = JsonSerializer.Serialize(payload);
            if (written > 0) paper.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
                return written;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxWriteAttempts)
            {
                // A writer got in between the re-read and the save: merge again onto
                // the newer row rather than discard extraction that was already paid for.
            }
        }
    }

    /// <summary>Writes (or removes) the two reserved bookkeeping keys from the stored markers.</summary>
    private static void ApplyFailureKeys(Dictionary<string, JsonElement> payload, Dictionary<string, ExtractionFailure> stored)
    {
        if (stored.Count > 0)
        {
            payload[FailuresKey] = JsonSerializer.SerializeToElement(
                stored.Values.OrderBy(failure => failure.AssetId, StringComparer.Ordinal).ToList(),
                FailureJson);
        }
        else
        {
            payload.Remove(FailuresKey);
        }

        var exhausted = stored.Values
            .Where(failure => failure.Attempts >= MaxAutomaticAttempts)
            .OrderBy(failure => failure.AssetId, StringComparer.Ordinal)
            .ToDictionary(failure => failure.AssetId, failure => failure.Attempts, StringComparer.Ordinal);
        if (exhausted.Count > 0)
        {
            payload[ExhaustedKey] = JsonSerializer.SerializeToElement(exhausted);
        }
        else
        {
            payload.Remove(ExhaustedKey);
        }
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

                // Optional remote producer (registered only on PostgreSQL, scoped, so it shares this paper's
                // DbContext). Absent, flag off, no healthy node or any doubt = Local: the in-process pass below,
                // exactly as before. The id cursor in NextBatchAsync is what rotates the window, so a paper that
                // remote jobs own is simply revisited on the next lap; nothing is touched to make it rotate.
                var remoteProducer = paperScope.ServiceProvider.GetService<IRemotePdfExtractionProducer>();
                if (remoteProducer is not null
                    && await remoteProducer.HandlePaperAsync(id, ct) != RemoteExtractionDecision.Local)
                {
                    continue;
                }

                var svc = paperScope.ServiceProvider.GetRequiredService<IContentTextExtractionService>();
                total += await svc.ExtractForPaperAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Writers kept saving this paper faster than the write could re-merge
                // (it already retried MaxWriteAttempts times). The asset is still
                // uncached, so a later pass picks it up.
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
    /// pre-filter. An asset whose automatic attempts are used up is listed under
    /// <see cref="ContentTextExtractionService.ExhaustedKey"/> with its id as a key,
    /// so it leaves the candidate set instead of being reloaded every cycle for
    /// nothing; an asset merely in back-off stays a candidate (a bounded, short wait).
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
