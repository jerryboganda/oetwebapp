using System.Collections.Concurrent;

namespace OetLearner.Api.Services.Conversation;

public sealed class ConversationRealtimeTurnStore
{
    private readonly ConcurrentDictionary<string, RealtimeTurnBuffer> _buffers = new(StringComparer.Ordinal);

    public bool TryBegin(
        string connectionId,
        string userId,
        string sessionId,
        string streamId,
        string audioMimeType,
        string locale,
        int maxConcurrentStreamsPerUser,
        TimeSpan idleTimeout,
        TimeSpan maxStreamAge,
        out string? errorCode)
    {
        errorCode = null;
        SweepExpired(idleTimeout, maxStreamAge);
        if (string.IsNullOrWhiteSpace(streamId) || streamId.Length > 96)
        {
            errorCode = "INVALID_STREAM";
            return false;
        }

        if (_buffers.Count >= ConversationRealtimeTransportLimits.MaximumActiveStreams)
        {
            errorCode = "REALTIME_GLOBAL_CONCURRENCY";
            return false;
        }

        var bufferedBytes = _buffers.Values.Sum(static buffer => buffer.TotalBytes);
        if (bufferedBytes >= ConversationRealtimeTransportLimits.MaximumBufferedBytes)
        {
            errorCode = "REALTIME_GLOBAL_SIZE";
            return false;
        }

        var activeForUser = _buffers.Values.Count(buffer => string.Equals(buffer.UserId, userId, StringComparison.Ordinal));
        if (activeForUser >= Math.Max(1, maxConcurrentStreamsPerUser))
        {
            errorCode = "REALTIME_CONCURRENCY";
            return false;
        }

        var key = Key(connectionId, sessionId, streamId);
        var buffer = new RealtimeTurnBuffer(connectionId, userId, sessionId, streamId, audioMimeType, locale);
        if (!_buffers.TryAdd(key, buffer))
        {
            errorCode = "STREAM_EXISTS";
            return false;
        }

        return true;
    }

