using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// One isolated PostgreSQL schema carrying the four remote-job tables (the SAME DDL as migration
/// <c>20270110090000_AddRemoteWorkersAndJobs</c>, via <see cref="RemoteJobsSchemaSql.Up"/>) plus the minimal slice of the existing
/// schema the appliers touch. Services are built by hand over fresh contexts, exactly as DI would build them per request.
/// </summary>
internal sealed class RemotePgHarness : IAsyncDisposable
{
    private const string SupportDdl = """
        CREATE TABLE "AuditEvents" (
            "Id"                 character varying(64)    NOT NULL,
            "OccurredAt"         timestamp with time zone NOT NULL,
            "ActorId"            character varying(64)    NOT NULL,
            "ActorAuthAccountId" character varying(64)    NULL,
            "ActorName"          character varying(128)   NOT NULL,
            "Action"             character varying(128)   NOT NULL,
            "ResourceType"       character varying(64)    NOT NULL,
            "ResourceId"         character varying(64)    NULL,
            "Details"            text                     NULL,
            CONSTRAINT "PK_AuditEvents" PRIMARY KEY ("OccurredAt", "Id")
        );
        CREATE TABLE "MediaAssets" (
            "Id"          character varying(64)  NOT NULL PRIMARY KEY,
            "Format"      character varying(16)  NOT NULL DEFAULT 'pdf',
            "StoragePath" character varying(512) NOT NULL DEFAULT 'media/sample.pdf',
            "SizeBytes"   bigint                 NOT NULL DEFAULT 0,
            "Sha256"      character varying(64)  NULL
        );
        CREATE TABLE "ContentPaperAssets" (
            "Id"           character varying(64) NOT NULL PRIMARY KEY,
            "PaperId"      character varying(64) NOT NULL,
            "MediaAssetId" character varying(64) NOT NULL
        );
        CREATE TABLE "ContentPapers" (
            "Id"                character varying(64)    NOT NULL PRIMARY KEY,
            "ExtractedTextJson" text                     NOT NULL DEFAULT '{}',
            "RowVersion"        integer                  NOT NULL DEFAULT 0,
            "Status"            integer                  NOT NULL DEFAULT 0,
            "UpdatedAt"         timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE "CompanionSources" (
            "Id"        uuid NOT NULL PRIMARY KEY,
            "SourceKey" text NOT NULL,
            "Version"   text NOT NULL
        );
        CREATE TABLE "CompanionChunks" (
            "Id"               uuid    NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
            "SourceId"         uuid    NOT NULL,
            "Ordinal"          integer NOT NULL,
            "Heading"          text    NULL,
            "Text"             text    NOT NULL,
            "PageNumber"       integer NULL,
            "TimestampSeconds" integer NULL
        );
        """;

    public const string PdfParams =
        "{\"mode\":\"flat\",\"minTextLength\":50,\"provider\":\"auto\",\"includePages\":true,\"replaceExisting\":false,\"purpose\":\"apply\"}";

    private readonly ConcurrentDictionary<string, Guid> _instances = new(StringComparer.Ordinal);

    private RemotePgHarness(PostgreSqlTestDatabase database)
    {
        Database = database;
    }

    public PostgreSqlTestDatabase Database { get; }

    public FixedRemoteFlags Flags { get; } = new(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract);

    public RemoteJobsOptions Options { get; } = new();

    public InMemoryFileStorage Storage { get; } = new();

    public RemoteOrphanTracker Orphans { get; } = new();

    public RemoteAuthCache AuthCache { get; } = new(TimeProvider.System);

    public TimeProvider Time => TimeProvider.System;

    /// <summary>A fresh normalised snapshot of <see cref="Options"/> each time (tests may change an option before calling a service).</summary>
    public RemoteJobsSettings Settings => RemoteTestData.Settings(Options);

    public static async Task<RemotePgHarness> CreateAsync()
    {
        var database = await PostgreSqlTestDatabase.CreateAsync();
        try
        {
            await database.ExecuteAsync(SupportDdl);
            await database.ExecuteAsync(RemoteJobsSchemaSql.Up);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }

        return new RemotePgHarness(database);
    }

