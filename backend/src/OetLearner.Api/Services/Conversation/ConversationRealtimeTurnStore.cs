using System.Collections.Concurrent;
using OetLearner.Api.Services.Conversation.Asr;

namespace OetLearner.Api.Services.Conversation;

public sealed class ConversationRealtimeTurnStore
{
    private readonly ConcurrentDictionary<string, RealtimeTurnBuffer> _buffers = new(StringComparer.Ordinal);
    private readonly object _beginLock = new();

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
        lock (_beginLock)
        {
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
    }

    public IReadOnlyList<IConversationRealtimeAsrSession> DetachExpiredProviderSessions(
        TimeSpan idleTimeout,
        TimeSpan maxStreamAge)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<IConversationRealtimeAsrSession>();
        foreach (var item in _buffers.ToList())
        {
            var buffer = item.Value;
            if (buffer.ProviderSession is null || buffer.IsCommitting) continue;

            if (idleTimeout <= TimeSpan.Zero || maxStreamAge <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout || now - buffer.StartedAt > maxStreamAge)
            {
                if (_buffers.TryRemove(item.Key, out var removed) && removed.ProviderSession is not null)
                {
                    sessions.Add(removed.ProviderSession);
                }
            }
        }

        return sessions;
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
            if (buffer.IsCommitting)
            {
                errorCode = "STREAM_COMMITTING";
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (idleTimeout <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout)
            {
                errorCode = "STREAM_IDLE_TIMEOUT";
                return false;
            }

            if (maxStreamAge <= TimeSpan.Zero || now - buffer.StartedAt > maxStreamAge)
            {
                errorCode = "STREAM_DURATION";
                return false;
            }
        }

        return true;
    }

    public bool TryAttachProviderSession(
        string connectionId,
        string sessionId,
        string streamId,
        IConversationRealtimeAsrSession providerSession)
    {
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer)) return false;

        lock (buffer.SyncRoot)
        {
            if (buffer.IsCommitting) return false;
            buffer.ProviderSession = providerSession;
            return true;
        }
    }

    public bool TryGetProviderSession(
        string connectionId,
        string sessionId,
        string streamId,
        out IConversationRealtimeAsrSession? providerSession)
    {
        providerSession = null;
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer)) return false;

        lock (buffer.SyncRoot)
        {
            providerSession = buffer.ProviderSession;
            return providerSession is not null;
        }
    }

    public bool TrySetProviderFinal(
        string connectionId,
        string sessionId,
        string streamId,
        ConversationRealtimeTranscriptFinal final)
    {
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer)) return false;

        lock (buffer.SyncRoot)
        {
            buffer.ProviderFinal = final;
            return true;
        }
    }

    public bool TryGetProviderFinal(
        string connectionId,
        string sessionId,
        string streamId,
        out ConversationRealtimeTranscriptFinal? final)
    {
        final = null;
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer)) return false;

        lock (buffer.SyncRoot)
        {
            final = buffer.ProviderFinal;
            return final is not null;
        }
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
            if (buffer.IsCommitting)
            {
                errorCode = "STREAM_COMMITTING";
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (idleTimeout <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout)
            {
                errorCode = "STREAM_IDLE_TIMEOUT";
                return false;
            }

            if (maxStreamAge <= TimeSpan.Zero || now - buffer.StartedAt > maxStreamAge)
            {
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
        out RealtimeTurnSnapshot? snapshot,
        out string? errorCode)
    {
        snapshot = null;
        errorCode = null;
        var key = Key(connectionId, sessionId, streamId);
        if (!_buffers.TryGetValue(key, out var buffer))
        {
            errorCode = "STREAM_NOT_FOUND";
            return false;
        }

        lock (buffer.SyncRoot)
        {
            if (buffer.IsCommitting)
            {
                errorCode = "STREAM_COMMITTING";
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (idleTimeout <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout)
            {
                errorCode = "STREAM_IDLE_TIMEOUT";
                return false;
            }

            if (maxStreamAge <= TimeSpan.Zero || now - buffer.StartedAt > maxStreamAge)
            {
                errorCode = "STREAM_DURATION";
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
            buffer.IsCommitting = true;
            return true;
        }
    }

    public bool TryFinalize(string connectionId, string sessionId, string streamId)
        => _buffers.TryRemove(Key(connectionId, sessionId, streamId), out _);

    public bool TryCancel(string connectionId, string sessionId, string streamId)
        => _buffers.TryRemove(Key(connectionId, sessionId, streamId), out _);

    public IReadOnlyList<IConversationRealtimeAsrSession> CancelConnection(string connectionId)
    {
        var providerSessions = new List<IConversationRealtimeAsrSession>();
        foreach (var item in _buffers.Where(item => item.Value.ConnectionId == connectionId).ToList())
        {
            if (_buffers.TryRemove(item.Key, out var buffer) && buffer.ProviderSession is not null)
            {
                providerSessions.Add(buffer.ProviderSession);
            }
        }

        return providerSessions;
    }

    private static string Key(string connectionId, string sessionId, string streamId)
        => $"{connectionId}:{sessionId}:{streamId}";

    private void SweepExpired(TimeSpan idleTimeout, TimeSpan maxStreamAge)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _buffers.ToList())
        {
            var buffer = item.Value;
            if ((idleTimeout <= TimeSpan.Zero || maxStreamAge <= TimeSpan.Zero || now - buffer.UpdatedAt > idleTimeout || now - buffer.StartedAt > maxStreamAge)
                && buffer.ProviderSession is null
                && !buffer.IsCommitting)
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
        public bool IsCommitting { get; set; }
        public IConversationRealtimeAsrSession? ProviderSession { get; set; }
        public ConversationRealtimeTranscriptFinal? ProviderFinal { get; set; }
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
