using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>A parsed <c>pdf.extract.result/1</c> (OET-RWP/1 section 6.1.3). Never trusted until validated.</summary>
public sealed record PdfExtractResult(
    string Schema,
    string EngineVersion,
    string InputSha256,
    string Mode,
    bool NeedsOcr,
    string? NeedsOcrReason,
    int PageCount,
    int EmbeddedChars,
    string? TextSha256,
    string? PagesSha256,
    IReadOnlyList<string> PageSha256s,
    IReadOnlyList<string>? Pages)
{
    /// <summary>The flat string <c>ExtractAsync</c> yields: pages joined by a blank line, trimmed.</summary>
    public string Flat() => string.Join("\n\n", Pages ?? Array.Empty<string>()).Trim();
}

/// <summary>Settings snapshot hashed into a PDF job's <c>SettingsHash</c> (producer and applier must agree).</summary>
public static class PdfExtractSettings
{
    public static string Hash(string mode, string provider, int minTextLength, bool replaceExisting)
        => RemoteJobKeys.SettingsHash(
        [
            new KeyValuePair<string, string>("minTextLength", minTextLength.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("mode", mode),
            new KeyValuePair<string, string>("provider", provider.Trim().ToLowerInvariant()),
            new KeyValuePair<string, string>("replaceExisting", replaceExisting ? "true" : "false"),
        ]);
}

/// <summary>Strict parsing and verification of a PDF extraction result. Pure: no database, no clock.</summary>
public static class PdfExtractResultValidator
{
    public const int MaxPageCount = 5000;
    public const int MaxPageChars = 1_000_000;
    public const int MaxTotalChars = 6_000_000;

    private static readonly HashSet<string> OcrReasons = new(StringComparer.Ordinal) { "not_pdf", "no_text_layer", "below_min_text" };

    public static bool TryParse(string resultJson, out PdfExtractResult? result, out string? error)
    {
        result = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(resultJson, new JsonDocumentOptions { MaxDepth = 8 });
            var json = new StrictJson(document.RootElement);
            if (!json.IsObject) return Fail("The result is not a JSON object.", out error);

            if (!json.TryString("schema", out var schema) || schema is null) return Fail("schema is required.", out error);
            if (!json.TryString("engineVersion", out var engine) || engine is null) return Fail("engineVersion is required.", out error);
            if (!json.TryString("inputSha256", out var inputSha) || inputSha is null) return Fail("inputSha256 is required.", out error);
            if (!json.TryString("mode", out var mode) || mode is null) return Fail("mode is required.", out error);
            if (!json.TryBool("needsOcr", out var needsOcr)) return Fail("needsOcr is required.", out error);
            if (!json.TryString("needsOcrReason", out var reason, allowNull: true)) return Fail("needsOcrReason is invalid.", out error);
            if (!json.TryInt("pageCount", out var pageCount)) return Fail("pageCount is required.", out error);
            if (!json.TryInt("embeddedChars", out var embeddedChars)) return Fail("embeddedChars is required.", out error);
            if (!json.TryString("textSha256", out var textSha, allowNull: true)) return Fail("textSha256 is invalid.", out error);
            if (!json.TryString("pagesSha256", out var pagesSha, allowNull: true)) return Fail("pagesSha256 is invalid.", out error);
            if (!json.TryStringArray("pageSha256s", out var pageShas, allowNull: false) || pageShas is null) return Fail("pageSha256s is required.", out error);
            if (!json.TryStringArray("pages", out var pages, allowNull: true)) return Fail("pages is invalid.", out error);

            result = new PdfExtractResult(
                schema, engine, inputSha, mode, needsOcr, reason, pageCount, embeddedChars, textSha, pagesSha, pageShas, pages);
            return true;
        }
        catch (JsonException)
        {
            return Fail("The result is not valid JSON.", out error);
        }

        static bool Fail(string message, out string? failure)
        {
            failure = message;
            return false;
        }
    }

