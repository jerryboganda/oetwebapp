using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.RemoteJobs;

public sealed record CompanionPrepChunk(string Heading, int PageNumber, string Text);

/// <summary>A parsed <c>companion.index-prep.result/1</c> (OET-RWP/1 section 6.2). Never trusted until validated.</summary>
public sealed record CompanionIndexPrepResult(
    string Schema,
    string EngineVersion,
    string InputSha256,
    bool NeedsOcr,
    string? NeedsOcrReason,
    int PageCount,
    int EmbeddedChars,
    string? Version,
    int ChunkCount,
    IReadOnlyList<CompanionPrepChunk> Chunks,
    string? ChunksSha256);

/// <summary>Settings snapshot hashed into a <c>companion.index-prep</c> job's <c>SettingsHash</c>.</summary>
public static class CompanionIndexPrepSettings
{
    public static string Hash(int minTextLength)
        => RemoteJobKeys.SettingsHash(
        [
            new KeyValuePair<string, string>("chunkerVersion", CompanionChunker.Version),
            new KeyValuePair<string, string>("maxChunkChars", CompanionChunker.MaxChunkChars.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("minChunkChars", CompanionChunker.MinChunkChars.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("minTextLength", minTextLength.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]);

    /// <summary>The job parameters offered to the agent.</summary>
    public static string ParamsJson(int minTextLength)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["minTextLength"] = minTextLength,
            ["maxChunkChars"] = CompanionChunker.MaxChunkChars,
            ["minChunkChars"] = CompanionChunker.MinChunkChars,
            ["chunkerVersion"] = CompanionChunker.Version,
        });
}

/// <summary>Strict parsing and verification of a companion index-prep result. Pure: no database, no clock.</summary>
public static partial class CompanionIndexPrepValidator
{
    public const int MaxChunks = 5000;

