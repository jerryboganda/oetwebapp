using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingTaskModelAnswerDto(
    Guid ScenarioId,
    string Status,
    bool IsCandidateVisible,
    string? ModelAnswerText,
    IReadOnlyList<string> GroundedFactReferences,
    string? HoldReason,
    string? RulebookVersion,
    string? ModelUsed,
    DateTimeOffset? GeneratedAt,
    string? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    bool IsStale);

/// <summary>
/// Generates and manages the ONE reusable, pre-generated Writing Model Answer
/// per task (spec: "Model Answer — generate once, save permanently, reuse").
/// Admin-triggered only; never called from the candidate submit path. Reuses
/// <see cref="WritingModelAnswerGroundingValidator"/> — the same fact-grounding
/// check the existing per-submission model-answer generator uses — so both
/// paths refuse to publish a sentence that cannot be traced to the case notes.
/// </summary>
public interface IWritingTaskModelAnswerService
{
    Task<WritingTaskModelAnswerDto?> GetAsync(Guid scenarioId, CancellationToken ct = default);
    Task<(IReadOnlyList<WritingTaskModelAnswerDto> Items, int Total)> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<WritingTaskModelAnswerDto> GenerateAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);
    Task<WritingTaskModelAnswerDto?> ApproveAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);
    Task<WritingTaskModelAnswerDto?> RejectAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);
}

