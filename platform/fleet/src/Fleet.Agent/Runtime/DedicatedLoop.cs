namespace Fleet.Agent;

/// <summary>
/// A periodic loop that owns a dedicated OS thread (never the thread pool). Heartbeats MUST keep their cadence while a
/// CPU-bound job saturates the pool (protocol 4.2.3 rule 1, RW-053), so they run here and block on their own thread.
/// </summary>
internal sealed class DedicatedLoop : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<TimeSpan> _tick;
    private readonly ILogger _log;
    private readonly string _name;
    private int _started;

    /// <param name="name">Thread name.</param>
    /// <param name="tick">One iteration; returns how long to sleep before the next one.</param>
    public DedicatedLoop(string name, Func<TimeSpan> tick, ILogger log, ThreadPriority priority = ThreadPriority.Normal)
    {
        _name = name;
        _tick = tick;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = name, Priority = priority };
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) _thread.Start();
    }

    /// <summary>Run the next iteration as soon as possible.</summary>
    public void Poke() => _wake.Set();

    public void Stop(TimeSpan join)
    {
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        _wake.Set();
        if (Volatile.Read(ref _started) == 1 && _thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join(join);
        }
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            // Reset BEFORE the tick: a Poke that arrives while the tick runs stays set and cuts the next sleep short.
            _wake.Reset();
            TimeSpan delay;
            try
            {
                delay = _tick();
            }
            catch (Exception ex)
            {
                _log.LogError("loop {Loop} iteration failed: {Error}", _name, Redact.Exception(ex));
                delay = TimeSpan.FromSeconds(1);
            }

            if (delay < TimeSpan.FromMilliseconds(10)) delay = TimeSpan.FromMilliseconds(10);
            try
            {
                if (!_wake.IsSet) _wake.Wait(delay, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        Stop(TimeSpan.FromSeconds(2));
        _stop.Dispose();
        _wake.Dispose();
    }
}
