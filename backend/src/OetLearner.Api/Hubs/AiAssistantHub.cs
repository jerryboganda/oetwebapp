using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts.AiAssistant;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;
using OetLearner.Api.Services.AiAssistant.Permissions;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub for streaming AI Assistant chat. Only admins with
/// <c>ai_assistant:use</c> may connect. Disabled-globally hub connections
/// are aborted with code <c>kill_switch</c> at handshake.
///
/// Client-callable methods:
///   - <c>StartTurn(threadId, content)</c> — start an assistant turn,
///     returns the new messageId and streams frames to the caller's group.
///   - <c>Subscribe(threadId)</c> — join the group for a thread to receive
///     frames from other connections (multi-tab).
///   - <c>Cancel(messageId)</c> — cancel an in-flight turn this user started.
///
/// Server pushes to <c>thread:{threadId}</c> groups via <c>ReceiveFrame</c>.
/// </summary>
[Authorize(Roles = ApplicationUserRoles.Admin)]
public sealed class AiAssistantHub : Hub
{
    public const string HubPath = "/v1/ai-assistant/hub";

    private readonly IServiceProvider _services;
    private readonly AiAssistantTurnRegistry _turns;
    private readonly IAiAssistantSettingsService _settingsService;
    private readonly ILogger<AiAssistantHub> _logger;

    public AiAssistantHub(
        IServiceProvider services,
        AiAssistantTurnRegistry turns,
        IAiAssistantSettingsService settingsService,
        ILogger<AiAssistantHub> logger)
    {
        _services = services;
        _turns = turns;
        _settingsService = settingsService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is null || !AiAssistantAuthorizationService.HasUse(user))
        {
            _logger.LogWarning("AiAssistantHub: unauthorized connect attempt rejected.");
            Context.Abort();
            return;
        }
        if (!_settingsService.Current.GlobalEnabled)
        {
            // Kill-switch on: refuse new connections.
            await Clients.Caller.SendAsync("KillSwitch", new { reason = "globally_disabled" });
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    public async Task Subscribe(Guid threadId)
    {
        var ownerId = GetUserId();
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var owns = await db.AiChatThreads
            .AnyAsync(t => t.Id == threadId && t.OwnerUserId == ownerId);
        if (!owns)
        {
            throw new HubException("forbidden");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(threadId));
    }

    public Task Unsubscribe(Guid threadId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(threadId));

    /// <summary>
    /// Start an assistant turn. Returns immediately with the new assistant
    /// message id; the actual completion streams asynchronously via
    /// <c>ReceiveFrame</c> calls to the thread group.
    /// </summary>
    public async Task<Guid> StartTurn(Guid threadId, string content, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new HubException("empty_content");
        }
        if (content.Length > 32_000)
        {
            throw new HubException("content_too_long");
        }
        if (!_settingsService.Current.GlobalEnabled)
        {
            throw new HubException("kill_switch");
        }

        var ownerId = GetUserId();
        var nowUtc = DateTimeOffset.UtcNow;

        Guid userMsgId;
        Guid assistantMsgId = Guid.NewGuid();

        // Persist the user message inside a short-lived scope so the
        // streaming task below can re-open its own scope.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var thread = await db.AiChatThreads
                .SingleOrDefaultAsync(t => t.Id == threadId && t.OwnerUserId == ownerId);
            if (thread is null)
            {
                throw new HubException("thread_not_found");
            }
            if (thread.IsArchived)
            {
                throw new HubException("thread_archived");
            }
            userMsgId = Guid.NewGuid();
            db.AiChatMessages.Add(new AiChatMessage
            {
                Id = userMsgId,
                ThreadId = thread.Id,
                Role = AiChatMessageRole.User,
                Content = content,
                CreatedAt = nowUtc,
            });
            thread.UpdatedAt = nowUtc;
            await db.SaveChangesAsync();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(threadId));

        // Fan-out via Channel<StreamFrame>. The orchestrator runs as a
        // background Task; we forward frames to the hub group as they arrive.
        var channel = Channel.CreateUnbounded<StreamFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var hostCt = Context.ConnectionAborted;
        var cts = _turns.Register(assistantMsgId, threadId, ownerId, hostCt);
        var hubContext = _services.GetRequiredService<IHubContext<AiAssistantHub>>();
        var group = GroupFor(threadId);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var orch = scope.ServiceProvider.GetRequiredService<IAgentOrchestrator>();
                var ctx = new AgentContext
                {
                    ThreadId = threadId,
                    MessageId = assistantMsgId,
                    ActorUserId = ownerId,
                    FrameSink = channel.Writer,
                };
                await orch.RunTurnAsync(ctx, content, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AiAssistantHub: orchestrator threw outside graceful handler.");
                try
                {
                    channel.Writer.TryWrite(new ErrorFrame
                    {
                        Type = "error",
                        ThreadId = threadId,
                        MessageId = assistantMsgId,
                        Message = "Internal error.",
                        Code = "internal_error",
                    });
                }
                catch { /* fail-soft */ }
                channel.Writer.TryComplete();
            }
        }, hostCt);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in channel.Reader.ReadAllAsync(hostCt))
                {
                    await hubContext.Clients.Group(group).SendAsync("ReceiveFrame", frame, hostCt);
                }
            }
            catch (OperationCanceledException) { /* connection closed */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AiAssistantHub: frame forwarder ended unexpectedly.");
            }
            finally
            {
                _turns.Release(assistantMsgId);
            }
        }, hostCt);

        return assistantMsgId;
    }

    public Task<bool> Cancel(Guid messageId)
    {
        var cancelled = _turns.TryCancel(messageId, GetUserId());
        return Task.FromResult(cancelled);
    }

    private Guid GetUserId()
    {
        var raw = Context.UserIdentifier
            ?? throw new HubException("no_user");
        if (!Guid.TryParse(raw, out var id))
        {
            // Auth account ids are 64-char strings; the chatbot keys on the
            // auth account id verbatim, deterministically hashed to a Guid
            // namespace so it fits the AiAssistant tables.
            id = StableGuid(raw);
        }
        return id;
    }

    private static string GroupFor(Guid threadId) => $"ai-thread:{threadId:N}";

    private static Guid StableGuid(string s)
    {
        // RFC 4122 v5-like deterministic Guid via SHA-256 of input.
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("aiasst:" + s));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        // Set version 5 / variant bits per RFC 4122.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