    public bool TryValidateStream(
        string connectionId,
        string sessionId,
        string streamId,
        TimeSpan idleTimeout,
        TimeSpan maxStreamAge,
        out string? errorCode)
    {
        errorCode = null;
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer))
        {
            errorCode = "STREAM_NOT_FOUND";
            return false;
        }

        lock (buffer.SyncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            if (idleTimeout <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout)
            {
                _buffers.TryRemove(key, out _);
                errorCode = "STREAM_IDLE_TIMEOUT";
                return false;
            }

            if (maxStreamAge <= TimeSpan.Zero || now - buffer.StartedAt > maxStreamAge)
            {
                _buffers.TryRemove(key, out _);
                errorCode = "STREAM_DURATION";
                return false;
            }
        }

        return true;
    }

    public bool TryAppend(
        string connectionId,
        string sessionId,
        string streamId,
        int sequence,
        byte[] audioBytes,
        long maxTotalBytes,
        TimeSpan idleTimeout,
        TimeSpan maxStreamAge,
        TimeSpan partialMinInterval,
        out RealtimeTurnAppendResult? result,
        out string? errorCode)
    {
        result = null;
        errorCode = null;
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer))
        {
            errorCode = "STREAM_NOT_FOUND";
            return false;
        }

        lock (buffer.SyncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            if (idleTimeout <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout)
            {
                _buffers.TryRemove(key, out _);
                errorCode = "STREAM_IDLE_TIMEOUT";
                return false;
            }

            if (maxStreamAge <= TimeSpan.Zero || now - buffer.StartedAt > maxStreamAge)
            {
                _buffers.TryRemove(key, out _);
                errorCode = "STREAM_DURATION";
                return false;
            }

            if (sequence <= buffer.LastSequence)
            {
                errorCode = "STREAM_SEQUENCE";
                return false;
            }

            var nextTotalBytes = buffer.TotalBytes + audioBytes.LongLength;
            if (maxTotalBytes <= 0 || nextTotalBytes > maxTotalBytes)
            {
                _buffers.TryRemove(key, out _);
                errorCode = "STREAM_SIZE";
                return false;
            }

            buffer.LastSequence = sequence;
            buffer.Chunks.Add(audioBytes);
            buffer.TotalBytes = nextTotalBytes;
            buffer.UpdatedAt = now;
            var shouldEmitPartial = partialMinInterval <= TimeSpan.Zero || buffer.LastPartialEmittedAt is null || now - buffer.LastPartialEmittedAt >= partialMinInterval;
            if (shouldEmitPartial) buffer.LastPartialEmittedAt = now;
            result = new RealtimeTurnAppendResult(buffer.TotalBytes, buffer.LastSequence, shouldEmitPartial);
            return true;
        }
    }

    public bool TryComplete(
        string connectionId,
        string sessionId,
        string streamId,
        TimeSpan idleTimeout,
        TimeSpan maxStreamAge,
        out RealtimeTurnSnapshot? snapshot)
    {
        snapshot = null;
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer)) return false;

        lock (buffer.SyncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            if (idleTimeout <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout)
            {
                _buffers.TryRemove(key, out _);
                return false;
            }

            if (maxStreamAge <= TimeSpan.Zero || now - buffer.StartedAt > maxStreamAge)
            {
                _buffers.TryRemove(key, out _);
                return false;
            }

            using var ms = new MemoryStream();
            foreach (var chunk in buffer.Chunks)
            {
                ms.Write(chunk, 0, chunk.Length);
            }

            snapshot = new RealtimeTurnSnapshot(
                buffer.ConnectionId,
                buffer.UserId,
                buffer.SessionId,
                buffer.StreamId,
                buffer.AudioMimeType,
                buffer.Locale,
                ms.ToArray(),
                buffer.StartedAt,
                DateTimeOffset.UtcNow,
                buffer.LastSequence);
            return true;
        }
    }

    public bool TryFinalize(string connectionId, string sessionId, string streamId)
        => _buffers.TryRemove(Key(connectionId, sessionId, streamId), out _);

    public bool TryCancel(string connectionId, string sessionId, string streamId)
        => _buffers.TryRemove(Key(connectionId, sessionId, streamId), out _);

    public void CancelConnection(string connectionId)
    {
        foreach (var item in _buffers.Where(item => item.Value.ConnectionId == connectionId).ToList())
        {
            _buffers.TryRemove(item.Key, out _);
        }
    }

    private static string Key(string connectionId, string sessionId, string streamId)
        => $"{connectionId}:{sessionId}:{streamId}";

    private void SweepExpired(TimeSpan idleTimeout, TimeSpan maxStreamAge)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _buffers.ToList())
        {
            var buffer = item.Value;
            if (idleTimeout <= TimeSpan.Zero || maxStreamAge <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout || now - buffer.StartedAt > maxStreamAge)
            {
                _buffers.TryRemove(item.Key, out _);
            }
        }
    }

    private sealed class RealtimeTurnBuffer(
        string connectionId,
        string userId,
        string sessionId,
        string streamId,
        string audioMimeType,
        string locale)
    {
        public object SyncRoot { get; } = new();
        public string ConnectionId { get; } = connectionId;
        public string UserId { get; } = userId;
        public string SessionId { get; } = sessionId;
        public string StreamId { get; } = streamId;
        public string AudioMimeType { get; } = audioMimeType;
        public string Locale { get; } = locale;
        public List<byte[]> Chunks { get; } = [];
        public long TotalBytes { get; set; }
        public int LastSequence { get; set; }
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastPartialEmittedAt { get; set; }
    }
}

public sealed record RealtimeTurnAppendResult(long TotalBytes, int LastSequence, bool ShouldEmitPartial);

public sealed record RealtimeTurnSnapshot(
    string ConnectionId,
    string UserId,
    string SessionId,
    string StreamId,
    string AudioMimeType,
    string Locale,
    byte[] AudioBytes,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int LastSequence);

public static class ConversationRealtimeTransportLimits
{
    public const int MaximumReceiveMessageBytes = 384 * 1024;
    public const int MaxBinaryChunkBytes = 256 * 1024;
    public const int MaximumActiveStreams = 512;
    public const long MaximumBufferedBytes = 64L * 1024 * 1024;
}
