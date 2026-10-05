using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The engine identity (RW-101), the embedded canary (RW-111), the hand-authored migration, and the DI wiring that keeps
/// the whole boundary dark and Postgres-only (RW-090..RW-092).
/// </summary>
public sealed class RemoteEngineMigrationAndWiringTests
{
    // ── engine identity: a changed extractor MUST come with a LayoutRevision bump ──────────────────────

    /// <summary>
    /// SHA-256 of <c>PdfPigPdfTextExtractor.cs</c> (line endings normalised to LF) for each <c>PdfTextEngine.LayoutRevision</c>.
    /// When the extractor's behaviour changes, bump <see cref="PdfTextEngine.LayoutRevision"/> by hand and ADD a row here: a helper
    /// compiled from the same source then reports a different engine version, and every in-flight remote job is re-keyed instead of
    /// silently producing text that differs from the in-process oracle.
    /// </summary>
    private static readonly Dictionary<int, string> KnownExtractorHashes = new()
    {
        [1] = "6e290e380a13ab92b587c5d425359e25616282e6ac46bbcf404fc6df589f4858",
    };

    [Fact]
    public void ExtractorSource_ChangedWithoutABumpOfTheLayoutRevision_FailsTheBuild()
    {
        var path = Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api", "Services", "Content", "PdfPigPdfTextExtractor.cs");
        var source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();

        Assert.True(
            KnownExtractorHashes.TryGetValue(PdfTextEngine.LayoutRevision, out var expected),
            $"PdfTextEngine.LayoutRevision is {PdfTextEngine.LayoutRevision} but this test has no recorded extractor hash for it. "
            + $"Add [{PdfTextEngine.LayoutRevision}] = \"{actual}\" to KnownExtractorHashes.");
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            "PdfPigPdfTextExtractor.cs changed. Bump PdfTextEngine.LayoutRevision (a helper must not be allowed to extract with the old "
            + $"behaviour) and add [{PdfTextEngine.LayoutRevision + 1}] = \"{actual}\" to KnownExtractorHashes.");
    }

    [Fact]
    public void EngineVersion_IsDerivedFromThePdfPigAssemblyAndTheLayoutRevision()
    {
        Assert.False(string.IsNullOrWhiteSpace(PdfTextEngine.PdfPigVersion));
        Assert.DoesNotContain('+', PdfTextEngine.PdfPigVersion);
        Assert.Equal(
            "pdfpig:" + PdfTextEngine.PdfPigVersion + "/oet-text:" + PdfTextEngine.LayoutRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PdfTextEngine.EngineVersion);
        Assert.Matches(new Regex(@"^[A-Za-z0-9._:+/-]{1,96}$", RegexOptions.CultureInvariant), PdfTextEngine.EngineVersion);
        Assert.True(RemoteWireValidation.IsEngineVersion(PdfTextEngine.EngineVersion));
        Assert.True(RemoteWireValidation.IsEngineVersion(RemoteJobKinds.EngineVersion(RemoteJobKinds.CompanionIndexPrep, new RemoteJobsOptions())));
    }

    // ── canary ───────────────────────────────────────────────────────────────

    [Fact]
    public void Canary_IsAnEmbeddedPdfThatThePrimaryCanActuallyExtract()
    {
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(RemoteCanary.PdfBytes, 0, 5), StringComparison.Ordinal);
        Assert.Equal(RemoteCanary.PdfBytes.LongLength, RemoteCanary.PdfSize);
        Assert.Equal(RemoteIds.Sha256Hex(RemoteCanary.PdfBytes), RemoteCanary.PdfSha256);
        Assert.True(RemoteCanary.PdfSize < 64 * 1024, "the canary must stay tiny");

        var expected = RemoteCanary.GetExpectedAsync().GetAwaiter().GetResult();

        // A fixture that extracts to nothing could never prove a node correct: fail the build, not production.
        Assert.True(expected.PageCount >= 1);
        Assert.True(expected.EmbeddedChars >= RemoteCanary.MinTextLength, "the canary document must carry real text");
        Assert.True(RemoteIds.IsSha256Hex(expected.TextSha256));
        Assert.True(RemoteIds.IsSha256Hex(expected.PagesSha256));
    }

    [Fact]
    public async Task Canary_ExpectedValuesAreExactlyWhatTheDocumentedFormulaYields()
    {
        var expected = await RemoteCanary.GetExpectedAsync();

        var extractor = new PdfPigPdfTextExtractor(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfPigPdfTextExtractor>.Instance);
        await using var stream = RemoteCanary.OpenRead();
        var pages = await extractor.ExtractPagesAsync(stream, CancellationToken.None);

        Assert.Equal(expected, RemoteCanary.Derive(pages));
        Assert.Equal(pages.Count, expected.PageCount);
        Assert.Equal(string.Join("\n\n", pages).Trim().Length, expected.EmbeddedChars);
    }

    [Fact]
    public void Canary_InputStreamsAreIndependent()
    {
        using var first = RemoteCanary.OpenRead();
        using var second = RemoteCanary.OpenRead();
        first.ReadByte();

        Assert.Equal(1, first.Position);
        Assert.Equal(0, second.Position);
    }

    // ── chunker identity ─────────────────────────────────────────────────────

    [Fact]
    public void CompanionEngineVersion_PinsTheChunkerNextToThePdfEngine()
    {
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.CompanionIndexPrep, new RemoteJobsOptions());

        Assert.Equal(PdfTextEngine.EngineVersion + "/" + CompanionChunker.Version, engine);
    }

    // ── hand-authored migration ──────────────────────────────────────────────

    [Fact]
    public void Migration_ExposesTheEfDiscoveryAttributes()
    {
        var migration = typeof(AddRemoteWorkersAndJobs).GetCustomAttribute<MigrationAttribute>();
        var context = typeof(AddRemoteWorkersAndJobs).GetCustomAttribute<DbContextAttribute>();

        Assert.NotNull(migration);
        Assert.Equal("20270110090000_AddRemoteWorkersAndJobs", migration!.Id);
        Assert.NotNull(context);
        Assert.Equal(typeof(LearnerDbContext), context!.ContextType);
    }

    private static List<MigrationOperation> RunUp(string provider)
    {
        var builder = new MigrationBuilder(provider);
        var up = typeof(AddRemoteWorkersAndJobs).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Up not found.");
        up.Invoke(new AddRemoteWorkersAndJobs(), [builder]);
        return builder.Operations.ToList();
    }

    [Fact]
    public void Migration_OnPostgres_IsOneIdempotentAdditiveScript()
    {
        var operations = RunUp("Npgsql.EntityFrameworkCore.PostgreSQL");

        var sql = Assert.IsType<SqlOperation>(Assert.Single(operations)).Sql;
        Assert.Same(RemoteJobsSchemaSql.Up, sql);

        foreach (var table in new[] { "RemoteWorkers", "RemoteCredentials", "RemoteJobs", "RemoteJobOutputs" })
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS \"{table}\"", sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(operations.OfType<DropTableOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<AlterColumnOperation>());
    }

    [Fact]
    public void Migration_OnAnyOtherProvider_DoesNothing()
    {
        Assert.Empty(RunUp("Microsoft.EntityFrameworkCore.Sqlite"));
        Assert.Empty(RunUp("Microsoft.EntityFrameworkCore.InMemory"));
    }

    [Fact]
    public void Schema_EncodesTheStateMachineInvariantsAsDatabaseConstraints()
    {
        var sql = RemoteJobsSchemaSql.Up;

        // lease shape, attempt bound, fence monotonic, unique idempotency key, partial claim index
        Assert.Contains("CK_RemoteJobs_LeaseShape", sql, StringComparison.Ordinal);
        Assert.Contains("\"Attempt\" >= 0 AND \"Attempt\" <= \"MaxAttempts\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"FenceToken\" >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("UX_RemoteJobs_IdempotencyKey", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE \"State\" = 'Queued'", sql, StringComparison.Ordinal);
        Assert.Contains("CK_RemoteWorkers_Status", sql, StringComparison.Ordinal);
        Assert.Contains("CK_RemoteCredentials_Hash", sql, StringComparison.Ordinal);

        // only a hash is ever stored: there is no column that could hold a plaintext token
        Assert.Contains("\"SecretHash\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Secret\" ", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Token\" ", sql, StringComparison.Ordinal);
    }

    // ── dependency injection: dark by default, Postgres only ─────────────────

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static bool HasHosted<T>(IServiceCollection services)
        => services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));

    [Fact]
    public void Wiring_OnANonPostgresProvider_RegistersOnlyTheOptions()
    {
        var services = new ServiceCollection();

        services.AddRemoteJobs(Config(("RemoteJobs:LeaseSeconds", "45")), isNpgsql: false, isWorker: false);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRemoteJobFlags));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRemotePdfExtractionProducer));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRemoteCompanionIndexPrep));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRemoteKindHandler));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(45, provider.GetRequiredService<IOptions<RemoteJobsOptions>>().Value.LeaseSeconds);
        Assert.Null(provider.GetService<IRemotePdfExtractionProducer>());
        Assert.Null(provider.GetService<IRemoteCompanionIndexPrep>());
    }

    [Fact]
    public void Wiring_OnPostgres_RegistersTheReaperEverywhere_AndTheComparerOnlyOnTheWorker()
    {
        var api = new ServiceCollection();
        api.AddRemoteJobs(Config(), isNpgsql: true, isWorker: false);
        var worker = new ServiceCollection();
        worker.AddRemoteJobs(Config(), isNpgsql: true, isWorker: true);

        Assert.True(HasHosted<RemoteJobReaper>(api));
        Assert.True(HasHosted<RemoteJobReaper>(worker));
        Assert.True(HasHosted<RemoteJobFlagSeeder>(api));

        // Re-extraction is CPU work: only the ai-worker does it, never an API slot.
        Assert.False(HasHosted<RemoteJobShadowComparer>(api));
        Assert.True(HasHosted<RemoteJobShadowComparer>(worker));
    }

    [Fact]
    public void Wiring_OnPostgres_RegistersOneHandlerPerImplementedKind_AndBothProducers()
    {
        var services = new ServiceCollection();
        services.AddRemoteJobs(Config(), isNpgsql: true, isWorker: false);

        var handlers = services.Where(d => d.ServiceType == typeof(IRemoteKindHandler)).Select(d => d.ImplementationType).ToList();
        Assert.Contains(typeof(PdfExtractKindHandler), handlers);
        Assert.Contains(typeof(CompanionIndexPrepKindHandler), handlers);
        Assert.Equal(2, handlers.Count);
        Assert.Contains(services, d => d.ServiceType == typeof(IRemotePdfExtractionProducer) && d.ImplementationType == typeof(RemotePdfExtractionProducer));
        Assert.Contains(services, d => d.ServiceType == typeof(IRemoteCompanionIndexPrep) && d.ImplementationType == typeof(RemoteCompanionIndexPrepProducer));
    }

    [Fact]
    public void Wiring_CallingItTwice_DoesNotDuplicateAnything()
    {
        var services = new ServiceCollection();
        services.AddRemoteJobs(Config(), isNpgsql: true, isWorker: true);
        var count = services.Count(d => d.ServiceType == typeof(IRemoteKindHandler));
        var hosted = services.Count(d => d.ServiceType == typeof(IHostedService));

        services.AddRemoteJobs(Config(), isNpgsql: true, isWorker: true);

        Assert.Equal(count, services.Count(d => d.ServiceType == typeof(IRemoteKindHandler)));
        Assert.Equal(hosted, services.Count(d => d.ServiceType == typeof(IHostedService)));
    }

    [Fact]
    public void Wiring_EveryFeatureFlagIsSeededOff_AndTheSeederNeverEnablesOne()
    {
        // The seeder is insert-only with Enabled = false: a restart can never turn the feature on or undo an operator's choice.
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "backend", "src", "OetLearner.Api", "Services", "RemoteJobs", "RemoteJobsServiceCollectionExtensions.cs"));

        Assert.Contains("Enabled = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Enabled = true", source, StringComparison.Ordinal);
        Assert.Equal(8, RemoteJobFlagKeys.All.Count);
        Assert.Equal(RemoteJobFlagKeys.All.Count, RemoteJobFlagKeys.All.Distinct().Count());
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