    /// <summary>
    /// Validates a parsed result against its job: echoes (schema, engine version, input fingerprint, mode), bounds, and
    /// recomputation of every hash the API can derive from <c>pages</c>. Structural problems are <c>Invalid</c> (strike);
    /// forbidden characters are <c>ContentRejected</c> (no strike).
    /// </summary>
    public static RemoteResultValidation Validate(RemoteJobRow job, PdfExtractResult r)
    {
        if (!string.Equals(r.Schema, $"pdf.extract.result/{job.SchemaVersion}", StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("schema differs from the job.");
        }

        if (!string.Equals(r.EngineVersion, job.EngineVersion, StringComparison.Ordinal))
        {
            return RemoteResultValidation.EngineMismatch("engineVersion differs from the job.");
        }

        if (!string.Equals(r.InputSha256, job.InputSha256, StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("inputSha256 differs from the job.");
        }

        var mode = RemoteJobParams.Mode(job.ParamsJson);
        var minTextLength = RemoteJobParams.MinTextLength(job.ParamsJson);
        var includePages = RemoteJobParams.IncludePages(job.ParamsJson);
        if (mode is not ("flat" or "pages") || !string.Equals(r.Mode, mode, StringComparison.Ordinal))
        {
            return RemoteResultValidation.Invalid("mode differs from the job.");
        }

        if (r.PageCount is < 0 or > MaxPageCount) return RemoteResultValidation.Invalid("pageCount is out of range.");
        if (r.EmbeddedChars < 0) return RemoteResultValidation.Invalid("embeddedChars is out of range.");
        if (r.PageSha256s.Count > MaxPageCount) return RemoteResultValidation.Invalid("pageSha256s is too long.");
        if (r.NeedsOcrReason is not null && !OcrReasons.Contains(r.NeedsOcrReason))
        {
            return RemoteResultValidation.Invalid("needsOcrReason is invalid.");
        }

        if (r.Pages is not null)
        {
            if (r.Pages.Count != r.PageCount) return RemoteResultValidation.Invalid("pages.length differs from pageCount.");
            long total = 0;
            foreach (var page in r.Pages)
            {
                if (page.Length > MaxPageChars) return RemoteResultValidation.Invalid("A page exceeds the size limit.");
                total += page.Length;
            }

            if (total > MaxTotalChars) return RemoteResultValidation.Invalid("The pages exceed the total size limit.");
        }

        if (r.NeedsOcr)
        {
            // The helper never OCRs: it hands the asset back and writes nothing.
            if (r.Pages is not null || r.PageSha256s.Count != 0 || r.TextSha256 is not null || r.PagesSha256 is not null)
            {
                return RemoteResultValidation.Invalid("A needsOcr result must carry no pages or hashes.");
            }

            if (r.NeedsOcrReason is null) return RemoteResultValidation.Invalid("needsOcrReason is required when needsOcr is true.");
            if (r.EmbeddedChars >= minTextLength) return RemoteResultValidation.Invalid("needsOcr contradicts embeddedChars.");
        }
        else
        {
            if (r.NeedsOcrReason is not null) return RemoteResultValidation.Invalid("needsOcrReason must be null when needsOcr is false.");
            if (!RemoteIds.IsSha256Hex(r.TextSha256) || !RemoteIds.IsSha256Hex(r.PagesSha256))
            {
                return RemoteResultValidation.Invalid("textSha256 and pagesSha256 must be 64 lowercase hex characters.");
            }

            if (r.PageSha256s.Count != r.PageCount || r.PageSha256s.Any(sha => !RemoteIds.IsSha256Hex(sha)))
            {
                return RemoteResultValidation.Invalid("pageSha256s must hold one hash per page.");
            }

            if (!string.Equals(RemoteIds.Sha256Hex(string.Join("\n", r.PageSha256s)), r.PagesSha256, StringComparison.Ordinal))
            {
                return RemoteResultValidation.Invalid("pagesSha256 does not match pageSha256s.");
            }

            if (includePages && r.Pages is null) return RemoteResultValidation.Invalid("pages are required for this job.");
            if (!includePages && r.Pages is not null) return RemoteResultValidation.Invalid("pages must be omitted for this job.");

            if (r.Pages is not null)
            {
                for (var i = 0; i < r.Pages.Count; i++)
                {
                    if (!string.Equals(RemoteIds.Sha256Hex(r.Pages[i]), r.PageSha256s[i], StringComparison.Ordinal))
                    {
                        return RemoteResultValidation.Invalid("A page hash does not match its text.");
                    }
                }

                var flat = r.Flat();
                var chars = mode == "flat" ? flat.Length : r.Pages.Sum(page => page.Length);
                if (r.EmbeddedChars != chars) return RemoteResultValidation.Invalid("embeddedChars does not match the pages.");
                if (!string.Equals(RemoteIds.Sha256Hex(flat), r.TextSha256, StringComparison.Ordinal))
                {
                    return RemoteResultValidation.Invalid("textSha256 does not match the pages.");
                }
            }

            if (r.EmbeddedChars < minTextLength) return RemoteResultValidation.Invalid("embeddedChars is below the threshold but needsOcr is false.");
        }

        // Content-class last: text that is structurally perfect but carries forbidden characters is handled locally.
        if (r.Pages is not null && r.Pages.Any(RemoteTextRules.HasForbiddenCharacter))
        {
            return RemoteResultValidation.ContentRejected("The text contains forbidden characters.");
        }

        var summary = JsonSerializer.Serialize(new
        {
            needsOcr = r.NeedsOcr,
            needsOcrReason = r.NeedsOcrReason,
            pageCount = r.PageCount,
            embeddedChars = r.EmbeddedChars,
            textSha256 = r.TextSha256,
            pagesSha256 = r.PagesSha256,
        });
        return RemoteResultValidation.Ok(r, summary);
    }
}

/// <summary>
/// <c>pdf.extract</c> v1: PdfPig tier only. The helper's output must be byte-identical to
/// <c>PdfPigPdfTextExtractor</c> in-process; OCR tiers never leave the API, so a short extraction returns
/// <c>needsOcr</c> and the existing local path takes over. Applier outcomes (OET-RWP/1 section 6.1.5): canary compare,
/// shadow summary, <c>NoOp</c> for needsOcr or an already-cached asset, <c>Discarded</c> for a vanished or changed asset,
/// else the asset text is merged into every referencing paper.
/// </summary>
public sealed class PdfExtractKindHandler(
    IRuntimeSettingsProvider runtimeSettings,
    ILogger<PdfExtractKindHandler> logger) : IRemoteKindHandler
{
    public string Kind => RemoteJobKinds.PdfExtract;

    public RemoteResultValidation Validate(
        RemoteJobRow job,
        string resultJson,
        IReadOnlyList<RemoteOutputRow> outputs,
        RemoteJobsOptions options)
    {
        if (!PdfExtractResultValidator.TryParse(resultJson, out var result, out var error))
        {
            return RemoteResultValidation.Invalid(error ?? "The result could not be parsed.");
        }

        return PdfExtractResultValidator.Validate(job, result!);
    }

    public Task<RemoteApplyOutcome> ApplyAsync(RemoteApplyContext context, CancellationToken ct)
    {
        var result = (PdfExtractResult)context.Parsed;
        return context.Job.Purpose switch
        {
            RemoteJobPurpose.Canary => ApplyCanaryAsync(context, result, ct),
            RemoteJobPurpose.Shadow => Task.FromResult(RemoteApplyOutcome.Shadow(new { result.PageCount, result.EmbeddedChars })),
            _ => ApplyDomainAsync(context, result, ct),
        };
    }

    private async Task<RemoteApplyOutcome> ApplyCanaryAsync(RemoteApplyContext context, PdfExtractResult result, CancellationToken ct)
    {
        var expected = await RemoteCanary.GetExpectedAsync();
        var ok = !result.NeedsOcr
            && result.PageCount == expected.PageCount
            && result.EmbeddedChars == expected.EmbeddedChars
            && string.Equals(result.TextSha256, expected.TextSha256, StringComparison.Ordinal)
            && string.Equals(result.PagesSha256, expected.PagesSha256, StringComparison.Ordinal);

        await RemoteDb.ExecuteAsync(
            context.Db,
            """
            UPDATE "RemoteWorkers" SET "LastCanaryAt" = clock_timestamp(), "LastCanaryOk" = @ok, "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id;
            """,
            parameters =>
            {
                parameters.AddWithValue("ok", ok);
                parameters.AddWithValue("id", context.NodeId);
            },
            ct);

        if (!ok)
        {
            // A canary mismatch quarantines the node IMMEDIATELY (not a strike): its extraction cannot be trusted.
            await RemoteNodeOps.QuarantineAsync(
                context.Db, context.NodeId, "known-answer canary mismatch", RemoteAudit.NodeActor(context.NodeId),
                "remote-worker", context.Options, context.Time.GetUtcNow(), ct);
        }

        return RemoteApplyOutcome.Applied(new { canaryOk = ok });
    }

    private async Task<RemoteApplyOutcome> ApplyDomainAsync(RemoteApplyContext context, PdfExtractResult result, CancellationToken ct)
    {
        // Nothing is ever written for a needsOcr result: the local path runs the full extractor, OCR tiers included.
        if (result.NeedsOcr) return RemoteApplyOutcome.NoOp("needs_ocr");

        var db = context.Db;
        var job = context.Job;
        var assetId = job.ResourceId;

        var media = await db.MediaAssets.AsNoTracking()
            .Where(m => m.Id == assetId)
            .Select(m => new { m.Sha256, m.SizeBytes })
            .FirstOrDefaultAsync(ct);
        var links = await db.ContentPaperAssets.AsNoTracking()
            .Where(a => a.MediaAssetId == assetId)
            .Select(a => new { a.Id, a.PaperId })
            .ToListAsync(ct);
        if (media is null || links.Count == 0) return RemoteApplyOutcome.Discarded("resource_gone");

        var manifestSize = RemoteInputOutputService.ReadManifest(job.InputsJson)
            .FirstOrDefault(entry => entry.Name == "pdf")?.SizeBytes;
        if (!string.Equals(media.Sha256, job.InputSha256, StringComparison.Ordinal) || manifestSize != media.SizeBytes)
        {
            return RemoteApplyOutcome.Discarded("stale_input");
        }

        var mode = RemoteJobParams.Mode(job.ParamsJson);
        var replace = RemoteJobParams.ReplaceExisting(job.ParamsJson);
        var current = (await runtimeSettings.GetAsync(ct)).PdfExtraction;
        var currentHash = PdfExtractSettings.Hash(mode, current.Provider, current.MinTextLengthForSuccess, replace);
        if (!string.Equals(currentHash, job.SettingsHash, StringComparison.Ordinal))
        {
            return RemoteApplyOutcome.Discarded("stale_settings");
        }

        var flat = result.Flat();
        var written = 0;
        var cached = 0;
        foreach (var link in links)
        {
            var status = await PaperExtractedTextCommit.CommitAsync(
                db, link.PaperId, link.Id, flat, replace, context.Time.GetUtcNow(), ct);
            switch (status)
            {
                case PaperTextCommitStatus.Written:
                    written++;
                    break;
                case PaperTextCommitStatus.AlreadyCached:
                    cached++;
                    break;
                case PaperTextCommitStatus.Unreadable:
                    logger.LogWarning("ExtractedTextJson of paper {PaperId} is unreadable; the remote result was not merged.", link.PaperId);
                    break;
            }
        }

        return written > 0
            ? RemoteApplyOutcome.Applied(new { papers = written, alreadyCached = cached, textSha256 = result.TextSha256 })
            : RemoteApplyOutcome.NoOp("already_cached");
    }
}