    public LearnerDbContext NewContext()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(Database.SchemaConnectionString, npgsql => npgsql.UseVector())
            .Options);

    public ValueTask DisposeAsync() => Database.DisposeAsync();

    // ── services (built the way DI builds them, one set per context) ─────────

    public RemoteJobQueue Queue(LearnerDbContext db) => new(db, Settings, Time);

    public RemoteClaimService Claims(LearnerDbContext db) => new(db, Flags, Settings, Time);

    public RemoteJobLifecycleService Lifecycle(LearnerDbContext db) => new(db, Settings, Time);

    public IRemoteKindHandler[] DefaultHandlers()
        => new IRemoteKindHandler[]
        {
            new PdfExtractKindHandler(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()), NullLogger<PdfExtractKindHandler>.Instance),
            new CompanionIndexPrepKindHandler(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base())),
        };

    public RemoteCompletionService Completion(LearnerDbContext db, IEnumerable<IRemoteKindHandler>? handlers = null)
        => new(db, handlers ?? DefaultHandlers(), Flags, Lifecycle(db), Settings, Time, NullLogger<RemoteCompletionService>.Instance);

    public RemoteJobSweeper Sweeper(LearnerDbContext db)
        => new(db, Flags, Storage, Settings, Time, NullLogger<RemoteJobSweeper>.Instance);

    public RemoteWorkerService Nodes(LearnerDbContext db)
        => new(db, Settings, Flags, Queue(db), Orphans, AuthCache, Time, NullLogger<RemoteWorkerService>.Instance);

    public RemoteInputOutputService IO(LearnerDbContext db, IFileStorage? storage = null)
        => new(db, Lifecycle(db), storage ?? Storage, NullLogger<RemoteInputOutputService>.Instance);

    public RemoteFleetJobsService FleetJobs(LearnerDbContext db) => new(db, Flags, Settings, Time);

    // ── raw SQL helpers ──────────────────────────────────────────────────────

    public async Task<int> SqlAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Database.Command(sql);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Database.Command(sql);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task<string?> StateOfAsync(string jobId)
        => ScalarAsync<string>("""SELECT "State" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", jobId));

    public Task<int> CountAsync(string sql, params (string Name, object? Value)[] parameters)
        => ScalarAsync<int>(sql, parameters);

    // ── nodes ────────────────────────────────────────────────────────────────

    /// <summary>Inserts a node that offers <c>pdf.extract</c> at the current engine version and has just heartbeated.</summary>
    public async Task<string> AddNodeAsync(
        string status = RemoteNodeStatus.Active,
        string[]? allowedKinds = null,
        int maxConcurrency = 2,
        bool freshHeartbeat = true,
        string? currentInstance = null,
        bool paused = false,
        string? nodeRef = null)
    {
        var id = RemoteIds.NewNodeId(DateTimeOffset.UtcNow);
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.PdfExtract, Options)!;
        var kindsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object> { ["kind"] = RemoteJobKinds.PdfExtract, ["schemaVersions"] = new[] { 1 }, ["engineVersion"] = engine },
        });

        await using var command = Database.Command(
            """
            INSERT INTO "RemoteWorkers"
                ("Id", "NodeRef", "DisplayName", "Status", "StatusChangedAt", "AllowedKinds", "MaxConcurrency", "Paused", "KindsJson",
                 "LastCapacityJson", "CurrentInstanceId", "LastHeartbeatAt", "CreatedAt", "UpdatedAt", "CreatedBy")
            VALUES
                (@id, @ref, 'Test node', @status, clock_timestamp(), @kinds, @max, @paused, @kindsJson,
                 '{"effectiveConcurrency": 2}'::jsonb, @instance,
                 CASE WHEN @fresh THEN clock_timestamp() ELSE clock_timestamp() - interval '10 minutes' END,
                 clock_timestamp(), clock_timestamp(), 'test');
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("ref", nodeRef ?? "node-" + id[3..13]);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("kinds", allowedKinds ?? new[] { RemoteJobKinds.PdfExtract });
        command.Parameters.AddWithValue("max", maxConcurrency);
        command.Parameters.AddWithValue("paused", paused);
        command.Parameters.Add(new NpgsqlParameter("kindsJson", NpgsqlDbType.Jsonb) { Value = kindsJson });
        command.Parameters.Add(new NpgsqlParameter("instance", NpgsqlDbType.Varchar) { Value = (object?)currentInstance ?? DBNull.Value });
        command.Parameters.AddWithValue("fresh", freshHeartbeat);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<RemoteWorker> NodeAsync(string nodeId)
    {
        await using var db = NewContext();
        return await db.RemoteWorkers.AsNoTracking().SingleAsync(w => w.Id == nodeId);
    }

    public Guid InstanceOf(string nodeId) => _instances.GetOrAdd(nodeId, _ => Guid.NewGuid());

    public RemoteClaimRequestDto ClaimRequest(
        string nodeId,
        Guid? claimId = null,
        Guid? instanceId = null,
        long heavySlots = 2,
        long effective = 2)
        => new()
        {
            ClaimId = claimId ?? Guid.NewGuid(),
            InstanceId = instanceId ?? InstanceOf(nodeId),
            AppliedRevision = 0,
            Kinds = new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract) },
            Capacity = RemoteTestData.Capacity(heavySlots: heavySlots, effective: effective),
            Agent = new RemoteAgentDto
            {
                Version = "1.0.0",
                ImageDigest = "sha256:" + new string('b', 64),
                Protocol = 1,
                ProtocolsSupported = new List<int> { 1 },
                StartedAt = DateTimeOffset.UtcNow,
            },
        };

    public async Task<RemoteClaimOutcome> ClaimAsync(string nodeId, Guid? claimId = null, Guid? instanceId = null)
    {
        await using var db = NewContext();
        var node = await db.RemoteWorkers.AsNoTracking().SingleAsync(w => w.Id == nodeId);
        return await Claims(db).ClaimAsync(node, ClaimRequest(nodeId, claimId, instanceId), CancellationToken.None);
    }

    /// <summary>Claims one job for the node and fails the test when nothing was leased.</summary>
    public async Task<RemoteJobRow> ClaimOneAsync(string nodeId)
    {
        var outcome = await ClaimAsync(nodeId);
        Assert.True(outcome.Leased is not null, $"expected a lease but got error {outcome.Error?.Code} / 204 {outcome.NoContentReason}");
        return outcome.Leased!.Job;
    }

    // ── jobs ─────────────────────────────────────────────────────────────────

    public static string ManifestJson(long size)
        => JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "pdf",
                ["sizeBytes"] = size,
                ["sha256"] = RemoteTestData.Sha,
                ["contentType"] = "application/pdf",
                ["storageKey"] = "media/sample.pdf",
            },
        });

    public async Task<RemoteEnqueueResult> EnqueueAsync(
        string resourceId = "media-1",
        string purpose = RemoteJobPurpose.Apply,
        string? sha = null,
        long size = 1234,
        int priority = 0,
        string? targetNode = null,
        bool force = false,
        string kind = RemoteJobKinds.PdfExtract,
        string? settingsHash = null,
        bool withFallback = true)
    {
        await using var db = NewContext();
        var spec = RemoteJobKinds.Find(kind)!;
        var request = new RemoteEnqueueRequest(
            kind,
            spec.SchemaVersion,
            purpose,
            "MediaAsset",
            resourceId,
            sha ?? RemoteTestData.Sha,
            RemoteJobKinds.EngineVersion(kind, Options)!,
            settingsHash ?? PdfExtractSettings.Hash("flat", "auto", 50, false),
            PdfParams,
            ManifestJson(size),
            RemoteJobKinds.LimitsFor(spec, purpose),
            "test",
            priority,
            targetNode,
            withFallback);
        return await Queue(db).EnqueueAsync(request, force, CancellationToken.None);
    }

    public async Task<RemoteJobRow> JobAsync(string jobId)
    {
        await using var db = NewContext();
        var row = await Queue(db).GetAsync(jobId, CancellationToken.None);
        Assert.NotNull(row);
        return row!;
    }

    public Task ExpireLeaseAsync(string jobId)
        => SqlAsync(
            """UPDATE "RemoteJobs" SET "LeaseExpiresAt" = clock_timestamp() - interval '2 seconds' WHERE "Id" = @id AND "State" = 'Leased';""",
            ("id", jobId));

    public Task MakeDueAsync(string jobId)
        => SqlAsync(
            """UPDATE "RemoteJobs" SET "NextAttemptAt" = clock_timestamp() - interval '1 second' WHERE "Id" = @id;""",
            ("id", jobId));

    /// <summary>Inserts a job that is already leased to the node (for routes that need a lease but not the claim path).</summary>
    public async Task<string> InsertLeasedJobAsync(string nodeId, string kind = RemoteJobKinds.MediaAudioExtract, long fence = 1)
    {
        var id = RemoteIds.NewJobId(DateTimeOffset.UtcNow);
        var spec = RemoteJobKinds.Find(kind)!;
        await using var command = Database.Command(
            """
            INSERT INTO "RemoteJobs"
                ("Id", "Kind", "SchemaVersion", "Purpose", "ResourceType", "ResourceId", "IdempotencyKey", "InputSha256", "EngineVersion",
                 "SettingsHash", "ParamsJson", "InputsJson", "LimitsJson", "Weight", "State", "Attempt", "MaxAttempts", "FenceToken",
                 "LeaseOwner", "LeaseExpiresAt", "DeadlineAt", "LeasedAt", "NextAttemptAt", "EnqueuedBy", "CreatedAt", "UpdatedAt")
            VALUES
                (@id, @kind, 1, 'apply', 'MediaAsset', @resource, @key, @sha, 'engine:1', @sha, '{}'::jsonb, '[]'::jsonb, @limits, @weight,
                 'Leased', 1, 3, @fence, @node, clock_timestamp() + interval '2 minutes', clock_timestamp() + interval '5 minutes',
                 clock_timestamp(), clock_timestamp(), 'test', clock_timestamp(), clock_timestamp());
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("resource", "res-" + id[3..9]);
        command.Parameters.AddWithValue("key", "key-" + id);
        command.Parameters.AddWithValue("sha", RemoteTestData.Sha);
        command.Parameters.Add(new NpgsqlParameter("limits", NpgsqlDbType.Jsonb) { Value = spec.Limits.ToJson() });
        command.Parameters.AddWithValue("weight", (short)spec.Limits.Weight);
        command.Parameters.AddWithValue("fence", fence);
        command.Parameters.AddWithValue("node", nodeId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    // ── content domain (the slice the pdf.extract applier reads and writes) ──

    public async Task SeedPdfAsync(
        string mediaId = "media-1",
        string? sha = null,
        long size = 1234,
        string paperId = "paper-1",
        string assetId = "asset-1",
        string extractedJson = "{}",
        string format = "pdf")
    {
        await SqlAsync(
            """INSERT INTO "MediaAssets" ("Id", "Format", "SizeBytes", "Sha256") VALUES (@m, @format, @size, @sha) ON CONFLICT ("Id") DO NOTHING;""",
            ("m", mediaId), ("format", format), ("size", size), ("sha", sha ?? RemoteTestData.Sha));
        await SqlAsync(
            """INSERT INTO "ContentPapers" ("Id", "ExtractedTextJson", "RowVersion", "UpdatedAt") VALUES (@p, @json, 3, now() - interval '3 days') ON CONFLICT ("Id") DO NOTHING;""",
            ("p", paperId), ("json", extractedJson));
        await SqlAsync(
            """INSERT INTO "ContentPaperAssets" ("Id", "PaperId", "MediaAssetId") VALUES (@a, @p, @m) ON CONFLICT ("Id") DO NOTHING;""",
            ("a", assetId), ("p", paperId), ("m", mediaId));
    }

    public Task<string?> PaperJsonAsync(string paperId = "paper-1")
        => ScalarAsync<string>("""SELECT "ExtractedTextJson" FROM "ContentPapers" WHERE "Id" = @p;""", ("p", paperId));

    public Task<int> PaperVersionAsync(string paperId = "paper-1")
        => ScalarAsync<int>("""SELECT "RowVersion" FROM "ContentPapers" WHERE "Id" = @p;""", ("p", paperId));

    // ── result bodies ────────────────────────────────────────────────────────

    public static readonly string[] SamplePages =
    [
        "Remote page one carries a comfortable amount of ordinary extracted text for the paper.",
        "Remote page two carries a little more extracted text so the total clears the threshold.",
    ];

    public static string PdfResultJson(
        RemoteJobRow job,
        IReadOnlyList<string>? pages = null,
        bool includePages = true,
        Action<Dictionary<string, object?>>? tamper = null)
    {
        var list = pages ?? SamplePages;
        var flat = string.Join("\n\n", list).Trim();
        var pageHashes = list.Select(page => RemoteIds.Sha256Hex(page)).ToArray();
        var body = new Dictionary<string, object?>
        {
            ["schema"] = "pdf.extract.result/" + job.SchemaVersion,
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["mode"] = "flat",
            ["needsOcr"] = false,
            ["needsOcrReason"] = null,
            ["pageCount"] = list.Count,
            ["embeddedChars"] = flat.Length,
            ["textSha256"] = RemoteIds.Sha256Hex(flat),
            ["pagesSha256"] = RemoteIds.Sha256Hex(string.Join("\n", pageHashes)),
            ["pageSha256s"] = pageHashes,
            ["pages"] = includePages ? list.ToArray() : null,
        };
        tamper?.Invoke(body);
        return JsonSerializer.Serialize(body);
    }

    public static byte[] CompleteBody(long fence, string resultJson, string? sha = null)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            fence,
            resultSha256 = sha ?? RemoteIds.Sha256Hex(resultJson),
            resultJson,
        });

    public async Task<RemoteCompleteOutcome> CompleteAsync(
        string nodeId,
        RemoteJobRow job,
        byte[] body,
        IEnumerable<IRemoteKindHandler>? handlers = null)
    {
        await using var db = NewContext();
        var node = await db.RemoteWorkers.AsNoTracking().SingleAsync(w => w.Id == nodeId);
        return await Completion(db, handlers).CompleteAsync(node, job.Id, body, CancellationToken.None);
    }

    public async Task<int> AuditCountAsync(string action)
        => await ScalarAsync<int>("""SELECT COUNT(*)::int FROM "AuditEvents" WHERE "Action" = @a;""", ("a", action));
}
