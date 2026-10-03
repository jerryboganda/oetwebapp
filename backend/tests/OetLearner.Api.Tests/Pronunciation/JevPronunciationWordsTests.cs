using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Ai.TypeSafe;
using static OetLearner.Api.Tests.Speaking.JevSpeakingTestKit;

namespace OetLearner.Api.Tests.Pronunciation;

/// <summary>
/// Pins the advisory Jev pronunciation word classification: flags off means zero calls, mismatched
/// pairs come from a word alignment, one batched call is made, code owns the confidence threshold,
/// no pronunciation number is touched, and an unavailable / slow / crashing Jev is "no judgment".
/// </summary>
public sealed class JevPronunciationWordsTests
{
    private static TypeSafeOptions On() => new() { Enabled = true, PronunciationWordsEnabled = true };

    private static readonly IReadOnlyList<PronunciationWordPair> TwoPairs =
    [
        new("the", "a"),
        new("daily", null),
    ];

    private static Task<PronunciationWordsAdvisory?> Run(
        FakeJudgments jev,
        TypeSafeOptions? options = null,
        IReadOnlyList<PronunciationWordPair>? pairs = null,
        TimeSpan? timeBox = null) =>
        JevPronunciationWords.ClassifyAsync(
            jev, options ?? On(), "take the tablet twice daily", "take a tablet twice", pairs ?? TwoPairs,
            "u1", "u1:1", default, timeBox);

    // ── Alignment ───────────────────────────────────────────────────────────

    [Fact]
    public void MismatchedPairs_FindsSubstitutionAndOmission()
    {
        var pairs = JevPronunciationWords.MismatchedPairs(
            ["take", "the", "tablet", "twice", "daily"], ["take", "a", "tablet", "twice"]);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(new PronunciationWordPair("the", "a"), pairs[0]);
        Assert.Equal(new PronunciationWordPair("daily", null), pairs[1]);
    }

    [Fact]
    public void MismatchedPairs_FindsInsertion()
    {
        var pairs = JevPronunciationWords.MismatchedPairs(["take", "tablet"], ["take", "the", "tablet"]);

        var pair = Assert.Single(pairs);
        Assert.Equal(new PronunciationWordPair(null, "the"), pair);
    }

    [Fact]
    public void MismatchedPairs_IgnoresCaseAndPunctuation()
    {
        Assert.Empty(JevPronunciationWords.MismatchedPairs(["Hello", "Doctor"], ["hello,", "doctor."]));
    }

    [Fact]
    public void MismatchedPairs_NothingHeard_YieldsNothing()
    {
        Assert.Empty(JevPronunciationWords.MismatchedPairs(["take", "tablet"], []));
    }

    [Fact]
    public void MismatchedPairs_IsCapped()
    {
        var reference = Enumerable.Range(0, 30).Select(i => "alpha" + i).ToArray();
        var heard = Enumerable.Range(0, 30).Select(i => "beta" + i).ToArray();

        Assert.Equal(JevPronunciationWords.MaxPairs, JevPronunciationWords.MismatchedPairs(reference, heard).Count);
    }

