using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Phase 8 of LISTENING-MODULE-PLAN.md — grounded-gateway AI extraction.
//
// Real implementation of `IListeningExtractionAi`. Routes the AI call through
// `IAiGatewayService.BuildGroundedPrompt(...)` with `Kind = RuleKind.Listening`
// and `FeatureCode = AiFeatureCodes.AdminListeningDraft` (platform-only). The
// gateway physically refuses ungrounded prompts via `PromptNotGroundedException`,
// and stamps every call into `AiUsageRecord` per docs/AI-USAGE-POLICY.md.
//
// Behaviour:
//  • Loads the ContentPaper + asset-keyed extracted text.
//  • Concatenates QuestionPaper + AudioScript + AnswerKey extracts as the
//    user message, with explicit role labels so the AI never has to guess.
//  • Asks the gateway for `AiTaskMode.GenerateListeningStructure` (canonical
//    24/6/12 split — see rulebooks/listening/<profession>/rulebook.v1.json).
//  • Parses the JSON reply, validates the 24/6/12 split, maps each item into
//    `ListeningAuthoredQuestion`.
//  • Any failure (gateway refusal, parse error, shape violation) falls back
//    to a deterministic placeholder structure with `IsStub = true` and a
//    stub-reason explaining what went wrong, so the admin UI never gets a
//    hard 500 from this seam.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class GroundedListeningExtractionAi(
    LearnerDbContext db,
    IAiGatewayService gateway,
    ILogger<GroundedListeningExtractionAi> logger)
    : IListeningExtractionAi
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public async Task<ListeningExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);

        if (paper is null)
            return Stub("ContentPaper not found.");

        var profession = ResolveProfession(paper);
        var sourceMessage = BuildSourceMessage(paper);
        if (string.IsNullOrWhiteSpace(sourceMessage))
            return Stub("No extracted text was found on the paper. Run the PDF extraction worker first.");

        AiGroundedPrompt prompt;
        try
        {
            prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Listening,
                Profession = profession,
                Task = AiTaskMode.GenerateListeningStructure,
            });
        }
        catch (RulebookNotFoundException ex)
        {
            logger.LogInformation(ex, "Listening rulebook missing for {Profession}; falling back to stub.", profession);
            return Stub($"No listening rulebook for profession {profession} yet; admin must select medicine or nursing source profession.");
        }
        catch (PromptNotGroundedException ex)
        {
            logger.LogError(ex, "Grounded prompt build failed for paper {PaperId}", paperId);
            return Stub("Grounded prompt build failed: " + ex.Message);
        }

        AiGatewayResult result;
        try
        {
            result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = sourceMessage,
                FeatureCode = AiFeatureCodes.AdminListeningDraft,
                Temperature = 0.1,
                MaxTokens = 6000,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI gateway call failed for paper {PaperId}", paperId);
            return Stub("AI gateway call failed: " + ex.Message);
        }

        var (questions, error) = ParseAndValidate(result.Completion);
        if (error is not null || questions is null)
        {
            logger.LogWarning("Grounded listening extraction parse failed for paper {PaperId}: {Error}", paperId, error);
            return Stub("AI reply could not be parsed: " + (error ?? "unknown error"));
        }

        return new ListeningExtractionAiResult(
            Questions: questions,
            RawResponseJson: result.Completion,
            IsStub: false,
            StubReason: null);
    }

    private static ExamProfession ResolveProfession(ContentPaper paper)
    {
        // Listening papers normally carry AppliesToAllProfessions=true. The
        // listening rulebook is profession-agnostic — Medicine and Nursing
        // share identical rules — so default to Medicine when no profession
        // is set. If an admin has tagged a profession explicitly, honour it.
        if (!string.IsNullOrEmpty(paper.ProfessionId)
            && Enum.TryParse<ExamProfession>(paper.ProfessionId, ignoreCase: true, out var p))
        {
            return p;
        }
        return ExamProfession.Medicine;
    }

    private string BuildSourceMessage(ContentPaper paper)
    {
        // ExtractedTextJson is { assetId: extractedText } as written by
        // ContentTextExtractionService. Re-attach role labels by joining
        // against paper.Assets so the AI never has to guess which blob is
        // the question paper vs the audio script vs the answer key.
        Dictionary<string, string>? extractedByAsset = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(paper.ExtractedTextJson))
            {
                extractedByAsset = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    paper.ExtractedTextJson, JsonOpts);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ExtractedTextJson on paper {PaperId} was not asset-keyed; treating as empty.", paper.Id);
        }
        extractedByAsset ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine($"# Listening paper source bundle — paperId {paper.Id}");
        sb.AppendLine();
        sb.AppendLine($"Title: {paper.Title}");
        sb.AppendLine($"Slug: {paper.Slug}");
        sb.AppendLine($"Difficulty: {paper.Difficulty}");
        sb.AppendLine();

        AppendRole(sb, paper, extractedByAsset, PaperAssetRole.QuestionPaper, "Question Paper");
        AppendRole(sb, paper, extractedByAsset, PaperAssetRole.AudioScript, "Audio Script");
        AppendRole(sb, paper, extractedByAsset, PaperAssetRole.AnswerKey, "Answer Key");

        return sb.ToString();
    }

    private static void AppendRole(
        StringBuilder sb,
        ContentPaper paper,
        IReadOnlyDictionary<string, string> extracted,
        PaperAssetRole role,
        string heading)
    {
        var assets = paper.Assets.Where(a => a.Role == role).ToList();
        if (assets.Count == 0) return;

        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        foreach (var a in assets)
        {
            if (!extracted.TryGetValue(a.Id, out var text) || string.IsNullOrWhiteSpace(text)) continue;
            sb.AppendLine("```");
            sb.AppendLine(text.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private static (IReadOnlyList<ListeningAuthoredQuestion>?, string?) ParseAndValidate(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion))
            return (null, "AI returned an empty completion.");

        // Tolerant of fenced ```json blocks.
        var json = StripFences(completion);

        ListeningExtractionReply? reply;
        try
        {
            reply = JsonSerializer.Deserialize<ListeningExtractionReply>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            return (null, "Reply was not valid JSON: " + ex.Message);
        }
        if (reply?.Questions is null || reply.Questions.Count == 0)
            return (null, "Reply did not contain a `questions` array.");

        var qs = reply.Questions;
        if (qs.Count != 42) return (null, $"Expected 42 questions, got {qs.Count}.");

        int partA = 0, partB = 0, partC = 0;
        foreach (var q in qs)
        {
            switch ((q.PartCode ?? "").ToUpperInvariant())
            {
                case "A1": case "A2": partA++; break;
                case "B": partB++; break;
                case "C1": case "C2": partC++; break;
            }
        }
        if (partA != 24) return (null, $"Expected 24 Part A items, got {partA}.");
        if (partB != 6) return (null, $"Expected 6 Part B items, got {partB}.");
        if (partC != 12) return (null, $"Expected 12 Part C items, got {partC}.");

        var mapped = new List<ListeningAuthoredQuestion>(42);
        var seen = new HashSet<int>();
        foreach (var q in qs)
        {
            if (q.Number < 1 || q.Number > 42) return (null, $"Invalid question number {q.Number}.");
            if (!seen.Add(q.Number)) return (null, $"Duplicate question number {q.Number}.");

            var part = (q.PartCode ?? "").ToUpperInvariant();
            var type = string.IsNullOrWhiteSpace(q.Type)
                ? (part == "B" || part.StartsWith("C") ? "multiple_choice_3" : "short_answer")
                : q.Type!;

            mapped.Add(new ListeningAuthoredQuestion(
                Id: $"lq-{q.Number}",
                Number: q.Number,
                PartCode: part,
                Type: type,
                Stem: q.Stem ?? string.Empty,
                Options: q.Options ?? Array.Empty<string>(),
                CorrectAnswer: q.CorrectAnswer ?? string.Empty,
                AcceptedAnswers: q.AcceptedAnswers ?? Array.Empty<string>(),
                Explanation: null,
                SkillTag: null,
                TranscriptExcerpt: q.TranscriptExcerpt,
                DistractorExplanation: q.DistractorExplanation,
                Points: q.Points <= 0 ? 1 : q.Points,
                OptionDistractorWhy: q.OptionDistractorWhy,
                OptionDistractorCategory: q.OptionDistractorCategory,
                SpeakerAttitude: q.SpeakerAttitude,
                TranscriptEvidenceStartMs: q.TranscriptEvidenceStartMs,
                TranscriptEvidenceEndMs: q.TranscriptEvidenceEndMs));
        }

        return (mapped.OrderBy(m => m.Number).ToList(), null);
    }

    private static string StripFences(string s)
    {
        var t = s.Trim();
        if (t.StartsWith("```"))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
        }
        return t.Trim();
    }

    private static ListeningExtractionAiResult Stub(string reason)
    {
        var items = new List<ListeningAuthoredQuestion>(42);
        for (var n = 1; n <= 12; n++) items.Add(BlankShortAnswer(n, "A1"));
        for (var n = 13; n <= 24; n++) items.Add(BlankShortAnswer(n, "A2"));
        for (var n = 25; n <= 30; n++) items.Add(BlankMcq(n, "B"));
        for (var n = 31; n <= 36; n++) items.Add(BlankMcq(n, "C1"));
        for (var n = 37; n <= 42; n++) items.Add(BlankMcq(n, "C2"));
        return new ListeningExtractionAiResult(items, RawResponseJson: null, IsStub: true, StubReason: reason);
    }

    private static ListeningAuthoredQuestion BlankShortAnswer(int number, string partCode) =>
        new(
            Id: $"lq-{number}",
            Number: number,
            PartCode: partCode,
            Type: "short_answer",
            Stem: string.Empty,
            Options: [],
            CorrectAnswer: string.Empty,
            AcceptedAnswers: [],
            Explanation: null,
            SkillTag: null,
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1);

    private static ListeningAuthoredQuestion BlankMcq(int number, string partCode) =>
        new(
            Id: $"lq-{number}",
            Number: number,
            PartCode: partCode,
            Type: "multiple_choice_3",
            Stem: string.Empty,
            Options: ["", "", ""],
            CorrectAnswer: string.Empty,
            AcceptedAnswers: [],
            Explanation: null,
            SkillTag: null,
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1,
            OptionDistractorWhy: [null, null, null],
            OptionDistractorCategory: [null, null, null]);

    private sealed class ListeningExtractionReply
    {
        public List<ListeningExtractionReplyQuestion>? Questions { get; set; }
    }

    private sealed class ListeningExtractionReplyQuestion
    {
        public int Number { get; set; }
        public string? PartCode { get; set; }
        public string? Type { get; set; }
        public string? Stem { get; set; }
        public IReadOnlyList<string>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public IReadOnlyList<string>? AcceptedAnswers { get; set; }
        public string? TranscriptExcerpt { get; set; }
        public string? DistractorExplanation { get; set; }
        public IReadOnlyList<string?>? OptionDistractorWhy { get; set; }
        public IReadOnlyList<string?>? OptionDistractorCategory { get; set; }
        public string? SpeakerAttitude { get; set; }
        public int? TranscriptEvidenceStartMs { get; set; }
        public int? TranscriptEvidenceEndMs { get; set; }
        public int Points { get; set; } = 1;
    }
}
