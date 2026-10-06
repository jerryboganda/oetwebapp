using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

/// <summary>pdf.extract v1 semantics (protocol 6.1, RW-104, RW-105, RW-109) and the local self-check canary.</summary>
public sealed class PdfExtractTests
{
    private static string Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static readonly string[] Pages = ["Page one text\nsecond line", "", "Third page"];

    [Fact]
    public void flat_mode_follows_the_definitions_of_section_6_1_3()
    {
        var extraction = PdfExtractCore.Evaluate(magic: true, Pages, "flat", 5);

        var flat = "Page one text\nsecond line\n\n\n\nThird page"; // join with a blank line between pages, then trim
        Assert.Equal(flat, extraction.Flat);
        Assert.Equal(flat.Length, extraction.EmbeddedChars);
        Assert.False(extraction.NeedsOcr);
        Assert.Null(extraction.NeedsOcrReason);
        Assert.Equal(Hex(flat), extraction.TextSha256);
        Assert.Equal(Pages.Select(Hex).ToArray(), extraction.PageSha256s);
        Assert.Equal(Hex(string.Join("\n", Pages.Select(Hex))), extraction.PagesSha256);
    }

    [Fact]
    public void pages_mode_thresholds_on_the_sum_of_page_lengths_not_the_flat_length()
    {
        var pages = new[] { "abc", "defg" }; // flat = "abc\n\ndefg" (9 chars), sum = 7

        Assert.Equal(7, PdfExtractCore.Evaluate(true, pages, "pages", 8).EmbeddedChars);
        Assert.True(PdfExtractCore.Evaluate(true, pages, "pages", 8).NeedsOcr);
        Assert.Equal(9, PdfExtractCore.Evaluate(true, pages, "flat", 8).EmbeddedChars);
        Assert.False(PdfExtractCore.Evaluate(true, pages, "flat", 8).NeedsOcr);
    }

    [Theory]
    [InlineData(false, 0, "not_pdf")]
    [InlineData(true, 0, "no_text_layer")]
    [InlineData(true, 1, "below_min_text")]
    public void RW104_below_threshold_no_text_layer_and_non_pdf_inputs_need_ocr_with_a_reason(bool magic, int pageCount, string reason)
    {
        var pages = pageCount == 0 ? Array.Empty<string>() : new[] { "tiny" };

        var extraction = PdfExtractCore.Evaluate(magic, pages, "flat", 50);

        Assert.True(extraction.NeedsOcr);
        Assert.Equal(reason, extraction.NeedsOcrReason);
        Assert.Null(extraction.TextSha256);
        Assert.Null(extraction.PagesSha256);
        Assert.Empty(extraction.PageSha256s);
    }

    [Fact]
    public void RW104_the_serialised_result_nulls_the_text_fields_when_ocr_is_needed_and_never_ships_pages()
    {
        var extraction = PdfExtractCore.Evaluate(true, new[] { "tiny" }, "flat", 50);
        var request = new PdfExtractRequest("flat", 50, true, EngineVersions.Pdf, new string('a', 64));

        using var json = JsonDocument.Parse(PdfExtractCore.Serialize(extraction, request, 123, 5, 6));
        var root = json.RootElement;

        Assert.True(root.GetProperty("needsOcr").GetBoolean());
        Assert.Equal("below_min_text", root.GetProperty("needsOcrReason").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("pages").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("textSha256").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("pagesSha256").ValueKind);
        Assert.Equal(0, root.GetProperty("pageSha256s").GetArrayLength());
    }

    [Fact]
    public void the_result_object_has_the_documented_members_in_order_and_the_stats_block()
    {
        var extraction = PdfExtractCore.Evaluate(true, Pages, "flat", 5);
        var request = new PdfExtractRequest("flat", 5, true, EngineVersions.Pdf, new string('b', 64));

        var bytes = PdfExtractCore.Serialize(extraction, request, 4096, 17, 33);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;

        Assert.Equal(
            new[] { "schema", "engineVersion", "inputSha256", "mode", "needsOcr", "needsOcrReason", "pageCount", "embeddedChars", "textSha256", "pagesSha256", "pageSha256s", "pages", "stats" },
            root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("pdf.extract.result/1", root.GetProperty("schema").GetString());
        Assert.Equal(3, root.GetProperty("pageCount").GetInt32());
        Assert.Equal(Pages, root.GetProperty("pages").EnumerateArray().Select(p => p.GetString()!).ToArray());
        var stats = root.GetProperty("stats");
        Assert.Equal(4096, stats.GetProperty("pdfBytes").GetInt32());
        Assert.Equal(17, stats.GetProperty("durationMs").GetInt64());
        Assert.Equal(33, stats.GetProperty("peakRssMiB").GetInt64());
        Assert.True(stats.GetProperty("sha256Verified").GetBoolean());
    }

    [Fact]
    public void include_pages_false_keeps_the_page_hashes_but_drops_the_text()
    {
        var extraction = PdfExtractCore.Evaluate(true, Pages, "flat", 5);
        var request = new PdfExtractRequest("flat", 5, false, EngineVersions.Pdf, new string('b', 64));

        using var json = JsonDocument.Parse(PdfExtractCore.Serialize(extraction, request, 1, 1, 1));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("pages").ValueKind);
        Assert.Equal(3, json.RootElement.GetProperty("pageSha256s").GetArrayLength());
    }

