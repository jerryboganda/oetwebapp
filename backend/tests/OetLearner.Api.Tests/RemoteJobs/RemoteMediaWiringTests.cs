using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The wiring around the media kinds that no PostgreSQL is needed to check: the migration, the model, the dependency injection that keeps
/// the whole thing dark and Postgres-only, and the guards that keep every provider call, every raw file access and every helper secret
/// out of the media code (OET-RWP/1 D2, D7 and section 10).
/// </summary>
public sealed class RemoteMediaWiringTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private static bool HasHosted<T>(IServiceCollection services)
        => services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));

    // ── migration ────────────────────────────────────────────────────────────

    private static List<MigrationOperation> RunUp()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(AddLiveClassRecordingAudioChunks).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Up not found.");
        up.Invoke(new AddLiveClassRecordingAudioChunks(), [builder]);
        return builder.Operations.ToList();
    }

    [Fact]
    public void Migration_ExposesTheEfDiscoveryAttributes_AndSortsAfterTheRemoteJobsMigration()
    {
        var migration = typeof(AddLiveClassRecordingAudioChunks).GetCustomAttribute<MigrationAttribute>();
        var context = typeof(AddLiveClassRecordingAudioChunks).GetCustomAttribute<DbContextAttribute>();

        Assert.NotNull(migration);
        Assert.Equal("20270111090000_AddLiveClassRecordingAudioChunks", migration!.Id);
        Assert.Equal(typeof(LearnerDbContext), context!.ContextType);
        Assert.True(
            string.CompareOrdinal(migration.Id, typeof(AddRemoteWorkersAndJobs).GetCustomAttribute<MigrationAttribute>()!.Id) > 0,
            "the column must be added after the remote-job tables exist");
    }

    [Fact]
    public void Migration_IsOneAdditiveIdempotentColumn_AndItsDownDropsOnlyThatColumn()
    {
        var up = Assert.IsType<SqlOperation>(Assert.Single(RunUp()));

        Assert.Equal("ALTER TABLE \"LiveClassRecordings\" ADD COLUMN IF NOT EXISTS \"AudioChunksJson\" text;", up.Sql);

        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddLiveClassRecordingAudioChunks).GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddLiveClassRecordingAudioChunks(), [builder]);
        var down = Assert.IsType<SqlOperation>(Assert.Single(builder.Operations));
        Assert.Equal("ALTER TABLE \"LiveClassRecordings\" DROP COLUMN IF EXISTS \"AudioChunksJson\";", down.Sql);
    }

    [Fact]
    public void Model_CarriesTheNullableChunkManifestColumn_AndTheSnapshotAgrees()
    {
        using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var property = db.Model.FindEntityType(typeof(LiveClassRecording))!.FindProperty(nameof(LiveClassRecording.AudioChunksJson));

        Assert.NotNull(property);
        Assert.True(property!.IsNullable);
        Assert.Equal(typeof(string), property.ClrType);

        var snapshot = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "backend", "src", "OetLearner.Api", "Data", "Migrations", "LearnerDbContextModelSnapshot.cs"));
        var block = snapshot[snapshot.IndexOf("modelBuilder.Entity(\"OetLearner.Api.Domain.LiveClassRecording\", b =>", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("b.ToTable(\"LiveClassRecordings\");", StringComparison.Ordinal)];
        Assert.Contains("b.Property<string>(\"AudioChunksJson\")", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChunkManifest_IsNeverPartOfWhatAClientIsSent()
    {
        // The manifest holds server-side storage keys: no DTO or endpoint of the Live Class surface may read it.
        var root = Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("AudioChunksJson", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AudioExtractKindHandler.cs",
                "LiveClassAudioManifest.cs",
                "LiveClassEntities.cs",
                "LiveClassRecordingProcessingService.cs",
            },
            offenders);
    }

    // ── dependency injection: dark by default, Postgres only ─────────────────

    [Fact]
    public void Wiring_OnANonPostgresProvider_RegistersNoMediaPieceAtAll()
    {
        var services = new ServiceCollection();
        services.AddRemoteJobs(Config(), isNpgsql: false, isWorker: true);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRemoteAudioExtraction));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRemoteSpeakingJoin));
        Assert.False(HasHosted<RemoteSpeakingJoinSweeper>(services));

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IRemoteAudioExtraction>());
        Assert.Null(provider.GetService<IRemoteSpeakingJoin>());
    }

    [Fact]
    public void Wiring_OnPostgres_RegistersBothMediaProducersScoped_AndTheirHandlers()
    {
        var services = new ServiceCollection();
        services.AddRemoteJobs(Config(), isNpgsql: true, isWorker: false);

        var audio = Assert.Single(services, d => d.ServiceType == typeof(IRemoteAudioExtraction));
        var join = Assert.Single(services, d => d.ServiceType == typeof(IRemoteSpeakingJoin));
        Assert.Equal(typeof(RemoteAudioExtractionProducer), audio.ImplementationType);
        Assert.Equal(typeof(RemoteSpeakingJoinProducer), join.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, audio.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, join.Lifetime);

        var handlers = services.Where(d => d.ServiceType == typeof(IRemoteKindHandler)).Select(d => d.ImplementationType).ToList();
        Assert.Contains(typeof(AudioExtractKindHandler), handlers);
        Assert.Contains(typeof(SpeakingJoinKindHandler), handlers);
        Assert.All(handlers, handler => Assert.Equal(1, handlers.Count(other => other == handler)));
    }

    [Fact]
    public void Wiring_TheSpeakingJoinSweeper_RunsOnlyOnTheAiWorker()
    {
        var api = new ServiceCollection();
        api.AddRemoteJobs(Config(), isNpgsql: true, isWorker: false);
        var worker = new ServiceCollection();
        worker.AddRemoteJobs(Config(), isNpgsql: true, isWorker: true);

        // The ai-worker executes the grades the precompute follows; an API slot never enqueues it.
        Assert.False(HasHosted<RemoteSpeakingJoinSweeper>(api));
        Assert.True(HasHosted<RemoteSpeakingJoinSweeper>(worker));
    }

    [Fact]
    public void Wiring_CallingItTwice_DoesNotDuplicateTheMediaPieces()
    {
        var services = new ServiceCollection();
        services.AddRemoteJobs(Config(), isNpgsql: true, isWorker: true);
        var before = services.Count(d => d.ServiceType is { } type && (type == typeof(IRemoteAudioExtraction) || type == typeof(IRemoteSpeakingJoin) || type == typeof(IHostedService)));

        services.AddRemoteJobs(Config(), isNpgsql: true, isWorker: true);

        Assert.Equal(before, services.Count(d => d.ServiceType is { } type && (type == typeof(IRemoteAudioExtraction) || type == typeof(IRemoteSpeakingJoin) || type == typeof(IHostedService))));
    }

    [Fact]
    public void TheConsumersOfTheMediaPieces_TakeThemAsOptionalConstructorParameters_SoTheyAreInertWhenAbsent()
    {
        foreach (var (type, parameter) in new[]
                 {
                     (typeof(LiveClassRecordingProcessingService), "remoteAudio"),
                     (typeof(SpeakingAudioEvidenceService), "remoteJoin"),
                     (typeof(SpeakingComplianceService), "remoteJoin"),
                 })
        {
            var constructor = type.GetConstructors().Single();
            var found = constructor.GetParameters().Single(p => p.Name == parameter);

            Assert.True(found.HasDefaultValue, $"{type.Name}.{parameter} must be optional");
            Assert.Null(found.DefaultValue);
        }
    }

    // ── guards: what the media code must never do ────────────────────────────

    private static readonly string[] MediaSources =
    [
        "Services/RemoteJobs/AudioExtractKindHandler.cs",
        "Services/RemoteJobs/SpeakingJoinKindHandler.cs",
        "Services/RemoteJobs/RemoteAudioExtractionProducer.cs",
        "Services/RemoteJobs/RemoteSpeakingJoinProducer.cs",
        "Services/LiveClasses/LiveClassAudioManifest.cs",
        "Services/LiveClasses/IRemoteAudioExtraction.cs",
        "Services/Speaking/IRemoteSpeakingJoin.cs",
        "Services/Speaking/SpeakingAudioClips.cs",
    ];

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api", relative));

    /// <summary>The file without its comment lines: a guard is about what the code DOES, not what its documentation mentions.</summary>
    private static string CodeOnly(string relative)
        => string.Join(
            "\n",
            Source(relative).Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void TheMediaCode_NeverCallsAnAiProvider_AndNeverSeesAProviderKey()
    {
        // OET-RWP/1 D7: helpers make no AI calls and hold no provider keys; every provider call (and its AiUsageRecord) stays on the
        // primary, in the existing stages. The kinds themselves are pure media plumbing.
        foreach (var relative in MediaSources)
        {
            var source = CodeOnly(relative);

            foreach (var forbidden in new[] { "IAiGatewayService", "IDirectAiCallRecorder", "AiGatewayRequest", "AiUsageRecord", "IRuntimeSettingsProvider", "ApiKey", "AiProvider" })
            {
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal), $"{relative} must not reference {forbidden}");
            }
        }
    }

    [Fact]
    public void TheMediaCode_ReachesStorageOnlyThroughIFileStorage()
    {
        foreach (var relative in MediaSources)
        {
            var source = CodeOnly(relative);

            foreach (var forbidden in new[] { "File.Open", "File.Read", "File.Write", "File.Create", "File.Delete", "File.Exists", "Directory.", "FileStream", "Path.Combine", "Path.GetTempPath" })
            {
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal), $"{relative} must not use {forbidden}; media goes through IFileStorage");
            }
        }
    }

    [Fact]
    public void TheTranscriptionStage_StillMakesItsOwnGatewayCalls_AndNothingElseDoes()
    {
        var stage = Source("Services/LiveClasses/LiveClassRecordingProcessingService.cs");

        // every chunk call is the same gateway call the single-recording path makes (so it is recorded the same way)
        Assert.Contains("FeatureCode = AiFeatureCodes.ClassRecordingTranscribe", stage, StringComparison.Ordinal);
        Assert.Contains("aiGateway.CompleteAsync", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("IDirectAiCallRecorder", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("new AiUsageRecord", stage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMediaProducers_NeverTouchGradingCreditsOrTheMaxRoute()
    {
        // Owner rules: the Claude Max route is never skipped and grading/credit logic is not altered by the fleet work.
        foreach (var relative in MediaSources)
        {
            var source = CodeOnly(relative);

            foreach (var forbidden in new[] { "writing-claude-sub", "PinnedProviderCode", "WritingAiClaudeQuotaExceededUntil", "SpeakingCreditSettlement", "AiCreditReservation", "SpeakingGradeChain" })
            {
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal), $"{relative} must not reference {forbidden}");
            }
        }
    }

    [Fact]
    public void TheSpeakingAudioStage_OnlyGainedAnOptionalLookup_ItsJudgeCallIsUnchanged()
    {
        var stage = Source("Services/Speaking/SpeakingAudioEvidenceService.cs");

        // the join is the only thing a helper can supply; the judge is still pinned to its own provider row and never sent the transcript
        Assert.Contains("Provider = AiProviderRegistry.SpeakingAudioProviderCode", stage, StringComparison.Ordinal);
        Assert.Contains("TryServePrecomputedJoinAsync", stage, StringComparison.Ordinal);
        Assert.Contains("transcoder.JoinToMp3Async(inputs, ct)", stage, StringComparison.Ordinal); // the local fallback is still there
    }

    [Fact]
    public void ThePcmJoiner_IsAPureStandaloneFile_SoTheAgentCanLinkCompileIt()
    {
        // OET-RWP/1 section 6.4: no IOptions, no configuration, no DI, no logging in the file a helper compiles
        var code = CodeOnly("Services/Speaking/PcmJoiner.cs");

        foreach (var forbidden in new[] { "IOptions", "ILogger", "IServiceProvider", "Microsoft.Extensions", "OetLearner.Api.Configuration", "FfmpegSpeakingAudioTranscoder" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        Assert.Contains("public static class PcmJoiner", code, StringComparison.Ordinal);
        Assert.DoesNotContain("class PcmJoiner", CodeOnly("Services/Speaking/SpeakingAudioTranscoder.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnvValidatorKnowsTheNewOption_AndComposeForwardsIt()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "deploy", "validate-production-env.sh"));
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.production.yml"));

        Assert.Contains("require_int_min_if_set REMOTEJOBS__SPEAKINGJOINOUTPUTTTLHOURS 1", script, StringComparison.Ordinal);
        Assert.Contains("RemoteJobs__SpeakingJoinOutputTtlHours: ${REMOTEJOBS__SPEAKINGJOINOUTPUTTTLHOURS:-24}", compose, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the OET repository root.");
    }
}
