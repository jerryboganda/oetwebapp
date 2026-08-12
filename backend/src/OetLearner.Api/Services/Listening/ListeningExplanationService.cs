using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

public interface IListeningExplanationService
{
    /// <summary>
    /// Generate an advisory explanation from the learner's stored answer on a
    /// submitted attempt. The service, not the client, supplies the answer,
    /// correct key, and approved evidence to the grounded gateway.
    /// </summary>
    Task<ListeningExplanationDto> GetSubmittedAttemptExplanationAsync(
        string userId,
        string attemptId,
        string questionId,
        string language,
        CancellationToken ct);
}

public sealed class ListeningGroundedExplanationUnavailableException(string message)
    : Exception(message);

public sealed record ListeningExplanationDto(
    string WhyCorrect,
    string WhyWrong,
    string TrapName,
    string AvoidTip,
    string Language);

public sealed class ListeningExplanationService(
    LearnerDbContext db,
    IAiGatewayService gateway,
    ILogger<ListeningExplanationService>? logger = null)
    : IListeningExplanationService
{
    private const string PromptTemplateId = "listening.explanation.v1";

    public async Task<ListeningExplanationDto> GetSubmittedAttemptExplanationAsync(
        string userId,
        string attemptId,
        string questionId,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(attemptId))
            throw new ArgumentException("attemptId must not be empty.", nameof(attemptId));
        if (string.IsNullOrWhiteSpace(questionId))
            throw new ArgumentException("questionId must not be empty.", nameof(questionId));

        var attempt = await db.ListeningAttempts.AsNoTracking()
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Listening attempt not found.");
        if (attempt.Status != ListeningAttemptStatus.Submitted)
            throw new InvalidOperationException("Grounded explanations are available only after submission.");

        var currentPaperRevision = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == attempt.PaperId && p.SubtestCode == "listening")
            .Select(p => p.PublishedRevisionId)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(attempt.PaperRevisionId)
            || string.IsNullOrWhiteSpace(currentPaperRevision)
            || !string.Equals(attempt.PaperRevisionId, currentPaperRevision, StringComparison.Ordinal))
        {
            throw new ListeningGroundedExplanationUnavailableException(
                "The submitted attempt is not pinned to the current published Listening revision.");
        }

        var answer = attempt.Answers.FirstOrDefault(a => a.ListeningQuestionId == questionId)
            ?? throw new KeyNotFoundException("The question has no stored answer on this attempt.");
        var question = await db.ListeningQuestions.AsNoTracking()
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.PaperId == attempt.PaperId, ct)
            ?? throw new KeyNotFoundException("Listening question not found for this attempt.");

        var questionVersionSnapshot = answer.QuestionVersionSnapshot
            ?? ResolveQuestionVersionSnapshot(attempt.LastQuestionVersionMapJson, questionId);
        if (!questionVersionSnapshot.HasValue || questionVersionSnapshot.Value != question.Version)
        {
            throw new ListeningGroundedExplanationUnavailableException(
                "The submitted attempt is not pinned to the current authored Listening question revision.");
        }

        var approvedRationale = await db.AssessmentRationales.AsNoTracking()
            .Where(r => r.Assessment == "listening"
                && r.QuestionRevisionId == question.Id
                && r.Status == AssessmentGovernanceStatus.Effective)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (approvedRationale is null
            || string.IsNullOrWhiteSpace(approvedRationale.RationaleText)
            || string.IsNullOrWhiteSpace(approvedRationale.SourceSentence))
        {
            throw new ListeningGroundedExplanationUnavailableException(
                "No effective author-approved rationale is available for this question.");
        }

        var lang = string.Equals(language?.Trim(), "ar", StringComparison.OrdinalIgnoreCase)
            ? "ar"
            : "en";
        var correctAnswer = ResolveJsonValue(question.CorrectAnswerJson);
        var storedAnswer = ResolveJsonValue(answer.UserAnswerJson);
        if (string.IsNullOrWhiteSpace(storedAnswer) || storedAnswer == "(unanswered)")
            throw new ListeningGroundedExplanationUnavailableException(
                "A grounded explanation requires a non-empty stored response.");
        var transcriptEvidence = question.TranscriptEvidenceText;
        var userMessage = BuildPrompt(
            question,
            correctAnswer,
            storedAnswer,
            approvedRationale.RationaleText,
            approvedRationale.SourceSentence,
            transcriptEvidence,
            lang);

        var groundedPrompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Listening,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateListeningExplanation,
        });

        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = groundedPrompt,
                UserInput = userMessage,
                Model = string.Empty,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.ListeningExplanation,
                PromptTemplateId = PromptTemplateId,
                UserId = userId,
            }, ct);
            return TryParse(result.Completion, lang)
                ?? throw new ListeningGroundedExplanationUnavailableException(
                    "The grounded gateway returned no usable explanation for this question.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "ListeningExplanationService — AI call failed for question '{QuestionId}'; explanation unavailable.",
                question.Id);
            throw new ListeningGroundedExplanationUnavailableException(
                "The grounded explanation is unavailable because the gateway failed.");
        }
    }

    private static string BuildPrompt(
        ListeningQuestion question,
        string correctAnswer,
        string storedAnswer,
        string approvedRationale,
        string sourceSentence,
        string? transcriptEvidence,
        string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine("For an OET Listening question after the attempt has been submitted:");
        sb.AppendLine($"Question: {question.Stem}");
        sb.AppendLine($"Options: {string.Join(" | ", question.Options.OrderBy(o => o.DisplayOrder).Select(o => $"{o.OptionKey}: {o.Text}"))}");
        sb.AppendLine($"Correct answer: {correctAnswer}");
        sb.AppendLine($"Learner's stored answer: {storedAnswer}");
        sb.AppendLine($"Author-approved rationale: {Truncate(approvedRationale)}");
        sb.AppendLine($"Author-approved source sentence: {Truncate(sourceSentence)}");
        if (!string.IsNullOrWhiteSpace(transcriptEvidence))
            sb.AppendLine($"Authored transcript evidence: {Truncate(transcriptEvidence)}");
        if (language == "ar")
            sb.AppendLine("Respond in Arabic (العربية), using clear language for an OET candidate.");
        sb.AppendLine("Return a SINGLE JSON object and no extra text:");
        sb.AppendLine("{");
        sb.AppendLine("  \"whyCorrect\": \"≤ 40 words\",");
        sb.AppendLine("  \"whyWrong\": \"≤ 40 words\",");
        sb.AppendLine("  \"trapName\": \"concise error or distractor label\",");
        sb.AppendLine("  \"avoidTip\": \"≤ 25 words\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static ListeningExplanationDto? TryParse(string completion, string language)
    {
        var json = ExtractJsonBlock(completion);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var whyCorrect = StringValue(doc.RootElement, "whyCorrect");
            var whyWrong = StringValue(doc.RootElement, "whyWrong");
            if (string.IsNullOrWhiteSpace(whyCorrect) || string.IsNullOrWhiteSpace(whyWrong)) return null;
            return new ListeningExplanationDto(
                whyCorrect.Trim(),
                whyWrong.Trim(),
                (StringValue(doc.RootElement, "trapName") ?? "Unknown").Trim(),
                (StringValue(doc.RootElement, "avoidTip") ?? string.Empty).Trim(),
                language);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveJsonValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(unanswered)";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString() ?? "(unanswered)"
                : doc.RootElement.ToString();
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static int? ResolveQuestionVersionSnapshot(string? versionMapJson, string questionId)
    {
        if (string.IsNullOrWhiteSpace(versionMapJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(versionMapJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(questionId, out var version)
                || !version.TryGetInt32(out var parsedVersion)
                || parsedVersion < 1)
            {
                return null;
            }

            return parsedVersion;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value) => value.Length <= 8_000 ? value : value[..8_000];

    private static string? StringValue(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ExtractJsonBlock(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) return trimmed;
        var start = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (start < 0) return null;
        var newline = trimmed.IndexOf('\n', start);
        var close = newline < 0 ? -1 : trimmed.IndexOf("```", newline + 1, StringComparison.Ordinal);
        if (newline < 0 || close < 0) return null;
        var inner = trimmed[(newline + 1)..close].Trim();
        return inner.StartsWith("{") && inner.EndsWith("}") ? inner : null;
    }
}
