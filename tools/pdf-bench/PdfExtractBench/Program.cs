using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Content;

return await BenchProgram.RunAsync(args);

/// <summary>
/// Commands (all print ONE JSON line on stdout, never extracted text):
///   extract &lt;pdf&gt;                      one cold extraction in this process, the parity oracle's answer
///   loop &lt;pdf&gt; --iterations N --warmup W  steady-state in-process cost (what the API pays per document)
///   version                              runtime / PdfPig versions
/// The JSON of <c>extract</c> follows the pdf.extract result envelope (page hashes, text hash, counts)
/// so an agent kernel's output can be compared field by field.
/// </summary>
internal static class BenchProgram
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return Usage();
        try
        {
            return args[0] switch
            {
                "extract" => await ExtractAsync(args),
                "loop" => await LoopAsync(args),
                "version" => VersionInfo(),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"bench error: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: PdfExtractBench extract <pdf> | loop <pdf> [--iterations N] [--warmup W] | version");
        return 2;
    }

    private static TimeSpan CpuTime(Process process)
    {
        process.Refresh();
        return process.TotalProcessorTime;
    }

    private static async Task<int> ExtractAsync(string[] args)
    {
        if (args.Length < 2) return Usage();
        var bytes = await File.ReadAllBytesAsync(args[1]);
        var process = Process.GetCurrentProcess();
        var cpuBefore = CpuTime(process);
        var allocatedBefore = GC.GetTotalAllocatedBytes(false);
        var stopwatch = Stopwatch.StartNew();

        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        IReadOnlyList<string> pages;
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            pages = await extractor.ExtractPagesAsync(stream, CancellationToken.None);
        }

        stopwatch.Stop();
        var cpuMs = (CpuTime(process) - cpuBefore).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
        process.Refresh();
        Console.Out.WriteLine(JsonSerializer.Serialize(
            Describe(bytes.LongLength, pages, stopwatch.Elapsed.TotalMilliseconds, cpuMs, process.PeakWorkingSet64, allocated),
            Json));
        return 0;
    }

    private static async Task<int> LoopAsync(string[] args)
    {
        if (args.Length < 2) return Usage();
        var iterations = IntOption(args, "--iterations", 5);
        var warmup = IntOption(args, "--warmup", 1);
        var bytes = await File.ReadAllBytesAsync(args[1]);
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        var process = Process.GetCurrentProcess();
        var runs = new List<object>();
        var pageCount = 0;

        for (var i = 0; i < warmup + iterations; i++)
        {
            var cpuBefore = CpuTime(process);
            var allocatedBefore = GC.GetTotalAllocatedBytes(false);
            var stopwatch = Stopwatch.StartNew();
            IReadOnlyList<string> pages;
            using (var stream = new MemoryStream(bytes, writable: false))
            {
                pages = await extractor.ExtractPagesAsync(stream, CancellationToken.None);
            }

            stopwatch.Stop();
            var cpuMs = (CpuTime(process) - cpuBefore).TotalMilliseconds;
            var allocated = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
            pageCount = pages.Count;
            if (i >= warmup)
            {
                runs.Add(new
                {
                    wallMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    cpuMs = Math.Round(cpuMs, 3),
                    allocatedBytes = allocated,
                });
            }
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "pdf.extract.bench.loop/1",
            pdfBytes = bytes.LongLength,
            pageCount,
            iterations = runs,
        }, Json));
        return 0;
    }

    private static int VersionInfo()
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            dotnet = Environment.Version.ToString(),
            pdfPig = EngineVersion(),
            os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount,
        }, Json));
        return 0;
    }

    private static object Describe(long pdfBytes, IReadOnlyList<string> pages, double durationMs, double cpuMs, long peakWorkingSet, long allocated)
    {
        // Exactly the API's semantics: flat = string.Join("\n\n", pages).Trim() (ExtractAsync's body).
        var flat = string.Join("\n\n", pages).Trim();
        var pageHashes = pages.Select(Sha256Hex).ToArray();
        return new
        {
            schema = "pdf.extract.bench/1",
            engine = "in-process-oracle",
            engineVersion = EngineVersion(),
            pdfBytes,
            pageCount = pages.Count,
            embeddedChars = flat.Length,
            textSha256 = Sha256Hex(flat),
            pageSha256s = pageHashes,
            pagesSha256 = Sha256Hex(string.Join("\n", pageHashes)),
            stats = new
            {
                durationMs = Math.Round(durationMs, 3),
                cpuMs = Math.Round(cpuMs, 3),
                peakRssMiB = Math.Round(peakWorkingSet / 1048576.0, 1),
                allocatedBytes = allocated,
            },
        };
    }

    private static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string EngineVersion()
        => typeof(UglyToad.PdfPig.PdfDocument).Assembly
               .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
           ?? "unknown";

    private static int IntOption(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) && value >= 0
            ? value
            : fallback;
    }
}
