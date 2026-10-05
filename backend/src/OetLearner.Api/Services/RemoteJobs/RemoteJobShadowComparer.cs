using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>The fields of a stored <c>pdf.extract</c> summary the comparer reads back.</summary>
public sealed record PdfSummaryView(bool NeedsOcr, int PageCount, int EmbeddedChars, string? TextSha256, string? PagesSha256);

/// <summary>Comparison of a helper's PDF result with a fresh in-process extraction. Pure.</summary>
public static class PdfParity
{
    /// <summary>
    /// The names of the fields that differ between a stored summary and the in-process oracle (empty = parity). When both sides
    /// say <c>needsOcr</c> the page count is not compared (an empty page list may report 0 or the real count).
    /// </summary>
    public static IReadOnlyList<string> Differences(PdfSummaryView remote, IReadOnlyList<string> oraclePages, int minTextLength)
    {
        var flat = string.Join("\n\n", oraclePages).Trim();
        var oracleNeedsOcr = flat.Length < minTextLength;
        var differences = new List<string>();

        if (remote.NeedsOcr != oracleNeedsOcr)
        {
            differences.Add("needsOcr");
            return differences;
        }

        if (oracleNeedsOcr) return differences;

        var oracle = RemoteCanary.Derive(oraclePages);
        if (remote.PageCount != oracle.PageCount) differences.Add("pageCount");
        if (remote.EmbeddedChars != oracle.EmbeddedChars) differences.Add("embeddedChars");
        if (!string.Equals(remote.TextSha256, oracle.TextSha256, StringComparison.Ordinal)) differences.Add("textSha256");
        if (!string.Equals(remote.PagesSha256, oracle.PagesSha256, StringComparison.Ordinal)) differences.Add("pagesSha256");
        return differences;
    }

    /// <summary>Deterministic fraction of job ids selected for verify-sampling (the same job is always in or always out).</summary>
    public static bool IsSampled(string jobId, double rate)
    {
        if (rate <= 0) return false;
        if (rate >= 1) return true;
        var bucket = uint.Parse(RemoteIds.Sha256Hex(jobId)[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bucket / (double)uint.MaxValue < rate;
    }
}

/// <summary>
/// Cutover evidence and post-cutover assurance for <c>pdf.extract</c> (OET-RWP/1 sections 6.1.5 and 9.3), run ONLY by the
/// <c>ai-worker</c> so the primary's API slots never pay for the re-extraction:
/// <list type="bullet">
/// <item><b>Shadow</b> jobs (hash-only, never applied) are compared with a FRESH in-process extraction of the asset (the stored text
/// may pre-date extractor fixes); any difference writes <c>RemoteJob.ShadowMismatch</c>.</item>
/// <item><b>Verify sampling</b> (<c>RemoteJobs:VerifySampleRate</c>, default 0) re-extracts a deterministic fraction of Applied jobs;
/// a mismatch audits, strikes the node, and replaces the helper's cached text with the in-process result, which is authoritative.</item>
/// </list>
/// A comparison is recorded in the job's summary (<c>comparison</c>) so each job is checked once. A verify candidate that the
/// deterministic sampler does not select is recorded as <c>not_sampled</c> in the same pass, so the scan window always advances to
/// newer jobs instead of re-reading the same unselected rows forever (at a 5% rate 95% of jobs are never selected).
/// </summary>
public sealed class RemoteJobShadowComparer(
    IServiceScopeFactory scopeFactory,
    RemoteJobsSettings settings,
    TimeProvider timeProvider,
    ILogger<RemoteJobShadowComparer> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>Most in-process re-extractions per purpose per pass: the ai-worker also grades and synthesises speech.</summary>
    internal const int BatchSize = 3;

    /// <summary>Verify candidates scanned (and, when not sampled, marked) per pass.</summary>
    internal const int ScanWindow = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Remote job comparer tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass: returns how many jobs received a verdict (match, mismatch, stale or unreadable).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        if (!db.Database.IsNpgsql()) return 0;

        var flags = scope.ServiceProvider.GetRequiredService<IRemoteJobFlags>();
        var snapshot = await flags.GetAsync(ct);
        var options = settings.Current;
        var compared = 0;

        if (snapshot.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Shadow))
        {
            foreach (var job in await PendingAsync(db, RemoteJobPurpose.Shadow, "Shadow", BatchSize, ct))
            {
                if (await CompareAsync(scope.ServiceProvider, db, job, authoritative: false, options, ct)) compared++;
            }
        }

        if (options.VerifySampleRate > 0 && snapshot.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply))
        {
            var window = await PendingAsync(db, RemoteJobPurpose.Apply, "Applied", ScanWindow, ct);
            var sampled = new List<RemoteJobRow>();
            var notSampled = new List<string>();
            foreach (var job in window)
            {
                if (PdfParity.IsSampled(job.Id, options.VerifySampleRate)) sampled.Add(job);
                else notSampled.Add(job.Id);
            }

            // Record the unselected rows so the next pass sees newer jobs (see the class comment).
            if (notSampled.Count > 0) await MarkManyAsync(db, notSampled, "not_sampled", ct);

            foreach (var job in sampled.Take(BatchSize))
            {
                if (await CompareAsync(scope.ServiceProvider, db, job, authoritative: true, options, ct)) compared++;
            }
        }

