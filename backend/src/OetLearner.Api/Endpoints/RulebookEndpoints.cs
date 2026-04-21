using System.Security.Claims;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Read-only rulebook endpoints + Writing linter + Speaking auditor +
/// grounded-AI gateway endpoint. Every AI request in the system flows
/// through <c>POST /v1/ai/complete</c>; building a prompt directly is not
/// exposed because the gateway already handles grounding.
/// </summary>
public static class RulebookEndpoints
{
    public static void MapRulebookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/rulebooks").RequireAuthorization();

        group.MapGet("/", (IRulebookLoader loader) =>
            Results.Ok(loader.All().Select(b => new
            {
                kind = b.Kind.ToString().ToLowerInvariant(),
                profession = b.Profession.ToString().ToLowerInvariant(),
                version = b.Version,
                authoritySource = b.AuthoritySource,
            })));

        group.MapGet("/writing/{profession}", (string profession, IRulebookLoader loader) =>
        {
            if (!Enum.TryParse<ExamProfession>(profession, ignoreCase: true, out var p))
                return Results.BadRequest(new { error = $"Unknown profession '{profession}'." });
            try { return Results.Ok(loader.Load(RuleKind.Writing, p)); }
            catch (RulebookNotFoundException) { return Results.NotFound(new { error = "Rulebook not registered." }); }
        });

        group.MapGet("/speaking/{profession}", (string profession, IRulebookLoader loader) =>
        {
            if (!Enum.TryParse<ExamProfession>(profession, ignoreCase: true, out var p))
                return Results.BadRequest(new { error = $"Unknown profession '{profession}'." });
            try { return Results.Ok(loader.Load(RuleKind.Speaking, p)); }
            catch (RulebookNotFoundException) { return Results.NotFound(new { error = "Rulebook not registered." }); }
        });

        group.MapGet("/conversation/{profession}", (string profession, IRulebookLoader loader) =>
        {
            if (!Enum.TryParse<ExamProfession>(profession, ignoreCase: true, out var p))
                return Results.BadRequest(new { error = $"Unknown profession '{profession}'." });
            try { return Results.Ok(loader.Load(RuleKind.Conversation, p)); }
            catch (RulebookNotFoundException) { return Results.NotFound(new { error = "Rulebook not registered." }); }
        });

        group.MapGet("/writing/{profession}/rule/{ruleId}",
            (string profession, string ruleId, IRulebookLoader loader) =>
            {
                if (!Enum.TryParse<ExamProfession>(profession, ignoreCase: true, out var p))
                    return Results.BadRequest(new { error = "Unknown profession." });
                var rule = loader.FindRule(RuleKind.Writing, p, ruleId);
                return rule is null ? Results.NotFound() : Results.Ok(rule);
            });

        group.MapGet("/speaking/{profession}/rule/{ruleId}",
            (string profession, string ruleId, IRulebookLoader loader) =>
            {
                if (!Enum.TryParse<ExamProfession>(profession, ignoreCase: true, out var p))
                    return Results.BadRequest(new { error = "Unknown profession." });
                var rule = loader.FindRule(RuleKind.Speaking, p, ruleId);
                return rule is null ? Results.NotFound() : Results.Ok(rule);
            });

        group.MapGet("/assessment/{kind}", (string kind, IRulebookLoader loader) =>
        {
            if (!Enum.TryParse<RuleKind>(kind, ignoreCase: true, out var k))
                return Results.BadRequest(new { error = "Unknown rulebook kind." });
            return Results.Ok(loader.GetAssessmentCriteria(k));
        });

        // Writing linter
        app.MapPost("/v1/writing/lint", (WritingLintRequest body, WritingRuleEngine engine) =>
        {
            if (!Enum.TryParse<ExamProfession>(body.Profession ?? "medicine", ignoreCase: true, out var prof))
                prof = ExamProfession.Medicine;
            var input = new WritingLintInput(
                body.LetterText ?? "",
                body.LetterType ?? "routine_referral",
                body.RecipientSpecialty,
                body.RecipientName,
                body.PatientAge,
                body.PatientIsMinor,
                body.CaseNotesMarkers,
                prof);
            var findings = engine.Lint(input);
            return Results.Ok(new
            {
                findings,
                totals = new
                {
                    critical = findings.Count(f => f.Severity == RuleSeverity.Critical),
                    major = findings.Count(f => f.Severity == RuleSeverity.Major),
                    minor = findings.Count(f => f.Severity == RuleSeverity.Minor),
                    info = findings.Count(f => f.Severity == RuleSeverity.Info),
                },
            });
        }).RequireAuthorization();

        // Speaking auditor
        app.MapPost("/v1/speaking/audit", (SpeakingAuditRequest body, SpeakingRuleEngine engine) =>
        {
            if (!Enum.TryParse<ExamProfession>(body.Profession ?? "medicine", ignoreCase: true, out var prof))
                prof = ExamProfession.Medicine;
            var input = new SpeakingAuditInput(
                body.Transcript ?? Array.Empty<SpeakingTurn>(),
                body.CardType ?? "first_visit_routine",
                prof,
                body.SilenceAfterDiagnosisMs);
            var findings = engine.Audit(input);
            return Results.Ok(new { findings });
        }).RequireAuthorization();

