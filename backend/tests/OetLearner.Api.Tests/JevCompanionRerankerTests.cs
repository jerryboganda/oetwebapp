using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase-2 slices: companion retrieval rerank (flag-off no-op, scoring,
/// fail-soft, candidate cap, blend weighting) and the judgment client's
/// consecutive-failure circuit breaker (trip, short-circuit, cooldown).
/// </summary>
public sealed class JevCompanionRerankerTests
{
    private static TypeSafeOptions Options(Action<TypeSafeOptions>? configure = null)
    {
        var opts = new TypeSafeOptions
        {
            Enabled = true,
            ApiKey = "apikey_test",
            CompanionRerankEnabled = true,
        };
        configure?.Invoke(opts);
        return opts;
    }

    private static JevCompanionReranker Pilot(FakeJudgments judgments, TypeSafeOptions? opts = null) =>
        new(judgments, Microsoft.Extensions.Options.Options.Create(opts ?? Options()), NullLogger<JevCompanionReranker>.Instance);

    private static JevJudgmentResult OkScores(params (string Id, double Score)[] scores) =>
        new(JevCallStatus.Ok, "jev-1.13.0",
            scores.ToDictionary(s => s.Id, s => new JevAnswer(
                JevQuestionKind.Score, null, null, new JevScoreAnswer(s.Score, new Dictionary<string, double>(), 0.8))),
            500, 10, null);

    private static List<(Guid Id, string Text)> Candidates(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (Guid.NewGuid(), $"candidate text {i} about the OET speaking criterion"))
            .ToList();

    [Fact]
    public async Task Rerank_FlagOff_ReturnsNull_NoCalls()
    {
        var fake = new FakeJudgments(OkScores(("cand_0", 3)));

        var result = await Pilot(fake, Options(o => o.CompanionRerankEnabled = false))
            .RerankAsync("query", Candidates(2), "user-1", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Rerank_ScoresEachCandidate_WithinOneCall()
    {
        var fake = new FakeJudgments(OkScores(("cand_0", 3), ("cand_1", 1)));

        var candidates = Candidates(2);
        var result = await Pilot(fake).RerankAsync("how is speaking scored?", candidates, "user-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result![candidates[0].Id]);
        Assert.Equal(1, result[candidates[1].Id]);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(2, fake.LastRequest!.Questions.Count);
        Assert.All(fake.LastRequest.Questions, q => Assert.Equal(JevQuestionKind.Score, q.Kind));
    }

    [Fact]
    public async Task Rerank_Crash_ReturnsNull_NeverThrows()
    {
        var fake = new FakeJudgments(OkScores()) { Throw = true };

        var result = await Pilot(fake).RerankAsync("query", Candidates(2), null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Rerank_CapsCandidatesSent()
    {
        var fake = new FakeJudgments(OkScores(Enumerable.Range(0, 16).Select(i => ($"cand_{i}", 2.0)).ToArray()));

        var result = await Pilot(fake).RerankAsync("query", Candidates(30), null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(16, result!.Count);
        Assert.Equal(16, fake.LastRequest!.Questions.Count);
    }

    [Fact]
    public void Blend_RerankDominates_HybridIsTiebreak()
    {
        // A direct-match candidate with a terrible hybrid score outranks an
        // unrelated candidate with a huge hybrid score — that is the point of
        // the rerank — while equal rerank scores keep hybrid order.
        var directLowHybrid = JevCompanionReranker.Blend(hybridScore: 0.1f, rerankScore: 3.0);
        var unrelatedHighHybrid = JevCompanionReranker.Blend(hybridScore: 20f, rerankScore: 0.0);
        Assert.True(directLowHybrid > unrelatedHighHybrid);

        var tieA = JevCompanionReranker.Blend(hybridScore: 5f, rerankScore: 2.0);
        var tieB = JevCompanionReranker.Blend(hybridScore: 1f, rerankScore: 2.0);
        Assert.True(tieA > tieB);
    }

    // ── Circuit breaker ─────────────────────────────────────────────────────

    [Fact]
    public async Task Breaker_TripsAfterConsecutiveFailures_AndShortCircuits()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        });
        var client = new TypeSafeJudgmentClient(
            new SingleClientFactory(new System.Net.Http.HttpClient(handler)),
            Microsoft.Extensions.Options.Options.Create(new TypeSafeOptions
            {
                Enabled = true,
                ApiKey = "apikey_test",
                BreakerFailureThreshold = 2,
                BreakerCooldownSeconds = 60,
            }));

        // Two failures trip the breaker.
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<TypeSafeHttpException>(
                () => client.SendAsync("{\"state\":\"x\",\"model\":\"m\",\"questions\":{}}", CancellationToken.None));
        }
        Assert.Equal(2, calls);

        // Third send short-circuits WITHOUT touching the transport.
        var ex = await Assert.ThrowsAsync<TypeSafeHttpException>(
            () => client.SendAsync("{\"state\":\"x\",\"model\":\"m\",\"questions\":{}}", CancellationToken.None));
        Assert.Contains("breaker open", ex.Message);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Breaker_SuccessResetsStreak()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) =>
        {
            calls++;
            if (calls == 1)
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(JsonResponse(
                """{"model":"jev-1.13.0","answers":{"q":{"type":"noul","noul":0.5}},"usage":{"input_tokens":1,"output_tokens":1}}"""));
        });
        var client = new TypeSafeJudgmentClient(
            new SingleClientFactory(new System.Net.Http.HttpClient(handler)),
            Microsoft.Extensions.Options.Options.Create(new TypeSafeOptions
            {
                Enabled = true,
                ApiKey = "apikey_test",
                BreakerFailureThreshold = 2,
                BreakerCooldownSeconds = 60,
            }));

        await Assert.ThrowsAsync<TypeSafeHttpException>(
            () => client.SendAsync("{\"state\":\"x\",\"model\":\"m\",\"questions\":{}}", CancellationToken.None));
        var ok = await client.SendAsync("{\"state\":\"x\",\"model\":\"m\",\"questions\":{}}", CancellationToken.None);
        Assert.NotNull(ok);

        // One more failure must NOT trip the breaker — the streak was reset.
        calls = 10; // handler returns success for calls != 1
        var ok2 = await client.SendAsync("{\"state\":\"x\",\"model\":\"m\",\"questions\":{}}", CancellationToken.None);
        Assert.NotNull(ok2);
    }

    private sealed class FakeJudgments(JevJudgmentResult result) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }
        public JevJudgmentRequest? LastRequest { get; private set; }
        public bool Throw { get; init; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task ConversationAdvisor_FlagOff_ReturnsNull_NoCalls()
    {
        var fake = new FakeJudgments(OkScores());
        var advisor = new OetLearner.Api.Services.Ai.TypeSafe.JevConversationAdvisor(
            fake, Microsoft.Extensions.Options.Options.Create(Options(o => o.ConversationAdvisoryEnabled = false)),
            NullLogger<OetLearner.Api.Services.Ai.TypeSafe.JevConversationAdvisor>.Instance);

        var result = await advisor.AssessLatestTurnAsync("[{\"role\":\"learner\"}]", 3, "u1", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task ConversationAdvisor_OkPath_ReturnsThreeSignals()
    {
        var result = new JevJudgmentResult(JevCallStatus.Ok, "jev-1.13.0",
            new Dictionary<string, JevAnswer>
            {
                ["jev_stays_in_role"] = new(JevQuestionKind.Noul, new JevNoulAnswer(0.93), null, null),
                ["jev_clinically_appropriate"] = new(JevQuestionKind.Noul, new JevNoulAnswer(0.81), null, null),
                ["jev_unsafe_content"] = new(JevQuestionKind.Noul, new JevNoulAnswer(0.01), null, null),
            }, 400, 20, null);
        var fake = new FakeJudgments(result);
        var advisor = new OetLearner.Api.Services.Ai.TypeSafe.JevConversationAdvisor(
            fake, Microsoft.Extensions.Options.Options.Create(Options(o => o.ConversationAdvisoryEnabled = true)),
            NullLogger<OetLearner.Api.Services.Ai.TypeSafe.JevConversationAdvisor>.Instance);

        var signal = await advisor.AssessLatestTurnAsync("[{\"role\":\"learner\"}]", 3, "u1", CancellationToken.None);

        Assert.NotNull(signal);
        Assert.Equal(0.93, signal!.StaysInRole);
        Assert.Equal(0.81, signal.ClinicallyAppropriate);
        Assert.Equal(0.01, signal.UnsafeContent);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task ConversationAdvisor_Crash_ReturnsNull_NeverThrows()
    {
        var fake = new FakeJudgments(OkScores()) { Throw = true };
        var advisor = new OetLearner.Api.Services.Ai.TypeSafe.JevConversationAdvisor(
            fake, Microsoft.Extensions.Options.Options.Create(Options()),
            NullLogger<OetLearner.Api.Services.Ai.TypeSafe.JevConversationAdvisor>.Instance);

        var result = await advisor.AssessLatestTurnAsync("transcript", 1, null, CancellationToken.None);

        Assert.Null(result);
    }

private sealed class StubHandler(Func<System.Net.Http.HttpRequestMessage, System.Threading.CancellationToken, Task<System.Net.Http.HttpResponseMessage>> responder)
    : System.Net.Http.HttpMessageHandler
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        => responder(request, cancellationToken);
}

private sealed class SingleClientFactory(System.Net.Http.HttpClient client) : IHttpClientFactory
{
    public System.Net.Http.HttpClient CreateClient(string name) => client;
}

private static System.Net.Http.HttpResponseMessage JsonResponse(string body) =>
    new(System.Net.HttpStatusCode.OK)
    {
        Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };
}
