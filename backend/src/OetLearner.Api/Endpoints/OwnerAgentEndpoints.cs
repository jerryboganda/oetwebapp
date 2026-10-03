using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.OwnerAgent;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Owner Agent Console public API (agent-console/CONTRACT.md §5), a thin,
/// owner-gated relay in front of the internal sidecar (§3).
///
/// <list type="bullet">
/// <item><c>GET /me</c> — any signed-in user; non-owners only ever see <c>isOwner:false</c>.</item>
/// <item><c>POST /unlock</c> — policy <c>OwnerAgentOwner</c> (owner, no unlock yet).</item>
/// <item>Everything else — policy <c>OwnerAgent</c> (owner + a valid unlock ticket from the
/// <c>X-Owner-Agent-Unlock</c> header or the HttpOnly <c>oet_owner_unlock</c> cookie). One unlock
/// covers every action for its fixed lifetime (default 60 min); there is no per-action step-up.</item>
/// <item>Kill switch: env <c>OwnerAgent:Enabled</c> + feature flag <c>owner_agent_console</c>,
/// read uncached and fail-closed; off ⇒ 503 on every route except <c>/me</c>. Authorization
/// runs first, so a non-owner gets 403 whether or not the console is enabled.</item>
/// </list>
///
/// CSRF: browser mutations reach this API through the Next <c>/api/backend</c> proxy, which
/// enforces the <c>x-csrf-token</c> double-submit (lib/backend-proxy.ts) before forwarding
/// with a Bearer token. The API authenticates the caller by Bearer header only, like every
/// other admin mutation; the <c>oet_owner_unlock</c> cookie only carries the unlock ticket
/// (bound to that Bearer session's <c>sfam</c>), is SameSite=Strict, and authorizes nothing
/// on its own.
/// </summary>
public static partial class OwnerAgentEndpoints
{
    public const string RoutePrefix = "/v1/owner-agent";
    public const int MaxMessageChars = 200_000;
    /// <summary>A Jev "review required" triage only blocks a message at least this long.</summary>
    private const int JevReviewMinMessageChars = 60;
    public const int MaxTitleChars = 200;
    public const int MaxNoteChars = 2_000;
    public const int MaxPrTitleChars = 256;
    public const int MaxPrBodyChars = 65_536;
    public const int MaxPasteCodeChars = 2_048;
    public const int MaxGithubTokenChars = 512;
    public const int MaxAuditTake = 500;
    public const string AuditChainHeader = "X-Owner-Agent-Audit-Chain";

    public const string ApplyUpdateInstructions =
        "The agent console is draining: no new turns will start, but the update workflow could not be dispatched "
        + "(check the Ship token). Run the 'agent-console.yml' workflow with apply=true to recreate the console.";

    public const string ApplyUpdateDispatchedInstructions =
        "The agent console is draining: no new turns will start. The 'agent-console.yml' workflow was dispatched "
        + "with apply=true and will recreate the console in a few minutes.";