        // Grounded AI gateway
        app.MapPost("/v1/ai/complete", async (
            AiCompleteRequest body,
            IAiGatewayService gateway,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<RuleKind>(body.Kind ?? "writing", ignoreCase: true, out var kind))
                return Results.BadRequest(new { error = "Unknown rulebook kind." });
            if (!Enum.TryParse<ExamProfession>(body.Profession ?? "medicine", ignoreCase: true, out var prof))
                prof = ExamProfession.Medicine;
            if (!Enum.TryParse<AiTaskMode>(body.Task ?? "score", ignoreCase: true, out var task))
                task = AiTaskMode.Score;

            var ctx = new AiGroundingContext
            {
                Kind = kind,
                Profession = prof,
                Task = task,
                LetterType = body.LetterType,
                CardType = body.CardType,
                CandidateCountry = body.CandidateCountry,
            };
            var prompt = gateway.BuildGroundedPrompt(ctx);

            // Classify this call into the feature-eligibility matrix so the
            // resolver, quota service, and admin explorer can reason about
            // it correctly. See docs/AI-USAGE-POLICY.md §5.
            var featureCode = ClassifyFeature(kind, task);
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var authAccountId = http.User.FindFirstValue("aid");

            try
            {
                var result = await gateway.CompleteAsync(new AiGatewayRequest
                {
                    Prompt = prompt,
                    UserInput = body.UserInput,
                    Provider = body.Provider ?? string.Empty,
                    Model = body.Model ?? "mock",
                    Temperature = body.Temperature ?? 0.2,
                    MaxTokens = body.MaxTokens,
                    UserId = userId,
                    AuthAccountId = authAccountId,
                    FeatureCode = featureCode,
                }, ct);
                return Results.Ok(new
                {
                    completion = result.Completion,
                    rulebookVersion = result.RulebookVersion,
                    appliedRuleIds = result.AppliedRuleIds,
                    metadata = result.Metadata,
                    promptHeadSnippet = prompt.SystemPrompt.Length > 400 ? prompt.SystemPrompt[..400] + "…" : prompt.SystemPrompt,
                });
            }
            catch (OetLearner.Api.Services.AiManagement.AiQuotaDeniedException qex)
            {
                // 429 is the right code for rate/quota denials. The UI maps
                // errorCode to an upgrade CTA; see docs/AI-USAGE-POLICY.md §4.
                return Results.Json(new
                {
                    errorCode = qex.ErrorCode,
                    error = qex.Message,
                }, statusCode: StatusCodes.Status429TooManyRequests);
            }
        }).RequireAuthorization();
    }

    /// <summary>
    /// Map the grounded prompt kind + task to a canonical feature code. Keep
    /// in sync with <c>AiFeatureCodes</c> and the matrix in
    /// <c>docs/AI-USAGE-POLICY.md</c> §5.
    /// </summary>
    private static string ClassifyFeature(RuleKind kind, AiTaskMode task)
    {
        return (kind, task) switch
        {
            (RuleKind.Writing, AiTaskMode.Score) => AiFeatureCodes.WritingGrade,
            (RuleKind.Writing, AiTaskMode.Coach) => AiFeatureCodes.WritingCoachExplain,
            (RuleKind.Writing, AiTaskMode.Correct) => AiFeatureCodes.WritingCoachSuggest,
            (RuleKind.Writing, AiTaskMode.Summarise) => AiFeatureCodes.SummarisePassage,
            (RuleKind.Writing, AiTaskMode.GenerateFeedback) => AiFeatureCodes.WritingCoachExplain,
            (RuleKind.Writing, AiTaskMode.GenerateContent) => AiFeatureCodes.AdminContentGeneration,
            (RuleKind.Speaking, AiTaskMode.Score) => AiFeatureCodes.SpeakingGrade,
            (RuleKind.Speaking, AiTaskMode.Coach) => AiFeatureCodes.PronunciationTip,
            (RuleKind.Speaking, AiTaskMode.Correct) => AiFeatureCodes.PronunciationTip,
            (RuleKind.Speaking, AiTaskMode.GenerateContent) => AiFeatureCodes.AdminContentGeneration,
            _ => AiFeatureCodes.Unclassified,
        };
    }
}

public sealed record WritingLintRequest(
    string? LetterText,
    string? LetterType,
    string? RecipientSpecialty,
    string? RecipientName,
    int? PatientAge,
    bool PatientIsMinor,
    WritingCaseNotesMarkers? CaseNotesMarkers,
    string? Profession);

public sealed record SpeakingAuditRequest(
    IReadOnlyList<SpeakingTurn>? Transcript,
    string? CardType,
    string? Profession,
    int? SilenceAfterDiagnosisMs);

public sealed record AiCompleteRequest(
    string? Kind,
    string? Profession,
    string? Task,
    string? LetterType,
    string? CardType,
    string? CandidateCountry,
    string? UserInput,
    string? Provider,
    string? Model,
    double? Temperature,
    int? MaxTokens);
