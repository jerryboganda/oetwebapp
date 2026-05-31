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
    List<ProposedAsset> Assets)
{
    public List<ImportReadinessIssue> ReadinessIssues { get; } = [];
    public IReadOnlyList<string> DeliveryModes { get; init; } = ["paper", "computer", "oet_home"];
    public string OfficialShape { get; set; } = string.Empty;
}

public sealed record ProposedAsset(
    string SourceRelativePath,
    PaperAssetRole Role,
    string? Part,
    string? SuggestedTitle);

public sealed record ProposedReference(
    string ProposalId,
    string Target,
    string Title,
    string SourceRelativePath,
    string? Kind,
    string? ProfessionId,
    string? SharedResourceKind,
    string? TemplateKey,
    int? SortOrder,
    string? SourceProvenance)
{
    public List<ImportReadinessIssue> ReadinessIssues { get; } = [];
}

public static class ImportReferenceTargets
{
    public const string SpeakingSharedResource = "SpeakingSharedResource";
    public const string RulebookReferencePdf = "RulebookReferencePdf";
    public const string ScoringPolicyBody = "ScoringPolicyBody";
    public const string ResultTemplate = "ResultTemplate";
}

public sealed record ImportReadinessIssue(
    string Code,
    string Severity,
    string Message);

public sealed record ImportInventory(
    int TotalFiles,
    int ClassifiedFileCount,
    int UnclassifiedFileCount,
    IReadOnlyDictionary<string, int> FilesByExtension,
    IReadOnlyDictionary<string, int> FilesByTopLevel)
{
    public static ImportInventory Empty { get; } = new(
        0,
        0,
        0,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}

public sealed record ImportManifest(
    List<ProposedPaper> Papers,
    List<ProposedFileIssue> Issues)
{
    public List<ProposedReference> References { get; init; } = [];
    public ImportInventory Inventory { get; init; } = ImportInventory.Empty;
    public List<ImportReadinessIssue> ReadinessIssues { get; init; } = [];
}

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
        var references = new List<ProposedReference>();
        var extensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var topLevelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var classified = 0;
        var resultTemplateSequence = 1;

        foreach (var raw in relativePaths)
        {
            var path = raw.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(path)) continue;
            var sourceSegments = path.Split('/');
            var segments = StripCourseRoot(sourceSegments);
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = "(none)";
            Increment(extensionCounts, ext);
            Increment(topLevelCounts, segments.Length > 0 ? segments[0] : "(root)");

            if (TryClassifyReference(segments, path, resultTemplateSequence, out var reference))
            {
                references.Add(reference);
                if (reference.Target == ImportReferenceTargets.ResultTemplate) resultTemplateSequence++;
                classified++;
                continue;
            }

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
            classified++;
        }

        var papers = paperBuckets.Values.ToList();
        ApplyReadiness(papers, references);
        var readiness = new List<ImportReadinessIssue>();
        if (issues.Count > 0)
        {
            readiness.Add(new(
                Code: "unclassified_files",
                Severity: "warning",
                Message: $"{issues.Count} file(s) could not be classified automatically and need admin review."));
        }

        return new ImportManifest(papers, issues)
        {
            References = references,
            Inventory = new ImportInventory(
                TotalFiles: relativePaths.Count(path => !string.IsNullOrWhiteSpace(path)),
                ClassifiedFileCount: classified,
                UnclassifiedFileCount: issues.Count,
                FilesByExtension: extensionCounts,
                FilesByTopLevel: topLevelCounts),
            ReadinessIssues = readiness,
        };
    }

    private static void Increment(IDictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
    }

    private static string[] StripCourseRoot(string[] segments)
    {
        if (segments.Length <= 1) return segments;
        if (IsKnownTopLevelSegment(segments[0])) return segments;
        return IsKnownTopLevelSegment(segments[1]) ? segments.Skip(1).ToArray() : segments;
    }

    private static bool IsKnownTopLevelSegment(string segment)
    {
        var lower = segment.ToLowerInvariant();
        return lower.Contains("listening")
            || lower.Contains("reading")
            || lower.Contains("writing")
            || lower.Contains("speaking")
            || lower.Contains("scoring")
            || lower.Contains("result")
            || lower.Contains("table format");
    }

    private static bool TryClassifyReference(
        string[] segments,
        string sourcePath,
        int resultTemplateSequence,
        out ProposedReference reference)
    {
        reference = default!;
        var joined = string.Join('/', segments).ToLowerInvariant();
        var full = sourcePath.ToLowerInvariant();
        var fileName = Path.GetFileName(sourcePath);
        var fileLower = fileName.ToLowerInvariant();
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var profession = DetectProfession(sourcePath, segments);

        if (string.Equals(fileName, "Scoring System.txt", StringComparison.OrdinalIgnoreCase))
        {
            reference = new ProposedReference(
                ProposalId: Guid.NewGuid().ToString("N"),
                Target: ImportReferenceTargets.ScoringPolicyBody,
                Title: "Scoring System",
                SourceRelativePath: sourcePath,
                Kind: null,
                ProfessionId: null,
                SharedResourceKind: null,
                TemplateKey: null,
                SortOrder: null,
                SourceProvenance: ContentDefaults.DefaultSourceProvenance);
            return true;
        }

        if (ext == ".pdf" && joined.Contains("speaking") && fileLower.Contains("assessment criteria"))
        {
            reference = new ProposedReference(
                Guid.NewGuid().ToString("N"),
                ImportReferenceTargets.SpeakingSharedResource,
                "Speaking Assessment Criteria",
                sourcePath,
                Kind: null,
                ProfessionId: null,
                SharedResourceKind: SpeakingSharedResourceKinds.AssessmentCriteria,
                TemplateKey: null,
                SortOrder: null,
                SourceProvenance: ContentDefaults.DefaultSourceProvenance);
            return true;
        }

        if (ext == ".pdf" && joined.Contains("speaking")
            && (fileLower.Contains("warm up") || fileLower.Contains("warm-up") || fileLower.Contains("intro questions")))
        {
            reference = new ProposedReference(
                Guid.NewGuid().ToString("N"),
                ImportReferenceTargets.SpeakingSharedResource,
                "Speaking Warm-Up Questions",
                sourcePath,
                Kind: null,
                ProfessionId: null,
                SharedResourceKind: SpeakingSharedResourceKinds.WarmUpQuestions,
                TemplateKey: null,
                SortOrder: null,
                SourceProvenance: ContentDefaults.DefaultSourceProvenance);
            return true;
        }

        if (ext == ".pdf" && joined.Contains("rulebook"))
        {
            var kind = DetectKind(joined);
            if (kind is not null)
            {
                reference = new ProposedReference(
                    Guid.NewGuid().ToString("N"),
                    ImportReferenceTargets.RulebookReferencePdf,
                    $"{TitleCase(kind)} Rulebook Reference PDF",
                    sourcePath,
                    Kind: kind,
                    ProfessionId: profession ?? "medicine",
                    SharedResourceKind: null,
                    TemplateKey: null,
                    SortOrder: null,
                    SourceProvenance: ContentDefaults.DefaultSourceProvenance);
                return true;
            }
        }

        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp"
            && (full.Contains("result") || full.Contains("table format")))
        {
            reference = new ProposedReference(
                Guid.NewGuid().ToString("N"),
                ImportReferenceTargets.ResultTemplate,
                $"Result template {resultTemplateSequence:00}",
                sourcePath,
                Kind: null,
                ProfessionId: profession,
                SharedResourceKind: null,
                TemplateKey: $"real-content-{resultTemplateSequence:00}-{Slugify(Path.GetFileNameWithoutExtension(fileName))}",
                SortOrder: resultTemplateSequence,
                SourceProvenance: ContentDefaults.DefaultSourceProvenance);
            return true;
        }

        return false;
    }

    private static string? DetectKind(string lowerPath)
    {
        if (lowerPath.Contains("listening")) return "listening";
        if (lowerPath.Contains("reading")) return "reading";
        if (lowerPath.Contains("writing")) return "writing";
        if (lowerPath.Contains("speaking")) return "speaking";
        return null;
    }

    private static string? DetectProfession(string sourcePath, string[] segments)
    {
        var joined = string.Join('/', segments.Concat([sourcePath])).ToLowerInvariant();
        return joined.Contains("medicine") ? "medicine" : null;
    }

    private static string TitleCase(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

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

        if (lower.Contains("writing"))
        {
            if (flower.Contains("answer sheet") || flower.Contains("model answer"))
                return (PaperAssetRole.ModelAnswer, null);
            if (flower.Contains("case notes") || lower.EndsWith(".pdf"))
                return (PaperAssetRole.CaseNotes, null);
        }

        if (lower.Contains("reading"))
        {
            if (flower.Contains("answer")) return (PaperAssetRole.AnswerKey, "A");
            if (flower.Contains("text booklet") || flower.Contains("text-booklet") || flower.Contains("booklet"))
                return (PaperAssetRole.Supplementary, "A");
            if (flower.Contains("b&c") || flower.Contains("b & c") || flower.Contains("part b") || flower.Contains("part c"))
                return (PaperAssetRole.QuestionPaper, "B+C");
            if ((flower.Contains("question") && flower.Contains("part a")) || flower.StartsWith("part a reading"))
                return (PaperAssetRole.QuestionPaper, "A");
        }

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

    private static void ApplyReadiness(IReadOnlyList<ProposedPaper> papers, IReadOnlyList<ProposedReference> references)
    {
        var hasSpeakingAssessment = references.Any(r => r.Target == ImportReferenceTargets.SpeakingSharedResource
            && r.SharedResourceKind == SpeakingSharedResourceKinds.AssessmentCriteria);
        var hasSpeakingWarmUp = references.Any(r => r.Target == ImportReferenceTargets.SpeakingSharedResource
            && r.SharedResourceKind == SpeakingSharedResourceKinds.WarmUpQuestions);

        foreach (var paper in papers)
        {
            paper.OfficialShape = paper.SubtestCode switch
            {
                "listening" => "OET Listening: 40 minutes, 3 parts, 42 questions (24 + 6 + 12).",
                "reading" => "OET Reading: 60 minutes, 3 parts, 42 questions (20 + 6 + 16).",
                "writing" => "OET Writing: 45-minute profession-specific formal letter from case notes.",
                "speaking" => "OET Speaking: approximately 20 minutes; full sub-test uses two 5-minute role plays.",
                _ => "OET paper structure requires admin review.",
            };

            var groups = paper.Assets.GroupBy(a => new { a.Role, a.Part }).ToList();
            foreach (var duplicate in groups.Where(group => group.Count() > 1))
            {
                paper.ReadinessIssues.Add(new(
                    Code: "duplicate_asset_slot",
                    Severity: "warning",
                    Message: $"Multiple assets map to {duplicate.Key.Role}/{duplicate.Key.Part ?? "paper"}; commit will make only one primary."));
            }

            RequireAsset(paper, PaperAssetRole.Audio, requiredFor: "listening");
            RequireAsset(paper, PaperAssetRole.QuestionPaper, requiredFor: "listening");
            RequireAsset(paper, PaperAssetRole.AudioScript, requiredFor: "listening");
            RequireAsset(paper, PaperAssetRole.AnswerKey, requiredFor: "listening");
            RequireAsset(paper, PaperAssetRole.QuestionPaper, requiredFor: "reading");
            RequireAsset(paper, PaperAssetRole.AnswerKey, requiredFor: "reading");
            RequireAsset(paper, PaperAssetRole.CaseNotes, requiredFor: "writing");
            RequireAsset(paper, PaperAssetRole.ModelAnswer, requiredFor: "writing");
            RequireAsset(paper, PaperAssetRole.RoleCard, requiredFor: "speaking");

            if (paper.SubtestCode == "speaking")
            {
                if (!hasSpeakingAssessment && !paper.Assets.Any(a => a.Role == PaperAssetRole.AssessmentCriteria))
                    paper.ReadinessIssues.Add(new("missing_assessment_criteria", "error", "Speaking cards need Assessment Criteria as a shared resource or paper asset."));
                if (!hasSpeakingWarmUp && !paper.Assets.Any(a => a.Role == PaperAssetRole.WarmUpQuestions))
                    paper.ReadinessIssues.Add(new("missing_warm_up_questions", "error", "Speaking cards need Warm-Up Questions as a shared resource or paper asset."));
            }

            if (paper.SubtestCode is "listening" or "reading")
            {
                paper.ReadinessIssues.Add(new(
                    Code: "structure_authoring_required",
                    Severity: "warning",
                    Message: "Assets are upload-ready; publish still requires the authored 42-question structure/import manifest."));
            }
            else if (paper.SubtestCode is "writing" or "speaking")
            {
                paper.ReadinessIssues.Add(new(
                    Code: "structure_authoring_required",
                    Severity: "warning",
                    Message: "Assets are upload-ready; publish still requires reviewed structured task/card content."));
            }
        }
    }

    private static void RequireAsset(ProposedPaper paper, PaperAssetRole role, string requiredFor)
    {
        if (!string.Equals(paper.SubtestCode, requiredFor, StringComparison.OrdinalIgnoreCase)) return;
        if (paper.Assets.Any(a => a.Role == role)) return;
        paper.ReadinessIssues.Add(new(
            Code: $"missing_{role.ToString().ToLowerInvariant()}",
            Severity: "error",
            Message: $"{paper.Title} is missing required {role} asset."));
    }

    private static string Slugify(string s)
    {
        var lower = s.ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
