using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

// Shared helpers for the live voice failover, health and duration-cap tests. They build the
// real LiveVoiceService exactly as Program.cs wires it, over an in-memory database, with the
// provider HTTP replaced by a scripted handler. Nothing here touches the network or the clock.

/// <summary>One request the service sent to a provider, reduced to what the tests assert on.</summary>
internal sealed record CapturedProviderRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    string? GoogApiKey,
    string? Body);

/// <summary>Answers provider HTTP from a script and records every request. Thread-safe: the probe
/// calls both providers in parallel.</summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly object gate = new();
    private readonly List<CapturedProviderRequest> requests = [];

    public IReadOnlyList<CapturedProviderRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return requests.ToArray();
            }
        }
    }

    public Func<CapturedProviderRequest, CancellationToken, Task<HttpResponseMessage>> Script { get; set; }
        = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK, "{}"));

    /// <summary>Answers synchronously. The responder may throw to simulate a transport fault.</summary>
    public void Route(Func<CapturedProviderRequest, HttpResponseMessage> respond)
        => Script = (request, _) => Task.FromResult(respond(request));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedProviderRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.FirstOrDefault() : null,
            body);
        lock (gate)
        {
            requests.Add(captured);
        }
        return await Script(captured, cancellationToken);
    }

    public static HttpResponseMessage Reply(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }
}

/// <summary>A NEW HttpClient per call, like the real factory: the service creates one per request.</summary>
internal sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>Keeps every formatted log line (and exception text) so a test can prove no secret was written.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object gate = new();
    private readonly List<(LogLevel Level, string Text)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Text)> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    public string AllText => string.Join('\n', Entries.Select(e => $"{e.Level}: {e.Text}"));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception);
        lock (gate)
        {
            entries.Add((logLevel, text));
        }
    }
}

/// <summary>
/// Fails any save that adds an AuditEvent, to prove a database fault never masks the provider 503.
/// A subclass rather than a SaveChanges interceptor: EF keeps one internal service provider per
/// distinct interceptor instance, so an instance per test would trip its "many service providers" guard.
/// </summary>
internal sealed class FaultingLearnerDbContext(DbContextOptions<LearnerDbContext> options) : LearnerDbContext(options)
{
    public bool FailAuditWrites { get; set; }

