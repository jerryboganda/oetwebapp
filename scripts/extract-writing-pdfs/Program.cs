using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

// One-shot tool: extracts the Writing 1-6 sample PDFs under
// "Project Real Content/Writing_/" and writes a deterministic seed JSON
// consumed by the WritingSampleSeeder hosted service.
//
// Usage: dotnet run --project scripts/extract-writing-pdfs

var repoRoot = ResolveRepoRoot();
var srcRoot = Path.Combine(repoRoot, "Project Real Content", "Writing_");
if (!Directory.Exists(srcRoot))
{
    Console.Error.WriteLine($"Source folder not found: {srcRoot}");
    return 1;
}

var outFile = Path.Combine(repoRoot, "backend", "src", "OetLearner.Api", "Data", "Seeds", "writing-samples.v1.json");
Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

var folderMap = new (string Pattern, string LetterType, string Title, string Slug)[]
{
    ("Writing 1*", "routine_referral",                 "Routine Referral",                                "writing-1-routine-referral"),
    ("Writing 2*", "non_medical_referral",             "Non-Medical Referral",                            "writing-2-non-medical-referral"),
    ("Writing 3*", "urgent_referral",                  "Urgent Referral",                                 "writing-3-urgent-referral"),
    ("Writing 4*", "update_discharge",                 "Update & Discharge to GP",                        "writing-4-update-discharge"),
    ("Writing 5*", "update_referral_specialist_to_gp","Update & Referral (Specialist to GP/Dentist)",     "writing-5-update-referral-specialist-to-gp"),
    ("Writing 6*", "transfer_letter",                  "Transfer Letter",                                 "writing-6-transfer-letter"),
};

var samples = new List<object>();
foreach (var entry in folderMap)
{
    var folder = Directory.EnumerateDirectories(srcRoot, entry.Pattern).FirstOrDefault();
    if (folder is null)
    {
        Console.WriteLine($"[skip] folder not found for {entry.Pattern}");
        continue;
    }
    var pdfs = Directory.EnumerateFiles(folder, "*.pdf").ToArray();
    if (pdfs.Length == 0)
    {
        Console.WriteLine($"[skip] no PDFs in {Path.GetFileName(folder)}");
        continue;
    }
    var modelAnswerPdf = pdfs.FirstOrDefault(p => Regex.IsMatch(Path.GetFileName(p), "answer", RegexOptions.IgnoreCase));
    var caseNotesPdf  = pdfs.FirstOrDefault(p => Regex.IsMatch(Path.GetFileName(p), "case\\s*notes", RegexOptions.IgnoreCase))
                        ?? pdfs.FirstOrDefault(p => p != modelAnswerPdf);
    if (caseNotesPdf is null || modelAnswerPdf is null)
    {
        Console.WriteLine($"[skip] missing case-notes or answer PDF in {Path.GetFileName(folder)}");
        continue;
    }

    Console.WriteLine($"Extracting {Path.GetFileName(folder)}...");
    var caseNotesText  = ExtractPdf(caseNotesPdf);
    var modelAnswerText = ExtractPdf(modelAnswerPdf);

    samples.Add(new
    {
        seedId          = $"seed:writing:v1:{entry.Slug}",
        slug            = entry.Slug,
        title           = entry.Title,
        profession      = "medicine",
        letterType      = entry.LetterType,
        sourceFolder    = Path.GetFileName(folder),
        caseNotesPdf    = Path.GetFileName(caseNotesPdf),
        modelAnswerPdf  = Path.GetFileName(modelAnswerPdf),
        caseNotesText,
        modelAnswerText,
    });
}

var doc = new
{
    schemaVersion = 1,
    generatedAtUtc = DateTime.UtcNow.ToString("o"),
    sourceAuthority = "Dr. Ahmed Hesham — The Tutor Book — OET Writing Samples (Writing 1–6)",
    samples,
};

var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
});
File.WriteAllText(outFile, json, new UTF8Encoding(false));
Console.WriteLine($"Wrote {samples.Count} samples to {outFile}");
return 0;

static string ExtractPdf(string path)
{
    using var doc = PdfDocument.Open(path);
    var sb = new StringBuilder();
    bool first = true;
    foreach (var page in doc.GetPages())
    {
        if (!first) sb.Append("\n\n");
        sb.Append(page.Text);
        first = false;
    }
    return sb.ToString().Trim();
}

static string ResolveRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "package.json")))
    {
        dir = Path.GetDirectoryName(dir);
    }
    return dir ?? Directory.GetCurrentDirectory();
}
