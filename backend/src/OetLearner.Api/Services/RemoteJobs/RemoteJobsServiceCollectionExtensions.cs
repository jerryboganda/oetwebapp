using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// Registers the remote-worker boundary (OET-RWP/1 section 9.4). The <c>RemoteJobs</c> options are always bound, but the claim/CAS
/// services, the producers, the appliers, the reaper and the hosted workers exist ONLY when the configured provider is Npgsql: their SQL
/// uses <c>FOR UPDATE SKIP LOCKED</c> and the database clock, and there is no SQLite or InMemory path. Elsewhere none of it is
/// registered, so the desktop/dev providers boot exactly as before. Everything is OFF until feature flags are turned on.
/// </summary>
public static class RemoteJobsServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isNpgsql,
        bool isWorker)
    {
        services.Configure<RemoteJobsOptions>(configuration.GetSection(RemoteJobsOptions.SectionName));
        if (!isNpgsql) return services;

        services.TryAddSingleton<RemoteJobsSettings>();
        services.TryAddSingleton<IRemoteJobFlags, RemoteJobFlags>();
        services.TryAddSingleton<RemoteRateLimits>();
        services.TryAddSingleton<RemoteAuthCache>();
        services.TryAddSingleton<RemoteOrphanTracker>();
        services.TryAddSingleton<RemoteLocalWaitTracker>();
        services.TryAddSingleton<IPrimaryHeadroom, CgroupPrimaryHeadroom>();

        services.TryAddScoped<IRemoteJobQueue, RemoteJobQueue>();
        services.TryAddScoped<RemotePlacement>();
        services.TryAddScoped<RemoteClaimService>();
        services.TryAddScoped<RemoteJobLifecycleService>();
        services.TryAddScoped<RemoteCompletionService>();
        services.TryAddScoped<RemoteInputOutputService>();
        services.TryAddScoped<RemoteWorkerService>();
        services.TryAddScoped<RemoteFleetJobsService>();
        services.TryAddScoped<RemoteFleetCredentialService>();
        services.TryAddScoped<RemoteJobSweeper>();

        // Producers consulted by existing workers/services; absent (null) everywhere else, which means "do it locally".
        services.TryAddScoped<IRemotePdfExtractionProducer, RemotePdfExtractionProducer>();
        services.TryAddScoped<IRemoteCompanionIndexPrep, RemoteCompanionIndexPrepProducer>();

        // Media kinds (OET-RWP/1 sections 6.3 and 6.4). The consumers are optional constructor parameters of the existing stages, so
        // with these absent (any non-PostgreSQL host) the Live Class transcription and the Speaking audio stage behave exactly as before.
        services.TryAddScoped<IRemoteAudioExtraction, RemoteAudioExtractionProducer>();
        services.TryAddScoped<IRemoteSpeakingJoin, RemoteSpeakingJoinProducer>();

        // One handler (validator + applier) per kind. A new kind adds a handler and a producer; nothing else changes.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteKindHandler, PdfExtractKindHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteKindHandler, CompanionIndexPrepKindHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteKindHandler, AudioExtractKindHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteKindHandler, SpeakingJoinKindHandler>());

        // The reaper runs in EVERY process (blue, green, ai-worker): all of its statements are state-conditional and skip locked rows.
        services.AddHostedService<RemoteJobReaper>();
        services.AddHostedService<RemoteJobFlagSeeder>();

        // Re-extraction for shadow/verify comparison is CPU work: only the ai-worker does it, never an API slot.
        if (isWorker) services.AddHostedService<RemoteJobShadowComparer>();

        // The Speaking join precompute follows the grades the ai-worker executes: only the ai-worker enqueues, and only once every flag is on.
        if (isWorker) services.AddHostedService<RemoteSpeakingJoinSweeper>();

        return services;
    }
}

/// <summary>
/// Seeds the remote-job feature flags as DISABLED rows so the admin flag UI lists them (OET-RWP/1 section 9.2). Idempotent and
/// insert-only: it never changes an existing row, so an operator's choice survives every restart. A missing row is OFF either way
/// (fail closed), so seeding is a convenience, not a dependency.
/// </summary>
public sealed class RemoteJobFlagSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<RemoteJobFlagSeeder> logger) : BackgroundService
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [RemoteJobFlagKeys.Master] = "Remote workers: master switch for producers and claim.",
        [RemoteJobFlagKeys.PdfExtract] = "Remote workers: extract PDF text on helpers (pdf.extract, apply).",
        [RemoteJobFlagKeys.PdfExtractShadow] = "Remote workers: shadow-compare helper PDF extraction (never applied).",
        [RemoteJobFlagKeys.CompanionIndexPrep] = "Remote workers: prepare Companion document chunks on helpers.",
        [RemoteJobFlagKeys.MediaAudioExtract] = "Remote workers: audio extraction for recordings on helpers.",
        [RemoteJobFlagKeys.MediaSpeakingJoin] = "Remote workers: join Speaking clips into one audio file on helpers.",
        [RemoteJobFlagKeys.FleetService] = "Remote workers: fleet manager service plane (/v1/internal/fleet).",
        [RemoteJobFlagKeys.FreezeApplies] = "EMERGENCY: refuse to apply any helper result and cancel the job.",
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            if (!db.Database.IsNpgsql()) return;

            var keys = RemoteJobFlagKeys.All.ToArray();
            var existing = (await db.FeatureFlags.AsNoTracking()
                    .Where(flag => keys.Contains(flag.Key))
                    .Select(flag => flag.Key)
                    .ToListAsync(stoppingToken))
                .ToHashSet(StringComparer.Ordinal);

            var now = DateTimeOffset.UtcNow;
            foreach (var key in RemoteJobFlagKeys.All.Where(key => !existing.Contains(key)))
            {
                db.FeatureFlags.Add(new FeatureFlag
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = Descriptions.TryGetValue(key, out var description) ? description : key,
                    Key = key,
                    FlagType = FeatureFlagType.Operational,
                    Enabled = false,
                    RolloutPercentage = 0,
                    Description = description,
                    Owner = "platform",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A race with another process seeding the same keys (unique index) or a transient failure: flags stay OFF either way.
            logger.LogInformation(ex, "Remote job feature flags were not seeded this start.");
        }
    }
}