    [GeneratedRegex("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCodePattern();

    public static IEndpointRouteBuilder MapOwnerAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{RoutePrefix}/me", GetMeAsync)
            .RequireAuthorization()
            .RequireRateLimiting("PerUser")
            .WithTags("Owner Agent");

        var ownerOnly = app.MapGroup(RoutePrefix)
            .RequireAuthorization(OwnerAgentPolicies.Owner)
            .AddEndpointFilter<OwnerAgentGateFilter>()
            .WithTags("Owner Agent");

        ownerOnly.MapPost("/unlock", UnlockAsync).RequireRateLimiting("AuthBruteforce");

        var console = app.MapGroup(RoutePrefix)
            .RequireAuthorization(OwnerAgentPolicies.Unlocked)
            .RequireRateLimiting("PerUser")
            .AddEndpointFilter<OwnerAgentGateFilter>()
            .WithTags("Owner Agent");

        // ── Unlock lifecycle ────────────────────────────────────────────────
        console.MapPost("/unlock/refresh", RefreshUnlock).RequireRateLimiting("PerUserWrite");
        console.MapPost("/lock", LockAsync).RequireRateLimiting("PerUserWrite");

        // ── Status / lease ──────────────────────────────────────────────────
        console.MapGet("/status", (HttpContext http, OwnerAgentClient client)
            => RelayResultAsync(client, http, HttpMethod.Get, OwnerAgentSidecarRoutes.Status, null));
        console.MapPost("/lease", LeaseAsync).RequireRateLimiting("PerUserWrite");

        // ── Engine sign-in (vendor flows run inside the sidecar) ────────────
        console.MapPost("/auth/{engine}/connect", ConnectEngineAsync).RequireRateLimiting("PerUserWrite");
        console.MapGet("/auth/{engine}/flows/{flowId}", (string engine, string flowId, HttpContext http, OwnerAgentClient client)
            => RelayResultAsync(client, http, HttpMethod.Get,
                OwnerAgentSidecarRoutes.AuthFlow(OwnerAgentIds.RequireEngine(engine), OwnerAgentIds.RequireUlid(flowId, "flowId")), null));
        console.MapPost("/auth/{engine}/code", SubmitEngineCodeAsync).RequireRateLimiting("PerUserWrite");
        console.MapPost("/auth/{engine}/cancel", CancelEngineFlowAsync).RequireRateLimiting("PerUserWrite");
        console.MapPost("/auth/{engine}/logout", LogoutEngineAsync).RequireRateLimiting("PerUserWrite");

        // ── GitHub tokens (write-only) ──────────────────────────────────────
        console.MapPut("/github-tokens", UpdateGithubTokensAsync).RequireRateLimiting("PerUserWrite");

        // ── Sessions ────────────────────────────────────────────────────────
        // Whitelisted list filters (q, engine, status, includeArchived, before, limit) are validated and forwarded.
        console.MapGet("/sessions", (HttpContext http, OwnerAgentClient client)
            => RelayResultAsync(client, http, HttpMethod.Get, OwnerAgentSidecarRoutes.SessionsList(http.Request.Query), null));
        console.MapPost("/sessions", CreateSessionAsync).RequireRateLimiting("PerUserWrite");
        console.MapGet("/sessions/{sessionId}", (string sessionId, HttpContext http, OwnerAgentClient client)
            => RelayResultAsync(client, http, HttpMethod.Get, OwnerAgentSidecarRoutes.Session(OwnerAgentIds.RequireUlid(sessionId, "sessionId")), null));
        console.MapPatch("/sessions/{sessionId}", UpdateSessionAsync).RequireRateLimiting("PerUserWrite");
        console.MapPost("/sessions/{sessionId}/messages", SendMessageAsync).RequireRateLimiting("PerUserWrite");
        console.MapPost("/sessions/{sessionId}/interrupt", (string sessionId, HttpContext http, OwnerAgentClient client)
                => RelayResultAsync(client, http, HttpMethod.Post,
                    OwnerAgentSidecarRoutes.SessionInterrupt(OwnerAgentIds.RequireUlid(sessionId, "sessionId")), null))
            .RequireRateLimiting("PerUserWrite");
        console.MapPost("/sessions/{sessionId}/handoff", HandoffAsync).RequireRateLimiting("PerUserWrite");
        console.MapPost("/sessions/{sessionId}/approvals/{approvalId}", DecideApprovalAsync).RequireRateLimiting("PerUserWrite");
        console.MapGet("/sessions/{sessionId}/diff", (string sessionId, HttpContext http, OwnerAgentClient client)
            => RelayResultAsync(client, http, HttpMethod.Get,
                OwnerAgentSidecarRoutes.SessionDiff(OwnerAgentIds.RequireUlid(sessionId, "sessionId")), null, OwnerAgentClient.AdminTimeout));
        console.MapPost("/sessions/{sessionId}/ship", ShipAsync).RequireRateLimiting("PerUserWrite");
        console.MapGet("/sessions/{sessionId}/ship", (string sessionId, HttpContext http, OwnerAgentClient client)
            => RelayResultAsync(client, http, HttpMethod.Get,
                OwnerAgentSidecarRoutes.SessionShip(OwnerAgentIds.RequireUlid(sessionId, "sessionId")), null));

        // ── Operations ──────────────────────────────────────────────────────
        console.MapPost("/kill-switch", KillSwitchAsync).RequireRateLimiting("PerUserWrite");
        console.MapPost("/apply-update", ApplyUpdateAsync).RequireRateLimiting("PerUserWrite");
        // Undo a kill switch / drain: sidecar POST /v1/admin/drain {draining:false}
        // (which also clears the kill-switch state). Additive to CONTRACT §5.
        console.MapPost("/resume", ResumeAsync).RequireRateLimiting("PerUserWrite");
        // Owner allow-list → emails, so History can show which admin account started a session
        // (sessions carry the creator's auth account id as `createdBy`).
        console.MapGet("/owners", async (IOptions<OwnerAgentOptions> options, LearnerDbContext db, CancellationToken ct) =>
        {
            var ids = OwnerAgentOptions.ParseOwnerAccountIds(options.Value.OwnerAccountIds).ToList();
            var items = await db.ApplicationUserAccounts.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .Select(a => new OwnerAgentOwnerDto(a.Id, a.Email))
                .ToListAsync(ct);
            return Results.Ok(new { items });
        });
        // "Latest AuditEvent rows" (CONTRACT §5) as { items, chainIntact } (newest first), the
        // shape lib/owner-agent/api.ts reads. Chain integrity of the returned window is reported
        // per row (hashValid), in chainIntact and in X-Owner-Agent-Audit-Chain.
        console.MapGet("/audit", async (HttpContext http, IOwnerAgentAuditService audit, [FromQuery] int? take, CancellationToken ct) =>
        {
            var page = await audit.ListAsync(Math.Clamp(take ?? 100, 1, MaxAuditTake), ct);
            http.Response.Headers[AuditChainHeader] = page.ChainIntact ? "intact" : "broken";
            return Results.Ok(page);
        });

        return app;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  /me + unlock lifecycle
    // ════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> GetMeAsync(
        HttpContext http,
        IOptions<OwnerAgentOptions> options,
        IOwnerAgentFeatureGate gate,
        IOwnerAgentUnlockService unlock)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!OwnerAgentIdentity.IsOwner(http.User, options.Value))
        {
            return Results.Ok(new OwnerAgentMeResponse(false, false, null, null, false, null));
        }

