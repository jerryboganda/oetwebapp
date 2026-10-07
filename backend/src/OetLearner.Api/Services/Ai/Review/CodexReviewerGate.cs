namespace OetLearner.Api.Services.Ai.Review;

/// <summary>Raised when a reviewer job cannot get a Codex slot within the shared queue policy.</summary>
public sealed class ReviewerCapacitySaturatedException(string message) : Exception(message);

/// <summary>
/// One gate slot; disposed when the provider call returns.
/// </summary>
public sealed class CodexReviewerPermit : IDisposable
{
    private readonly CodexReviewerGate _gate;
    private readonly string _assessmentType;
    private int _released;

    internal CodexReviewerPermit(CodexReviewerGate gate, string assessmentType)
    {
        _gate = gate;
        _assessmentType = assessmentType;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) _gate.Release(_assessmentType);
    }
}

/// <summary>
/// The single coordinated capacity limit for GPT-6.1 Sol / Codex review work across BOTH Writing and Speaking.
/// A FIFO waiter queue (arrival order, regardless of assessment type) so neither type can permanently starve
/// the other; at most <see cref="Capacity"/> in-flight Codex reviews at once; every wait is bounded by
/// <see cref="MaxQueueWaitSeconds"/> so a saturated or wedged lane falls back to the API provider instead of piling up.
/// In-process singleton (<see cref="Default"/>): the worker container runs Speaking reviews and the API slots
/// can run detached Writing reviews, so the global bound is capacity × active processes; the durable circuit
/// breaker and the sidecar's own serial lane are the cross-process backstop.
/// </summary>
public sealed class CodexReviewerGate
{
    public static CodexReviewerGate Default { get; } = new(capacity: 2, maxQueueWaitSeconds: 45);

    private readonly object _lock = new();
    private readonly Queue<Waiter> _waiters = new();
    private readonly ReviewerQueueMetrics _metrics;
    private int _capacity;
    private TimeSpan _maxQueueWait;
    private int _inFlight;

    public CodexReviewerGate(int capacity, int maxQueueWaitSeconds, ReviewerQueueMetrics? metrics = null)
    {
        _capacity = Math.Max(1, capacity);
        _maxQueueWait = TimeSpan.FromSeconds(Math.Max(1, maxQueueWaitSeconds));
        _metrics = metrics ?? ReviewerQueueMetrics.Instance;
    }

    public int Capacity => _capacity;

    public TimeSpan MaxQueueWait => _maxQueueWait;

    public int MaxQueueWaitSeconds => (int)_maxQueueWait.TotalSeconds;

    /// <summary>Atomically updates capacity and queue-wait (startup configuration; not a hot path).</summary>
    public void Configure(int capacity, int maxQueueWaitSeconds)
    {
        lock (_lock)
        {
            _capacity = Math.Max(1, capacity);
            _maxQueueWait = TimeSpan.FromSeconds(Math.Max(1, maxQueueWaitSeconds));
        }
    }

    /// <summary>Wait for a slot, FIFO across Writing and Speaking. Throws
    /// <see cref="ReviewerCapacitySaturatedException"/> after <see cref="MaxQueueWaitSeconds"/>.</summary>
    public async Task<CodexReviewerPermit> WaitAsync(string assessmentType, string assessmentId, CancellationToken ct)
    {
        var waiter = new Waiter(assessmentType, assessmentId);
        var acquiredAt = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            _metrics.JobArrived(assessmentType);
            if (_inFlight < _capacity)
            {
                _inFlight++;
                waiter.Granted = true;
            }
            else
            {
                _waiters.Enqueue(waiter);
                _metrics.JobQueued(assessmentType);
            }
        }

        if (waiter.Granted)
        {
            _metrics.JobStarted(assessmentType, TimeSpan.Zero);
            return new CodexReviewerPermit(this, assessmentType);
        }

        using var timeout = new CancellationTokenSource(_maxQueueWait);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await waiter.Tcs.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock)
            {
                if (waiter.Granted)
                {
                    // The slot was transferred to us concurrently with our timeout/cancellation: hand it back.
                    // JobStarted never ran for this waiter, so its in-flight gauge was never incremented.
                    ReleaseLocked(assessmentType, countFinished: false);
                }
                else
                {
                    waiter.Cancelled = true;
                    _metrics.JobExpired(assessmentType);
                }
            }

            if (timeout.IsCancellationRequested)
            {
                throw new ReviewerCapacitySaturatedException(
                    $"No Codex review slot for {assessmentType} job {assessmentId} within {_maxQueueWait.TotalSeconds:0}s; API fallback applies.");
            }

            throw;
        }

        var queueWait = DateTimeOffset.UtcNow - acquiredAt;
        _metrics.JobStarted(assessmentType, queueWait);
        return new CodexReviewerPermit(this, assessmentType);
    }

    internal void Release(string assessmentType)
    {
        lock (_lock) ReleaseLocked(assessmentType);
    }

    private void ReleaseLocked(string assessmentType, bool countFinished = true)
    {
        // Transfer the slot to the next live waiter (FIFO), or free it.
        Waiter? next = null;
        while (_waiters.Count > 0)
        {
            var candidate = _waiters.Dequeue();
            if (candidate.Cancelled) continue;
            next = candidate;
            break;
        }

        if (next is null)
        {
            _inFlight--;
        }
        else
        {
            next.Granted = true;
            next.Tcs.TrySetResult(true);
        }

        if (countFinished) _metrics.JobFinished(assessmentType);
    }

    private sealed class Waiter
    {
        public Waiter(string assessmentType, string assessmentId)
        {
            AssessmentType = assessmentType;
            AssessmentId = assessmentId;
        }

        public string AssessmentType { get; }
        public string AssessmentId { get; }
        public TaskCompletionSource<bool> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Granted { get; set; }
        public bool Cancelled { get; set; }
    }
}

