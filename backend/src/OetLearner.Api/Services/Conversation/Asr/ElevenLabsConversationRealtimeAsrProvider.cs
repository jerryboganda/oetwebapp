using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Asr;

public sealed class ElevenLabsConversationRealtimeAsrProvider(
    IConversationOptionsProvider optionsProvider,
    ILogger<ElevenLabsConversationRealtimeAsrProvider> logger) : IConversationRealtimeAsrProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ConversationOptions ReadOptions() => optionsProvider.GetAsync().GetAwaiter().GetResult();

    public string Name => "elevenlabs-stt";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ReadOptions().ElevenLabsSttApiKey);

    public async Task<IConversationRealtimeAsrSession> StartAsync(
        ConversationRealtimeAsrStartRequest request,
        IConversationRealtimeTranscriptSink sink,
        CancellationToken ct)
    {
        var options = await optionsProvider.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(options.ElevenLabsSttApiKey))
        {
            throw new InvalidOperationException("ElevenLabs realtime STT is not configured.");
        }

        var audioFormat = NormalizeAudioFormat(options.ElevenLabsSttAudioFormat);
        if (RequiresRawPcm(audioFormat) && !IsRawPcmMimeType(request.AudioMimeType))
        {
            throw new ConversationAsrException(
                "elevenlabs_audio_format_unsupported",
                "ElevenLabs realtime STT currently requires raw PCM chunks; browser MediaRecorder container audio must use batch fallback until transcoding is enabled.");
        }

        var uri = BuildRealtimeUri(options, request, audioFormat);
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("xi-api-key", options.ElevenLabsSttApiKey);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.RealtimeSttProviderConnectTimeoutSeconds, 1, 60)));
            await socket.ConnectAsync(uri, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            socket.Dispose();
            throw new ConversationAsrException(
                "elevenlabs_realtime_connect_timeout",
                "ElevenLabs realtime STT connection timed out before the provider accepted the session.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new ElevenLabsRealtimeAsrSession(socket, request, sink, logger);
    }

    private static Uri BuildRealtimeUri(
        ConversationOptions options,
        ConversationRealtimeAsrStartRequest request,
        string audioFormat)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.ElevenLabsSttBaseUrl)
            ? "https://api.elevenlabs.io/v1"
            : options.ElevenLabsSttBaseUrl.Trim().TrimEnd('/');
        var baseUri = new Uri(baseUrl);
        if (!IsApprovedElevenLabsHost(baseUri))
        {
            throw new ConversationAsrException(
                "elevenlabs_realtime_base_url",
                "ElevenLabs realtime STT base URL must use the approved ElevenLabs API host.");
        }

        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme switch
            {
                "https" => "wss",
                "wss" => "wss",
                _ => "wss",
            },
        };

        builder.Path = $"{builder.Path.TrimEnd('/')}/speech-to-text/realtime";
        var query = new List<string>
        {
            Query("model_id", string.IsNullOrWhiteSpace(options.ElevenLabsSttModel) ? "scribe_v2_realtime" : options.ElevenLabsSttModel.Trim()),
            Query("audio_format", audioFormat),
            Query("commit_strategy", NormalizeCommitStrategy(options.ElevenLabsSttCommitStrategy)),
            Query("include_timestamps", "true"),
            Query("include_language_detection", "true"),
            Query("enable_logging", options.ElevenLabsSttEnableProviderLogging ? "true" : "false"),
        };

        var language = string.IsNullOrWhiteSpace(options.ElevenLabsSttLanguage) ? request.Locale : options.ElevenLabsSttLanguage.Trim();
        if (!string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            query.Add(Query("language_code", language));
        }

        foreach (var keyterm in ParseKeyterms(options.ElevenLabsSttKeytermsCsv))
        {
            query.Add(Query("keyterms", keyterm));
        }

        builder.Query = string.Join("&", query);
        return builder.Uri;
    }

    private static string Query(string name, string value)
        => $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

    private static bool IsApprovedElevenLabsHost(Uri uri)
        => string.Equals(uri.Host, "api.elevenlabs.io", StringComparison.OrdinalIgnoreCase)
           && uri.Scheme is "https" or "wss";

    private static string NormalizeAudioFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "pcm_16000";
        var normalized = value.Trim().ToLowerInvariant();
        return string.Equals(normalized, "pcm_s16le_16", StringComparison.Ordinal)
            ? "pcm_16000"
            : normalized;
    }

    private static string NormalizeCommitStrategy(string? value)
        => string.Equals(value?.Trim(), "vad", StringComparison.OrdinalIgnoreCase) ? "vad" : "manual";

    private static bool RequiresRawPcm(string audioFormat)
        => audioFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
           || audioFormat.StartsWith("ulaw_", StringComparison.OrdinalIgnoreCase);

    private static bool IsRawPcmMimeType(string mimeType)
    {
        var normalized = mimeType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized is "audio/pcm" or "audio/l16" or "audio/raw" or "application/octet-stream";
    }

    private static IReadOnlyList<string> ParseKeyterms(string csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => term.Length is > 0 and <= 20 && !ContainsUnsupportedKeytermCharacter(term))
                .Take(50)
                .ToArray();

    private static bool ContainsUnsupportedKeytermCharacter(string term)
        => term.IndexOfAny(['<', '>', '{', '}', '[', ']', '\\']) >= 0;

    private sealed class ElevenLabsRealtimeAsrSession : IConversationRealtimeAsrSession
    {
        private readonly ClientWebSocket _socket;
        private readonly ConversationRealtimeAsrStartRequest _request;
        private readonly IConversationRealtimeTranscriptSink _sink;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _receiveCts = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly TaskCompletionSource _finalReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _receiveTask;
        private string? _providerSessionId;
        private int _sequence;
        private bool _closed;

        public ElevenLabsRealtimeAsrSession(
            ClientWebSocket socket,
            ConversationRealtimeAsrStartRequest request,
            IConversationRealtimeTranscriptSink sink,
            ILogger logger)
        {
            _socket = socket;
            _request = request;
            _sink = sink;
            _logger = logger;
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
        }

        public async Task SendAudioAsync(ConversationRealtimeAudioChunk chunk, CancellationToken ct)
        {
            if (_closed || _socket.State != WebSocketState.Open) return;

            await SendMessageAsync(new
            {
                message_type = "input_audio_chunk",
                audio_base_64 = Convert.ToBase64String(chunk.Audio.Span),
                commit = chunk.IsFinal,
                sample_rate = 16000,
            }, ct);
        }

        public async Task CompleteAsync(CancellationToken ct)
        {
            if (_closed || _socket.State != WebSocketState.Open) return;

            await SendMessageAsync(new
            {
                message_type = "input_audio_chunk",
                audio_base_64 = string.Empty,
                commit = true,
                sample_rate = 16000,
            }, ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await _finalReceived.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("ElevenLabs realtime STT did not emit a committed transcript before timeout for {SessionId}.", _request.SessionId);
            }
        }

        public async Task AbortAsync(string reason, CancellationToken ct)
        {
            _closed = true;
            _receiveCts.Cancel();
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, TruncateCloseReason(reason), ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _closed = true;
            _receiveCts.Cancel();
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromMilliseconds(250));
            }
            catch (Exception)
            {
                // The receive loop observes socket disposal/cancellation during normal cleanup.
            }
            _sendGate.Dispose();
            _receiveCts.Dispose();
            _socket.Dispose();
        }

        private async Task SendMessageAsync(object payload, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            await _sendGate.WaitAsync(ct);
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[32 * 1024];
            while (!ct.IsCancellationRequested && _socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                try
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        await HandleMessageAsync(Encoding.UTF8.GetString(message.ToArray()), ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ElevenLabs realtime STT receive loop ended for {SessionId}.", _request.SessionId);
                    await EmitProviderErrorAndCloseAsync(
                        new ConversationAsrException("elevenlabs_realtime_receive_error", "ElevenLabs realtime STT stream ended unexpectedly."),
                        CancellationToken.None);
                    return;
                }
            }
        }

        private async Task HandleMessageAsync(string json, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var messageType = root.TryGetProperty("message_type", out var typeProperty)
                ? typeProperty.GetString()
                : null;

            switch (messageType)
            {
                case "session_started":
                    _providerSessionId = root.TryGetProperty("session_id", out var sessionId) ? sessionId.GetString() : null;
                    break;
                case "partial_transcript":
                    await _sink.OnPartialAsync(new ConversationRealtimeTranscriptPartial(
                        _request.StreamId,
                        ReadString(root, "text"),
                        null,
                        null,
                        null,
                        Interlocked.Increment(ref _sequence)), ct);
                    break;
                case "committed_transcript":
                case "committed_transcript_with_timestamps":
                    await EmitFinalAsync(root, messageType, ct);
                    break;
                default:
                    if (messageType?.StartsWith("scribe_", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await EmitProviderErrorAndCloseAsync(new ConversationAsrException(
                                NormalizeProviderErrorCode(messageType),
                                "ElevenLabs realtime STT returned a provider error."),
                            ct);
                    }
                    break;
            }
        }

        private async Task EmitProviderErrorAndCloseAsync(ConversationAsrException error, CancellationToken ct)
        {
            try
            {
                await _sink.OnProviderErrorAsync(error, ct);
            }
            finally
            {
                _closed = true;
                _finalReceived.TrySetResult();
                await CloseSocketAfterTerminalErrorAsync();
            }
        }

        private async Task CloseSocketAfterTerminalErrorAsync()
        {
            _receiveCts.Cancel();
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "provider_error", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ElevenLabs realtime STT socket close failed after provider error for {SessionId}.", _request.SessionId);
            }
            finally
            {
                _socket.Dispose();
            }
        }

        private async Task EmitFinalAsync(JsonElement root, string messageType, CancellationToken ct)
        {
            var text = ReadString(root, "text").Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var sequence = Interlocked.Increment(ref _sequence);
            var durationMs = EstimateDurationMs(root);
            await _sink.OnFinalAsync(new ConversationRealtimeTranscriptFinal(
                _request.StreamId,
                text,
                0.9,
                durationMs,
                "elevenlabs-stt",
                $"elevenlabs:{_providerSessionId ?? _request.StreamId}:{sequence}",
                $"elevenlabs realtime {messageType}",
                null), ct);
            _finalReceived.TrySetResult();
        }

        private static int EstimateDurationMs(JsonElement root)
        {
            if (!root.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var maxSeconds = 0d;
            foreach (var word in words.EnumerateArray())
            {
                if (word.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Number)
                {
                    maxSeconds = Math.Max(maxSeconds, end.GetDouble());
                }
            }

            return (int)Math.Round(maxSeconds * 1000);
        }

        private static string ReadString(JsonElement root, string property)
            => root.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

        private static string NormalizeProviderErrorCode(string messageType)
            => messageType.Replace('-', '_').ToLowerInvariant();

        private static string TruncateCloseReason(string reason)
            => reason.Length <= 120 ? reason : reason[..120];
    }
}