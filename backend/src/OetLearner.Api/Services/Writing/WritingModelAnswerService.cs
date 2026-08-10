using System.Text.Json;
using System.Text.RegularExpressions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingModelAnswerGroundingResult(
    bool IsGrounded,
    IReadOnlyList<string> UnmappedSentences);

public static class WritingModelAnswerGroundingValidator
{
    private static readonly string[] ClinicalTerms =
    ["asthma", "eczema", "hay fever", "diabetes", "cancer", "stroke", "allergy", "inhaler", "medicine", "dose", "mg", "surgery", "admission"];

    public static WritingModelAnswerGroundingResult Validate(string modelAnswer, IReadOnlyList<string> caseNoteFacts)
    {
        var source = string.Join(" ", caseNoteFacts ?? Array.Empty<string>());
        var unmapped = new List<string>();
        foreach (var sentence in Regex.Split(modelAnswer ?? string.Empty, @"(?<=[.!?])\s+"))
        {
            var value = sentence.Trim();
            if (value.Length == 0) continue;
            var terms = ClinicalTerms.Where(term => value.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (terms.Any(term => !source.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                unmapped.Add(value);
                continue;
            }

            var meaningful = Regex.Matches(value.ToLowerInvariant(), @"[a-z]{4,}")
                .Select(match => match.Value)
                .Where(word => word is not ("please" or "patient" or "review" or "write" or "your" or "this"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (meaningful.Length > 0
                && !caseNoteFacts.Any(fact => meaningful.Count(word => fact.Contains(word, StringComparison.OrdinalIgnoreCase)) >= Math.Min(2, meaningful.Length)))
            {
                unmapped.Add(value);
            }
        }

        return new WritingModelAnswerGroundingResult(unmapped.Count == 0, unmapped);
    }
}

public sealed record WritingModelAnswerGenerationResult(bool IsReady, string HoldReason);

/// <summary>
/// Generates the post-score model answer through the canonical grounded AI
/// gateway and refuses to publish any sentence that cannot be mapped to the
/// immutable case-note snapshot.
/// </summary>
public sealed class WritingModelAnswerService(IAiGatewayService gateway, ILogger<WritingModelAnswerService> logger)
{
    public async Task<WritingModelAnswerGenerationResult> PopulateAsync(
        WritingAssessmentReportV11 report,
        WritingAssessmentModelAnswer answer,
        string userId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(answer);

        try
        {
            if (!RulebookProfessionParser.TryParse(report.Profession, out var profession))
                return Hold(answer, "model_answer_profession_pack_unavailable");

            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = report.LetterType,
                Task = AiTaskMode.GenerateContent,
            });
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = $$"""
                    Produce the post-score OET Writing model answer for this exact task.
                    Return one JSON object only:
                    {"modelAnswerText":"...","correctedCandidateLetter":"... or null","whyThisWorks":["..."],"groundedFactReferences":["case-note-line:1"]}
                    The model answer must be approximately 180-200 words, recipient-appropriate,
                    and use only facts present in the case notes. Do not add diagnoses, tests,
                    treatment, dates, or requests that are not in the case notes.

                    Written task:
                    ---
                    {{report.TaskSnapshot}}
                    ---
                    Case notes:
                    ---
                    {{report.CaseNotesSnapshot}}
                    ---
                    Candidate letter (for learning feedback only):
                    ---
                    {{report.OriginalLetterSnapshot}}
                    ---
                    """,
                Temperature = 0.1,
                MaxTokens = 1800,
                FeatureCode = AiFeatureCodes.WritingGrade,
                PromptTemplateId = "writing.model-answer.v1",
                UserId = userId,
                AssessmentContext = AiAssessmentContext.Practice,
            }, ct);

            var parsed = Parse(result.Completion);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ModelAnswerText))
                return Hold(answer, "model_answer_unreadable");

            var words = Regex.Matches(parsed.ModelAnswerText, @"\b[\p{L}\p{N}’'-]+\b").Count;
            if (words < 180 || words > 200)
                return Hold(answer, "model_answer_word_count_out_of_range");

            var grounding = WritingModelAnswerGroundingValidator.Validate(
                parsed.ModelAnswerText,
                report.Facts.Select(x => x.FactText).ToArray());
            if (!grounding.IsGrounded)
            {
                answer.HoldReason = "model_answer_unmapped_sentence";
                answer.UpdatedAt = DateTimeOffset.UtcNow;
                return new WritingModelAnswerGenerationResult(false, answer.HoldReason);
            }

            answer.Status = WritingAssessmentModelAnswerStatus.Ready;
            answer.IsCandidateVisible = false;
            answer.ModelAnswerText = parsed.ModelAnswerText.Trim();
            answer.CorrectedCandidateLetter = string.IsNullOrWhiteSpace(parsed.CorrectedCandidateLetter)
                ? null
                : parsed.CorrectedCandidateLetter.Trim();
            answer.WhyThisWorksJson = JsonSerializer.Serialize(parsed.WhyThisWorks ?? []);
            answer.GroundedFactReferencesJson = JsonSerializer.Serialize(parsed.GroundedFactReferences ?? []);
            answer.HoldReason = null;
            answer.UpdatedAt = DateTimeOffset.UtcNow;
            return new WritingModelAnswerGenerationResult(true, "ready_for_release");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Grounded Writing model-answer generation failed for report {ReportId}", report.Id);
            return Hold(answer, "model_answer_generation_failed");
        }
    }

    private static WritingModelAnswerGenerationResult Hold(WritingAssessmentModelAnswer answer, string reason)
    {
        answer.Status = WritingAssessmentModelAnswerStatus.HeldForReview;
        answer.IsCandidateVisible = false;
        answer.HoldReason = reason;
        answer.UpdatedAt = DateTimeOffset.UtcNow;
        return new WritingModelAnswerGenerationResult(false, reason);
    }

    private static Draft? Parse(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<Draft>(completion[start..(end + 1)], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Draft(
        string? ModelAnswerText,
        string? CorrectedCandidateLetter,
        IReadOnlyList<string>? WhyThisWorks,
        IReadOnlyList<string>? GroundedFactReferences);
}