    // ── Classification ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FlagOffOrMasterOff_MakesNoCall(bool masterEnabled, bool flagOn)
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(Ok()));
        var options = new TypeSafeOptions { Enabled = masterEnabled, PronunciationWordsEnabled = flagOn };

        var advisory = await Run(jev, options);

        Assert.Null(advisory);
        Assert.Empty(jev.Calls);
    }

    [Fact]
    public async Task NoMismatch_MakesNoCall()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(Ok()));

        var advisory = await Run(jev, pairs: []);

        Assert.False(advisory!.Available);
        Assert.Equal("no_mismatch", advisory.Reason);
        Assert.Empty(jev.Calls);
    }

    [Fact]
    public async Task OneBatchedCall_OneChoicePerPair()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(Ok(
            ChoiceAnswer("pair_0", "substitution"),
            ChoiceAnswer("pair_1", "omission"))));

        var advisory = await Run(jev);

        var (request, call) = Assert.Single(jev.Calls);
        Assert.Equal("jev.pronunciation.words", call.FeatureCode);
        Assert.Equal(2, request.Questions.Count);
        Assert.All(request.Questions, q =>
        {
            Assert.Equal(JevQuestionKind.Choice, q.Kind);
            Assert.Equal(
                new[] { "correct", "substitution", "omission", "insertion", "unclear" }.OrderBy(x => x),
                q.ChoiceCriteria!.Keys.OrderBy(x => x));
        });
        Assert.True(advisory!.Available);
        Assert.Equal(new[] { "substitution", "omission" }, advisory.Words.Select(w => w.Verdict).ToArray());
    }

    [Fact]
    public async Task ConfidentCorrect_FlagsPipelineLikelyWrong_LowConfidenceDoesNot()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(Ok(
            ChoiceAnswer("pair_0", "correct", confidence: 0.90),
            ChoiceAnswer("pair_1", "correct", confidence: 0.30))));

        var advisory = await Run(jev);

        Assert.True(advisory!.Words[0].PipelineLikelyWrong);
        Assert.False(advisory.Words[1].Confident);
        Assert.False(advisory.Words[1].PipelineLikelyWrong);
    }

    [Fact]
    public async Task UnknownChoice_IsIgnored_AndAllInvalidMeansUnavailable()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(Ok(
            ChoiceAnswer("pair_0", "not_a_real_option"))));

        var advisory = await Run(jev);

        Assert.False(advisory!.Available);
        Assert.Equal("jev_invalid_contract", advisory.Reason);
        Assert.Null(JevPronunciationWords.AdvisoryPayload(advisory, "u1", "th", "P01.1"));
    }

    [Fact]
    public async Task UnavailableResult_MeansNoAdvisory()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(JevJudgmentResult.Unavailable("provider_down")));

        var advisory = await Run(jev);

        Assert.False(advisory!.Available);
        Assert.Empty(advisory.Words);
    }

    [Fact]
    public async Task SlowJev_IsCutOffByTheTimeBox()
    {
        var jev = new FakeJudgments((_, _, ct) => Hang(ct));

        var advisory = await Run(jev, timeBox: TimeSpan.FromMilliseconds(50)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(advisory!.Available);
        Assert.Equal("jev_timeout", advisory.Reason);
    }

    [Fact]
    public async Task ThrowingJev_MeansNoAdvisory()
    {
        var jev = new FakeJudgments((_, _, _) => throw new InvalidOperationException("boom"));

        var advisory = await Run(jev);

        Assert.False(advisory!.Available);
        Assert.Equal("jev_crashed", advisory.Reason);
    }

    [Fact]
    public async Task Payload_CarriesClassificationOnly_NoScores()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(Ok(
            ChoiceAnswer("pair_0", "substitution"),
            ChoiceAnswer("pair_1", "omission"))));
        var advisory = await Run(jev);

        var payload = JevPronunciationWords.AdvisoryPayload(advisory, "u1", "th", "P01.1");

        Assert.NotNull(payload);
        Assert.Equal(
            new[] { "model", "targetPhoneme", "targetRuleId", "userId", "version", "words" },
            payload!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    // ── Numbers unchanged ───────────────────────────────────────────────────

    [Fact]
    public void WhisperProvider_AdvisoryRunsBeforeRefinement_AndCannotReturnAScore()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "backend", "src", "OetLearner.Api", "Services", "Pronunciation", "WhisperPronunciationAsrProvider.cs"));

        Assert.Contains("private async Task TryAdviseWordsAsync(", source); // returns Task, never an AsrResult
        Assert.Contains("await TryAdviseWordsAsync(", source);
        Assert.DoesNotContain("= await TryAdviseWordsAsync(", source);

        var start = source.IndexOf("private async Task TryAdviseWordsAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<AsrResult?> TryRefineViaGroundedAiAsync(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var helper = CodeOnly(source[start..end]);
        Assert.Contains("db.AuditEvents.Add(", helper);
        foreach (var forbidden in new[] { "AccuracyScore", "OverallScore", "ProjectedSpeakingScaled", "PronunciationProjected", "WordScores" })
            Assert.DoesNotContain(forbidden, helper);
    }

    // Comment lines are ignored so a word in prose can never trip the guard.
    private static string CodeOnly(string source) => string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src", "OetLearner.Api")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