    [GeneratedRegex("^Page [0-9]+( \\([0-9]+\\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    private static readonly HashSet<string> OcrReasons = new(StringComparer.Ordinal) { "not_pdf", "no_text_layer", "below_min_text" };

    /// <summary>SHA-256 over <c>heading|pageNumber|sha256(text)</c> lines joined by a newline (OET-RWP/1 section 6.2).</summary>
    public static string ChunksSha256(IReadOnlyList<CompanionPrepChunk> chunks)
        => RemoteIds.Sha256Hex(string.Join(
            "\n",
            chunks.Select(chunk => chunk.Heading + "|" + chunk.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                   + "|" + RemoteIds.Sha256Hex(chunk.Text))));

    public static bool TryParse(string resultJson, out CompanionIndexPrepResult? result, out string? error)
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
            if (!json.TryBool("needsOcr", out var needsOcr)) return Fail("needsOcr is required.", out error);
            if (!json.TryString("needsOcrReason", out var reason, allowNull: true)) return Fail("needsOcrReason is invalid.", out error);
            if (!json.TryInt("pageCount", out var pageCount)) return Fail("pageCount is required.", out error);
            if (!json.TryInt("embeddedChars", out var embeddedChars)) return Fail("embeddedChars is required.", out error);
            if (!json.TryString("version", out var version, allowNull: true)) return Fail("version is invalid.", out error);
            if (!json.TryInt("chunkCount", out var chunkCount)) return Fail("chunkCount is required.", out error);
            if (!json.TryString("chunksSha256", out var chunksSha, allowNull: true)) return Fail("chunksSha256 is invalid.", out error);
            if (!json.TryObjectArray("chunks", out var rawChunks) || rawChunks is null) return Fail("chunks is required.", out error);
            if (rawChunks.Count > MaxChunks) return Fail("chunks is too long.", out error);

            var chunks = new List<CompanionPrepChunk>(rawChunks.Count);
            foreach (var raw in rawChunks)
            {
                var chunk = new StrictJson(raw);
                if (!chunk.TryString("heading", out var heading) || heading is null
                    || !chunk.TryInt("pageNumber", out var pageNumber)
                    || !chunk.TryString("text", out var text) || text is null)
                {
                    return Fail("chunks[] entries need heading, pageNumber and text.", out error);
                }

                chunks.Add(new CompanionPrepChunk(heading, pageNumber, text));
            }

            result = new CompanionIndexPrepResult(
                schema, engine, inputSha, needsOcr, reason, pageCount, embeddedChars, version, chunkCount, chunks, chunksSha);
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

    public static RemoteResultValidation Validate(RemoteJobRow job, CompanionIndexPrepResult r)
    {
        if (!string.Equals(r.Schema, $"companion.index-prep.result/{job.SchemaVersion}", StringComparison.Ordinal))
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

        var minTextLength = RemoteJobParams.MinTextLength(job.ParamsJson);
        if (r.PageCount is < 0 or > PdfExtractResultValidator.MaxPageCount) return RemoteResultValidation.Invalid("pageCount is out of range.");
        if (r.EmbeddedChars < 0) return RemoteResultValidation.Invalid("embeddedChars is out of range.");
        if (r.ChunkCount != r.Chunks.Count || r.ChunkCount > MaxChunks) return RemoteResultValidation.Invalid("chunkCount does not match chunks.");
        if (r.NeedsOcrReason is not null && !OcrReasons.Contains(r.NeedsOcrReason)) return RemoteResultValidation.Invalid("needsOcrReason is invalid.");

        if (r.NeedsOcr)
        {
            if (r.Chunks.Count != 0 || r.Version is not null || r.ChunksSha256 is not null)
            {
                return RemoteResultValidation.Invalid("A needsOcr result must carry no chunks.");
            }

            if (r.NeedsOcrReason is null) return RemoteResultValidation.Invalid("needsOcrReason is required when needsOcr is true.");
            if (r.EmbeddedChars >= minTextLength) return RemoteResultValidation.Invalid("needsOcr contradicts embeddedChars.");
        }
        else
        {
            if (r.NeedsOcrReason is not null) return RemoteResultValidation.Invalid("needsOcrReason must be null when needsOcr is false.");
            if (r.EmbeddedChars < minTextLength) return RemoteResultValidation.Invalid("embeddedChars is below the threshold but needsOcr is false.");
            if (r.Chunks.Count == 0) return RemoteResultValidation.Invalid("A text result must carry at least one chunk.");
            if (r.Version is null || !VersionPattern().IsMatch(r.Version)) return RemoteResultValidation.Invalid("version must be 16 lowercase hex characters.");
            if (!RemoteIds.IsSha256Hex(r.ChunksSha256)) return RemoteResultValidation.Invalid("chunksSha256 must be 64 lowercase hex characters.");

            foreach (var chunk in r.Chunks)
            {
                if (chunk.Text.Length is < 1 or > CompanionChunker.MaxChunkChars) return RemoteResultValidation.Invalid("A chunk text length is out of range.");
                if (!HeadingPattern().IsMatch(chunk.Heading)) return RemoteResultValidation.Invalid("A chunk heading is invalid.");
                if (chunk.PageNumber < 1 || chunk.PageNumber > Math.Max(1, r.PageCount)) return RemoteResultValidation.Invalid("A chunk page number is out of range.");
            }

            if (!string.Equals(ChunksSha256(r.Chunks), r.ChunksSha256, StringComparison.Ordinal))
            {
                return RemoteResultValidation.Invalid("chunksSha256 does not match the chunks.");
            }

            // Content-class last: forbidden characters send the asset to the local path, no strike.
            if (r.Chunks.Any(chunk => RemoteTextRules.HasForbiddenCharacter(chunk.Text) || RemoteTextRules.HasForbiddenCharacter(chunk.Heading)))
            {
                return RemoteResultValidation.ContentRejected("The text contains forbidden characters.");
            }
        }

        var summary = JsonSerializer.Serialize(new
        {
            needsOcr = r.NeedsOcr,
            needsOcrReason = r.NeedsOcrReason,
            pageCount = r.PageCount,
            embeddedChars = r.EmbeddedChars,
            chunkCount = r.ChunkCount,
            version = r.Version,
            chunksSha256 = r.ChunksSha256,
        });
        return RemoteResultValidation.Ok(r, summary);
    }
}

/// <summary>
/// <c>companion.index-prep</c> v1: PdfPig page extraction plus the chunking algorithm of <see cref="CompanionChunker"/>.
/// Embeddings, the corpus guard and the hash-gated commit of <c>CompanionIndexWriter</c> stay on the API. The applier
/// therefore only validates and PARKS the result (<c>Deferred</c>): the follow-up embeds chunks (a paid AI call), which must
/// never run inside the completion transaction. The reindex path consumes the parked result and clears it.
/// </summary>
public sealed class CompanionIndexPrepKindHandler(IRuntimeSettingsProvider runtimeSettings) : IRemoteKindHandler
{
    public string Kind => RemoteJobKinds.CompanionIndexPrep;

    public RemoteResultValidation Validate(
        RemoteJobRow job,
        string resultJson,
        IReadOnlyList<RemoteOutputRow> outputs,
        RemoteJobsOptions options)
    {
        if (!CompanionIndexPrepValidator.TryParse(resultJson, out var result, out var error))
        {
            return RemoteResultValidation.Invalid(error ?? "The result could not be parsed.");
        }

        return CompanionIndexPrepValidator.Validate(job, result!);
    }

    public async Task<RemoteApplyOutcome> ApplyAsync(RemoteApplyContext context, CancellationToken ct)
    {
        var result = (CompanionIndexPrepResult)context.Parsed;
        if (result.NeedsOcr) return RemoteApplyOutcome.NoOp("needs_ocr");

        var job = context.Job;
        var assetId = job.ResourceId;
        var media = await context.Db.MediaAssets.AsNoTracking()
            .Where(m => m.Id == assetId)
            .Select(m => new { m.Sha256 })
            .FirstOrDefaultAsync(ct);
        if (media is null) return RemoteApplyOutcome.Discarded("resource_gone");
        if (!string.Equals(media.Sha256, job.InputSha256, StringComparison.Ordinal)) return RemoteApplyOutcome.Discarded("stale_input");

        var current = (await runtimeSettings.GetAsync(ct)).PdfExtraction;
        if (!string.Equals(CompanionIndexPrepSettings.Hash(current.MinTextLengthForSuccess), job.SettingsHash, StringComparison.Ordinal))
        {
            return RemoteApplyOutcome.Discarded("stale_settings");
        }

        return RemoteApplyOutcome.Deferred(new { result.ChunkCount, result.Version });
    }
}
