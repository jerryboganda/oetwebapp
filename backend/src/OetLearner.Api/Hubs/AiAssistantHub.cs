using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.Rulebook;

using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub for the AI Assistant. Supports multi-role access (admin, expert, learner)
/// with role-scoped tool availability and system prompts.
///
/// Client → Server methods:
///   StartTurn(threadId, userMessage, context?) — begins an assistant turn with streaming response
///   CancelTurn(threadId) — cancels a running turn
///   CreateThread(title?) — creates a new thread
///   ListThreads(skip, take) — lists user's threads
///
/// Server → Client events:
///   MessageDelta(threadId, chunk) — streaming text chunk
///   MessageComplete(threadId, messageId, fullContent) — turn complete
///   ToolCallStart(threadId, toolCallId, toolName, args) — tool invocation starting
///   ToolCallResult(threadId, toolCallId, result) — tool result
///   TurnError(threadId, errorCode, errorMessage) — error during turn
///   ThreadCreated(threadId, title) — new thread created
/// </summary>
[Authorize]
public class AiAssistantHub(
    IAiAssistantOrchestrator orchestrator,
    ILogger<AiAssistantHub> logger) : Hub
{
    private static string? GetUserId(HubCallerContext context)
        => context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string GetUserRole(HubCallerContext context)
    {
        var claims = context.User;
        if (claims?.IsInRole("admin") == true || claims?.IsInRole("system_admin") == true)
            return "admin";
        if (claims?.IsInRole("expert") == true)
            return "expert";
        return "learner";
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

        // Add to user-specific group for targeted notifications
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        logger.LogInformation("AI Assistant hub connected: {UserId} ({Role})",
            userId, GetUserRole(Context));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Creates a new conversation thread for the current user.
    /// </summary>
    public async Task<AiAssistantThreadDto?> CreateThread(string? title)
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var role = GetUserRole(Context);
        var thread = await orchestrator.CreateThreadAsync(userId, role, title, Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ThreadCreated", thread.Id, thread.Title);
        return thread;
    }

    /// <summary>
    /// Starts an assistant turn: sends user message, triggers the ReAct loop,
    /// and streams response chunks back via MessageDelta events.
    /// </summary>
    /// <param name="context">
    /// Optional surface hint: identifiers only (route, resource, question, video
    /// position), never page content. The server resolves what they mean and
    /// whether the learner may see them; exam mode is decided from the database
    /// and ignores this entirely.
    /// </param>
    /// <param name="imageAttachments">Inline images for this turn. Each entry
    /// is <c>data:{mime};base64,{bytes}</c>; the orchestrator forwards them to
    /// the provider as native vision parts (UBAG: ubag_attachments). Cap and
    /// type checks are enforced server-side.</param>
    /// <param name="documentAttachment">Extracted document text for this turn
    /// (<c>fileName|mimeType|text</c>, pipe-escaped by the client). Folded
    /// into the prompt so text-only providers can answer about the file.</param>
    public async Task StartTurn(
        string threadId,
        string userMessage,
        CompanionContextEnvelope? context = null,
        IReadOnlyList<string>? imageAttachments = null,
        string? documentAttachment = null)
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId)) return;

        var role = GetUserRole(Context);

        // Attachment guardrails (fail-closed): images are data-URLs capped at
        // 3 per turn / 5 MB each, images-only mime allowlist, and document
        // text capped at 60 KB. Anything over the line drops the attachment
        // with a TurnError instead of reaching the provider.
        var images = AiAssistantAttachmentGuard.ParseImages(imageAttachments, out var imageError);
        if (imageError is not null)
        {
            await Clients.Caller.SendAsync("TurnError", threadId, "ATTACHMENT_REJECTED", imageError,
                cancellationToken: Context.ConnectionAborted);
            return;
        }
        var document = AiAssistantAttachmentGuard.ParseDocument(documentAttachment, out var documentError);
        if (documentError is not null)
        {
            await Clients.Caller.SendAsync("TurnError", threadId, "ATTACHMENT_REJECTED", documentError,
                cancellationToken: Context.ConnectionAborted);
            return;
        }

        try
        {
            await foreach (var evt in orchestrator.RunTurnAsync(
                threadId, userId, role, userMessage, context, Context.ConnectionAborted,
                images, document))
            {
                switch (evt)
                {
                    case AssistantTextDelta delta:
                        await Clients.Caller.SendAsync("MessageDelta", threadId, delta.Chunk,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantToolCallStart toolStart:
                        await Clients.Caller.SendAsync("ToolCallStart", threadId,
                            toolStart.ToolCallId, toolStart.ToolName, toolStart.ArgsJson,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantToolCallResult toolResult:
                        await Clients.Caller.SendAsync("ToolCallResult", threadId,
                            toolResult.ToolCallId, toolResult.ResultJson, toolResult.IsError,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantCitationsResolved citations:
                        await Clients.Caller.SendAsync("Citations", threadId,
                            citations.Citations,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantTurnComplete complete:
                        await Clients.Caller.SendAsync("MessageComplete", threadId,
                            complete.MessageId, complete.FullContent,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantTurnError error:
                        await Clients.Caller.SendAsync("TurnError", threadId,
                            error.ErrorCode, error.ErrorMessage,
                            cancellationToken: Context.ConnectionAborted);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or cancelled — normal flow
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI Assistant turn failed for user {UserId} thread {ThreadId}", userId, threadId);
            await Clients.Caller.SendAsync("TurnError", threadId, "INTERNAL_ERROR",
                "An unexpected error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Cancels a running turn for the given thread.
    /// </summary>
    public async Task CancelTurn(string threadId)
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await orchestrator.CancelTurnAsync(threadId, userId, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("TurnCancelled", threadId);
    }
}

// --- DTOs for hub communication ---

public sealed record AiAssistantThreadDto(string Id, string Title, string Role, DateTimeOffset CreatedAt, string? ModelOverride = null);

/// <summary>Attachment guardrails shared by every assistant turn. Kept on the
/// hub (not the orchestrator) so rejection happens before any DB write, usage
/// record, or provider call.</summary>
internal static class AiAssistantAttachmentGuard
{
    internal const int MaxImagesPerTurn = 3;
    internal const int MaxImageBytes = 5 * 1024 * 1024;
    internal const int MaxDocumentChars = 60_000;

    private static readonly HashSet<string> AllowedImageMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
    };

    internal static IReadOnlyList<AiProviderImageAttachment>? ParseImages(
        IReadOnlyList<string>? dataUrls, out string? error)
        => ParseImageAttachments(dataUrls, out error);

    internal static AiProviderDocumentAttachment? ParseDocument(
        string? packed, out string? error)
        => ParseDocumentAttachment(packed, out error);

    private static IReadOnlyList<AiProviderImageAttachment>? ParseImageAttachments(
        IReadOnlyList<string>? dataUrls, out string? error)
    {
        error = null;
        if (dataUrls is null || dataUrls.Count == 0) return null;
        if (dataUrls.Count > MaxImagesPerTurn)
        {
            error = $"Too many images: max {MaxImagesPerTurn} per message.";
            return null;
        }
        var output = new List<AiProviderImageAttachment>(dataUrls.Count);
        foreach (var dataUrl in dataUrls)
        {
            if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                error = "Images must be data URLs (data:image/...;base64,...).";
                return null;
            }
            var comma = dataUrl.IndexOf(',');
            if (comma < 0)
            {
                error = "Malformed image data URL.";
                return null;
            }
            var meta = dataUrl[5..comma];
            var mime = meta.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(part => part.Contains('/')) ?? string.Empty;
            if (!AllowedImageMimes.Contains(mime))
            {
                error = "Only JPG, PNG, GIF and WEBP images are supported.";
                return null;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            }
            catch (FormatException)
            {
                error = "Image data is not valid base64.";
                return null;
            }
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
            {
                error = "Each image must be non-empty and at most 5 MB.";
                return null;
            }
            output.Add(new AiProviderImageAttachment { MimeType = mime, Data = bytes });
        }
        return output;
    }

    private static AiProviderDocumentAttachment? ParseDocumentAttachment(
        string? packed, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(packed)) return null;
        // Client packs fileName|mimeType|text with \| and \\ escaping.
        var parts = SplitPacked(packed);
        if (parts.Count != 3 || string.IsNullOrWhiteSpace(parts[2]))
        {
            error = "Malformed document attachment.";
            return null;
        }
        if (parts[2].Length > MaxDocumentChars)
        {
            error = "Document text is too large (max ~60 KB of text).";
            return null;
        }
        return new AiProviderDocumentAttachment
        {
            FileName = parts[0].Length == 0 ? "document" : parts[0],
            MimeType = parts[1].Length == 0 ? "text/plain" : parts[1],
            Text = parts[2],
        };
    }

    private static List<string> SplitPacked(string packed)
    {
        var parts = new List<string> { string.Empty, string.Empty, string.Empty };
        var current = new System.Text.StringBuilder();
        var index = 0;
        for (var i = 0; i < packed.Length; i++)
        {
            var ch = packed[i];
            if (ch == '\\' && i + 1 < packed.Length && (packed[i + 1] is '|' or '\\'))
            {
                current.Append(packed[i + 1]);
                i++;
            }
            else if (ch == '|' && index < 2)
            {
                parts[index++] = current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        parts[index] = current.ToString();
        return parts;
    }
}

// --- Streaming event types ---

public abstract record AssistantStreamEvent;
public sealed record AssistantTextDelta(string Chunk) : AssistantStreamEvent;
public sealed record AssistantToolCallStart(string ToolCallId, string ToolName, string ArgsJson) : AssistantStreamEvent;
public sealed record AssistantToolCallResult(string ToolCallId, string ResultJson, bool IsError) : AssistantStreamEvent;
public sealed record AssistantTurnComplete(string MessageId, string FullContent) : AssistantStreamEvent;
public sealed record AssistantTurnError(string ErrorCode, string ErrorMessage) : AssistantStreamEvent;

/// <summary>
/// The approved sources an AI Learning Companion answer is grounded in, emitted
/// before the first token. Only the companion path produces these; the admin and
/// expert assistants have no retrieval step, so their turns never send this
/// event and clients that ignore it are unaffected.
/// </summary>
public sealed record AssistantCitationsResolved(IReadOnlyList<AssistantCitation> Citations) : AssistantStreamEvent;

/// <summary>
/// One cited source. <paramref name="Ordinal"/> matches the <c>[S#]</c> label the
/// prompt gave the model, so a claim in the answer can be traced to a source.
/// Carries no source text — a citation names where an answer came from; it is not
/// a second channel for delivering paid content.
/// </summary>
public sealed record AssistantCitation(
    int Ordinal,
    string SourceKey,
    string SourceTitle,
    string Authority,
    string? Heading,
    int? PageNumber,
    int? TimestampSeconds);
