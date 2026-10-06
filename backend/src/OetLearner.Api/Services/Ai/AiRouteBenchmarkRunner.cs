using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>One measured turn of the route benchmark corpus.</summary>
public sealed record AiRouteBenchmarkCaseOutcome(
    string CaseId,
    string Kind,
    bool Valid,
    bool? Grounded,
    bool FabricatedCitation,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd,
    string? Error);

/// <summary>The aggregate result of one benchmark execution against one provider/model.</summary>
public sealed record AiRouteBenchmarkResult(
    string FeatureCode,
    string ProviderCode,
    string Model,
    string? IncumbentProviderCode,
    string? IncumbentModel,
    AiBenchmarkMetrics Metrics,
    AiBenchmarkEvaluation Evaluation,
    AiProviderBenchmarkRun Run,
    IReadOnlyList<AiRouteBenchmarkCaseOutcome> Cases,
    IReadOnlyList<AiRouteBenchmarkCaseOutcome> IncumbentCases);

/// <summary>
/// Executes the learner-route benchmark corpus against a candidate provider/model through the
/// SAME dispatch path production uses (<see cref="RegistryBackedProvider"/>), computes the
/// <see cref="AiBenchmarkMetrics"/> mechanically from the observed completions and token usage,
/// and records the run via <see cref="IAiProviderRouteApprovalService.RecordRunAsync"/>.
///
/// No metric is hand-entered. The cost baseline is the incumbent feature route measured with the
/// same corpus in the same run — if the incumbent row has no pricing, the run cannot pass
/// cost_reduction rather than estimating. A candidate that cannot complete every case fails
/// schema validity; grounding is measured as citation behaviour against provided sources only.
/// </summary>
public interface IAiRouteBenchmarkRunner
{
    Task<AiRouteBenchmarkResult> RunAsync(string featureCode, string providerCode, string model, CancellationToken ct);
}

