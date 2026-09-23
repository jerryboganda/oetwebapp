using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// $0 full-corpus Speaking compatibility harness (PDF §10B/§10C): every
/// published card through the real Speaking path with a canned grader, named
/// failures per card/profession, and the per-profession free-sample rows.
/// Runs on the InMemory test host (no transactions: synthetic rows stay in the
/// throwaway store); the SQLite class below proves the rollback.
/// </summary>
public sealed class SpeakingCorpusCompatibilityTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private const string Route = "/v1/admin/speaking/corpus-compatibility";

    [Fact]
    public async Task ReadyCard_PassesEveryStage_AndIsItsProfessionsFreeSample()
    {
        var profession = NewProfession();
        var cardId = await CorpusSeed.SeedCardAsync(factory.Services, profession);

        var report = await RunAsync(profession);

        Assert.Equal("controlled-no-provider", report.Mode);
        var card = Assert.Single(report.Cards);
        Assert.True(card.Passed, Explain(card));
        Assert.Equal(cardId, card.CardId);
        Assert.Equal(
            new[]
            {
                SpeakingCorpusCompatibilityService.StagePublished,
                SpeakingCorpusCompatibilityService.StageProjection,
                SpeakingCorpusCompatibilityService.StageReadiness,
                SpeakingCorpusCompatibilityService.StageInstructions,
                SpeakingCorpusCompatibilityService.StageSession,
                SpeakingCorpusCompatibilityService.StageGrading,
            },
            card.Stages.Select(s => s.Name).ToArray());
        Assert.Contains("CANNED", card.Stages.Single(s => s.Name == SpeakingCorpusCompatibilityService.StageGrading).Detail);
        Assert.Equal(new SpeakingCorpusTotals(1, 1, 0), report.Totals);

        var row = Assert.Single(report.Professions);
        Assert.Equal(profession, row.ProfessionId);
        Assert.Equal(1, row.PublishedCards);
        Assert.Equal(1, row.CandidateLoadableCards);
        Assert.Equal(cardId, row.FreeSampleCardId);
        Assert.True(row.FreeSampleCardPassed);
        Assert.Equal("ok", row.State);
        Assert.Equal("controlled_unavailable", row.WritingState);
    }

    [Fact]
    public async Task NotReadyCard_FailsAtLiveVoiceReadiness_NamingCardAndProfession()
    {
        var profession = NewProfession();
        var cardId = await CorpusSeed.SeedCardAsync(factory.Services, profession, scriptNeedsOwnerInput: true);

        var report = await RunAsync(profession);

        var card = Assert.Single(report.Cards);
        Assert.False(card.Passed);
        Assert.Equal(cardId, card.CardId);
        Assert.Equal(profession, card.ProfessionId);
        Assert.Equal(SpeakingCorpusCompatibilityService.StageReadiness, card.FailedStage);
        Assert.Contains("owner input", card.Error);
        // Independent stages are still evaluated and reported.
        Assert.True(card.Stages.Single(s => s.Name == SpeakingCorpusCompatibilityService.StageSession).Ok, Explain(card));
        Assert.Equal(new SpeakingCorpusTotals(1, 0, 1), report.Totals);

        // The card is still offered as the free sample, so the §10C row says so.
        var row = Assert.Single(report.Professions);
        Assert.Equal(cardId, row.FreeSampleCardId);
        Assert.False(row.FreeSampleCardPassed);
        Assert.Equal("free_card_failing", row.State);
    }

    [Fact]
    public async Task ProfessionWithoutEligibleFreeCard_IsControlledUnavailable()
    {
        var profession = NewProfession();
        await CorpusSeed.SeedCardAsync(factory.Services, profession, contentItemStatus: ContentStatus.Draft);

        var report = await RunAsync(profession);

        var card = Assert.Single(report.Cards);
        Assert.Equal(SpeakingCorpusCompatibilityService.StagePublished, card.FailedStage);
        Assert.Contains("Draft", card.Error);
        var row = Assert.Single(report.Professions);
        Assert.Equal(0, row.CandidateLoadableCards);
        Assert.Null(row.FreeSampleCardId);
        Assert.Null(row.FreeSampleCardPassed);
        Assert.Equal("controlled_unavailable", row.State);
    }

    [Fact]
    public void LearnerProjection_NeverCarriesEmotionGoalOrTopic()
    {
        var card = CorpusSeed.NewCard("rpc-projection", "ci-projection", "medicine");
        card.PatientEmotion = "angry";
        card.CommunicationGoal = "Persuade";
        card.ClinicalTopic = "hypertension";

        var projection = SpeakingSessionService.ProjectLearnerCard(card);
        var json = JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(SpeakingCorpusCompatibilityService.CheckProjection(projection).Ok);
        Assert.DoesNotContain("angry", json);
        Assert.DoesNotContain("Persuade", json);
        Assert.DoesNotContain("hypertension", json);
        // The check itself fails closed on any leaked key.
        Assert.ThrowsAny<Exception>(() => SpeakingCorpusCompatibilityService.CheckProjection(new
        {
            professionId = "medicine",
            candidateRole = "Doctor",
            setting = "Clinic",
            background = "Background",
            tasks = new[] { "Explain" },
            patientEmotion = "angry",
        }));
    }

    [Fact]
    public void ScriptedTranscript_IsCandidateFirst_EightTurns()
    {
        var turns = SpeakingCorpusCompatibilityService.BuildScriptedTurns(CorpusSeed.NewCard("rpc-turns", "ci-turns", "medicine"));

        Assert.Equal(8, turns.Count);
        Assert.Equal("candidate", turns[0].Speaker);
        Assert.Equal(new[] { "candidate", "patient" }, turns.Select(t => t.Speaker).Distinct().ToArray());
    }

    [Fact]
    public async Task Endpoint_RequiresAdminContentWrite()
    {
        using var learner = Client(role: "learner", permissions: null);
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.PostAsync($"{Route}?cardId=none", null)).StatusCode);

        using var readOnlyAdmin = Client(role: "admin", permissions: "content:read");
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnlyAdmin.PostAsync($"{Route}?cardId=none", null)).StatusCode);

        using var admin = Client(role: "admin", permissions: "content:read,content:write");
        var response = await admin.PostAsync($"{Route}?cardId=none", null);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal("controlled-no-provider", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("totals").GetProperty("cards").GetInt32());
    }

    private async Task<SpeakingCorpusReport> RunAsync(string profession)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SpeakingCorpusCompatibilityService>()
            .RunAsync("test-admin", profession, null, null, null, default);
    }

    private HttpClient Client(string role, string? permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"{role}-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{role}-corpus@example.test");
        if (permissions is not null) client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", permissions);
        return client;
    }

    private static string NewProfession() => $"hp{Guid.NewGuid():N}"[..14];

    internal static string Explain(SpeakingCorpusCardResult card)
        => string.Join(" | ", card.Stages.Select(s => $"{s.Name}={(s.Ok ? "ok" : "FAIL")}: {s.Detail}"));
}

