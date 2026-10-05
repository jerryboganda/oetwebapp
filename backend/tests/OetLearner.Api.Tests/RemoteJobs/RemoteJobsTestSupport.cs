using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>A clock tests can move by hand.</summary>
internal sealed class MutableClock : TimeProvider
{
    private DateTimeOffset _now;

    public MutableClock(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    private readonly T _value;

    public StaticOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Feature flags fixed by the test (an absent key is OFF, exactly like production).</summary>
internal sealed class FixedRemoteFlags : IRemoteJobFlags
{
    public FixedRemoteFlags(params string[] enabledKeys)
    {
        Snapshot = Build(enabledKeys);
    }

    public RemoteFlagSnapshot Snapshot { get; private set; }

    public void Set(params string[] enabledKeys) => Snapshot = Build(enabledKeys);

    public Task<RemoteFlagSnapshot> GetAsync(CancellationToken ct) => Task.FromResult(Snapshot);

    public void Invalidate()
    {
    }

    private static RemoteFlagSnapshot Build(string[] keys)
        => new(new HashSet<string>(keys, StringComparer.Ordinal));
}

internal sealed class FixedHeadroom : IPrimaryHeadroom
{
    public bool Value { get; set; }

    public bool HasHeadroom() => Value;
}

internal static class RemoteTestData
{
    public static readonly string Sha = new('a', 64);

    public static RemoteJobsSettings Settings(RemoteJobsOptions? options = null)
        => new(new StaticOptionsMonitor<RemoteJobsOptions>(options ?? new RemoteJobsOptions()));

    public static string Engine(RemoteJobsOptions? options = null)
        => RemoteJobKinds.EngineVersion(RemoteJobKinds.PdfExtract, options ?? new RemoteJobsOptions())!;

    /// <summary>A job row object for the pure validators and classifiers (not stored anywhere).</summary>
    public static RemoteJobRow Row(
        string state = RemoteJobState.Queued,
        string kind = RemoteJobKinds.PdfExtract,
        string purpose = RemoteJobPurpose.Apply,
        string? leaseOwner = null,
        long fence = 0,
        string? engine = null,
        string? inputSha = null,
        string paramsJson = "{\"mode\":\"flat\",\"minTextLength\":50,\"includePages\":true,\"replaceExisting\":false}",
        string inputsJson = "[]",
        string settingsHash = "",
        long? settledFence = null,
        string? settledBy = null,
        string? settledCode = null,
        string? resultSha = null,
        string? applyOutcome = null,
        string? failureCode = null,
        string? targetNode = null)
        => new()
        {
            Id = RemoteIds.NewJobId(DateTimeOffset.UtcNow),
            Kind = kind,
            SchemaVersion = 1,
            Purpose = purpose,
            ResourceType = "MediaAsset",
            ResourceId = "media-1",
            IdempotencyKey = "key-" + Guid.NewGuid().ToString("N"),
            InputSha256 = inputSha ?? Sha,
            EngineVersion = engine ?? Engine(),
            SettingsHash = settingsHash.Length == 0 ? Sha : settingsHash,
            ParamsJson = paramsJson,
            InputsJson = inputsJson,
            State = state,
            LeaseOwner = leaseOwner,
            FenceToken = fence,
            SettledFence = settledFence,
            SettledBy = settledBy,
            SettledCode = settledCode,
            ResultSha256 = resultSha,
            ApplyOutcome = applyOutcome,
            FailureCode = failureCode,
            TargetNodeId = targetNode,
            EnqueuedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
        };

    public static RemoteKindOfferDto Offer(string kind, string? engine = null, params int[] schemas)
        => new()
        {
            Kind = kind,
            SchemaVersions = schemas.Length == 0 ? new List<int> { 1 } : schemas.ToList(),
            EngineVersion = engine ?? RemoteJobKinds.EngineVersion(kind, new RemoteJobsOptions()),
        };

    public static RemoteCapacityDto Capacity(
        long cpu = 3000,
        long mem = 5120,
        long tmp = 3072,
        long heavySlots = 2,
        long effective = 2)
        => new()
        {
            CpuBudgetFreeMilli = cpu,
            MemBudgetFreeMiB = mem,
            TmpFreeMiB = tmp,
            HeavySlotsFree = heavySlots,
            EffectiveConcurrency = effective,
        };
}