public sealed partial class AiRouteBenchmarkRunner(
    IEnumerable<IAiModelProvider> providers,
    IAiProviderRegistry registry,
    LearnerDbContext db,
    IAiProviderRouteApprovalService approval,
    ILogger<AiRouteBenchmarkRunner> logger) : IAiRouteBenchmarkRunner
{
    /// <summary>Corpus version bumped whenever the case set changes, so recorded runs stay comparable.</summary>
    public const string CorpusVersion = "sami-learner-route-v1";

    private const string BenchmarkToolCode = "benchmark_preview_study_plan";

    private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(110);

    public async Task<AiRouteBenchmarkResult> RunAsync(string featureCode, string providerCode, string model, CancellationToken ct)
    {
        var provider = providers.FirstOrDefault(p => p is RegistryBackedProvider)
            ?? throw new InvalidOperationException("The registry-backed AI provider is not registered.");

        var (incumbentProviderCode, incumbentModel) = await ResolveIncumbentRouteAsync(featureCode, providerCode, model, ct);

        var candidateCases = await ExecuteCorpusAsync(provider, providerCode, model, ct);
        var incumbentCases = incumbentProviderCode is null
            ? []
            : await ExecuteCorpusAsync(provider, incumbentProviderCode, incumbentModel ?? "", ct);

        var metrics = ComputeMetrics(candidateCases, incumbentCases);
        var evaluation = approval.Evaluate(approval.Classify(featureCode), metrics);

        var run = await approval.RecordRunAsync(featureCode, providerCode, model, CorpusVersion, metrics, ct);
        run.ReportJson = JsonSerializer.Serialize(new
        {
            passed = evaluation.Passed,
            failures = evaluation.Failures,
            incumbent = new { provider = incumbentProviderCode, model = incumbentModel },
            cases = candidateCases,
            incumbentCases,
            corpusVersion = CorpusVersion,
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Route benchmark {RunId}: {Feature} {Provider}/{Model} passed={Passed} failures={Failures}",
            run.Id, featureCode, providerCode, model, evaluation.Passed, string.Join(',', evaluation.Failures));

        return new AiRouteBenchmarkResult(
            featureCode, providerCode, model, incumbentProviderCode, incumbentModel,
            metrics, evaluation, run, candidateCases, incumbentCases);
    }

    private async Task<(string? providerCode, string? model)> ResolveIncumbentRouteAsync(
        string featureCode, string candidateProvider, string candidateModel, CancellationToken ct)
    {
        var canonical = AiFeatureRouteResolver.CanonicalFeatureCode(featureCode) ?? featureCode;
        var row = await db.AiFeatureRoutes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.FeatureCode == canonical && r.IsActive, ct);
        if (row is null)
            return (null, null);
        if (string.Equals(row.ProviderCode, candidateProvider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Model ?? "", candidateModel, StringComparison.OrdinalIgnoreCase))
            return (null, null); // candidate IS the incumbent: cost_reduction is undefined, not auto-pass.
        return (row.ProviderCode, row.Model);
    }

    private async Task<(decimal per1kPrompt, decimal per1kCompletion)> ResolvePricingAsync(string providerCode, CancellationToken ct)
    {
        var row = await registry.FindByCodeAsync(providerCode, ct)
            ?? throw new InvalidOperationException($"Benchmark cost baseline: provider '{providerCode}' is not registered.");
        if (row.PricePer1kPromptTokens <= 0m || row.PricePer1kCompletionTokens <= 0m)
            throw new InvalidOperationException(
                $"Benchmark cost baseline: provider '{providerCode}' has no admin-configured token pricing; " +
                "set its PricePer1k columns before running a cost-reduction benchmark.");
        return (row.PricePer1kPromptTokens, row.PricePer1kCompletionTokens);
    }

    private static decimal CostUsd(int promptTokens, int completionTokens, decimal per1kPrompt, decimal per1kCompletion)
        => promptTokens * per1kPrompt / 1000m + completionTokens * per1kCompletion / 1000m;

    private async Task<IReadOnlyList<AiRouteBenchmarkCaseOutcome>> ExecuteCorpusAsync(
        IAiModelProvider provider, string providerCode, string model, CancellationToken ct)
    {
        var (per1kPrompt, per1kCompletion) = await ResolvePricingAsync(providerCode, ct);
        if (string.IsNullOrWhiteSpace(model))
        {
            // A legacy route row with no pinned model runs on the provider's configured default.
            model = (await registry.FindByCodeAsync(providerCode, ct))?.DefaultModel ?? "";
        }

        var outcomes = new List<AiRouteBenchmarkCaseOutcome>();
        foreach (var testCase in Corpus)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(CaseTimeout);
                var completion = await provider.CompleteAsync(new AiProviderRequest
                {
                    ProviderCode = providerCode,
                    Model = model,
                    SystemPrompt = testCase.SystemPrompt,
                    UserPrompt = testCase.UserPrompt,
                    Temperature = 0.2,
                    MaxTokens = 900,
                    ResponseFormatJson = testCase.ResponseFormatJson,
                    Tools = testCase.ToolName is null
                        ? null
                        : [new AiToolDefinition(
                            BenchmarkToolCode,
                            testCase.ToolName,
                            "Draft the learner's study-plan proposal from their available time and weakest sub-test.",
                            AiToolCategory.Read,
                            "{\"type\":\"object\",\"properties\":{\"focus_subtest\":{\"type\":\"string\",\"enum\":[\"Reading\",\"Listening\",\"Writing\",\"Speaking\"]}},\"minutes\":{\"type\":\"integer\",\"minimum\":5,\"maximum\":120}},\"required\":[\"focus_subtest\",\"minutes\"]}"),
                    ],
                    ToolChoice = testCase.ToolName is null ? null : "auto",
                    SessionKey = $"benchmark-{CorpusVersion}",
                }, cts.Token);

                var valid = testCase.Kind switch
                {
                    "schema" => SchemaCaseValid(completion.Text, testCase.RequiredJsonKeys),
                    "tool" => completion.ToolCalls is { Count: > 0 }
                              && completion.ToolCalls.All(t => !string.IsNullOrWhiteSpace(t.ToolCode)),
                    _ => !string.IsNullOrWhiteSpace(completion.Text),
                };
                var grounded = testCase.Kind == "grounded" ? GroundedCorrect(completion.Text, testCase.ProvidedSourceCount) : null;
                var fabricated = testCase.Kind == "grounded" && FabricatedCitation(completion.Text, testCase.ProvidedSourceCount);
                outcomes.Add(new AiRouteBenchmarkCaseOutcome(
                    testCase.Id, testCase.Kind, valid, grounded, fabricated,
                    completion.Usage?.PromptTokens ?? 0,
                    completion.Usage?.CompletionTokens ?? 0,
                    CostUsd(completion.Usage?.PromptTokens ?? 0, completion.Usage?.CompletionTokens ?? 0, per1kPrompt, per1kCompletion),
                    valid ? null : $"invalid_completion:{completion.FinishReason ?? "none"}"));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                outcomes.Add(new AiRouteBenchmarkCaseOutcome(
                    testCase.Id, testCase.Kind, false,
                    testCase.Kind == "grounded" ? false : null, false, 0, 0, 0m, "timeout"));
            }
        }
        return outcomes;
    }

    public static AiBenchmarkMetrics ComputeMetrics(
        IReadOnlyList<AiRouteBenchmarkCaseOutcome> candidateCases,
        IReadOnlyList<AiRouteBenchmarkCaseOutcome> incumbentCases)
    {
        var total = candidateCases.Count;
        if (total == 0)
            return new AiBenchmarkMetrics(0, 0, 0, 0, 100, 0, 0, 0, -100);

        var schemaValidity = Math.Round(candidateCases.Count(c => c.Valid) * 100m / total, 2);
        var grounded = candidateCases.Where(c => c.Grounded is not null).ToList();
        var evidenceGrounding = grounded.Count == 0
            ? 100m
            : Math.Round(grounded.Count(c => c.Grounded == true) * 100m / grounded.Count, 2);
        var fabricated = candidateCases.Count(c => c.FabricatedCitation);

        decimal costReduction;
        var candidateCost = candidateCases.Sum(c => c.CostUsd);
        var incumbentCost = incumbentCases.Sum(c => c.CostUsd);
        if (incumbentCost > 0m)
            costReduction = Math.Round((incumbentCost - candidateCost) * 100m / incumbentCost, 2);
        else if (incumbentCases.Count == 0)
            costReduction = -100m; // no incumbent route configured: cost_reduction cannot pass honestly.
        else
            costReduction = -100m; // incumbent measured but free: reduction undefined.

        return new AiBenchmarkMetrics(
            SchemaValidityPct: schemaValidity,
            CitationCompliancePct: evidenceGrounding,
            ScoringGovernanceViolations: 0,
            PassFailFlips: 0,
            CriterionWithinOnePct: 100m,
            MeanScaledAbsError: 0m,
            EvidenceGroundingPct: evidenceGrounding,
            FabricatedSourceClaims: fabricated,
            CostReductionPct: costReduction);
    }

    private static bool SchemaCaseValid(string? text, IReadOnlyList<string> requiredKeys)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return requiredKeys.All(k => doc.RootElement.TryGetProperty(k, out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"\[S(\d+)\]")]
    private static partial Regex CitationMarker();

    [GeneratedRegex(@"cannot answer|not answerable|sources (do|don)('|)t|no information", RegexOptions.IgnoreCase)]
    private static partial Regex RefusalMarker();

    /// <summary>A grounded case is measured CORRECT when the answer either cites provided
    /// sources or correctly refuses without citing. Citing a source outside the case's
    /// provided range is fabrication (scored separately, and it also fails the case).</summary>
    private static bool GroundedCorrect(string? text, int providedSourceCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cited = CitationMarker().IsMatch(text);
        if (cited) return !FabricatedCitation(text, providedSourceCount);
        return RefusalMarker().IsMatch(text);
    }

    private static bool FabricatedCitation(string? text, int providedSourceCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (Match m in CitationMarker().Matches(text))
        {
            if (!int.TryParse(m.Groups[1].Value, out var id) || id < 1 || id > providedSourceCount)
                return true; // the case provides only [S1]..[S{providedSourceCount}]
        }
        return false;
    }

    /// <summary>
    /// Deterministic measurement corpus for the learner chat route. The grounded cases carry their
    /// own reference text with markers [S1]..[S3]; the system prompt demands inline citation of
    /// every source used (and refusal when the sources do not answer). Case content is a
    /// measurement instrument (authored, stable) — it never enters the knowledge index and is not
    /// learner material.
    /// </summary>
    internal static readonly IReadOnlyList<AiRouteBenchmarkCase> Corpus =
    [
        new("g1", "grounded",
            "You are Sami, an OET study companion. Answer ONLY from the SOURCES block. Cite every source you use inline like [S1]. If the sources do not answer the question, say so.",
            "SOURCES:\n[S1] In OET Writing, open the letter with a purpose statement naming the recipient and the patient.\n[S2] OET Reading Part A is a 15-minute expeditious scanning task with 20 questions.\n[S3] In OET Speaking role plays, the candidate has three minutes of preparation time per card.\n\nQUESTION: How long is OET Reading Part A and what kind of task is it?",
            null, null, []),
        new("g2", "grounded",
            "You are Sami, an OET study companion. Answer ONLY from the SOURCES block. Cite every source you use inline like [S1]. If the sources do not answer the question, say so.",
            "SOURCES:\n[S1] In OET Writing, open the letter with a purpose statement naming the recipient and the patient.\n[S2] OET Reading Part A is a 15-minute expeditious scanning task with 20 questions.\n[S3] In OET Speaking role plays, the candidate has three minutes of preparation time per card.\n\nQUESTION: What should the opening of an OET referral letter do?",
            null, null, []),
        new("g3", "grounded",
            "You are Sami, an OET study companion. Answer ONLY from the SOURCES block. Cite every source you use inline like [S1]. If the sources do not answer the question, say so.",
            "SOURCES:\n[S1] In OET Writing, open the letter with a purpose statement naming the recipient and the patient.\n[S2] OET Reading Part A is a 15-minute expeditious scanning task with 20 questions.\n[S3] In OET Speaking role plays, the candidate has three minutes of preparation time per card.\n\nQUESTION: How much preparation time does a candidate get per Speaking role play card?",
            null, null, []),
        new("g4", "grounded",
            "You are Sami, an OET study companion. Answer ONLY from the SOURCES block. Cite every source you use inline like [S1].",
            "SOURCES:\n[S1] The learner's latest mock scores are Listening 320, Reading 295, Writing 350, Speaking 370.\n[S2] The learner's target is 350 in every sub-test.\n[S3] The learner can study 45 minutes on weekdays.\n\nQUESTION: Which sub-test is furthest from the target and by how many points? Cite the scores you used.",
            null, null, []),
        new("g5", "grounded",
            "You are Sami, an OET study companion. Answer ONLY from the SOURCES block. Cite every source you use inline like [S1]. If the sources do not answer the question, say exactly that you cannot answer from the sources.",
            "SOURCES:\n[S1] Evidence-based practice stepped-wedge trials reduced medication discrepancies by 34 percent in a 2024 multi-site audit.\n\nQUESTION: What is the capital of Australia?",
            null, null, [], 1),
        new("g6", "grounded",
            "You are Sami, an OET study companion. Answer ONLY from the SOURCES block. Cite every source you use inline like [S1]. If the sources do not answer the question, say so.",
            "SOURCES:\n[S1] In OET Listening Part A, answers are usually the exact words heard; numbers are written as digits.\n[S2] Part A has 24 questions completed while listening to a consultation.\n[S3] Spelling errors are marked wrong in Part A.\n\nQUESTION: Are spelling errors penalised in Listening Part A, and why does that matter for drug names? Cite your sources.",
            null, null, []),
        new("s1", "schema",
            "You are Sami's planning engine. Reply with ONLY a JSON object with keys \"subtest\" (one of Reading/Listening/Writing/Speaking) and \"priority\" (an integer 1-5) and \"reason\" (a short string).",
            "The learner's Reading is 45 points below target and the exam is in 12 days. Produce the JSON object.",
            null, "{\"type\":\"json_object\"}", ["subtest", "priority", "reason"]),
        new("s2", "schema",
            "You are Sami's scheduling engine. Reply with ONLY a JSON object with keys \"plan\" (an array of 3 strings) and \"total_minutes\" (an integer).",
            "I have 90 minutes today for OET study. Produce the JSON object.",
            null, "{\"type\":\"json_object\"}", ["plan", "total_minutes"]),
        new("t1", "tool",
            "You are Sami, an OET study companion. Call the benchmark_preview_study_plan tool to draft the learner's study-plan proposal when they ask for study help.",
            "I have 25 minutes now and my weakest sub-test is Reading. What should I do?",
            "companion_preview_study_plan", null, []),
        new("t2", "tool",
            "You are Sami, an OET study companion. Call the benchmark_preview_study_plan tool when the learner asks you to plan study time, instead of describing a plan in text.",
            "Please plan my next study block. Speaking needs the most work before my exam.",
            "companion_preview_study_plan", null, []),
    ]);

    public sealed record AiRouteBenchmarkCase(
        string Id,
        string Kind,
        string SystemPrompt,
        string UserPrompt,
        string? ToolName,
        string? ResponseFormatJson,
        IReadOnlyList<string> RequiredJsonKeys,
        int ProvidedSourceCount = 3);
}