        var ct = http.RequestAborted;
        var accountId = OwnerAccountId(http);
        var availability = await gate.GetAvailabilityAsync(ct);
        // Header first, else the HttpOnly cookie — so a reload / new tab reports "unlocked".
        var ticket = OwnerAgentHeaders.ReadUnlockTicket(http);
        var validation = ticket is null ? null : await unlock.ValidateAsync(http.User, ticket, ct);
        var unlocked = validation is { IsValid: true };
        var blockedUntil = await unlock.GetUnlockBlockedUntilAsync(accountId, ct);

        return Results.Ok(new OwnerAgentMeResponse(
            IsOwner: true,
            Unlocked: unlocked,
            UnlockExpiresAt: unlocked ? validation!.ExpiresAt : null,
            AbsoluteExpiresAt: unlocked ? validation!.AbsoluteExpiresAt : null,
            FeatureEnabled: availability.IsAvailable,
            UnlockBlockedUntil: blockedUntil));
    }

    private static async Task<IResult> UnlockAsync(
        HttpContext http,
        OwnerAgentUnlockRequest? request,
        AuthService authService,
        IOwnerAgentUnlockService unlock,
        IOwnerAgentAuditService audit,
        TimeProvider timeProvider)
    {
        var ct = http.RequestAborted;
        var accountId = OwnerAccountId(http);
        if (OwnerAgentIdentity.GetSessionFamilyId(http.User) is null)
        {
            throw ApiException.Forbidden("owner_agent_session_family_required", "Sign in again before unlocking the agent console.");
        }

        if (string.IsNullOrWhiteSpace(request?.Password) || string.IsNullOrWhiteSpace(request.Code))
        {
            throw ApiException.Validation("owner_agent_unlock_fields_required", "Your password and a current authenticator code are required.");
        }

        var blockedUntil = await unlock.GetUnlockBlockedUntilAsync(accountId, ct);
        if (blockedUntil is not null)
        {
            await audit.WriteAsync(http.User, OwnerAgentAuditActions.UnlockFailed, null, new Dictionary<string, object?>
            {
                ["reason"] = "reenrolment_cooldown",
                ["blockedUntil"] = blockedUntil.Value,
            }, ct);
            throw ApiException.Forbidden(
                "owner_agent_unlock_cooldown",
                $"The authenticator was re-enrolled recently. The console can be unlocked again after {blockedUntil.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");
        }

        try
        {
            await authService.VerifyAuthenticatorStepUpAsync(http.User, request.Password, request.Code, ct);
        }
        catch (ApiException ex)
        {
            await audit.WriteAsync(http.User, OwnerAgentAuditActions.UnlockFailed, null, new Dictionary<string, object?>
            {
                ["reason"] = ex.ErrorCode,
            }, ct);
            throw;
        }

        var ticket = unlock.Issue(http.User);
        await unlock.RecordUnlockedAsync(accountId, OwnerAgentIdentity.GetSessionFamilyId(http.User), ticket.TicketId, ct);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.Unlock, null, new Dictionary<string, object?>
        {
            ["ticketId"] = ticket.TicketId,
            ["expiresAt"] = ticket.ExpiresAt,
            ["absoluteExpiresAt"] = ticket.AbsoluteExpiresAt,
        }, ct);

        // The browser carries the ticket in an HttpOnly cookie for the unlock's fixed lifetime
        // (reloads, new tabs and other admin pages stay unlocked; page script never sees it).
        OwnerAgentUnlockCookie.Append(http, ticket.Ticket, ticket.ExpiresAt, timeProvider.GetUtcNow());
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new OwnerAgentUnlockResponse(ticket.Ticket, ticket.ExpiresAt, ticket.AbsoluteExpiresAt));
    }

    /// <summary>
    /// Re-mints the current ticket with the SAME fixed expiry (never extends the unlock) and
    /// re-sets the cookie. Kept for back-compat; the admin UI no longer needs to call it.
    /// </summary>
    private static IResult RefreshUnlock(HttpContext http, IOwnerAgentUnlockService unlock, TimeProvider timeProvider)
    {
        var refreshed = unlock.Refresh(CurrentUnlock(http));
        OwnerAgentUnlockCookie.Append(http, refreshed.Ticket, refreshed.ExpiresAt, timeProvider.GetUtcNow());
        return Results.Ok(new OwnerAgentUnlockResponse(refreshed.Ticket, refreshed.ExpiresAt, refreshed.AbsoluteExpiresAt));
    }

    private static async Task<IResult> LockAsync(
        HttpContext http,
        IOwnerAgentUnlockService unlock,
        IOwnerAgentAuditService audit)
    {
        var current = CurrentUnlock(http);
        // Durable revocation watermark (every ticket issued before now dies, in every tab and
        // session) plus an explicit cookie expiry for this browser.
        await unlock.LockAsync(current.AccountId!, current.SessionFamilyId, "owner_lock", http.RequestAborted);
        OwnerAgentUnlockCookie.Clear(http);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.Lock, null, new Dictionary<string, object?>
        {
            ["ticketId"] = current.TicketId,
        }, http.RequestAborted);
        return Results.Ok(new { locked = true });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Status / lease / engines / GitHub
    // ════════════════════════════════════════════════════════════════════════

    private static Task<IResult> LeaseAsync(
        HttpContext http,
        OwnerAgentLeaseRequest? request,
        OwnerAgentClient client,
        TimeProvider timeProvider)
    {
        // Lease = min(unlock expiry, now + 3 min, requested); the sidecar clamps again.
        var current = CurrentUnlock(http);
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(3);
        if (current.ExpiresAt is { } unlockExpires && unlockExpires < expires)
        {
            expires = unlockExpires;
        }

        if (request?.ExpiresAt is { } requested && requested < expires)
        {
            expires = requested < now ? now : requested;
        }

        return RelayResultAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.Lease, new { expiresAt = expires.UtcDateTime });
    }

    private static async Task<IResult> ConnectEngineAsync(
        string engine,
        OwnerAgentConnectRequest? request,
        HttpContext http,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var validEngine = OwnerAgentIds.RequireEngine(engine);
        object? body = null;
        if (validEngine == OwnerAgentIds.OpenCode)
        {
            var providerId = OwnerAgentIds.RequireOpaque(request?.ProviderId, "providerId", 128);
            var methodIndex = request?.MethodIndex;
            if (methodIndex is null or < 0 or > 100)
            {
                throw ApiException.Validation("invalid_method_index", "'methodIndex' must be from 0 through 100.");
            }

            body = new { providerId, methodIndex };
        }
        else if (request?.ProviderId is not null || request?.MethodIndex is not null)
        {
            throw ApiException.Validation("invalid_engine_auth", "Provider selection is only supported for OpenCode.");
        }

        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.AuthConnect(validEngine), body);
        var details = new Dictionary<string, object?>
        {
            ["engine"] = validEngine,
            ["status"] = relay.Status,
            ["flowId"] = ReadString(relay.Json, "flowId"),
        };
        if (body is not null)
        {
            details["providerId"] = request!.ProviderId;
            details["methodIndex"] = request.MethodIndex;
        }
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.EngineConnect, validEngine, details, http.RequestAborted);
        return relay.Result;
    }

    private static Task<IResult> SubmitEngineCodeAsync(
        string engine,
        HttpContext http,
        OwnerAgentConnectCodeRequest? request,
        OwnerAgentClient client)
    {
        var validEngine = OwnerAgentIds.RequireEngine(engine);
        var flowId = OwnerAgentIds.RequireUlid(request?.FlowId, "flowId");
        // Vendor paste-back code: opaque, forwarded to the sidecar only, never logged or audited.
        var code = OwnerAgentIds.RequireOpaque(request?.Code, "code", MaxPasteCodeChars);
        return RelayResultAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.AuthCode(validEngine), new { flowId, code });
    }

    private static Task<IResult> CancelEngineFlowAsync(
        string engine,
        HttpContext http,
        OwnerAgentFlowRequest? request,
        OwnerAgentClient client)
    {
        var validEngine = OwnerAgentIds.RequireEngine(engine);
        var flowId = OwnerAgentIds.RequireUlid(request?.FlowId, "flowId");
        return RelayResultAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.AuthCancel(validEngine), new { flowId });
    }

    private static async Task<IResult> LogoutEngineAsync(
        string engine,
        HttpContext http,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var validEngine = OwnerAgentIds.RequireEngine(engine);
        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.AuthLogout(validEngine), null);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.EngineLogout, validEngine, new Dictionary<string, object?>
        {
            ["engine"] = validEngine,
            ["status"] = relay.Status,
        }, http.RequestAborted);
        return relay.Result;
    }

    private static async Task<IResult> UpdateGithubTokensAsync(
        HttpContext http,
        OwnerAgentGithubTokensRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var agentToken = ValidateGithubToken(request?.AgentToken, "agentToken");
        var shipToken = ValidateGithubToken(request?.ShipToken, "shipToken");
        if (agentToken is null && shipToken is null)
        {
            throw ApiException.Validation("github_token_required", "Provide agentToken and/or shipToken.");
        }

        var relay = await RelayAsync(client, http, HttpMethod.Put, OwnerAgentSidecarRoutes.GithubTokens, new { agentToken, shipToken });
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.GithubTokensUpdated, null, new Dictionary<string, object?>
        {
            ["agentTokenProvided"] = agentToken is not null,
            ["shipTokenProvided"] = shipToken is not null,
            ["status"] = relay.Status,
        }, http.RequestAborted);

        if (relay.Response is null)
        {
            return relay.Result;
        }

        if (!relay.Response.IsSuccess)
        {
            // Never relay a sidecar error body for this route: it could quote the input.
            var code = ReadErrorCode(relay.Json) ?? "owner_agent_github_tokens_rejected";
            return Error(http, relay.Response.StatusCode is >= 400 and < 500 ? relay.Response.StatusCode : 502,
                code, "The agent console rejected the GitHub token update.", retryable: false);
        }

        // Re-shape: only the documented GithubStatus fields, so a token can never be echoed.
        var login = ReadString(relay.Json, "login");
        return Results.Ok(new OwnerAgentGithubStatusResponse(
            ReadBool(relay.Json, "agentTokenSet") ?? false,
            ReadBool(relay.Json, "shipTokenSet") ?? false,
            login is null ? null : OwnerAgentAuditSanitizer.Scrub(login, 64)));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Sessions
    // ════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> CreateSessionAsync(
        HttpContext http,
        OwnerAgentCreateSessionRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit,
        ITypeSafeJudgmentService judgments,
        IOptions<TypeSafeOptions> typeSafeOptions,
        ILoggerFactory loggerFactory)
    {
        var engine = OwnerAgentIds.RequireEngine(request?.Engine);
        var model = OwnerAgentIds.RequireOpaque(request?.Model, "model");
        var effort = OwnerAgentIds.OptionalOpaque(request?.Effort, "effort");
        var mode = OwnerAgentIds.RequireMode(request?.Mode);
        var title = OwnerAgentIds.OptionalText(request?.Title, "title", MaxTitleChars);
        var initialMessage = OwnerAgentIds.OptionalText(request?.InitialMessage, "initialMessage", MaxMessageChars);

        // Same triage as a follow-up message. Only the owner's own first message is screened; a handoff
        // summary is machine generated and never goes through here.
        JevDevelopmentAdvisory? jevTriage = null;
        JevDevelopmentAdvisory? jevAdvisory = null;
        if (!string.IsNullOrWhiteSpace(initialMessage))
        {
            (jevTriage, jevAdvisory) = await TriageOwnerMessageAsync(
                initialMessage, judgments, typeSafeOptions.Value, loggerFactory, http.RequestAborted);
        }

        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.Sessions,
            new { engine, model, effort, mode, title, initialMessage, jevAdvisory });
        var sessionId = ReadString(relay.Json, "id");
        var details = new Dictionary<string, object?>
        {
            ["engine"] = engine,
            ["model"] = model,
            ["effort"] = effort,
            ["mode"] = mode,
            ["hasInitialMessage"] = initialMessage is not null,
            ["initialMessagePreview"] = initialMessage,
            ["status"] = relay.Status,
        };
        if (jevTriage is not null)
            AddJevAudit(details, jevTriage);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.SessionCreated, SafeId(sessionId), details, http.RequestAborted);
        return relay.Result;
    }

    /// <summary>
    /// Advisory Jev triage of an owner message, shared by the session's first message and every follow-up.
    /// Fail-open: <c>unavailable</c> (no key, outage, open breaker, timeout) forwards the message with no
    /// advice, because Jev is advice for the break-glass console and never a gate on it. A real
    /// <c>review_required</c> judgment only blocks a substantive message (a terse "continue" has too little
    /// context to triage). The returned advisory is non-null only for a confident <c>ok</c>, the one shape
    /// the sidecar accepts. Both are null while triage is off.
    /// </summary>
    private static async Task<(JevDevelopmentAdvisory? Triage, JevDevelopmentAdvisory? Advisory)> TriageOwnerMessageAsync(
        string text,
        ITypeSafeJudgmentService judgments,
        TypeSafeOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!options.Enabled || !options.DevelopmentTriageEnabled)
            return (null, null);

        var triage = await JevWorkflowAdvisor.TriageDevelopmentAsync(judgments, options, text, ct);
        if (triage?.Status == "unavailable")
        {
            loggerFactory.CreateLogger("OwnerAgentEndpoints").LogWarning(
                "Jev development triage unavailable ({Reason}); forwarding the owner message without advice.",
                triage?.Reason);
            return (triage, null);
        }

        if (triage?.RequiresHumanReview == true && text.Trim().Length >= JevReviewMinMessageChars)
            throw ApiException.Conflict("jev_review_required", "Jev could not establish the task and impact. Clarify the request before continuing.");

        return (triage, triage?.Status == "ok" ? triage : null);
    }

    private static void AddJevAudit(Dictionary<string, object?> details, JevDevelopmentAdvisory? triage)
    {
        details["jevStatus"] = triage?.Status;
        details["jevModel"] = triage?.Model;
        details["jevTask"] = triage?.TaskKind;
        details["jevRisk"] = triage?.RiskLevel;
        details["jevEffort"] = triage?.EffortTier;
        details["jevReason"] = triage?.Reason;
    }

    private static async Task<IResult> UpdateSessionAsync(
        string sessionId,
        HttpContext http,
        OwnerAgentUpdateSessionRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var id = OwnerAgentIds.RequireUlid(sessionId, "sessionId");
        var title = OwnerAgentIds.OptionalText(request?.Title, "title", MaxTitleChars);
        var mode = request?.Mode is null ? null : OwnerAgentIds.RequireMode(request.Mode);
        var model = OwnerAgentIds.OptionalOpaque(request?.Model, "model");
        var effort = OwnerAgentIds.OptionalOpaque(request?.Effort, "effort");
        var archived = request?.Archived;
        if (title is null && mode is null && model is null && effort is null && archived is null)
        {
            throw ApiException.Validation("session_update_empty", "Provide at least one of title, mode, model, effort or archived.");
        }

        var relay = await RelayAsync(client, http, HttpMethod.Patch, OwnerAgentSidecarRoutes.Session(id),
            new { title, mode, model, effort, archived });
        if (mode is not null)
        {
            await audit.WriteAsync(http.User, OwnerAgentAuditActions.ModeChanged, id, new Dictionary<string, object?>
            {
                ["mode"] = mode,
                ["status"] = relay.Status,
            }, http.RequestAborted);
        }

        return relay.Result;
    }

    private static async Task<IResult> SendMessageAsync(
        string sessionId,
        HttpContext http,
        OwnerAgentSendMessageRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit,
        ITypeSafeJudgmentService judgments,
        IOptions<TypeSafeOptions> typeSafeOptions,
        ILoggerFactory loggerFactory)
    {
        var id = OwnerAgentIds.RequireUlid(sessionId, "sessionId");
        var text = OwnerAgentIds.OptionalText(request?.Text, "text", MaxMessageChars);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw ApiException.Validation("text_required", "Message text is required.");
        }

        var model = OwnerAgentIds.OptionalOpaque(request?.Model, "model");
        var effort = OwnerAgentIds.OptionalOpaque(request?.Effort, "effort");
        JevDevelopmentAdvisory? jevTriage = null;
        JevDevelopmentAdvisory? jevAdvisory = null;
        if (typeSafeOptions.Value.Enabled && typeSafeOptions.Value.DevelopmentTriageEnabled)
        {
            var session = await RelayAsync(client, http, HttpMethod.Get, OwnerAgentSidecarRoutes.Session(id), null);
            if (session.Status is < 200 or >= 300)
                return session.Result;

            (jevTriage, jevAdvisory) = await TriageOwnerMessageAsync(
                text, judgments, typeSafeOptions.Value, loggerFactory, http.RequestAborted);
        }

        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.SessionMessages(id),
            new { text, model, effort, jevAdvisory });
        var details = new Dictionary<string, object?>
        {
            ["length"] = text.Length,
            ["preview"] = text,
            ["model"] = model,
            ["effort"] = effort,
            ["turnId"] = ReadString(relay.Json, "turnId"),
            ["status"] = relay.Status,
        };
        AddJevAudit(details, jevTriage);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.MessageSent, id, details, http.RequestAborted);
        return relay.Result;
    }

    private static async Task<IResult> HandoffAsync(
        string sessionId,
        HttpContext http,
        OwnerAgentHandoffRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var id = OwnerAgentIds.RequireUlid(sessionId, "sessionId");
        var engine = OwnerAgentIds.RequireEngine(request?.Engine);
        var model = OwnerAgentIds.RequireOpaque(request?.Model, "model");
        var effort = OwnerAgentIds.OptionalOpaque(request?.Effort, "effort");
        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.SessionHandoff(id),
            new { engine, model, effort });
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.SessionCreated, SafeId(ReadString(relay.Json, "id")), new Dictionary<string, object?>
        {
            ["handoffFrom"] = id,
            ["engine"] = engine,
            ["model"] = model,
            ["effort"] = effort,
            ["status"] = relay.Status,
        }, http.RequestAborted);
        return relay.Result;
    }

    private static async Task<IResult> DecideApprovalAsync(
        string sessionId,
        string approvalId,
        HttpContext http,
        OwnerAgentApprovalDecisionRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var id = OwnerAgentIds.RequireUlid(sessionId, "sessionId");
        var approval = OwnerAgentIds.RequireUlid(approvalId, "approvalId");
        var decision = request?.Decision is { } rawDecision && OwnerAgentIds.Decisions.Contains(rawDecision)
            ? rawDecision
            : throw ApiException.Validation("invalid_decision", "Decision must be 'approve', 'deny' or 'approve_session'.");
        var nonce = OwnerAgentIds.RequireOpaque(request?.Nonce, "nonce");
        var note = OwnerAgentIds.OptionalText(request?.Note, "note", MaxNoteChars);

        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.SessionApproval(id, approval),
            new { decision, nonce, note });
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.ApprovalDecided, id, new Dictionary<string, object?>
        {
            ["approvalId"] = approval,
            ["decision"] = decision,
            ["note"] = note,
            ["status"] = relay.Status,
        }, http.RequestAborted);
        return relay.Result;
    }

    private static async Task<IResult> ShipAsync(
        string sessionId,
        HttpContext http,
        OwnerAgentShipRequest? request,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var id = OwnerAgentIds.RequireUlid(sessionId, "sessionId");
        var prTitle = OwnerAgentIds.OptionalText(request?.PrTitle, "prTitle", MaxPrTitleChars);
        var prBody = OwnerAgentIds.OptionalText(request?.PrBody, "prBody", MaxPrBodyChars);
        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.SessionShip(id),
            new { prTitle, prBody }, OwnerAgentClient.AdminTimeout);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.ShipStarted, id, new Dictionary<string, object?>
        {
            ["shipId"] = ReadString(relay.Json, "shipId"),
            ["phase"] = ReadString(relay.Json, "phase"),
            ["prTitle"] = prTitle,
            ["status"] = relay.Status,
        }, http.RequestAborted);
        return relay.Result;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Operations
    // ════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> KillSwitchAsync(
        HttpContext http,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.StopAll, null, OwnerAgentClient.AdminTimeout);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.KillSwitch, null, new Dictionary<string, object?>
        {
            ["stoppedTurns"] = ReadInt(relay.Json, "stoppedTurns"),
            ["killedProcesses"] = ReadInt(relay.Json, "killedProcesses"),
            ["status"] = relay.Status,
        }, http.RequestAborted);
        return relay.Result;
    }

    /// <summary>
    /// Resume after the kill switch or a drain: the sidecar accepts new turns again. Relayed
    /// as <c>{ draining, activeTurns }</c>. Nothing that was stopped is restarted.
    /// </summary>
    private static async Task<IResult> ResumeAsync(
        HttpContext http,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.Drain, new { draining = false }, OwnerAgentClient.AdminTimeout);
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.Resume, null, new Dictionary<string, object?>
        {
            ["draining"] = ReadBool(relay.Json, "draining"),
            ["status"] = relay.Status,
        }, http.RequestAborted);
        return relay.Result;
    }

    /// <summary>
    /// "Apply update" = drain the sidecar, then recreate it via <c>agent-console.yml</c> with
    /// <c>apply=true</c>. The API holds no GitHub credential (by design): the sidecar's
    /// <c>POST /v1/admin/apply-update</c> drains and dispatches the workflow with its Ship PAT.
    /// When the dispatch fails (e.g. no Ship token) the console stays draining and the UI
    /// shows how to finish by hand.
    /// </summary>
    private static async Task<IResult> ApplyUpdateAsync(
        HttpContext http,
        OwnerAgentClient client,
        IOwnerAgentAuditService audit)
    {
        var relay = await RelayAsync(client, http, HttpMethod.Post, OwnerAgentSidecarRoutes.ApplyUpdate, null, OwnerAgentClient.AdminTimeout);
        var dispatched = ReadBool(relay.Json, "dispatched") ?? false;
        await audit.WriteAsync(http.User, OwnerAgentAuditActions.ApplyUpdate, null, new Dictionary<string, object?>
        {
            ["draining"] = ReadBool(relay.Json, "draining"),
            ["activeTurns"] = ReadInt(relay.Json, "activeTurns"),
            ["dispatched"] = dispatched,
            ["status"] = relay.Status,
        }, http.RequestAborted);

        if (relay.Response is not { IsSuccess: true })
        {
            return relay.Result;
        }

        return Results.Ok(new OwnerAgentApplyUpdateResponse(
            ReadBool(relay.Json, "draining") ?? true,
            ReadInt(relay.Json, "activeTurns"),
            dispatched,
            dispatched ? ApplyUpdateDispatchedInstructions : ApplyUpdateInstructions));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════

    private sealed record Relay(OwnerAgentSidecarResponse? Response, IResult Result)
    {
        public int Status => Response?.StatusCode ?? StatusCodes.Status502BadGateway;
        public JsonElement? Json => Response?.TryParse();
    }

    private static async Task<IResult> RelayResultAsync(
        OwnerAgentClient client,
        HttpContext http,
        HttpMethod method,
        string path,
        object? body,
        TimeSpan? timeout = null)
        => (await RelayAsync(client, http, method, path, body, timeout)).Result;

    private static async Task<Relay> RelayAsync(
        OwnerAgentClient client,
        HttpContext http,
        HttpMethod method,
        string path,
        object? body,
        TimeSpan? timeout = null)
    {
        try
        {
            var response = await client.SendAsync(method, path, body, OwnerAccountId(http), CorrelationId(http), http.RequestAborted, timeout);
            return new Relay(response, ToResult(http, response));
        }
        catch (OwnerAgentSidecarException ex)
        {
            return new Relay(null, SidecarFailure(http, ex));
        }
    }

    private static IResult ToResult(HttpContext http, OwnerAgentSidecarResponse response)
    {
        if (response.StatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        if (response.StatusCode is >= 200 and < 300)
        {
            // Pass-through JSON (CONTRACT §5).
            return Results.Content(response.Json, "application/json", Encoding.UTF8, response.StatusCode);
        }

        if (response.StatusCode is >= 400 and < 500)
        {
            // 4xx: keep the sidecar's { error: { code, message } } (CONTRACT §3) and also
            // surface it flat as { code, message, retryable, correlationId } — the shape the
            // app's shared API client (lib/api/client.ts) reads for every other endpoint.
            var json = response.TryParse();
            var code = ReadErrorCode(json) ?? "owner_agent_request_rejected";
            var message = ReadErrorMessage(json) ?? "The agent console rejected the request.";
            return Results.Json(new
            {
                code,
                message,
                retryable = response.StatusCode == StatusCodes.Status429TooManyRequests,
                correlationId = CorrelationId(http),
                error = new { code, message },
            }, statusCode: response.StatusCode);
        }

        return Error(http, StatusCodes.Status502BadGateway, "owner_agent_sidecar_error",
            "The agent console reported an internal error.", retryable: true);
    }

    /// <summary>Sidecar error message (<c>{ error: { message } }</c>), trimmed and capped.</summary>
    private static string? ReadErrorMessage(JsonElement? json)
    {
        if (json is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = message.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= 500 ? text : text[..500];
    }

    private static IResult SidecarFailure(HttpContext http, OwnerAgentSidecarException ex) => ex.Code switch
    {
        OwnerAgentSidecarException.NotConfigured => Error(http, StatusCodes.Status503ServiceUnavailable, ex.Code, "The agent console is not configured on this server.", retryable: false),
        OwnerAgentSidecarException.Timeout => Error(http, StatusCodes.Status504GatewayTimeout, ex.Code, "The agent console did not respond in time.", retryable: true),
        OwnerAgentSidecarException.Unavailable => Error(http, StatusCodes.Status502BadGateway, ex.Code, "The agent console is not reachable.", retryable: true),
        OwnerAgentSidecarException.Rejected => Error(http, StatusCodes.Status502BadGateway, ex.Code, "The agent console rejected the API's credentials.", retryable: false),
        _ => Error(http, StatusCodes.Status502BadGateway, ex.Code, "The agent console returned an unusable response.", retryable: true),
    };

    private static IResult Error(HttpContext http, int statusCode, string code, string message, bool retryable)
        => Results.Json(new { code, message, retryable, correlationId = CorrelationId(http) }, statusCode: statusCode);

    private static OwnerAgentUnlockValidation CurrentUnlock(HttpContext http)
        => http.Items.TryGetValue(OwnerAgentUnlockValidation.HttpContextItemKey, out var value)
           && value is OwnerAgentUnlockValidation { IsValid: true, AccountId: not null } validation
            ? validation
            : throw ApiException.Forbidden(OwnerAgentFailureCodes.UnlockRequired, OwnerAgentFailureCodes.Describe(OwnerAgentFailureCodes.UnlockRequired));

    private static string OwnerAccountId(HttpContext http)
        => OwnerAgentIdentity.GetAuthAccountId(http.User)
           ?? throw ApiException.Forbidden(OwnerAgentFailureCodes.NotOwner, "An owner account is required.");

    private static string? CorrelationId(HttpContext http)
        => http.Items.TryGetValue("CorrelationId", out var value) ? value as string : null;

    private static string? ValidateGithubToken(string? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        // Empty string = clear the stored token (sidecar decides). Otherwise a single
        // printable, whitespace-free token.
        if (value.Length > MaxGithubTokenChars || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c > '~'))
        {
            throw ApiException.Validation($"invalid_{name}", $"'{name}' must be a single token of at most {MaxGithubTokenChars} printable ASCII characters.");
        }

        return value;
    }

    private static string? SafeId(string? value) => OwnerAgentIds.IsUlid(value) ? value : null;

    private static string? ReadString(JsonElement? json, string property)
        => json is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement? json, string property)
        => json is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(property, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadInt(JsonElement? json, string property)
        => json is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    /// <summary>Sidecar error shape <c>{ error: { code } }</c>; only a safe snake_case code is surfaced.</summary>
    private static string? ReadErrorCode(JsonElement? json)
    {
        if (json is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var code)
            || code.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = code.GetString();
        return text is not null && SafeCodePattern().IsMatch(text) ? text : null;
    }
}

/// <summary>
/// Kill switch + cache hygiene for every gated console route: 503 unless the env switch
/// and the <c>owner_agent_console</c> flag are both on; responses are <c>no-store</c>.
/// </summary>
internal sealed class OwnerAgentGateFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var gate = http.RequestServices.GetRequiredService<IOwnerAgentFeatureGate>();
        var availability = await gate.GetAvailabilityAsync(http.RequestAborted);
        if (!availability.IsAvailable)
        {
            return Results.Json(new
            {
                code = "owner_agent_disabled",
                message = "The agent console is currently disabled.",
                reason = availability.DisabledReason,
                retryable = false,
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return await next(context);
    }
}
