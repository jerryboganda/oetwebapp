using System.Text.Json;

namespace Fleet.Agent;

internal sealed record SelfCheckResult(bool PdfOk, string? FfmpegVersion, bool MediaOk);

/// <summary>
/// The agent's LOCAL self-check canary (the API additionally runs a server-side known-answer canary, protocol 6.5). Before the
/// agent claims anything it runs a built-in PDF through the production child-process path and compares the answer with the
/// in-process extractor and with the expected text, and (when ffmpeg is present) decodes and encodes one second of silence.
/// A failing PDF check keeps the agent Degraded: it heartbeats but never claims.
/// </summary>
internal sealed class SelfCheck
{
    private readonly IChildRunner _children;
    private readonly IProcessRunner _processes;
    private readonly ScratchManager _scratch;
    private readonly string _ffmpeg;
    private readonly ILogger _log;

    public SelfCheck(IChildRunner children, IProcessRunner processes, ScratchManager scratch, string ffmpegPath, ILogger log)
    {
        _children = children;
        _processes = processes;
        _scratch = scratch;
        _ffmpeg = ffmpegPath;
        _log = log;
    }

    public async Task<SelfCheckResult> RunAsync(CancellationToken ct)
    {
        var pdfOk = await CheckPdfAsync(ct).ConfigureAwait(false);
        var version = await FfmpegProbe.DetectVersionAsync(_processes, _ffmpeg, ct).ConfigureAwait(false);
        var mediaOk = version is not null && await CheckMediaAsync(ct).ConfigureAwait(false);
        _log.LogInformation("self-check pdf={Pdf} ffmpeg={Ffmpeg} media={Media}", pdfOk, version ?? "absent", mediaOk);
        return new SelfCheckResult(pdfOk, version, mediaOk);
    }

    private async Task<bool> CheckPdfAsync(CancellationToken ct)
    {
        var directory = Path.Combine(_scratch.Root, ".selfcheck");
        try
        {
            Directory.CreateDirectory(directory);
            var pdf = CanaryPdf.Build();
            var input = Path.Combine(directory, "canary.pdf");
            var output = Path.Combine(directory, "result.json");
            await File.WriteAllBytesAsync(input, pdf, ct).ConfigureAwait(false);
            var sha = Hashing.Sha256Hex(pdf);
            var run = new ChildRun(
                JobKinds.PdfExtract, input, output,
                new ChildParams { Mode = "flat", MinTextLength = 5, IncludePages = true, EngineVersion = EngineVersions.Pdf, InputSha256 = sha, TimeoutSeconds = 60 },
                MemLimitMiB: 512, Timeout: TimeSpan.FromSeconds(60));
            var outcome = await _children.RunAsync(run, null, ct).ConfigureAwait(false);
            if (outcome.ExitCode != ChildExit.Ok || outcome.TimedOut) return false;

            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.GetProperty("needsOcr").GetBoolean() || root.GetProperty("pageCount").GetInt32() != 1) return false;
            var pages = root.GetProperty("pages").EnumerateArray().Select(p => p.GetString() ?? "").ToList();
            if (pages.Count != 1 || CanaryPdf.Normalise(pages[0]) != CanaryPdf.Text) return false;

            // The child must agree byte for byte with the in-process extractor linked into this very assembly.
            var direct = PdfExtractCore.Extract(pdf, "flat", 5, ct);
            if (direct.NeedsOcr || direct.Pages.Count != 1 || !string.Equals(direct.Pages[0], pages[0], StringComparison.Ordinal)) return false;
            return root.GetProperty("textSha256").GetString() == direct.TextSha256;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("pdf self-check failed: {Error}", Redact.Exception(ex));
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Wiped at the next start anyway.
            }
        }
    }

    private async Task<bool> CheckMediaAsync(CancellationToken ct)
    {
        try
        {
            var pcm = new MemoryStream();
            var decoded = await _processes.RunAsync(new ProcessSpec
            {
                FileName = _ffmpeg,
                Arguments = FfmpegArgs.SilenceOneSecond(),
                ConsumeStdout = (stdout, token) => stdout.CopyToAsync(pcm, token),
                Timeout = TimeSpan.FromSeconds(20),
            }, ct).ConfigureAwait(false);
            // One second of 16 kHz mono s16le is 32,000 bytes; allow a frame of slack either way.
            if (decoded.ExitCode != 0 || pcm.Length < 30_000 || pcm.Length > 34_000) return false;

            var mp3 = new MemoryStream();
            var encoded = await _processes.RunAsync(new ProcessSpec
            {
                FileName = _ffmpeg,
                Arguments = FfmpegArgs.EncodePcmToMp3(48),
                Stdin = new MemoryStream(pcm.ToArray(), writable: false),
                ConsumeStdout = (stdout, token) => stdout.CopyToAsync(mp3, token),
                Timeout = TimeSpan.FromSeconds(20),
            }, ct).ConfigureAwait(false);
            return encoded.ExitCode == 0 && mp3.Length > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("media self-check failed: {Error}", Redact.Exception(ex));
            return false;
        }
    }
}
