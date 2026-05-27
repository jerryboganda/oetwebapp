using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Native WebSocket fallback for the Writing Coach panel. Sits next to the
/// SignalR <c>WritingCoachHub</c> for clients that prefer raw WS (browser
/// EventSource fallback per the frontend realtime.ts contract).
///
/// Protocol:
///   Client → Server: JSON frame matching <see cref="WritingCoachHintRequest"/>.
///   Server → Client: JSON frame <c>{ type: "hint", payload: WritingCoachHintResponse }</c>
///                    or <c>{ type: "error", code, message }</c>.
///
/// Mounted at <c>/ws/writing/coach/{sessionId}</c>.
/// </summary>
public static class WritingCoachWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWritingCoachWebSocketEndpoint(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/writing/coach/{sessionId}", HandleAsync)
            .RequireAuthorization("LearnerOnly")
            .WithName("WritingCoachWebSocket");
        return app;
    }

    private static async Task HandleAsync(
        HttpContext context,
        string sessionId,
        IWritingCoachServiceV2 coach,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("WritingCoachWebSocket");

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var ct = context.RequestAborted;
        var buffer = new byte[8 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var received = await ReceiveFullMessageAsync(socket, buffer, ct);
                if (received is null) break;

                WritingCoachHintRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<WritingCoachHintRequest>(received, JsonOptions);
                }
                catch (JsonException jx)
                {
                    logger.LogDebug(jx, "Invalid Writing coach WS payload for {UserId} session {SessionId}", userId, sessionId);
                    await SendAsync(socket, new { type = "error", code = "BAD_REQUEST", message = "Invalid coach payload." }, ct);
                    continue;
                }

                if (request is null)
                {
                    await SendAsync(socket, new { type = "error", code = "BAD_REQUEST", message = "empty payload" }, ct);
                    continue;
                }

                // Path-scoped sessionId wins over body sessionId (defence in depth).
                var normalized = request with { SessionId = sessionId };

                try
                {
                    var hints = await coach.RequestHintsAsync(userId, normalized, ct);
                    foreach (var hint in hints)
                    {
                        await SendAsync(socket, new { type = "hint", payload = hint }, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Writing coach WS hint generation failed for {UserId} session {SessionId}", userId, sessionId);
                    await SendAsync(socket, new { type = "error", code = "COACH_FAILED", message = "Coach is unavailable right now." }, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected — normal
        }
        finally
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "coach-session-end", CancellationToken.None);
                }
                catch
                {
                    // swallow — socket may already be aborted
                }
            }
        }
    }

    private static async Task<string?> ReceiveFullMessageAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task SendAsync<T>(WebSocket socket, T payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
    }
}
