using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// Results are untrusted (OET-RWP/1 section 4.5.2 step 3): every echo, bound and hash is re-derived by the API.
/// RW-075, RW-080, RW-081, RW-104, RW-105, RW-109. Pure: no database.
/// </summary>
public sealed class RemoteResultValidatorTests
{
    private const string ApplyParams = "{\"mode\":\"flat\",\"minTextLength\":50,\"includePages\":true,\"replaceExisting\":false}";
    private const string ShadowParams = "{\"mode\":\"flat\",\"minTextLength\":50,\"includePages\":false,\"replaceExisting\":false,\"purpose\":\"shadow\"}";

    private static readonly string[] TwoPages =
    [
        "Page one of the sample paper has enough characters to pass the minimum text threshold easily.",
        "Page two continues the sample paper with a second paragraph of ordinary clinical text.",
    ];

    private static string Flat(IReadOnlyList<string> pages) => string.Join("\n\n", pages).Trim();

    private static string PdfResult(
        RemoteJobRow job,
        IReadOnlyList<string>? pages = null,
        bool includePages = true,
        Action<Dictionary<string, object?>>? tamper = null)
    {
        var list = pages ?? TwoPages;
        var flat = Flat(list);
        var pageHashes = list.Select(page => RemoteIds.Sha256Hex(page)).ToArray();
        var body = new Dictionary<string, object?>
        {
            ["schema"] = "pdf.extract.result/" + job.SchemaVersion,
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["mode"] = "flat",
            ["needsOcr"] = false,
            ["needsOcrReason"] = null,
            ["pageCount"] = list.Count,
            ["embeddedChars"] = flat.Length,
            ["textSha256"] = RemoteIds.Sha256Hex(flat),
            ["pagesSha256"] = RemoteIds.Sha256Hex(string.Join("\n", pageHashes)),
            ["pageSha256s"] = pageHashes,
            ["pages"] = includePages ? list.ToArray() : null,
        };
        tamper?.Invoke(body);
        return JsonSerializer.Serialize(body);
    }