    public int AuditWriteFailures { get; private set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailAuditWrites && ChangeTracker.Entries<AuditEvent>().Any(entry => entry.State == EntityState.Added))
        {
            AuditWriteFailures++;
            throw new DbUpdateException("Simulated audit write failure.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Stands in for the OpenAI hang-up path: records which Speaking sessions the sweeper closed.</summary>
internal sealed class RecordingSessionCloser : ILiveVoiceProviderSessionCloser
{
    private readonly object gate = new();
    private readonly List<string> closed = [];

    public IReadOnlyList<string> Closed
    {
        get
        {
            lock (gate)
            {
                return closed.ToArray();
            }
        }
    }

    /// <summary>Set to make the hang-up fail for a session.</summary>
    public Func<string, Exception?>? Fault { get; set; }

    public Task<int> CloseProviderSessionsAsync(string speakingSessionId, CancellationToken ct)
    {
        lock (gate)
        {
            closed.Add(speakingSessionId);
        }
        if (Fault?.Invoke(speakingSessionId) is { } fault)
        {
            throw fault;
        }
        return Task.FromResult(1);
    }
}

/// <summary>A minted-session route that answers both providers with a fresh id every call and 200s the hang-up.</summary>
internal sealed class HappyProviderRoute
{
    private int counter;

    public HttpResponseMessage Respond(CapturedProviderRequest request)
    {
        var n = Interlocked.Increment(ref counter);
        if (request.Uri.AbsolutePath.EndsWith("/hangup", StringComparison.Ordinal))
        {
            return ScriptedHttpHandler.Reply(HttpStatusCode.OK, "{}");
        }
        return request.Uri.Host switch
        {
            "api.openai.com" => ScriptedHttpHandler.Reply(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new
                {
                    session = new { id = $"sess_test_{n}" },
                    transport = new { sdp = $"v=0 answer {n}" },
                })),
            "generativelanguage.googleapis.com" => ScriptedHttpHandler.Reply(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new { name = $"auth_tokens/test-{n}" })),
            _ => ScriptedHttpHandler.Reply(HttpStatusCode.NotFound, "{}"),
        };
    }
}

internal sealed record SeededLiveVoiceSession(
    string UserId,
    string SessionId,
    string CardId,
    string AttemptId,
    string Marker);

/// <summary>A real LiveVoiceService and everything around it.</summary>
internal sealed class LiveVoiceRig(
    LiveVoiceService service,
    LearnerDbContext db,
    LiveVoiceProviderProbeState state,
    ScriptedHttpHandler handler,
    CapturingLogger<LiveVoiceService> log,
    MutableTimeProvider clock,
    LiveVoiceOptions options) : IDisposable
{
    public LiveVoiceService Service { get; } = service;
    public LearnerDbContext Db { get; } = db;
    public LiveVoiceProviderProbeState State { get; } = state;
    public ScriptedHttpHandler Handler { get; } = handler;
    public CapturingLogger<LiveVoiceService> Log { get; } = log;
    public MutableTimeProvider Clock { get; } = clock;
    public LiveVoiceOptions Options { get; } = options;

    /// <summary>The database as its fault-injecting subclass; only when the rig was created with <c>faultingDb</c>.</summary>
    public FaultingLearnerDbContext FaultingDb => (FaultingLearnerDbContext)Db;

    public void Dispose() => Db.Dispose();
}

internal static class LiveVoiceTestKit
{
    public const string OpenAiKey = "sk-live-test-key-not-for-logs-0001";
    public const string GeminiKey = "gm-live-test-key-not-for-logs-0002";
    public const string OfferSdp = "v=0 candidate-offer-sdp-not-for-logs";

    public static readonly DateTimeOffset Epoch = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public static LiveVoiceOptions DefaultOptions(string primary = "openai")
        => new()
        {
            PrimaryProvider = primary,
            OpenAiApiKey = OpenAiKey,
            GeminiApiKey = GeminiKey,
        };

    /// <summary>Both providers configured and catalog-verified unless told otherwise.</summary>
    public static LiveVoiceRig Create(
        LiveVoiceOptions? options = null,
        bool verified = true,
        DateTimeOffset? now = null,
        bool faultingDb = false)
    {
        options ??= DefaultOptions();
        var clock = new MutableTimeProvider(now ?? Epoch);
        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"live-voice-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        LearnerDbContext db = faultingDb ? new FaultingLearnerDbContext(dbOptions) : new LearnerDbContext(dbOptions);

        var state = new LiveVoiceProviderProbeState(clock);
        if (verified)
        {
            state.Set(LiveVoiceProviders.OpenAi, true, null);
            state.Set(LiveVoiceProviders.Gemini, true, null);
        }

        var handler = new ScriptedHttpHandler();
        handler.Route(new HappyProviderRoute().Respond);
        var log = new CapturingLogger<LiveVoiceService>();
        var service = new LiveVoiceService(
            db,
            Options.Create(options),
            Options.Create(new SpeakingComplianceOptions()),
            new HandlerHttpClientFactory(handler),
            new SpeakingPatientTurnService(db, clock),
            new LiveVoiceAdvisoryQueue(),
            new LiveVoiceContentReadinessService(db, null, NullLogger<LiveVoiceContentReadinessService>.Instance),
            state,
            clock,
            log);
        return new LiveVoiceRig(service, db, state, handler, log, clock, options);
    }

