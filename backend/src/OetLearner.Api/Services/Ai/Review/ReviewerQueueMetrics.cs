using System.Collections.Concurrent;

namespace OetLearner.Api.Services.Ai.Review;

/// <summary>
/// In-process counters/gauges for the shared reviewer queue, split by assessment type ("writing"/"speaking").
/// Read through <see cref="Snapshot"/>; the gate and the runner update it. In-process; the gateway's own
/// <c>AiUsageRecord</c> rows already carry the provider/model per physical call, so this layer stays a
/// low-cardinality aggregate surfaced through structured logs.
/// </summary>
public sealed class ReviewerQueueMetrics
{
    public static ReviewerQueueMetrics Instance { get; } = new();

    private readonly ConcurrentDictionary<string, TypeMetrics> _types = new(StringComparer.OrdinalIgnoreCase);

    private TypeMetrics For(string type) => _types.GetOrAdd(type, static t => new TypeMetrics(t));

    public void JobArrived(string type) => For(type).Arrived.Increment();
    public void JobQueued(string type)
    {
        var m = For(type);
        m.Queued.Increment();
        m.Waiting.Increment();
    }

    public void JobStarted(string type, TimeSpan queueWait)
    {
        var m = For(type);
        m.Waiting.Increment(-1);
        m.WaitingStarted.Increment();
        m.QueueWaitMsSum.Add((long)queueWait.TotalMilliseconds);
        m.InFlight.Increment();
    }
    public void JobExpired(string type)
    {
        var m = For(type);
        m.Waiting.Increment(-1);
        m.GateTimeouts.Increment();
    }
    public void JobFinished(string type) => For(type).InFlight.Increment(-1);
    public void CodexOutcome(string type, string outcome)
    {
        var m = For(type);
        m.CodexTotal.Increment();
        switch (outcome)
        {
            case "success": m.CodexSuccess.Increment(); break;
            case "quota": m.CodexQuota.Increment(); break;
            case "timeout": m.CodexTimeout.Increment(); break;
            case "unavailable": m.CodexUnavailable.Increment(); break;
            default: m.CodexOtherFailure.Increment(); break;
        }
    }
    public void ApiFallbackStarted(string type, string reason)
    {
        var m = For(type);
        m.ApiFallbacks.Increment();
        m.LastFallbackReason = reason;
    }
    public void ReviewCompleted(string type, bool usedApiFallback, long totalMs)
    {
        var m = For(type);
        m.TotalCompleted.Increment();
        if (usedApiFallback) m.FallbackCompleted.Increment();
        m.TotalDurationMsSum.Add(totalMs);
    }

    public ReviewerQueueSnapshot Snapshot()
    {
        var items = new List<ReviewerQueueSnapshotItem>();
        foreach (var (type, m) in _types)
        {
            items.Add(new ReviewerQueueSnapshotItem(
                type,
                m.Arrived.Value,
                m.Queued.Value,
                Math.Max(0, m.Waiting.Value),
                Math.Max(0, m.InFlight.Value),
                m.GateTimeouts.Value,
                m.CodexTotal.Value,
                m.CodexSuccess.Value,
                m.CodexQuota.Value,
                m.CodexTimeout.Value,
                m.CodexUnavailable.Value,
                m.CodexOtherFailure.Value,
                m.ApiFallbacks.Value,
                m.TotalCompleted.Value,
                m.FallbackCompleted.Value,
                m.QueueWaitMsSum.Value / Math.Max(1, m.WaitingStarted.Value),
                m.TotalDurationMsSum.Value / Math.Max(1, m.TotalCompleted.Value),
                m.LastFallbackReason));
        }

        return new ReviewerQueueSnapshot(items.OrderBy(i => i.AssessmentType, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private sealed class TypeMetrics
    {
        public TypeMetrics(string type) => AssessmentType = type;

        public string AssessmentType { get; }
        public Counter Arrived { get; } = new();
        public Counter Queued { get; } = new();
        public Counter Waiting { get; } = new();
        public Counter WaitingStarted { get; } = new();
        public Counter InFlight { get; } = new();
        public Counter GateTimeouts { get; } = new();
        public Counter CodexTotal { get; } = new();
        public Counter CodexSuccess { get; } = new();
        public Counter CodexQuota { get; } = new();
        public Counter CodexTimeout { get; } = new();
        public Counter CodexUnavailable { get; } = new();
        public Counter CodexOtherFailure { get; } = new();
        public Counter ApiFallbacks { get; } = new();
        public Counter TotalCompleted { get; } = new();
        public Counter FallbackCompleted { get; } = new();
        public Counter QueueWaitMsSum { get; } = new();
        public Counter TotalDurationMsSum { get; } = new();
        public volatile string? LastFallbackReason;
    }

    private sealed class Counter
    {
        private long _value;
        public long Value => Interlocked.Read(ref _value);
        public void Increment(long delta = 1) => Interlocked.Add(ref _value, delta);
        public void Add(long value) => Interlocked.Add(ref _value, value);
    }
}

public sealed record ReviewerQueueSnapshotItem(
    string AssessmentType,
    long Arrived,
    long Queued,
    long Waiting,
    long InFlight,
    long GateTimeouts,
    long CodexTotal,
    long CodexSuccess,
    long CodexQuota,
    long CodexTimeout,
    long CodexUnavailable,
    long CodexOtherFailure,
    long ApiFallbacks,
    long TotalCompleted,
    long FallbackCompleted,
    long AvgQueueWaitMs,
    long AvgTotalDurationMs,
    string? LastFallbackReason);

public sealed record ReviewerQueueSnapshot(IReadOnlyList<ReviewerQueueSnapshotItem> Items);

