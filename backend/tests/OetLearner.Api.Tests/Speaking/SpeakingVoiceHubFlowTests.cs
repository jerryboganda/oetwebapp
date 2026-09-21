using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

// Real authenticated SignalR transport and application persistence; only
// external ASR, model completion and TTS are deterministic test doubles.
public sealed class SpeakingVoiceHubFlowTests
{
    [Fact]
    public async Task Audio_turn_traverses_authenticated_hub_and_persists_transcript_and_recording()
    {
        var speech = new SpeechProviders();
        await using var original = new FirstPartyAuthTestWebApplicationFactory();
        await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IConversationAsrProviderSelector>();
            services.RemoveAll<IConversationTtsProviderSelector>();
            services.RemoveAll<IAiGatewayService>();
            services.AddSingleton<IConversationAsrProviderSelector>(speech);
            services.AddSingleton<IConversationTtsProviderSelector>(speech);
            services.AddSingleton<IAiGatewayService, PlanGateway>();
        }));
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest(SeedData.LearnerEmail, SeedData.LocalSeedPassword, true));
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
        var sessionId = $"voice-{Guid.NewGuid():N}"[..18];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var consent = scope.ServiceProvider.GetRequiredService<IOptions<SpeakingComplianceOptions>>().Value;
            db.RolePlayCards.Add(new RolePlayCard
            {
                Id = sessionId, ContentItemId = "st-001", ProfessionId = "medicine",
                ScenarioTitle = "Voice regression", Setting = "Clinic", CandidateRole = "Doctor",
                InterlocutorRole = "Patient", RolePlayTimeSeconds = 300,
            });
            db.InterlocutorScripts.Add(new InterlocutorScript
            {
                Id = sessionId, RolePlayCardId = sessionId, OpeningResponse = "I am worried.",
                HiddenInformation = "Unelicited private fact", LayLanguageTriggersJson = "[]",
            });
            db.SpeakingSessions.Add(new SpeakingSession
            {
                Id = sessionId, RolePlayCardId = sessionId, UserId = auth.CurrentUser.UserId,
                Mode = SpeakingSessionMode.AiSelfPractice, State = SpeakingSessionState.Prep,
                ConsentVersion = consent.CurrentConsentVersion, ConsentAcceptedAt = DateTimeOffset.UtcNow,
            });
            foreach (var type in new[] { SpeakingComplianceConsentTypes.Recording,
                         SpeakingComplianceConsentTypes.AiProcessing, SpeakingComplianceConsentTypes.Retention })
                db.SpeakingComplianceConsents.Add(new SpeakingComplianceConsent
                {
                    Id = $"{sessionId}-{type}", UserId = auth.CurrentUser.UserId,
                    ConsentType = type, ConsentVersion = consent.CurrentConsentVersion,
                    AcceptedAt = DateTimeOffset.UtcNow,
                });
            foreach (var (key, value) in new (string, decimal)[]
                     { ("concurrency_budget", 20), ("cost_ceiling", 10), ("latency_sla_ms", 60000),
                         ("retention_days", 30), ("stt_cost_per_minute", .01m), ("tts_cost_per_1000_characters", .01m) })
                db.SpeakingSimulationV11OwnerApprovals.Add(Approval(sessionId, key, value));
            var providerApproval = Approval(sessionId, "audio_assessment_approval", null);
            providerApproval.EvidenceJson = "{\"provider\":\"test-audio\"}";
            db.SpeakingSimulationV11OwnerApprovals.Add(providerApproval);
            await db.SaveChangesAsync();
        }

        await using var connection = new HubConnectionBuilder().WithUrl(
            new Uri(factory.Server.BaseAddress, "/v1/conversations/hub"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(auth.AccessToken);
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            }).Build();
        var ready = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("SpeakingRoleplayReady", value => ready.TrySetResult(value));
        connection.On<JsonElement>("PatientUtterance", value => reply.TrySetResult(value));
        connection.On<string, string>("SpeakingRoleplayError", (code, message) =>
        {
            var error = new InvalidOperationException($"{code}: {message}");
            ready.TrySetException(error);
            reply.TrySetException(error);
        });
        await connection.StartAsync();
        await connection.InvokeAsync("StartSpeakingRoleplay", sessionId);
        var handshake = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(handshake.GetProperty("patientSpeaksFirst").GetBoolean());
        Assert.False(reply.Task.IsCompleted);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var session = await db.SpeakingSessions.SingleAsync(x => x.Id == sessionId);
            session.State = SpeakingSessionState.Active;
            session.RolePlayStartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await connection.InvokeAsync("SendSpeakingRoleplayTurn", sessionId,
            Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }), "audio/webm", (string?)null, "voice-turn-1");
        var utterance = await reply.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(string.IsNullOrWhiteSpace(utterance.GetProperty("audioUrl").GetString()));
        Assert.Equal(1, speech.AsrCalls);
        Assert.Equal(1, speech.TtsCalls);
        Assert.DoesNotContain("Unelicited private fact", utterance.GetRawText());
        reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await connection.InvokeAsync("SendSpeakingRoleplayTurn", sessionId,
            Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }), "audio/webm", (string?)null, "voice-turn-1");
        var replayed = await reply.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(utterance.GetRawText(), replayed.GetRawText());
        Assert.Equal(1, speech.AsrCalls);
        Assert.Equal(1, speech.TtsCalls);
        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Single(await verificationDb.SpeakingRecordings.Where(x => x.SpeakingSessionId == sessionId).ToListAsync());
        var transcript = await verificationDb.SpeakingTranscripts.AsNoTracking()
            .SingleAsync(x => x.SpeakingSessionId == sessionId && x.IsLatest);
        Assert.Contains("Good morning", JsonSerializer.Serialize(transcript));
        Assert.Single(await verificationDb.SpeakingSimulationV11TurnTelemetryRows
            .Where(x => x.SpeakingSessionId == sessionId).ToListAsync());
    }

    private static SpeakingSimulationV11OwnerApproval Approval(string prefix, string key, decimal? value) => new()
    {
        Id = $"{prefix}-{key}", ApprovalKey = key, ScopeKey = "global",
        SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
        RubricVersion = SpeakingSimulationV11Contracts.RubricVersion,
        Status = SpeakingSimulationV11ApprovalStatus.Approved, NumericValue = value,
    };

    private sealed class SpeechProviders : IConversationAsrProviderSelector, IConversationTtsProviderSelector,
        IConversationAsrProvider, IConversationTtsProvider
    {
        public int AsrCalls { get; private set; }
        public int TtsCalls { get; private set; }
        public string Name => "voice-test";
        public bool IsConfigured => true;
        public Task<IConversationAsrProvider> SelectAsync(CancellationToken ct = default) => Task.FromResult<IConversationAsrProvider>(this);
        public Task<IConversationRealtimeAsrProvider?> TrySelectRealtimeAsync(CancellationToken ct = default) => Task.FromResult<IConversationRealtimeAsrProvider?>(null);
        public Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default) => Task.FromResult<IConversationTtsProvider?>(this);
        public Task<IConversationTtsProvider?> TrySelectAsync(string name, CancellationToken ct = default) => TrySelectAsync(ct);
        public Task<bool> IsTtsDisabledAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<ConversationAsrResult> TranscribeAsync(ConversationAsrRequest request, CancellationToken ct)
        {
            AsrCalls++;
            Assert.Equal(4, request.AudioBytes);
            return Task.FromResult(new ConversationAsrResult("Good morning. How can I help?", .98, 1200, "en", Name, null));
        }
        public Task<ConversationTtsResult> SynthesizeAsync(ConversationTtsRequest request, CancellationToken ct)
        {
            TtsCalls++;
            return Task.FromResult(new ConversationTtsResult([1, 2, 3], "audio/mpeg", 900, Name, null));
        }
    }

    private sealed class PlanGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new()
        {
            SystemPrompt = "Test grounded plan", TaskInstruction = "Select safe response",
            Metadata = new AiGroundedPromptMetadata
            {
                RulebookVersion = "test", RulebookKind = context.Kind, Profession = context.Profession,
                AppliedRulesCount = 1, AppliedRuleIds = ["CONVERSATION_TEST"],
            },
        };
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default) => Task.FromResult(new AiGatewayResult
        {
            Completion = "{\"FactSelections\":[],\"NonFactualResponseKind\":\"Acknowledgement\"}",
            Metadata = request.Prompt!.Metadata, RulebookVersion = "test", AppliedRuleIds = ["CONVERSATION_TEST"],
            ResolvedProvider = "test", ResolvedModel = "test",
        });
    }
}