    /// <summary>
    /// A published card with an authored roleplayer script, an Active (by default) session that has
    /// consent, its legacy attempt and the account consents live voice requires. The card carries
    /// <c>marker</c> in its visible AND hidden text, so a leak into another session's instructions
    /// is detectable.
    /// </summary>
    public static async Task<SeededLiveVoiceSession> SeedReadySessionAsync(
        LearnerDbContext db,
        DateTimeOffset now,
        SpeakingSessionState state = SpeakingSessionState.Active,
        DateTimeOffset? rolePlayStartedAt = null,
        bool clearRolePlayStart = false,
        DateTimeOffset? endedAt = null,
        DateTimeOffset? updatedAt = null,
        int rolePlaySeconds = 300,
        SpeakingSessionMode mode = SpeakingSessionMode.AiSelfPractice,
        string? examSessionId = null,
        bool needsOwnerInput = false,
        string? marker = null)
    {
        marker ??= Guid.NewGuid().ToString("N")[..8];
        var userId = $"lv-user-{Guid.NewGuid():N}";
        var cardId = $"lv-card-{Guid.NewGuid():N}";
        var sessionId = $"sps_{Guid.NewGuid():N}";
        var attemptId = $"att_{Guid.NewGuid():N}";
        var consentVersion = new SpeakingComplianceOptions().CurrentConsentVersion;

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Live Voice Learner",
            Email = $"{userId}@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        db.RolePlayCards.Add(new RolePlayCard
        {
            Id = cardId,
            ContentItemId = $"ci-{cardId}",
            ProfessionId = "medicine",
            ScenarioTitle = $"Scenario {marker}",
            Setting = "General practice",
            CandidateRole = "Doctor",
            InterlocutorRole = "Patient",
            Background = $"Visible background {marker}",
            Task1 = "Take a history",
            PrepTimeSeconds = 180,
            RolePlayTimeSeconds = rolePlaySeconds,
            PatientEmotion = "worried",
            CommunicationGoal = "Reassure",
            ClinicalTopic = "general",
            Difficulty = "exam",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.InterlocutorScripts.Add(new InterlocutorScript
        {
            Id = $"lv-script-{Guid.NewGuid():N}",
            RolePlayCardId = cardId,
            NeedsOwnerInput = needsOwnerInput,
            OpeningResponse = "Doctor, my knee hurts.",
            HiddenInformation = $"HIDDEN-{marker}",
            PatientBackground = $"PATIENT-BACKGROUND-{marker}",
            ResistanceLevel = ResistanceLevel.Low,
            ClosingCue = "Accept advice",
            EmotionalState = "anxious",
            LayLanguageTriggersJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = userId,
            ContentId = $"ci-{cardId}",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = SpeakingSessionModes.ToCode(mode),
            State = state == SpeakingSessionState.Finished ? AttemptState.Submitted : AttemptState.InProgress,
            StartedAt = now,
            CreatedAt = now,
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
        });
        db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = userId,
            RolePlayCardId = cardId,
            ExamSessionId = examSessionId,
            Mode = mode,
            State = state,
            AttemptId = attemptId,
            ConsentAcceptedAt = now,
            ConsentVersion = consentVersion,
            RolePlayStartedAt = clearRolePlayStart
                ? null
                : rolePlayStartedAt ?? (state is SpeakingSessionState.Active or SpeakingSessionState.Finished ? now : null),
            EndedAt = endedAt,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
        });
        foreach (var consentType in new[]
                 {
                     SpeakingComplianceConsentTypes.Recording,
                     SpeakingComplianceConsentTypes.AiProcessing,
                     SpeakingComplianceConsentTypes.Retention,
                 })
        {
            db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
            {
                Id = $"scc-{Guid.NewGuid():N}",
                UserId = userId,
                ConsentType = consentType,
                ConsentVersion = consentVersion,
                AcceptedAt = now,
            });
        }
        await db.SaveChangesAsync();
        return new SeededLiveVoiceSession(userId, sessionId, cardId, attemptId, marker);
    }

    /// <summary>A well-formed OpenAI live-session error body.</summary>
    public static string OpenAiError(string code, string message, string? type = null)
        => JsonSerializer.Serialize(new { error = new { code, type = type ?? code, message } });

    public static string AllText(IEnumerable<CapturedProviderRequest> requests)
        => string.Join('\n', requests.Select(r => $"{r.Method} {r.Uri} {r.Authorization} {r.GoogApiKey} {r.Body}"));
}
