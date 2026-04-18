using System.Text.RegularExpressions;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// Convention Parser — Slice 5
//
// Turns a file tree (unzipped) that matches the OET folder conventions used
// in Project Real Content/ into a proposed import manifest. Deterministic:
// no external calls, no DB reads. The admin reviews the manifest before any
// DB write happens.
//
// Conventions recognised (see docs/CONTENT-UPLOAD-PLAN.md §6):
//   ─ Listening Sample N / Audio N / *.mp3
//   ─ Listening Sample N / *Question-Paper.pdf
//   ─ Listening Sample N / *Audio-Script.pdf
//   ─ Listening Sample N / *Answer-Key.pdf  (also "Answers.pdf")
//   ─ Reading Sample N / Part A Reading*.pdf  (Part="A")
//   ─ Reading Sample N / Reading Part B&C*.pdf  (Part="B+C")
//   ─ Writing N (<LetterType label>) / *Case Notes*.pdf   + *Answer Sheet*.pdf
//   ─ Card N (<CardType label>) / *.pdf
//   ─ "Same for all professions" in folder → AppliesToAllProfessions=true
//   ─ "(Medicine only)" in folder → ProfessionId=medicine
// ═════════════════════════════════════════════════════════════════════════════

public sealed record ProposedPaper(
    string ProposalId,
    string SubtestCode,
    string Title,
    string? ProfessionId,
    bool AppliesToAllProfessions,
    string? CardType,
    string? LetterType,
    string? SourceProvenance,
    List<ProposedAsset> Assets);

public sealed record ProposedAsset(
    string SourceRelativePath,
    PaperAssetRole Role,
    string? Part,
    string? SuggestedTitle);

public sealed record ImportManifest(
    List<ProposedPaper> Papers,
    List<ProposedFileIssue> Issues);

public sealed record ProposedFileIssue(
    string RelativePath,
    string IssueCode,
    string Message);

public interface IContentConventionParser
{
    ImportManifest Parse(IReadOnlyList<string> relativePaths);
}

public sealed class ContentConventionParser : IContentConventionParser
{
    // Letter type detection from "Writing N (Something)" folder names.
    private static readonly (Regex pattern, string code)[] LetterTypeRules =
    {
        (new Regex(@"routine\s*referral", RegexOptions.IgnoreCase), "routine_referral"),
        (new Regex(@"urgent\s*referral", RegexOptions.IgnoreCase), "urgent_referral"),
        (new Regex(@"non[\s-]*medical|occupational\s*therapist", RegexOptions.IgnoreCase), "non_medical_referral"),
        (new Regex(@"update.*discharge|discharge.*gp", RegexOptions.IgnoreCase), "update_discharge"),
        (new Regex(@"update.*referral|specialist.*gp", RegexOptions.IgnoreCase), "update_referral_specialist_to_gp"),
        (new Regex(@"transfer\s*letter", RegexOptions.IgnoreCase), "transfer_letter"),
    };

    private static readonly (Regex pattern, string code)[] CardTypeRules =
    {
        (new Regex(@"already\s*known", RegexOptions.IgnoreCase), "already_known_pt"),
        (new Regex(@"follow[\s-]*up", RegexOptions.IgnoreCase), "follow_up_visit"),
        (new Regex(@"examination", RegexOptions.IgnoreCase), "examination"),
        (new Regex(@"first\s*visit.*emergency|emergency\s*card", RegexOptions.IgnoreCase), "first_visit_emergency"),
        (new Regex(@"first\s*visit(?!\s*-?\s*emergency)", RegexOptions.IgnoreCase), "first_visit_routine"),
        (new Regex(@"breaking\s*bad\s*news|bbn", RegexOptions.IgnoreCase), "breaking_bad_news"),
    };

    public ImportManifest Parse(IReadOnlyList<string> relativePaths)
    {
        var issues = new List<ProposedFileIssue>();
        var paperBuckets = new Dictionary<string, ProposedPaper>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in relativePaths)
        {
            var path = raw.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(path)) continue;
            var segments = path.Split('/');
            if (segments.Length < 2) { issues.Add(new(path, "flat_file", "File is at the root — cannot infer paper context.")); continue; }
            if (!TryClassify(segments, out var subtest, out var paperTitle, out var applyAll, out var profession, out var cardType, out var letterType, out var issue))
            {
                issues.Add(new(path, "unknown_structure", issue ?? "Cannot determine which subtest this file belongs to."));
                continue;
            }

            var bucketKey = $"{subtest}|{paperTitle}|{profession ?? "all"}";
            if (!paperBuckets.TryGetValue(bucketKey, out var paper))
            {
                paper = new ProposedPaper(
                    ProposalId: Guid.NewGuid().ToString("N"),
                    SubtestCode: subtest,
                    Title: paperTitle,
                    ProfessionId: profession,
                    AppliesToAllProfessions: applyAll,
                    CardType: cardType,
                    LetterType: letterType,
                    SourceProvenance: ContentDefaults.DefaultSourceProvenance,
                    Assets: new List<ProposedAsset>());
                paperBuckets[bucketKey] = paper;
            }

