using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>Shared fakes and answer builders for the Jev Speaking tests (no tests of its own).</summary>
internal static class JevSpeakingTestKit
{
    /// <summary>Records every judgment call (and appends its feature code to an optional shared log
    /// so a test can assert ordering against the grade call).</summary>
    internal sealed class FakeJudgments(
        Func<JevJudgmentRequest, JevCallMetadata, CancellationToken, Task<JevJudgmentResult>> respond,
        List<string>? log = null) : ITypeSafeJudgmentService
    {
        public List<(JevJudgmentRequest Request, JevCallMetadata Call)> Calls { get; } = new();

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls.Add((request, call));
            log?.Add(call.FeatureCode);
            return respond(request, call, ct);
        }
    }

    public static TypeSafeOptions Flags(bool readiness = false, bool crosscheck = false, bool enabled = true) => new()
    {
        Enabled = enabled,
        SpeakingReadinessEnabled = readiness,
        SpeakingCrosscheckEnabled = crosscheck,
    };

    public static JevJudgmentResult Ok(params (string Id, JevAnswer Answer)[] answers) =>
        new(JevCallStatus.Ok, "jev-1.13.0",
            answers.ToDictionary(a => a.Id, a => a.Answer, StringComparer.Ordinal), 100, 5, null);

    public static (string Id, JevAnswer Answer) NoulAnswer(string id, double probability) =>
        (id, new JevAnswer(JevQuestionKind.Noul, new JevNoulAnswer(probability), null, null));

    public static (string Id, JevAnswer Answer) ScoreAnswer(string id, double position, double confidence = 0.9) =>
        (id, new JevAnswer(JevQuestionKind.Score, null, null,
            new JevScoreAnswer(position, new Dictionary<string, double>(), confidence)));

    public static (string Id, JevAnswer Answer) ChoiceAnswer(string id, string choice, double confidence = 0.9) =>
        (id, new JevAnswer(JevQuestionKind.Choice, null,
            new JevChoiceAnswer(choice, new Dictionary<string, double> { [choice] = confidence }, confidence), null));

    /// <summary>Readiness verdict: <paramref name="onTask"/> is P(on task); the other two are problem probabilities.</summary>
    public static JevJudgmentResult Readiness(double onTask, double gibberish, double injection) => Ok(
        NoulAnswer(JevSpeakingAdvisor.SpokeOnTaskId, onTask),
        NoulAnswer(JevSpeakingAdvisor.GibberishOrNoiseId, gibberish),
        NoulAnswer(JevSpeakingAdvisor.GraderInstructionsId, injection));

    public static JevJudgmentResult CleanReadiness() => Readiness(onTask: 0.95, gibberish: 0.02, injection: 0.01);

    /// <summary>A judgment that never answers: it only ends when the token is cancelled.</summary>
    public static async Task<JevJudgmentResult> Hang(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return JevJudgmentResult.Disabled("unreachable");
    }
}