/// <summary>
/// On a relational provider the harness must persist nothing: the synthetic
/// learner, session, transcript, credit hold and assessment all roll back.
/// </summary>
public sealed class SpeakingCorpusCompatibilityRollbackTests
{
    [Fact]
    public async Task RelationalRun_PassesAndPersistsNothing()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"oet-corpus-harness-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = new SqliteHarnessFactory(sqlitePath);
            var profession = $"hp{Guid.NewGuid():N}"[..14];
            await CorpusSeed.SeedCardAsync(factory.Services, profession);

            SpeakingCorpusReport report;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                report = await scope.ServiceProvider.GetRequiredService<SpeakingCorpusCompatibilityService>()
                    .RunAsync("test-admin", profession, null, null, null, default);
            }

            var card = Assert.Single(report.Cards);
            Assert.True(card.Passed, SpeakingCorpusCompatibilityTests.Explain(card));
            Assert.Contains("rolled back", card.Stages.Single(s => s.Name == SpeakingCorpusCompatibilityService.StageSession).Detail);

            await using var verify = factory.Services.CreateAsyncScope();
            var db = verify.ServiceProvider.GetRequiredService<LearnerDbContext>();
            Assert.False(await db.Users.AnyAsync(u => u.Id.StartsWith("corpus-harness-")));
            Assert.False(await db.SpeakingSessions.AnyAsync());
            Assert.False(await db.SpeakingTranscripts.AnyAsync());
            Assert.False(await db.SpeakingRecordings.AnyAsync());
            Assert.False(await db.SpeakingAiAssessments.AnyAsync());
            Assert.False(await db.AiCreditReservations.AnyAsync());
            Assert.False(await db.Attempts.AnyAsync(a => a.SubtestCode == "speaking"));
        }
        finally
        {
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-wal", $"{sqlitePath}-shm" })
            {
                try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
            }
        }
    }

    private sealed class SqliteHarnessFactory(string sqlitePath) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={sqlitePath}",
                });
            });
        }
    }
}

internal static class CorpusSeed
{
    public static RolePlayCard NewCard(string cardId, string contentItemId, string profession) => new()
    {
        Id = cardId,
        ContentItemId = contentItemId,
        ProfessionId = profession,
        ScenarioTitle = "Chest pain follow-up",
        Setting = "General practice",
        CandidateRole = "Doctor",
        InterlocutorRole = "Patient",
        Background = "The patient returns after an ECG.",
        Tasks = new[] { "Explain the ECG result", "Find out the patient's concerns", "Agree a follow-up plan" },
        PrepTimeSeconds = 180,
        RolePlayTimeSeconds = 300,
        Status = ContentStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static async Task<string> SeedCardAsync(
        IServiceProvider services,
        string profession,
        bool scriptNeedsOwnerInput = false,
        ContentStatus contentItemStatus = ContentStatus.Published)
    {
        var cardId = $"rpc-h-{Guid.NewGuid():N}";
        var contentItemId = $"ci-h-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.ContentItems.Add(new ContentItem
        {
            Id = contentItemId,
            ContentType = "speaking_roleplay",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = "Chest pain follow-up",
            Difficulty = "core",
            Status = contentItemStatus,
            PublishedRevisionId = $"{contentItemId}-r1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
        });
        db.RolePlayCards.Add(NewCard(cardId, contentItemId, profession));
        db.InterlocutorScripts.Add(new InterlocutorScript
        {
            Id = $"is-h-{Guid.NewGuid():N}",
            RolePlayCardId = cardId,
            PatientBackground = "Worried about the heart.",
            OpeningResponse = "Hello doctor, I came about my ECG.",
            Prompt1 = "Is it serious?",
            HiddenInformation = "Father had a heart attack at 50.",
            ClosingCue = "Thank you, doctor.",
            EmotionalState = "anxious",
            ContentOrigin = "authored",
            NeedsOwnerInput = scriptNeedsOwnerInput,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return cardId;
    }
}