            // ── Classify the file by role ─────────────────────────────────
            var (role, part) = ClassifyFile(path);
            paper.Assets.Add(new ProposedAsset(
                SourceRelativePath: path,
                Role: role,
                Part: part,
                SuggestedTitle: Path.GetFileNameWithoutExtension(segments[^1])));
        }

        return new ImportManifest(paperBuckets.Values.ToList(), issues);
    }

    private static bool TryClassify(
        string[] segments,
        out string subtest,
        out string paperTitle,
        out bool appliesToAll,
        out string? profession,
        out string? cardType,
        out string? letterType,
        out string? issue)
    {
        subtest = ""; paperTitle = ""; appliesToAll = false; profession = null;
        cardType = null; letterType = null; issue = null;

        var joined = string.Join('/', segments).ToLowerInvariant();

        // Profession scope (folder names contain these hints)
        appliesToAll = segments.Any(s => s.Contains("same for all professions", StringComparison.OrdinalIgnoreCase));
        if (!appliesToAll && segments.Any(s => s.Contains("(Medicine only)", StringComparison.OrdinalIgnoreCase)))
            profession = "medicine";

        // Subtest
        if (joined.Contains("listening"))
        {
            subtest = "listening";
            var sampleFolder = segments.FirstOrDefault(s => s.StartsWith("Listening Sample", StringComparison.OrdinalIgnoreCase));
            if (sampleFolder is null) { issue = "Listening file not under 'Listening Sample N' folder."; return false; }
            paperTitle = sampleFolder.Trim();
            appliesToAll = true;
            return true;
        }
        if (joined.Contains("reading"))
        {
            subtest = "reading";
            var sampleFolder = segments.FirstOrDefault(s => s.StartsWith("Reading Sample", StringComparison.OrdinalIgnoreCase));
            if (sampleFolder is null) { issue = "Reading file not under 'Reading Sample N' folder."; return false; }
            paperTitle = sampleFolder.Trim();
            appliesToAll = true;
            return true;
        }
        if (joined.Contains("writing"))
        {
            subtest = "writing";
            var writingFolder = segments.FirstOrDefault(s => Regex.IsMatch(s, @"^Writing\s*\d", RegexOptions.IgnoreCase));
            if (writingFolder is null) { issue = "Writing file not under 'Writing N' folder."; return false; }
            paperTitle = writingFolder.Trim();
            letterType = DetectByRules(writingFolder, LetterTypeRules);
            profession ??= "medicine";
            return true;
        }
        if (joined.Contains("speaking") || segments.Any(s => s.StartsWith("Card ", StringComparison.OrdinalIgnoreCase)))
        {
            subtest = "speaking";
            var cardFolder = segments.FirstOrDefault(s => s.StartsWith("Card ", StringComparison.OrdinalIgnoreCase));
            if (cardFolder is not null)
            {
                paperTitle = cardFolder.Trim();
                cardType = DetectByRules(cardFolder, CardTypeRules);
                profession ??= "medicine";
                return true;
            }
            // Speaking cross-cutting docs (warm-up, criteria) — treat each as its
            // own "paper" so they land as standalone reference assets.
            paperTitle = Path.GetFileNameWithoutExtension(segments[^1]);
            appliesToAll = true;
            return true;
        }

        issue = "No recognisable subtest keyword in path.";
        return false;
    }

    private static (PaperAssetRole role, string? part) ClassifyFile(string path)
    {
        var lower = path.ToLowerInvariant();
        var fname = Path.GetFileName(path);
        var flower = fname.ToLowerInvariant();

        if (lower.EndsWith(".mp3") || lower.EndsWith(".m4a") || lower.EndsWith(".wav") || lower.Contains("/audio "))
            return (PaperAssetRole.Audio, null);

        if (flower.Contains("question-paper") || flower.Contains("question paper"))
            return (PaperAssetRole.QuestionPaper, null);

        if (flower.Contains("audio-script") || flower.Contains("audio script"))
            return (PaperAssetRole.AudioScript, null);

        if (flower.Contains("answer-key") || flower.Contains("answer key")
            || flower.Equals("answers.pdf", StringComparison.OrdinalIgnoreCase)
            || flower.StartsWith("listening sample") && flower.Contains("answer"))
            return (PaperAssetRole.AnswerKey, null);

        if (flower.Contains("case notes"))
            return (PaperAssetRole.CaseNotes, null);

        if (flower.Contains("answer sheet"))
            return (PaperAssetRole.ModelAnswer, null);

        // Reading Part A / B+C
        if (flower.Contains("reading part a") || flower.StartsWith("part a reading"))
            return (PaperAssetRole.QuestionPaper, "A");
        if (flower.Contains("reading part b") || flower.Contains("part b&c") || flower.Contains("part b & c"))
            return (PaperAssetRole.QuestionPaper, "B+C");

        // Speaking cards are single PDFs named "1.pdf", "2.pdf", "Card 6.pdf", …
        if (path.Contains("speaking", StringComparison.OrdinalIgnoreCase)
            || path.Contains("card ", StringComparison.OrdinalIgnoreCase))
        {
            if (flower.Contains("assessment criteria")) return (PaperAssetRole.AssessmentCriteria, null);
            if (flower.Contains("warm up") || flower.Contains("warm-up") || flower.Contains("intro")) return (PaperAssetRole.WarmUpQuestions, null);
            return (PaperAssetRole.RoleCard, null);
        }

        return (PaperAssetRole.Supplementary, null);
    }

    private static string? DetectByRules(string input, (Regex pattern, string code)[] rules)
        => rules.FirstOrDefault(r => r.pattern.IsMatch(input)).code;
}