    private static string NeedsOcrResult(RemoteJobRow job, int embeddedChars = 10, string? reason = "below_min_text")
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema"] = "pdf.extract.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["mode"] = "flat",
            ["needsOcr"] = true,
            ["needsOcrReason"] = reason,
            ["pageCount"] = 3,
            ["embeddedChars"] = embeddedChars,
            ["textSha256"] = null,
            ["pagesSha256"] = null,
            ["pageSha256s"] = Array.Empty<string>(),
            ["pages"] = null,
        });

    private static RemoteResultValidation ValidatePdf(RemoteJobRow job, string json)
    {
        var handler = new PdfExtractKindHandler(
            new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()),
            NullLogger<PdfExtractKindHandler>.Instance);
        return handler.Validate(job, json, [], new RemoteJobsOptions());
    }

    // ── pdf.extract ──────────────────────────────────────────────────────────

    [Fact]
    public void Pdf_ACorrectResult_IsAccepted_AndSummarised()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        var validation = ValidatePdf(job, PdfResult(job));

        Assert.True(validation.IsOk, validation.Message);
        var parsed = Assert.IsType<PdfExtractResult>(validation.Parsed);
        Assert.Equal(2, parsed.PageCount);
        Assert.Equal(Flat(TwoPages), parsed.Flat());
        Assert.Contains("\"pageCount\":2", validation.SummaryJson, StringComparison.Ordinal);
        Assert.DoesNotContain("clinical", validation.SummaryJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_AHashOnlyShadowResult_IsAccepted_ButOnlyWithoutPages()
    {
        var shadow = RemoteTestData.Row(purpose: RemoteJobPurpose.Shadow, paramsJson: ShadowParams);

        Assert.True(ValidatePdf(shadow, PdfResult(shadow, includePages: false)).IsOk);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(shadow, PdfResult(shadow, includePages: true)).Status);
    }

    [Fact]
    public void Pdf_AnApplyJob_RequiresThePages()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, includePages: false)).Status);
    }

    [Fact]
    public void Pdf_ADifferentEngineVersion_IsAnEngineMismatch()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        var validation = ValidatePdf(job, PdfResult(job, tamper: body => body["engineVersion"] = "pdfpig:0.0.0/oet-text:0"));

        Assert.Equal(RemoteValidationStatus.EngineMismatch, validation.Status);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("inputSha256")]
    [InlineData("mode")]
    public void Pdf_AWrongEcho_IsInvalid(string member)
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        var validation = ValidatePdf(job, PdfResult(job, tamper: body => body[member] = member == "inputSha256" ? new string('c', 64) : "wrong"));

        Assert.Equal(RemoteValidationStatus.Invalid, validation.Status);
    }

    [Theory]
    [InlineData("textSha256")]
    [InlineData("pagesSha256")]
    public void Pdf_AHashThatDoesNotMatchThePages_IsInvalid(string member)
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        var validation = ValidatePdf(job, PdfResult(job, tamper: body => body[member] = new string('d', 64)));

        Assert.Equal(RemoteValidationStatus.Invalid, validation.Status);
    }

    [Fact]
    public void Pdf_APageHashThatDoesNotMatchItsText_IsInvalid()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        var validation = ValidatePdf(job, PdfResult(job, tamper: body =>
        {
            var hashes = ((string[])body["pageSha256s"]!).ToArray();
            hashes[0] = new string('e', 64);
            body["pageSha256s"] = hashes;
            // Keep pagesSha256 self-consistent so ONLY the per-page check can catch it.
            body["pagesSha256"] = RemoteIds.Sha256Hex(string.Join("\n", hashes));
        }));

        Assert.Equal(RemoteValidationStatus.Invalid, validation.Status);
    }

    [Fact]
    public void Pdf_PageCountAndEmbeddedCharsMustAgreeWithThePages()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, tamper: b => b["pageCount"] = 3)).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, tamper: b => b["embeddedChars"] = 999)).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, tamper: b => b["pageCount"] = -1)).Status);
    }

    [Fact]
    public void Pdf_TextBelowTheThreshold_CannotClaimToBeText()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);
        var tiny = new[] { "too short" };

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, tiny)).Status);
    }

    [Fact]
    public void Pdf_NeedsOcr_IsAcceptedOnlyWhenItCarriesNothingAndIsConsistent()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        Assert.True(ValidatePdf(job, NeedsOcrResult(job)).IsOk);
        Assert.True(ValidatePdf(job, NeedsOcrResult(job, 0, "no_text_layer")).IsOk);

        // contradicts embeddedChars (50 or more characters is text)
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, NeedsOcrResult(job, embeddedChars: 80)).Status);
        // no reason
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, NeedsOcrResult(job, reason: null)).Status);
        // unknown reason
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, NeedsOcrResult(job, reason: "because")).Status);
    }

    [Fact]
    public void Pdf_NeedsOcr_MustNotSmuggleInPages()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema"] = "pdf.extract.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["mode"] = "flat",
            ["needsOcr"] = true,
            ["needsOcrReason"] = "below_min_text",
            ["pageCount"] = 1,
            ["embeddedChars"] = 10,
            ["textSha256"] = null,
            ["pagesSha256"] = null,
            ["pageSha256s"] = Array.Empty<string>(),
            ["pages"] = new[] { "smuggled" },
        });

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, json).Status);
    }

    [Fact]
    public void Pdf_ForbiddenCharacters_AreContentRejected_NotAStrike()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);
        var dirty = new[]
        {
            "Clean first page with plenty of ordinary characters to pass the threshold.",
            "Second page has a control character \u0001 hidden inside it but is otherwise fine text.",
        };

        var validation = ValidatePdf(job, PdfResult(job, dirty));

        Assert.Equal(RemoteValidationStatus.ContentRejected, validation.Status);
    }

    [Fact]
    public void Pdf_OversizeBounds_AreEnforced()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);
        var huge = new[] { new string('x', PdfExtractResultValidator.MaxPageChars + 1) };

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, huge)).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schema\":1}")]
    [InlineData("{\"a\":[[[[[[[[[[1]]]]]]]]]]}")]
    public void Pdf_MalformedResults_AreInvalidAndNeverThrow(string json)
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, json).Status);
    }

    [Fact]
    public void Pdf_AStringWhereANumberBelongs_IsInvalid()
    {
        var job = RemoteTestData.Row(paramsJson: ApplyParams);

        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, tamper: b => b["pageCount"] = "2")).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidatePdf(job, PdfResult(job, tamper: b => b["needsOcr"] = "false")).Status);
    }

    [Fact]
    public void PdfSettingsHash_ChangesWithEverySettingThatChangesTheOutput()
    {
        var baseline = PdfExtractSettings.Hash("flat", "auto", 50, false);

        Assert.Equal(baseline, PdfExtractSettings.Hash("flat", " AUTO ", 50, false));
        Assert.NotEqual(baseline, PdfExtractSettings.Hash("pages", "auto", 50, false));
        Assert.NotEqual(baseline, PdfExtractSettings.Hash("flat", "pdfpig", 50, false));
        Assert.NotEqual(baseline, PdfExtractSettings.Hash("flat", "auto", 51, false));
        Assert.NotEqual(baseline, PdfExtractSettings.Hash("flat", "auto", 50, true));
    }

    // ── companion.index-prep ─────────────────────────────────────────────────

    private static RemoteJobRow CompanionJob()
        => RemoteTestData.Row(
            kind: RemoteJobKinds.CompanionIndexPrep,
            engine: RemoteJobKinds.EngineVersion(RemoteJobKinds.CompanionIndexPrep, new RemoteJobsOptions()),
            paramsJson: CompanionIndexPrepSettings.ParamsJson(50));

    private static string CompanionResult(
        RemoteJobRow job,
        IReadOnlyList<string> pages,
        Action<Dictionary<string, object?>>? tamper = null)
    {
        var chunks = CompanionChunker.Build(pages);
        var prep = chunks.Select(c => new CompanionPrepChunk(c.Heading, c.PageNumber ?? 1, c.Text)).ToList();
        var body = new Dictionary<string, object?>
        {
            ["schema"] = "companion.index-prep.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["needsOcr"] = false,
            ["needsOcrReason"] = null,
            ["pageCount"] = pages.Count,
            ["embeddedChars"] = Flat(pages).Length,
            ["version"] = CompanionChunker.ChecksumVersion(pages),
            ["chunkCount"] = prep.Count,
            ["chunksSha256"] = CompanionIndexPrepValidator.ChunksSha256(prep),
            ["chunks"] = prep.Select(c => new Dictionary<string, object?>
            {
                ["heading"] = c.Heading,
                ["pageNumber"] = c.PageNumber,
                ["text"] = c.Text,
            }).ToArray(),
        };
        tamper?.Invoke(body);
        return JsonSerializer.Serialize(body);
    }

    private static RemoteResultValidation ValidateCompanion(RemoteJobRow job, string json)
    {
        var handler = new CompanionIndexPrepKindHandler(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()));
        return handler.Validate(job, json, [], new RemoteJobsOptions());
    }

    private static string[] LongPages() =>
    [
        new string('a', 300) + " first page about the opening of a referral letter.",
        new string('b', 300) + " second page about the ordering of the paragraphs.",
    ];

    [Fact]
    public void Companion_ACorrectResult_IsAccepted()
    {
        var job = CompanionJob();

        var validation = ValidateCompanion(job, CompanionResult(job, LongPages()));

        Assert.True(validation.IsOk, validation.Message);
        var parsed = Assert.IsType<CompanionIndexPrepResult>(validation.Parsed);
        Assert.Equal(2, parsed.ChunkCount);
        Assert.Equal(CompanionChunker.ChecksumVersion(LongPages()), parsed.Version);
    }

    [Fact]
    public void Companion_ChunkCountAndHash_MustMatchTheChunks()
    {
        var job = CompanionJob();

        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, CompanionResult(job, LongPages(), b => b["chunkCount"] = 5)).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, CompanionResult(job, LongPages(), b => b["chunksSha256"] = new string('f', 64))).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, CompanionResult(job, LongPages(), b => b["version"] = "NOT-HEX")).Status);
    }

    [Fact]
    public void Companion_AChunkOverTheCap_IsInvalid()
    {
        var job = CompanionJob();

        var json = CompanionResult(job, LongPages(), body =>
        {
            var oversize = new List<CompanionPrepChunk> { new("Page 1", 1, new string('z', CompanionChunker.MaxChunkChars + 1)) };
            body["chunkCount"] = 1;
            body["chunksSha256"] = CompanionIndexPrepValidator.ChunksSha256(oversize);
            body["chunks"] = new[]
            {
                new Dictionary<string, object?> { ["heading"] = "Page 1", ["pageNumber"] = 1, ["text"] = oversize[0].Text },
            };
        });

        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, json).Status);
    }

    [Fact]
    public void Companion_ABadHeadingOrPageNumber_IsInvalid()
    {
        var job = CompanionJob();

        string WithChunk(string heading, int pageNumber)
            => CompanionResult(job, LongPages(), body =>
            {
                var chunk = new List<CompanionPrepChunk> { new(heading, pageNumber, "Some chunk text that is long enough.") };
                body["chunkCount"] = 1;
                body["chunksSha256"] = CompanionIndexPrepValidator.ChunksSha256(chunk);
                body["chunks"] = new[]
                {
                    new Dictionary<string, object?> { ["heading"] = heading, ["pageNumber"] = pageNumber, ["text"] = chunk[0].Text },
                };
            });

        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, WithChunk("Chapter 1", 1)).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, WithChunk("Page 1", 0)).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, WithChunk("Page 9", 9)).Status);
        Assert.True(ValidateCompanion(job, WithChunk("Page 2 (1)", 2)).IsOk);
    }

    [Fact]
    public void Companion_ForbiddenCharacters_AreContentRejected()
    {
        var job = CompanionJob();
        var pages = new[] { new string('a', 300) + " text with a control \u0002 character in it." };

        Assert.Equal(RemoteValidationStatus.ContentRejected, ValidateCompanion(job, CompanionResult(job, pages)).Status);
    }

    [Fact]
    public void Companion_NeedsOcr_CarriesNoChunks()
    {
        var job = CompanionJob();
        string Json(Action<Dictionary<string, object?>>? tamper = null)
        {
            var body = new Dictionary<string, object?>
            {
                ["schema"] = "companion.index-prep.result/1",
                ["engineVersion"] = job.EngineVersion,
                ["inputSha256"] = job.InputSha256,
                ["needsOcr"] = true,
                ["needsOcrReason"] = "no_text_layer",
                ["pageCount"] = 4,
                ["embeddedChars"] = 0,
                ["version"] = null,
                ["chunkCount"] = 0,
                ["chunksSha256"] = null,
                ["chunks"] = Array.Empty<object>(),
            };
            tamper?.Invoke(body);
            return JsonSerializer.Serialize(body);
        }

        Assert.True(ValidateCompanion(job, Json()).IsOk);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, Json(b => b["version"] = "0123456789abcdef")).Status);
        Assert.Equal(RemoteValidationStatus.Invalid, ValidateCompanion(job, Json(b => b["embeddedChars"] = 500)).Status);
    }

    [Fact]
    public void Companion_AnEngineMismatch_IsReportedAsOne()
    {
        var job = CompanionJob();

        var validation = ValidateCompanion(job, CompanionResult(job, LongPages(), b => b["engineVersion"] = "pdfpig:0/oet-text:0/companion-chunker:0"));

        Assert.Equal(RemoteValidationStatus.EngineMismatch, validation.Status);
    }

    [Fact]
    public void CompanionSettingsHash_IsStableAndTiedToTheChunkerVersion()
    {
        Assert.Equal(CompanionIndexPrepSettings.Hash(50), CompanionIndexPrepSettings.Hash(50));
        Assert.NotEqual(CompanionIndexPrepSettings.Hash(50), CompanionIndexPrepSettings.Hash(60));
        Assert.Contains(CompanionChunker.Version, CompanionIndexPrepSettings.ParamsJson(50), StringComparison.Ordinal);
    }
}