    [Fact]
    public void RW088_the_hash_of_the_result_is_the_hash_of_the_exact_utf8_bytes_including_non_ascii_text()
    {
        var pages = new[] { "café über naïve – dash", "tab\there" };
        var extraction = PdfExtractCore.Evaluate(true, pages, "flat", 5);
        var request = new PdfExtractRequest("flat", 5, true, EngineVersions.Pdf, new string('c', 64));

        var bytes = PdfExtractCore.Serialize(extraction, request, 10, 1, 1);
        var strict = new UTF8Encoding(false, true).GetString(bytes);

        Assert.Equal(Hex(strict), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using var json = JsonDocument.Parse(strict);
        Assert.Equal(pages, json.RootElement.GetProperty("pages").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    [Fact]
    public void RW109_an_unpaired_surrogate_can_never_be_sent_and_is_reported_not_silently_replaced()
    {
        var pages = new[] { "bad \ud800 surrogate in the page text" };

        Assert.Throws<UnrepresentableResultException>(() => PdfExtractCore.Evaluate(true, pages, "flat", 5));
    }

    [Fact]
    public void protocol_bounds_are_checked_before_sending_so_an_honest_node_is_never_struck()
    {
        var tooManyPages = Enumerable.Repeat("page text", PdfExtractCore.MaxPages + 1).ToArray();
        var hugePage = new[] { new string('x', PdfExtractCore.MaxPageChars + 1) };

        Assert.Throws<UnrepresentableResultException>(() => PdfExtractCore.Evaluate(true, tooManyPages, "flat", 5));
        Assert.Throws<UnrepresentableResultException>(() => PdfExtractCore.Evaluate(true, hugePage, "flat", 5));
    }

    [Fact]
    public void pdf_magic_must_be_at_byte_zero()
    {
        Assert.True(PdfExtractCore.HasPdfMagic(Encoding.ASCII.GetBytes("%PDF-1.7\n")));
        Assert.False(PdfExtractCore.HasPdfMagic(Encoding.ASCII.GetBytes(" %PDF-1.7\n")));
        Assert.False(PdfExtractCore.HasPdfMagic(Encoding.ASCII.GetBytes("%PDF")));
        var extraction = PdfExtractCore.Extract(Encoding.ASCII.GetBytes("not a pdf at all, just text"), "flat", 5, CancellationToken.None);
        Assert.True(extraction.NeedsOcr);
        Assert.Equal("not_pdf", extraction.NeedsOcrReason);
    }

    [Fact]
    public void the_canary_pdf_extracts_to_its_known_text_through_the_linked_extractor()
    {
        var extraction = PdfExtractCore.Extract(CanaryPdf.Build(), "flat", 5, CancellationToken.None);

        Assert.False(extraction.NeedsOcr);
        Assert.Single(extraction.Pages);
        Assert.Equal(CanaryPdf.Text, CanaryPdf.Normalise(extraction.Pages[0]));
        Assert.Equal(Hex(extraction.Flat), extraction.TextSha256);
    }

    [Fact]
    public void the_canary_pdf_is_deterministic()
    {
        Assert.Equal(CanaryPdf.Build(), CanaryPdf.Build());
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(CanaryPdf.Build(), 0, 5));
    }

    [Fact]
    public void the_engine_version_has_the_documented_shape()
    {
        Assert.Matches(@"^pdfpig:[0-9A-Za-z.\-]+/oet-text:\d+$", EngineVersions.Pdf);
        Assert.DoesNotContain("+", EngineVersions.Pdf);
        Assert.Matches(Wire.EngineVersionPattern, EngineVersions.Pdf);
        Assert.Equal(EngineVersions.Pdf + "/companion-chunker:1", EngineVersions.CompanionIndexPrep);
        Assert.Equal("ffmpeg:7.1.1/audio-extract:1", EngineVersions.Media("7.1.1", "audio-extract:1"));
        Assert.Null(EngineVersions.Media(null, "audio-extract:1"));
    }

    [Fact]
    public async Task the_local_self_check_passes_the_canary_through_the_child_runner_path()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("scratch"));
        var scratch = new ScratchManager(dir.File("scratch"), dir.File("tmp"), TestLog.Instance);
        var selfCheck = new SelfCheck(new InProcessChildRunner(), new FakeProcessRunner { Handler = (_, _) => Task.FromResult(new ProcessRunResult(ProcessRunner.NotStarted, false, "")) },
            scratch, "ffmpeg", TestLog.Instance);

        var result = await selfCheck.RunAsync(CancellationToken.None);

        Assert.True(result.PdfOk);
        Assert.Null(result.FfmpegVersion);
        Assert.False(result.MediaOk);
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, ".selfcheck")));
    }

    [Fact]
    public async Task a_failing_child_fails_the_self_check_so_the_agent_stays_degraded()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("scratch"));
        var scratch = new ScratchManager(dir.File("scratch"), dir.File("tmp"), TestLog.Instance);
        var selfCheck = new SelfCheck(new BrokenChildRunner(), new FakeProcessRunner(), scratch, "ffmpeg", TestLog.Instance);

        var result = await selfCheck.RunAsync(CancellationToken.None);

        Assert.False(result.PdfOk);
    }

    private sealed class BrokenChildRunner : IChildRunner
    {
        public Task<ChildOutcome> RunAsync(ChildRun run, JobLease? lease, CancellationToken ct) =>
            Task.FromResult(new ChildOutcome(ChildExit.Unexpected, false, false, 0));
    }

    [Fact]
    public async Task the_in_process_child_runner_writes_the_same_result_the_executor_returns()
    {
        using var dir = new TempDir();
        var pdf = CanaryPdf.Build();
        File.WriteAllBytes(dir.File("in.pdf"), pdf);
        var run = new ChildRun(JobKinds.PdfExtract, dir.File("in.pdf"), dir.File("out.json"),
            new ChildParams { Mode = "flat", MinTextLength = 5, IncludePages = true, EngineVersion = EngineVersions.Pdf, InputSha256 = TestIds.Sha(pdf) }, 512, TimeSpan.FromSeconds(30));

        var outcome = await new InProcessChildRunner().RunAsync(run, null, CancellationToken.None);

        Assert.Equal(ChildExit.Ok, outcome.ExitCode);
        using var json = JsonDocument.Parse(File.ReadAllBytes(dir.File("out.json")));
        Assert.Equal(TestIds.Sha(pdf), json.RootElement.GetProperty("inputSha256").GetString());
    }

    [Theory]
    [InlineData(ChildExit.Ok, null)]
    [InlineData(ChildExit.Timeout, FailCodes.Timeout)]
    [InlineData(ChildExit.OutOfMemory, FailCodes.Oom)]
    [InlineData(137, FailCodes.Oom)]
    [InlineData(ChildExit.Unrepresentable, FailCodes.ExtractException)]
    [InlineData(ChildExit.Unexpected, FailCodes.ExtractException)]
    [InlineData(ChildExit.BadArguments, FailCodes.InternalError)]
    [InlineData(139, FailCodes.InternalError)]
    public void child_exit_codes_map_onto_the_agent_fail_codes(int exit, string? expected)
    {
        var mapped = PdfChildExecutor.MapExit(new ChildOutcome(exit, false, false, 0));

        Assert.Equal(expected, mapped?.FailCode);
        Assert.True(mapped is null || mapped.Retryable == (expected is FailCodes.Timeout or FailCodes.Oom or FailCodes.InternalError));
    }

    [Fact]
    public void a_timed_out_child_is_a_retryable_timeout_regardless_of_its_exit_code()
    {
        var mapped = PdfChildExecutor.MapExit(new ChildOutcome(-1, TimedOut: true, Canceled: false, PeakRssMiB: 0));

        Assert.Equal(FailCodes.Timeout, mapped!.FailCode);
        Assert.True(mapped.Retryable);
    }

    [Fact]
    public void the_child_entry_rejects_bad_arguments_without_throwing()
    {
        Assert.Equal(ChildExit.BadArguments, ChildEntry.Run(new[] { "--child", "pdf.extract" }));
        Assert.Equal(ChildExit.BadArguments, ChildEntry.Run(new[] { "--child", "pdf.extract", "--in", "a", "--params", "b", "--bogus", "c" }));
        Assert.Equal(ChildExit.Unexpected, ChildEntry.Run(new[] { "--child", "pdf.extract", "--in", "/no/such/file", "--params", "/no/such/params", "--out", "/no/such/out" }));
    }

    [Fact]
    public void the_child_entry_produces_the_result_file_end_to_end()
    {
        using var dir = new TempDir();
        var pdf = CanaryPdf.Build();
        File.WriteAllBytes(dir.File("in.pdf"), pdf);
        var parameters = new ChildParams { Mode = "flat", MinTextLength = 5, IncludePages = true, EngineVersion = EngineVersions.Pdf, InputSha256 = TestIds.Sha(pdf), TimeoutSeconds = 60 };
        File.WriteAllBytes(dir.File("params.json"), JsonSerializer.SerializeToUtf8Bytes(parameters, ProtocolJson.Options));

        var exit = ChildEntry.Run(new[] { "--child", JobKinds.PdfExtract, "--in", dir.File("in.pdf"), "--params", dir.File("params.json"), "--out", dir.File("out.json") });

        Assert.Equal(ChildExit.Ok, exit);
        Assert.True(File.Exists(dir.File("out.json")));
    }
}