public sealed class WritingTaskModelAnswerService(
    LearnerDbContext db,
    IAiGatewayService gateway,
    TimeProvider clock,
    ILogger<WritingTaskModelAnswerService> logger) : IWritingTaskModelAnswerService
{
    // Must fit AiOperation.PromptVersion, which is [MaxLength(32)] - confirmed
    // via production Npgsql exception (22001: value too long for type
    // character varying(32)) after the longer "writing.model-answer-pregenerate.v1"
    // (35 chars) broke every single pilot generation call.
    private const string PromptVersion = "writing.model-answer-pregen.v1";
    // Pinned per owner instruction: Model Answers are candidate-facing exemplar
    // content, generated once per task, so quality is prioritised over the
    // platform's default (cheaper) provider. Provider/Model set explicitly on
    // the request bypass feature-route resolution (AiGatewayService.cs).
    private const string PinnedProvider = "anthropic";
    private const string PinnedModel = "claude-sonnet-5";
    // Owner instruction: this is one-time content-authoring, not per-candidate
    // grading, so pay for max reasoning quality. budget_tokens must stay below
    // MaxTokens (Anthropic requires max_tokens > thinking.budget_tokens); the
    // ~4k gap leaves generous room for the ~200-word letter + JSON wrapper.
    private const int ThinkingBudgetTokens = 24000;
    private const int MaxCompletionTokens = 28000;

    public async Task<WritingTaskModelAnswerDto?> GetAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.AsNoTracking().FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario);
    }

    public async Task<(IReadOnlyList<WritingTaskModelAnswerDto> Items, int Total)> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingTaskModelAnswers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<WritingAssessmentModelAnswerStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(x => x.Status == parsed);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var scenarioIds = rows.Select(r => r.ScenarioId).ToList();
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => scenarioIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var items = rows.Select(r => ToDto(r, scenarios.GetValueOrDefault(r.ScenarioId))).ToList();
        return (items, total);
    }

    public async Task<WritingTaskModelAnswerDto> GenerateAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing task was not found.");

        var sentences = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => s.ScenarioId == scenarioId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);

        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new WritingTaskModelAnswer { Id = Guid.NewGuid(), ScenarioId = scenarioId, CreatedAt = now };
            db.WritingTaskModelAnswers.Add(row);
        }

        var taskSnapshot = scenario.TaskPromptMarkdown ?? string.Empty;
        var caseNotesText = string.Join("\n", sentences
            .Where(s => s.RelevanceLabel is "relevant" or "maybe")
            .Select(s => $"- {s.SentenceText}"));
        var allFacts = sentences.Select(s => s.SentenceText).ToArray();
        row.SourceContentHash = ComputeHash($"{taskSnapshot}\n---\n{caseNotesText}");
        row.UpdatedAt = now;

        if (sentences.Count == 0)
        {
            return Hold(row, "model_answer_case_notes_unavailable");
        }

        if (!RulebookProfessionParser.TryParse(scenario.Profession, out var profession))
        {
            return Hold(row, "model_answer_profession_pack_unavailable");
        }

        try
        {
            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = scenario.LetterType,
                Task = AiTaskMode.GenerateContent,
            });

            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                Provider = PinnedProvider,
                Model = PinnedModel,
                Temperature = 0.1,
                MaxTokens = MaxCompletionTokens,
                EnableExtendedThinking = true,
                ThinkingBudgetTokens = ThinkingBudgetTokens,
                FeatureCode = AiFeatureCodes.WritingModelAnswerPregenerate,
                PromptTemplateId = PromptVersion,
                UserId = adminUserId,
                AssessmentContext = AiAssessmentContext.Practice,
                ResourceId = scenarioId.ToString("D"),
                ResourceType = "writing_task_model_answer",
                UserInput = $$"""
                    Produce the ONE reusable OET Writing model answer for this task. It will be
                    shown to every candidate who completes this exact task, so it must be a
                    faithful, high-quality exemplar grounded ONLY in the case notes below.
                    Return one JSON object only:
                    {"modelAnswerText":"...","whyThisWorks":["..."],"groundedFactReferences":["..."]}
                    The model answer must be approximately 180-200 words, recipient-appropriate
                    per the writing task instruction, and use only facts present in the case
                    notes. Do not add diagnoses, tests, treatment, dates, or requests that are
                    not in the case notes.

                    Writing task:
                    ---
                    {{taskSnapshot}}
                    ---
                    Case notes:
                    ---
                    {{caseNotesText}}
                    ---
                    """,
            }, ct);

            var parsed = Parse(result.Completion);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ModelAnswerText))
            {
                return Hold(row, "model_answer_unreadable");
            }

            var words = Regex.Matches(parsed.ModelAnswerText, @"\b[\p{L}\p{N}’'-]+\b").Count;
            if (words < 180 || words > 200)
            {
                return Hold(row, "model_answer_word_count_out_of_range");
            }

            var grounding = WritingModelAnswerGroundingValidator.Validate(parsed.ModelAnswerText, allFacts);
            if (!grounding.IsGrounded)
            {
                return Hold(row, "model_answer_unmapped_sentence");
            }

            row.Status = WritingAssessmentModelAnswerStatus.Ready;
            row.IsCandidateVisible = false; // still needs an explicit admin approve
            row.ModelAnswerText = parsed.ModelAnswerText.Trim();
            row.GroundedFactReferencesJson = JsonSerializer.Serialize(parsed.GroundedFactReferences ?? []);
            row.HoldReason = null;
            row.RulebookVersion = string.IsNullOrWhiteSpace(result.RulebookVersion) ? null : result.RulebookVersion;
            row.PromptVersion = PromptVersion;
            row.ModelUsed = string.IsNullOrWhiteSpace(result.ResolvedModel) ? PinnedModel : result.ResolvedModel;
            row.GeneratedAt = now;
            row.ApprovedByUserId = null;
            row.ApprovedAt = null;

            await db.SaveChangesAsync(ct);
            return ToDto(row, scenario);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model-answer pregeneration failed for scenario {ScenarioId}", scenarioId);
            return Hold(row, "model_answer_generation_failed");
        }
    }

    public async Task<WritingTaskModelAnswerDto?> ApproveAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;
        if (row.Status != WritingAssessmentModelAnswerStatus.Ready)
        {
            throw ApiException.Validation("model_answer_not_ready", "Only a Ready model answer can be approved.");
        }

        row.IsCandidateVisible = true;
        row.ApprovedByUserId = adminUserId;
        row.ApprovedAt = clock.GetUtcNow();
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario);
    }

    public async Task<WritingTaskModelAnswerDto?> RejectAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;

        row.Status = WritingAssessmentModelAnswerStatus.Rejected;
        row.IsCandidateVisible = false;
        row.HoldReason = "rejected_by_admin";
        row.ApprovedByUserId = adminUserId;
        row.ApprovedAt = clock.GetUtcNow();
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario);
    }

    private WritingTaskModelAnswerDto Hold(WritingTaskModelAnswer row, string reason)
    {
        row.Status = WritingAssessmentModelAnswerStatus.HeldForReview;
        row.IsCandidateVisible = false;
        row.HoldReason = reason;
        row.UpdatedAt = clock.GetUtcNow();
        db.SaveChanges();
        return ToDto(row, null);
    }

    private static WritingTaskModelAnswerDto ToDto(WritingTaskModelAnswer row, WritingScenario? scenario)
    {
        _ = scenario; // reserved: a full staleness check needs the current case-notes
                      // text too (not just the scenario row), so it is intentionally
                      // not computed on this cheap list/get projection today. The
                      // stored SourceContentHash is enough for a future admin action
                      // to detect drift by recomputing over the live case notes.
        var refs = ParseFactReferences(row.GroundedFactReferencesJson);
        return new WritingTaskModelAnswerDto(
            row.ScenarioId,
            row.Status.ToString(),
            row.IsCandidateVisible,
            row.ModelAnswerText,
            refs,
            row.HoldReason,
            row.RulebookVersion,
            row.ModelUsed,
            row.GeneratedAt,
            row.ApprovedByUserId,
            row.ApprovedAt,
            IsStale: false);
    }

    private static IReadOnlyList<string> ParseFactReferences(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

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
        IReadOnlyList<string>? WhyThisWorks,
        IReadOnlyList<string>? GroundedFactReferences);
}
