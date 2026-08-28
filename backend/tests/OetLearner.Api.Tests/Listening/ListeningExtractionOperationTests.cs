using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// the EXTRACTION one-run-ever lockout and the failure-reconciliation gap.
///
/// <para>
/// Two defects, one call site. (a) Both extraction services hard-coded
/// <c>ResourceVersion = 1</c> while their owner-policy guard was already
/// counting attempts and allowing up to five per paper: the very first run
/// permanently owned the only identity the control plane would ever mint, so
/// every corrected re-upload was refused as a duplicate. (b) The operation was
/// only closed on the success path, so an OCR/transport/schema/save failure left
/// the row <see cref="AiOperationState.Leased"/> — which is exactly the shape
/// the control plane reads as "someone else is working on this", blocking that
/// resource for ever.
/// </para>
///
/// <para>
/// These run the REAL <see cref="DirectAiCallRecorder"/> over the REAL
/// <see cref="AiOperationStore"/> against Sqlite, so the assertions are about
/// genuine unique-index behaviour rather than a hand-written duplicate rule.
/// Part B/C is the vehicle because it is the simpler of the two paths; both
/// services now share the identical shape.
/// </para>
/// </summary>
public sealed class ListeningExtractionOperationTests : IAsyncDisposable
{
    private const string PaperId = "paper-bc";
    private const string AdminId = "admin-1";
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;
    private readonly ServiceProvider _provider;

    public ListeningExtractionOperationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();

        // The recorder resolves IAiOperationStore per call from its own scope;
        // in this fixture every scope shares the one Sqlite context.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAiOperationStore>(_ => new AiOperationStore(_db));
        _provider = services.BuildServiceProvider(validateScopes: true);

        _db.ContentPapers.Add(new ContentPaper
        {
            Id = PaperId,
            SubtestCode = "listening",
            Title = "W2",
            Slug = "w2",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            ExtractedTextJson = "{}",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Item 2: the one-run-ever lockout ────────────────────────────────────────

    /// <summary>
    /// A failed run must not wedge the paper. The second policy-authorized run
    /// gets the NEXT attempt ordinal, so it mints a distinct idempotency key and
    /// resource slot and is allowed to proceed.
    /// </summary>
    [Fact]
    public async Task SecondAuthorizedRun_AfterAProviderFailure_OpensANewOperation()
    {
        var http = new SequencedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
            _ => JsonResponse(ToolUseBody(SixAnswers)));
        var service = NewService(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(service));

        var failed = await _db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(1, failed.ResourceVersion);
        Assert.Equal(AiOperationState.FailedTerminal, failed.State);

        var result = await RunAsync(service);

        Assert.Equal("B", result.Part);
        Assert.Equal(2, http.Calls);

        var operations = await _db.AiOperations.AsNoTracking().OrderBy(o => o.ResourceVersion).ToListAsync();
        Assert.Equal(2, operations.Count);
        Assert.Equal(new int?[] { 1, 2 }, operations.Select(o => o.ResourceVersion).ToArray());
        Assert.Equal(AiOperationState.Completed, operations[1].State);

        // Distinct control-plane identity on BOTH unique dimensions.
        Assert.NotEqual(operations[0].IdempotencyKey, operations[1].IdempotencyKey);
        Assert.NotEqual(operations[0].ResourceSlotKey, operations[1].ResourceSlotKey);
    }

    /// <summary>
    /// The anti-double-charge property is unchanged: a second caller that
    /// computed the SAME authorized attempt ordinal (the race the audit-ledger
    /// guard can still lose) is refused with zero extra provider calls. The
    /// deleted audit row reproduces exactly that ordinal collision.
    /// </summary>
    [Fact]
    public async Task ConcurrentDuplicateOfTheSameAttempt_IsStillRefused_WithNoSecondProviderCall()
    {
        var http = new SequencedHandler(_ => JsonResponse(ToolUseBody(SixAnswers)));
        var service = NewService(http);

        await RunAsync(service);
        Assert.Equal(1, http.Calls);

        // Rewind the ledger so the next run recomputes attempt ordinal 1 — the
        // same identity the completed run already owns.
        _db.AuditEvents.RemoveRange(await _db.AuditEvents.ToListAsync());
        await _db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ApiException>(() => RunAsync(service));

        Assert.Equal("listening_partbc_already_running", refused.ErrorCode);
        Assert.Equal(1, http.Calls);
        Assert.Equal(1, await _db.AiOperations.AsNoTracking().CountAsync());
    }

    /// <summary>The owner policy — not the control plane — stays the single
    /// authority on how many runs a paper may have.</summary>
    [Fact]
    public async Task ExtractionRetryCap_RemainsOwnedByThePolicy()
    {
        _db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            AiExtractionEnabled = true,
            AiExtractionMaxRetriesPerPaper = 2,
        });
        await _db.SaveChangesAsync();

