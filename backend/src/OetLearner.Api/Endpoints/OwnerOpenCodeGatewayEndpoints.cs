using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.OwnerAgent;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Endpoints;

// Private control-plane transport. Provider credentials stay inside the API.
public static class OwnerOpenCodeGatewayEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ToolNames = ["Read", "Write", "Edit", "Bash"];

    public static void MapOwnerOpenCodeGatewayEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/internal/owner-agent/opencode").AllowAnonymous();
        group.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<OwnerAgentOptions>>().Value;
            var token = context.HttpContext.Request.Headers[OwnerAgentClient.InternalTokenHeader].ToString();
            var owner = context.HttpContext.Request.Headers[OwnerAgentClient.OwnerAccountHeader].ToString();
            if (!options.Enabled || options.InternalToken is not { Length: >= 32 } || token.Length is < 32 or > 512
                || !options.IsOwnerAccount(owner)
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(options.InternalToken))))
                return Results.Unauthorized();
            return await next(context);
        });
        group.MapGet("/status", async (IAiProviderRegistry registry, CancellationToken ct) =>
        {
            var provider = await registry.FindByCodeAsync("opencode", ct);
            var models = provider is null ? [] : Models(provider);
            var ready = provider is not null && GatewayUrl(provider.BaseUrl) && models.Length > 0
                && !string.IsNullOrWhiteSpace(await registry.GetPlatformKeyAsync(provider.Code, ct));
            return Results.Ok(new { ready, transport = "direct_gateway", label = "Direct OpenCode gateway", models,
                defaultModel = models.Contains(provider?.DefaultModel) ? provider!.DefaultModel : models.FirstOrDefault(),
                reasoningEffort = provider?.ReasoningEffort,
                detail = ready ? "Shared provider configuration is ready." : "Configure an active OpenCode gateway and credential in /admin/ai-providers." });
        });
        group.MapPost("/completions", CompleteAsync);
    }

    private static string[] Models(OetLearner.Api.Domain.AiProvider provider)
    {
        var allowed = (provider.AllowedModelsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new[] { "deepseek-v4.1-flash", "glm-5.3-flash", "glm-5.3" }.Where(m => allowed.Contains(m, StringComparer.Ordinal)).ToArray();
    }

    private static bool GatewayUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "opencode.ai" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsolutePath.TrimEnd('/') is "/zen/go/v1" or "/zen/v1";

    private static async Task CompleteAsync(HttpContext context, GatewayTurn turn, IAiProviderRegistry registry,
        IEnumerable<IAiModelProvider> providers, OwnerAgentClient console, CancellationToken ct)
    {
        if (!OwnerAgentIds.IsUlid(turn.SessionId)) { context.Response.StatusCode = 400; return; }
        var owner = context.Request.Headers[OwnerAgentClient.OwnerAccountHeader].ToString();
        var control = await console.SendAsync(HttpMethod.Get, $"v1/sessions/{turn.SessionId}/gateway-context", null, owner, null, ct);
        var state = control.TryParse();
        if (!control.IsSuccess || state is null || !state.Value.TryGetProperty("allowed", out var allowed) || allowed.ValueKind != JsonValueKind.True)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "console_session_not_ready", message = "The console turn, unlock or lease is no longer active." }, ct);
            return;
        }
        var provider = await registry.FindByCodeAsync("opencode", ct);
        if (provider is null || !GatewayUrl(provider.BaseUrl) || !Models(provider).Contains(turn.Model)
            || string.IsNullOrWhiteSpace(await registry.GetPlatformKeyAsync(provider.Code, ct)))
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = "gateway_not_ready", message = "Enable the gateway, model and credential in /admin/ai-providers." }, ct);
            return;
        }
        if (turn.Messages is not { Count: > 0 and <= 128 } || turn.Tools is not { Count: <= 4 }
            || turn.Messages.Any(m => m.Role is not ("system" or "user" or "assistant" or "tool") || (m.Content?.Length ?? 0) > 262144)
            || turn.Tools.Any(t => !ToolNames.Contains(t.Name) || string.IsNullOrWhiteSpace(t.Description) || t.Parameters.ValueKind != JsonValueKind.Object)
            || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(turn, Json)) > 2 * 1024 * 1024)
        { context.Response.StatusCode = 400; return; }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        using var writer = new SemaphoreSlim(1);
        async Task Write(object value, CancellationToken token)
        {
            await writer.WaitAsync(token);
            try { await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(value, Json)}\n\n", token); await context.Response.Body.FlushAsync(token); }
            finally { writer.Release(); }
        }
        async Task KeepAlive()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            try { while (await timer.WaitForNextTickAsync(lifetime.Token)) await Write(new { type = "heartbeat" }, lifetime.Token); }
            catch (OperationCanceledException) { }
        }
        var heartbeat = KeepAlive();
        try
        {
            await Write(new { type = "start", model = turn.Model }, lifetime.Token);
            var completion = await providers.First(p => p.Name == "registry").CompleteAsync(new AiProviderRequest
            {
                ProviderCode = "opencode", Model = turn.Model, SessionKey = turn.SessionId, Messages = turn.Messages,
                SystemPrompt = turn.Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "",
                UserPrompt = turn.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "",
                Tools = turn.Tools.Select(t => new AiToolDefinition(t.Name, t.Name, t.Description, AiToolCategory.Read, t.Parameters.GetRawText())).ToArray(),
                ToolChoice = "auto", MaxTokens = 16384,
                OnTextDelta = (text, token) => Write(new { type = "text_delta", text }, token),
            }, lifetime.Token);
            await Write(new { type = "completion", text = completion.Text, providerState = completion.ProviderState,
                model = completion.ServedModel ?? turn.Model, usage = completion.Usage, finishReason = completion.FinishReason,
                toolCalls = completion.ToolCalls?.Select(t => new { id = t.Id, name = t.ToolCode, arguments = t.ArgsJson }) }, lifetime.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await Write(new { type = "error", code = ex is OperationCanceledException ? "gateway_timeout" : "gateway_failed",
                message = "Direct OpenCode gateway did not complete this operation. Check provider readiness and retry explicitly; executed tools are retained." }, ct);
        }
        finally { lifetime.Cancel(); await heartbeat; }
    }

    public sealed record GatewayTool(string Name, string Description, JsonElement Parameters);
    public sealed record GatewayTurn(string SessionId, string Model, List<AiChatMessage> Messages, List<GatewayTool> Tools);
}