        return compared;
    }

    /// <summary>
    /// Uncompared jobs of the last two days, least recently touched first. A job the pass could not read is touched
    /// (<see cref="TouchAsync"/>) so it rotates to the back instead of blocking the head of the line.
    /// </summary>
    private static Task<List<RemoteJobRow>> PendingAsync(LearnerDbContext db, string purpose, string outcome, int limit, CancellationToken ct)
        => RemoteDb.QueryAsync(
            db,
            "SELECT " + RemoteJobRow.Columns("j") + " FROM \"RemoteJobs\" j"
            + " WHERE j.\"Kind\" = 'pdf.extract' AND j.\"Purpose\" = @purpose AND j.\"State\" = 'Succeeded' AND j.\"ApplyOutcome\" = @outcome"
            + " AND (j.\"ResultSummaryJson\"->>'comparison') IS NULL AND j.\"ResourceType\" = 'MediaAsset'"
            + " AND j.\"CompletedAt\" > clock_timestamp() - interval '2 days'"
            + " AND j.\"UpdatedAt\" > clock_timestamp() - interval '2 days'"
            + " ORDER BY j.\"UpdatedAt\", j.\"Id\" LIMIT @limit;",
            parameters =>
            {
                parameters.AddWithValue("purpose", purpose);
                parameters.AddWithValue("outcome", outcome);
                parameters.AddWithValue("limit", limit);
            },
            RemoteJobRow.Read,
            ct);

    /// <summary>True when a verdict was recorded for the job; false when it was left for a later pass.</summary>
    private async Task<bool> CompareAsync(
        IServiceProvider services,
        LearnerDbContext db,
        RemoteJobRow job,
        bool authoritative,
        RemoteJobsOptions options,
        CancellationToken ct)
    {
        var storage = services.GetRequiredService<IFileStorage>();
        var oracle = services.GetRequiredService<PdfPigPdfTextExtractor>();
        var assetId = job.ResourceId;

        var media = await db.MediaAssets.AsNoTracking()
            .Where(m => m.Id == assetId)
            .Select(m => new { m.Sha256, m.StoragePath })
            .FirstOrDefaultAsync(ct);
        if (media is null || !string.Equals(media.Sha256, job.InputSha256, StringComparison.Ordinal))
        {
            await MarkAsync(db, job.Id, "stale", ct);
            return true;
        }

        var summary = ReadSummary(job.ResultSummaryJson);
        if (summary is null)
        {
            await MarkAsync(db, job.Id, "unreadable", ct);
            return true;
        }

        IReadOnlyList<string> pages;
        try
        {
            await using var stream = await storage.OpenReadAsync(media.StoragePath, ct);
            pages = await oracle.ExtractPagesAsync(stream, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // The object is gone: the input the helper saw no longer exists, so there is nothing to compare against.
            logger.LogWarning(ex, "Asset {AssetId} has no stored object; remote job {JobId} is not compared.", assetId, job.Id);
            await MarkAsync(db, job.Id, "stale", ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Possibly transient: retry later, but rotate to the back so one unreadable asset cannot starve newer jobs.
            logger.LogWarning(ex, "Could not read asset {AssetId} to compare remote job {JobId}.", assetId, job.Id);
            await TouchAsync(db, job.Id, ct);
            return false;
        }

        var minTextLength = RemoteJobParams.MinTextLength(job.ParamsJson);
        var differences = PdfParity.Differences(summary, pages, minTextLength);
        var now = timeProvider.GetUtcNow();

        if (differences.Count == 0)
        {
            await MarkAsync(db, job.Id, "match", ct);
            return true;
        }

        var action = authoritative ? "RemoteJob.VerifyMismatch" : "RemoteJob.ShadowMismatch";
        await RemoteAudit.WriteAsync(
            db, RemoteAudit.ReaperActor, "remote-job-comparer", action, RemoteAudit.ResourceJob, job.Id,
            new { kind = job.Kind, differences, inputSha256 = job.InputSha256, node = job.SettledBy }, now, ct);
        await MarkAsync(db, job.Id, "mismatch", ct);

        if (!authoritative || job.SettledBy is null) return true;

        // The in-process result is authoritative: strike the node and replace the cached text (same merge rules as the applier).
        await RemoteNodeOps.AddStrikeAsync(db, job.SettledBy, "verify_sample_mismatch", options, now, ct);
        if (pages.Count == 0) return true;

        var flat = string.Join("\n\n", pages).Trim();
        var links = await db.ContentPaperAssets.AsNoTracking()
            .Where(a => a.MediaAssetId == assetId)
            .Select(a => new { a.Id, a.PaperId })
            .ToListAsync(ct);
        foreach (var link in links)
        {
            await PaperExtractedTextCommit.CommitAsync(db, link.PaperId, link.Id, flat, replaceExisting: true, now, ct);
        }

        return true;
    }

    private static PdfSummaryView? ReadSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("needsOcr", out var needsOcr) || needsOcr.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            if (!root.TryGetProperty("pageCount", out var pageCount) || !pageCount.TryGetInt32(out var pages)) return null;
            if (!root.TryGetProperty("embeddedChars", out var embedded) || !embedded.TryGetInt32(out var chars)) return null;

            static string? Text(JsonElement element, string name)
                => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            return new PdfSummaryView(needsOcr.GetBoolean(), pages, chars, Text(root, "textSha256"), Text(root, "pagesSha256"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task<int> MarkAsync(LearnerDbContext db, string jobId, string comparison, CancellationToken ct)
        => MarkManyAsync(db, [jobId], comparison, ct);

    private static Task<int> MarkManyAsync(LearnerDbContext db, IReadOnlyCollection<string> jobIds, string comparison, CancellationToken ct)
        => RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteJobs" SET
                "ResultSummaryJson" = jsonb_set(COALESCE("ResultSummaryJson", '{}'::jsonb), '{comparison}', to_jsonb(@comparison::text)),
                "UpdatedAt" = clock_timestamp()
            WHERE "Id" = ANY(@ids);
            """,
            parameters =>
            {
                parameters.AddWithValue("comparison", comparison);
                parameters.AddWithValue("ids", jobIds.ToArray());
            },
            ct);

    private static Task<int> TouchAsync(LearnerDbContext db, string jobId, CancellationToken ct)
        => RemoteDb.ExecuteAsync(
            db,
            """UPDATE "RemoteJobs" SET "UpdatedAt" = clock_timestamp() WHERE "Id" = @id;""",
            parameters => parameters.AddWithValue("id", jobId),
            ct);
}
