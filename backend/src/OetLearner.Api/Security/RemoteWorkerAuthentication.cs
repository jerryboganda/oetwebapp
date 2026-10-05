using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Security;

/// <summary>
/// Bearer authentication for the remote-worker boundary (OET-RWP/1 section 2.2): per-node tokens
/// (<c>orw1_...</c>, scheme <c>RemoteWorker</c>) on the job plane and the fleet-service credential
/// (<c>ofs1_...</c>, scheme <c>FleetService</c>) on the service plane.
///
/// <para>
/// Deliberately NOT the learner JwtBearer pipeline: a token carries no dot, so the default handler
/// fails fast on parse (and Program.cs short-circuits it with <c>NoResult</c>), and nothing here touches the
/// learner account tables. The lookup is by token id (primary key); the secret is compared with
/// <c>CryptographicOperations.FixedTimeEquals</c> over SHA-256 bytes, and an unknown id is compared
/// against a dummy hash so the work done is identical. Revoked, expired, unknown and wrong-secret
/// credentials are indistinguishable to the caller (same 401 body).
/// </para>
/// </summary>
public abstract class RemoteCredentialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Failed verifications allowed per source IP per minute before it is answered 429.</summary>
    public const int FailedVerificationsPerMinute = 120;

    private const string FailureBucket = "remote-auth-fail";

    /// <summary><c>node</c> or <c>fleet</c>.</summary>
    protected abstract string ExpectedKind { get; }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var services = Context.RequestServices;
        var limits = services.GetRequiredService<RemoteRateLimits>();
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();

        // Pre-authentication throttle: it must not depend on HttpContext.User (nothing is authenticated yet).
        var ip = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (limits.IsExceeded(FailureBucket, ip, FailedVerificationsPerMinute, out _))
        {
            Context.Items[RemoteWorkerAuth.ThrottledItem] = true;
            return AuthenticateResult.Fail("throttled");
        }

        if (!RemoteTokenFormat.TryParse(header, ExpectedKind, out var tokenId, out var secret))
        {
            limits.Hit(FailureBucket, ip);
            return AuthenticateResult.Fail("invalid");
        }

        var timeProvider = services.GetRequiredService<TimeProvider>();
        var cache = services.GetRequiredService<RemoteAuthCache>();
        var cacheable = Context.GetEndpoint()?.Metadata.GetMetadata<RemoteAuthCacheableMetadata>() is not null;

        RemoteAuthSnapshot? snapshot = null;
        if (!(cacheable && cache.TryGet(tokenId, out snapshot)))
        {
            var db = services.GetRequiredService<LearnerDbContext>();
            snapshot = await LoadAsync(db, tokenId, timeProvider.GetUtcNow(), Context.RequestAborted);
            if (snapshot is not null && cacheable) cache.Set(tokenId, snapshot);
        }

        // An unknown token id still performs a full hash comparison (dummy hash).
        var secretMatches = RemoteTokenFormat.Verify(secret, snapshot?.Credential.SecretHash);
        var now = timeProvider.GetUtcNow();

        var valid = secretMatches
            && snapshot is not null
            && string.Equals(snapshot.Credential.Kind, ExpectedKind, StringComparison.Ordinal)
            && snapshot.Credential.RevokedAt is null
            && snapshot.Credential.ExpiresAt > now
            && (ExpectedKind != RemoteTokenFormat.NodeKind
                || (snapshot.Node is not null
                    && !string.Equals(snapshot.Node.Status, RemoteNodeStatus.Revoked, StringComparison.Ordinal)));

        if (!valid)
        {
            // Never reveal which condition failed; a revoked entry must not linger in the cache.
            cache.Remove(tokenId);
            limits.Hit(FailureBucket, ip);
            return AuthenticateResult.Fail("invalid");
        }

        var credential = snapshot!.Credential;
        var claims = new List<Claim>
        {
            new(RemoteWorkerAuth.KindClaim, credential.Kind),
            new(RemoteWorkerAuth.TokenIdClaim, credential.TokenId),
        };
        if (credential.NodeId is not null) claims.Add(new Claim(RemoteWorkerAuth.NodeIdClaim, credential.NodeId));

        Context.Items[RemoteWorkerAuth.CredentialItem] = credential;
        if (snapshot.Node is not null) Context.Items[RemoteWorkerAuth.NodeItem] = snapshot.Node;

        if (cache.ShouldRecordUse(credential.TokenId))
        {
            await RecordUseAsync(services.GetRequiredService<LearnerDbContext>(), credential.TokenId, now);
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(RemoteWorkerAuth.ThrottledItem))
        {
            await RemoteProblems.RateLimited(60).ExecuteAsync(Context);
            return;
        }

        await RemoteProblems.Unauthorized().ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => RemoteProblems.Forbidden("forbidden", "This credential may not call this route.").ExecuteAsync(Context);

    private static async Task<RemoteAuthSnapshot?> LoadAsync(
        LearnerDbContext db,
        string tokenId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var credential = await db.RemoteCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TokenId == tokenId, ct);
        if (credential is null) return null;

        RemoteWorker? node = null;
        if (credential.NodeId is not null)
        {
            node = await db.RemoteWorkers.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == credential.NodeId, ct);
        }

        return new RemoteAuthSnapshot(credential, node, now);
    }

    private async Task RecordUseAsync(LearnerDbContext db, string tokenId, DateTimeOffset now)
    {
        try
        {
            // Attach a key-only stub and modify one column: a single-column UPDATE on every provider.
            var stub = new RemoteCredential { TokenId = tokenId };
            db.RemoteCredentials.Attach(stub);
            stub.LastUsedAt = now;
            db.Entry(stub).Property(c => c.LastUsedAt).IsModified = true;
            await db.SaveChangesAsync(Context.RequestAborted);
            db.Entry(stub).State = EntityState.Detached;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // LastUsedAt is informational; authentication must not fail because it could not be written.
            Logger.LogDebug(ex, "Could not record remote credential use.");
        }
    }
}

/// <summary>Per-node token authentication (scheme <c>RemoteWorker</c>).</summary>
public sealed class RemoteWorkerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : RemoteCredentialAuthenticationHandler(options, logger, encoder)
{
    protected override string ExpectedKind => RemoteTokenFormat.NodeKind;
}

/// <summary>Fleet-service credential authentication (scheme <c>FleetService</c>).</summary>
public sealed class FleetServiceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : RemoteCredentialAuthenticationHandler(options, logger, encoder)
{
    protected override string ExpectedKind => RemoteTokenFormat.FleetKind;
}

public static class RemoteWorkerAuthorizationExtensions
{
    /// <summary>
    /// Registers <c>RemoteWorkerOnly</c> and <c>FleetServiceOnly</c>: each names its scheme EXPLICITLY, so the
    /// learner JWT scheme can never satisfy them and a node token can never reach a learner route.
    /// </summary>
    public static void AddRemoteWorkerPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(RemoteWorkerAuth.NodePolicy, policy => policy
            .AddAuthenticationSchemes(RemoteWorkerAuth.NodeScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(RemoteWorkerAuth.KindClaim, RemoteTokenFormat.NodeKind));
        options.AddPolicy(RemoteWorkerAuth.FleetPolicy, policy => policy
            .AddAuthenticationSchemes(RemoteWorkerAuth.FleetScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(RemoteWorkerAuth.KindClaim, RemoteTokenFormat.FleetKind));
    }
}