        var http = new SequencedHandler(_ => JsonResponse(ToolUseBody(SixAnswers)));
        var service = NewService(http);

        await RunAsync(service);
        await RunAsync(service);

        var capped = await Assert.ThrowsAsync<ApiException>(() => RunAsync(service));
        Assert.Equal("listening_ai_extraction_retry_limit_reached", capped.ErrorCode);
        Assert.Equal(2, http.Calls);
    }

    // ── Item 4: every exit path from a held lease reconciles ────────────────────

    /// <summary>
    /// A provider HTTP error is a known outcome (the provider answered), so the
    /// operation closes FailedTerminal — never left Leased, and never
    /// Indeterminate, which would block replay for ever.
    /// </summary>
    [Fact]
    public async Task ProviderHttpFailure_ClosesTheOperation_AndRethrows()
    {
        var http = new SequencedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("upstream down") });
        var service = NewService(http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(service));

        var operation = await _db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.FailedTerminal, operation.State);
        Assert.NotEqual(AiOperationState.Leased, operation.State);
        Assert.Null(operation.ResultRef);
    }

    /// <summary>
    /// A schema/parse failure happens AFTER a paid, successful provider turn.
    /// The original validation error must still surface, and the operation must
    /// still be reconciled so the next authorized attempt can own a new version.
    /// </summary>
    [Fact]
    public async Task SchemaParseFailure_ClosesTheOperation_AndPreservesTheOriginalError()
    {
        var http = new SequencedHandler(
            _ => JsonResponse(ToolUseBody("\"not-an-array\"")),
            _ => JsonResponse(ToolUseBody(SixAnswers)));
        var service = NewService(http);

        var error = await Assert.ThrowsAsync<ApiException>(() => RunAsync(service));
        Assert.Equal("listening_partbc_bad_output", error.ErrorCode);

        var operation = await _db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.FailedTerminal, operation.State);

        // ...and the paper is not wedged: the next authorized attempt proceeds.
        var recovered = await RunAsync(service);
        Assert.Equal("B", recovered.Part);
        Assert.Equal(2, await _db.AiOperations.AsNoTracking().CountAsync());
    }

    /// <summary>An OCR failure happens while the lease is held but before any
    /// Anthropic spend; it must reconcile too.</summary>
    [Fact]
    public async Task OcrFailure_ClosesTheOperation_WithNoProviderCall()
    {
        var http = new SequencedHandler(_ => JsonResponse(ToolUseBody(SixAnswers)));
        var service = NewService(http, ocr: new ThrowingOcrService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(service));

        Assert.Equal(0, http.Calls);
        var operation = await _db.AiOperations.AsNoTracking().SingleAsync();
        Assert.Equal(AiOperationState.FailedTerminal, operation.State);
    }

    // ── Failure classification ──────────────────────────────────────────────────

    /// <summary>
    /// The reconciler must never turn an ambiguous post-send failure into a
    /// replayable one — that is how a duplicate charge happens.
    /// </summary>
    [Fact]
    public void ReconcilerClassification_KeepsAmbiguousFailuresNonReplayable()
    {
        var ct = new CancellationToken(canceled: true);

        Assert.Equal(AiOperationState.Cancelled,
            DirectAiOperationReconciler.ClassifyFailure(new OperationCanceledException(), ct));

        // Not caller-cancelled => an HttpClient timeout: the request may be live.
        Assert.Equal(AiOperationState.Indeterminate,
            DirectAiOperationReconciler.ClassifyFailure(new OperationCanceledException(), default));

        // Provider answered => outcome known.
        Assert.Equal(AiOperationState.FailedTerminal, DirectAiOperationReconciler.ClassifyFailure(
            new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway), default));

        // Proven pre-send => nothing was billed.
        Assert.Equal(AiOperationState.FailedTerminal, DirectAiOperationReconciler.ClassifyFailure(
            new HttpRequestException(HttpRequestError.NameResolutionError), default));

        // Mid-flight connection/TLS failures stay ambiguous.
        Assert.Equal(AiOperationState.Indeterminate, DirectAiOperationReconciler.ClassifyFailure(
            new HttpRequestException(HttpRequestError.SecureConnectionError), default));
        Assert.Equal(AiOperationState.Indeterminate, DirectAiOperationReconciler.ClassifyFailure(
            new HttpRequestException(HttpRequestError.ConnectionError), default));

        // Parse/save failures after a known-good response are decided.
        Assert.Equal(AiOperationState.FailedTerminal,
            DirectAiOperationReconciler.ClassifyFailure(new System.Text.Json.JsonException("bad"), default));
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static Task<ListeningPartBCImportResult> RunAsync(ListeningPartBCExtractionService service)
        => service.ExtractFromUploadAsync(
            PaperId, "B",
            new[] { (new byte[] { 1, 2, 3 }, "application/pdf") },
            new byte[] { 4, 5, 6 }, "application/pdf",
            AdminId, CancellationToken.None);

    private ListeningPartBCExtractionService NewService(SequencedHandler http, IOcrService? ocr = null)
        => new(
            _db,
            ocr ?? new StubOcrService(),
            new StubProviderRegistry(),
            new StaticHttpClientFactory(http),
            new DirectAiCallRecorder(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<DirectAiCallRecorder>.Instance),
            new FixedClock(Now),
            NullLogger<ListeningPartBCExtractionService>.Instance);

    private const string SixAnswers =
        "[{\"number\":25,\"correctAnswer\":\"A\"},{\"number\":26,\"correctAnswer\":\"B\"}," +
        "{\"number\":27,\"correctAnswer\":\"C\"},{\"number\":28,\"correctAnswer\":\"A\"}," +
        "{\"number\":29,\"correctAnswer\":\"B\"},{\"number\":30,\"correctAnswer\":\"C\"}]";

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ToolUseBody(string answersJson)
        => "{\"usage\":{\"input_tokens\":10,\"output_tokens\":5},\"content\":[{\"type\":\"tool_use\","
           + "\"name\":\"emit_part_bc_answers\",\"input\":{\"answers\":" + answersJson + "}}]}";

    /// <summary>Replays one scripted response per call, holding the last one so
    /// a test can run the same service repeatedly.</summary>
    private sealed class SequencedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var index = Math.Min(Calls, responses.Length - 1);
            Calls++;
            return Task.FromResult(responses[index](request));
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubOcrService : IOcrService
    {
        public Task<string> OcrToMarkdownAsync(
            byte[] documentBytes, string mimeType, string featureCode, string? userId, CancellationToken ct)
            => Task.FromResult("# OCR\n\n25 A\n26 B\n27 C\n28 A\n29 B\n30 C");
    }

    private sealed class ThrowingOcrService : IOcrService
    {
        public Task<string> OcrToMarkdownAsync(
            byte[] documentBytes, string mimeType, string featureCode, string? userId, CancellationToken ct)
            => throw new InvalidOperationException("OCR provider unavailable");
    }

    private sealed class StubProviderRegistry : IAiProviderRegistry
    {
        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult<AiProvider?>(new AiProvider
            {
                Id = "provider-anthropic",
                Code = "anthropic",
                Name = "Anthropic",
                Dialect = AiProviderDialect.Anthropic,
                BaseUrl = "https://api.anthropic.test",
                DefaultModel = "claude-sonnet-5",
                PricePer1kPromptTokens = 0.003m,
                PricePer1kCompletionTokens = 0.015m,
                IsActive = true,
            });

        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());

        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());

        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
            => Task.FromResult<string?>("test-platform-key");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
