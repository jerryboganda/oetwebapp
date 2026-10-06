namespace Fleet.Agent;

/// <summary>
/// Protocol number negotiation for rollback skew (protocol 2.4, RW-012). The agent speaks the set S and sends the highest
/// member it believes the API accepts. A 426 carries the API's supported list: a non-empty intersection downgrades (and the
/// caller retries once), an empty one is a mismatch that parks the agent in ProtocolMismatch.
/// </summary>
internal sealed class ProtocolNegotiator
{
    private readonly int[] _supported;
    private int _current;
    private int _probeIndex;

    public ProtocolNegotiator(IReadOnlyList<int>? supported = null)
    {
        _supported = (supported ?? Wire.SupportedProtocols).Distinct().OrderBy(n => n).ToArray();
        if (_supported.Length == 0) throw new ArgumentException("at least one protocol number is required");
        _current = _supported[^1];
    }

    public int Current => Volatile.Read(ref _current);

    public IReadOnlyList<int> Supported => _supported;

    /// <summary>
    /// Apply a 426 answer. Returns true when the agent moved to a different number and the request should be retried once.
    /// </summary>
    public bool TryDowngrade(int[]? apiSupported)
    {
        if (apiSupported is null || apiSupported.Length == 0) return false;
        var common = _supported.Intersect(apiSupported).ToArray();
        if (common.Length == 0) return false;
        var best = common.Max();
        if (best == Current) return false;
        Volatile.Write(ref _current, best);
        return true;
    }

    /// <summary>ProtocolMismatch probes cycle through every number this build supports (section 5.2).</summary>
    public int NextProbe()
    {
        var index = Interlocked.Increment(ref _probeIndex) - 1;
        var number = _supported[index % _supported.Length];
        Volatile.Write(ref _current, number);
        return number;
    }

    public void Reset() => Volatile.Write(ref _current, _supported[^1]);
}
