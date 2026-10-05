namespace Fleet.Agent;

/// <summary>
/// Content-addressed cache of reusable immutable inputs on the scratch tmpfs. A second job over the same PDF (a retry after
/// a lost completion, a shadow plus an apply job, pdf.extract plus companion.index-prep) skips the download.
///
/// Hygiene (D2, H2): the cache lives on tmpfs, is wiped at start, is size-capped, entries expire (TTL), and entries are only
/// ever served for a sha256 the API itself put in the claimed job's manifest, so the per-job access rule is unchanged.
/// </summary>
internal sealed class InputCache
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly long _maxEntryBytes;
    private readonly TimeSpan _ttl;
    private readonly IMonotonicClock _clock;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public required string Path { get; init; }
        public required long Size { get; init; }
        public long LastUsedMs { get; set; }
        public long ExpiresMs { get; set; }
        public int Pins { get; set; }
    }

    public InputCache(string directory, long maxBytes, long maxEntryBytes, TimeSpan ttl, IMonotonicClock clock)
    {
        _directory = directory;
        _maxBytes = Math.Max(0, maxBytes);
        _maxEntryBytes = Math.Max(0, maxEntryBytes);
        _ttl = ttl;
        _clock = clock;
    }

    public bool Enabled => _maxBytes > 0 && _ttl > TimeSpan.Zero;

    public long TotalBytes
    {
        get { lock (_gate) return _entries.Values.Sum(e => e.Size); }
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>Pins and returns the cached file when present, unexpired and of the exact manifest size.</summary>
    public CachedInput? TryAcquire(string sha256, long expectedSize)
    {
        if (!Enabled) return null;
        lock (_gate)
        {
            EvictExpiredLocked();
            if (!_entries.TryGetValue(sha256, out var entry)) return null;
            if (entry.Size != expectedSize || !File.Exists(entry.Path) || new FileInfo(entry.Path).Length != expectedSize)
            {
                RemoveLocked(sha256, entry);
                return null;
            }

            entry.Pins++;
            entry.LastUsedMs = _clock.NowMs;
            return new CachedInput(this, sha256, entry.Path);
        }
    }

    /// <summary>
    /// Adopts a verified input file by renaming it into the cache (same tmpfs, no copy). Returns false and leaves the file
    /// where it is when the entry is not cacheable (too large, cache full of pinned entries, disabled).
    /// </summary>
    public bool TryAdopt(string sha256, string verifiedFile, long size)
    {
        if (!Enabled || size <= 0 || size > _maxEntryBytes || size > _maxBytes) return false;
        lock (_gate)
        {
            EvictExpiredLocked();
            if (_entries.ContainsKey(sha256)) return false;
            MakeRoomLocked(size);
            if (_entries.Values.Sum(e => e.Size) + size > _maxBytes) return false;

            Directory.CreateDirectory(_directory);
            var target = Path.Combine(_directory, sha256);
            try
            {
                File.Move(verifiedFile, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            var now = _clock.NowMs;
            _entries[sha256] = new Entry { Path = target, Size = size, LastUsedMs = now, ExpiresMs = now + (long)_ttl.TotalMilliseconds };
            return true;
        }
    }

    /// <summary>Drop every unpinned entry (state change to quarantined, auth failure, shutdown).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var pair in _entries.ToArray())
            {
                if (pair.Value.Pins == 0) RemoveLocked(pair.Key, pair.Value);
            }
        }
    }

    internal void Release(string sha256)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(sha256, out var entry) && entry.Pins > 0) entry.Pins--;
        }
    }

    private void MakeRoomLocked(long needed)
    {
        var total = _entries.Values.Sum(e => e.Size);
        if (total + needed <= _maxBytes) return;
        foreach (var pair in _entries.Where(p => p.Value.Pins == 0).OrderBy(p => p.Value.LastUsedMs).ToArray())
        {
            RemoveLocked(pair.Key, pair.Value);
            total -= pair.Value.Size;
            if (total + needed <= _maxBytes) return;
        }
    }

    private void EvictExpiredLocked()
    {
        var now = _clock.NowMs;
        foreach (var pair in _entries.Where(p => p.Value.Pins == 0 && p.Value.ExpiresMs <= now).ToArray())
        {
            RemoveLocked(pair.Key, pair.Value);
        }
    }

    private void RemoveLocked(string sha256, Entry entry)
    {
        _entries.Remove(sha256);
        try
        {
            File.Delete(entry.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The tmpfs is wiped at the next start anyway.
        }
    }
}

/// <summary>A pinned cache entry; dispose to unpin so eviction may remove it.</summary>
internal sealed class CachedInput : IDisposable
{
    private readonly InputCache _cache;
    private readonly string _sha256;
    private int _released;

    internal CachedInput(InputCache cache, string sha256, string path)
    {
        _cache = cache;
        _sha256 = sha256;
        Path = path;
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) _cache.Release(_sha256);
    }
}
