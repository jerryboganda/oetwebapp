using System.Text.RegularExpressions;
using Fleet.Core.Crypto;
using Fleet.Core.Policy;
using Fleet.Core.Ssh;
using Fleet.Core.Validation;
using Fleet.Manager.Configuration;
using Fleet.Manager.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Operations;

public sealed record ReleaseView(
    string Id,
    string Sha,
    string RunId,
    string AgentRepository,
    string AgentDigest,
    string? AgentImageId,
    string? ManagerDigest,
    DateTimeOffset RecordedAt,
    bool Approved,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy)
{
    public static ReleaseView From(ReleaseEntity r) => new(
        r.Id, r.Sha, r.RunId, r.AgentRepository, r.AgentDigest, r.AgentImageId, r.ManagerDigest,
        r.RecordedAt, r.Approved, r.ApprovedAt, r.ApprovedBy);
}

/// <summary>The <c>fleet-release.json</c> record the CI sync job sends (OET-RWP/1 section 8.7).</summary>
public sealed record ReleaseRecordInput(
    string Sha,
    string RunId,
    string AgentRepository,
    string AgentDigest,
    string? AgentImageId,
    string? ManagerDigest);

/// <summary>The body of <c>POST /internal/sync</c>: the release record plus a job-scoped registry token (memory only).</summary>
public sealed record SyncPayload(ReleaseRecordInput Record, string? RegistryUsername, string? RegistryToken)
{
    /// <summary>Never prints the token, so a stray log statement cannot leak it.</summary>
    public override string ToString() => "SyncPayload { Record = " + Record + ", RegistryUsername = " + RegistryUsername + ", RegistryToken = [redacted] }";
}

/// <summary>
/// Holds the per-rollout registry token in memory for at most 60 minutes (OET-RWP/1 section 8.7, mode A).
/// It is never written to SQLite, a file, argv or the environment, and is zeroed on expiry or replacement.
/// A manager restart simply forgets it; the next sync supplies a new one.
/// </summary>
public sealed class RolloutTokenHolder
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private string? _username;
    private SecretBuffer? _token;
    private DateTimeOffset _expiresAt;

    public RolloutTokenHolder(TimeProvider time)
    {
        _time = time;
    }

    public bool HasToken
    {
        get
        {
            lock (_gate)
            {
                ExpireIfNeeded();
                return _token is not null;
            }
        }
    }

    public void Set(string username, SecretBuffer token, TimeSpan ttl)
    {
        lock (_gate)
        {
            _token?.Dispose();
            _username = username;
            _token = token;
            _expiresAt = _time.GetUtcNow() + ttl;
        }
    }

    /// <summary>Copies the credential out for one registry login. The string copy is short-lived and never logged.</summary>
    public bool TryGetCredential(out string username, out string token)
    {
        lock (_gate)
        {
            ExpireIfNeeded();
            if (_token is null || _username is null)
            {
                username = string.Empty;
                token = string.Empty;
                return false;
            }

            username = _username;
            token = System.Text.Encoding.UTF8.GetString(_token.AsSpan());
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _token?.Dispose();
            _token = null;
            _username = null;
        }
    }

    private void ExpireIfNeeded()
    {
        if (_token is not null && _time.GetUtcNow() >= _expiresAt)
        {
            _token.Dispose();
            _token = null;
            _username = null;
        }
    }
}

/// <summary>
/// Agent image releases: recorded from CI, approved by the owner, then pushed to nodes as the
/// <c>approvedDigests</c> window (current plus the previous two, at most 8; OET-RWP/1 section 8.7). A
/// node whose running digest is outside that window stops claiming, so an unapproved image can never take work.
/// </summary>
public sealed class ReleaseService
{
    private static readonly Regex ShaPattern = new(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);
    private static readonly Regex RunIdPattern = new(@"\A[0-9]{1,20}\z", RegexOptions.CultureInvariant);
    private static readonly Regex RegistryUserPattern = new(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly TimeProvider _time;
    private readonly IAuditService _audit;
    private readonly IOptions<FleetOptions> _options;
    private readonly RolloutTokenHolder _tokens;
    private readonly OperationSignal _signal;

    public ReleaseService(
        IDbContextFactory<FleetDbContext> factory,
        TimeProvider time,
        IAuditService audit,
        IOptions<FleetOptions> options,
        RolloutTokenHolder tokens,
        OperationSignal signal)
    {
        _factory = factory;
        _time = time;
        _audit = audit;
        _options = options;
        _tokens = tokens;
        _signal = signal;
    }

    /// <summary>Validates and records a release. Idempotent on the commit SHA (a repeated record returns the existing row).</summary>
    public async Task<ReleaseView> IngestAsync(ReleaseRecordInput input, string actor, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (!ShaPattern.IsMatch(input.Sha))
        {
            issues.Add(new ValidationIssue("sha", "release_invalid", "sha must be 40 lowercase hex characters."));
        }

        if (!RunIdPattern.IsMatch(input.RunId))
        {
            issues.Add(new ValidationIssue("runId", "release_invalid", "runId must be numeric."));
        }

        if (!string.Equals(input.AgentRepository, FleetCtlVerbs.AgentRepository, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("agentRepository", "release_invalid", "agentRepository is not the fleet agent repository."));
        }

        if (!InputValidator.IsValidImageDigest(input.AgentDigest))
        {
            issues.Add(new ValidationIssue("agentDigest", "release_invalid", "agentDigest must be sha256:<64 hex>."));
        }

        if (input.AgentImageId is not null && !InputValidator.IsValidImageDigest(input.AgentImageId))
        {
            issues.Add(new ValidationIssue("agentImageId", "release_invalid", "agentImageId must be sha256:<64 hex>."));
        }

        if (input.ManagerDigest is not null && !InputValidator.IsValidImageDigest(input.ManagerDigest))
        {
            issues.Add(new ValidationIssue("managerDigest", "release_invalid", "managerDigest must be sha256:<64 hex>."));
        }

        if (issues.Count > 0)
        {
            throw new FleetValidationException(issues);
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Releases.FirstOrDefaultAsync(r => r.Sha == input.Sha, cancellationToken);
        if (existing is not null)
        {
            return ReleaseView.From(existing);
        }

        var now = _time.GetUtcNow();
        var entity = new ReleaseEntity
        {
            Id = "rel_" + Guid.NewGuid().ToString("N"),
            Sha = input.Sha,
            RunId = input.RunId,
            AgentRepository = input.AgentRepository,
            AgentDigest = input.AgentDigest,
            AgentImageId = input.AgentImageId,
            ManagerDigest = input.ManagerDigest,
            RecordedAt = now,
            Approved = _options.Value.Image.AutoApproveDigests,
            ApprovedAt = _options.Value.Image.AutoApproveDigests ? now : null,
            ApprovedBy = _options.Value.Image.AutoApproveDigests ? "policy:auto-approve" : null,
        };
        db.Releases.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await _audit.AppendAsync(
            actor,
            "release.recorded",
            entity.Id,
            new Dictionary<string, object?> { ["sha"] = entity.Sha, ["digest"] = entity.AgentDigest, ["approved"] = entity.Approved },
            cancellationToken);
        return ReleaseView.From(entity);
    }

    public async Task<IReadOnlyList<ReleaseView>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Releases.AsNoTracking().OrderByDescending(r => r.RecordedAt).Take(100).ToListAsync(cancellationToken);
        return rows.Select(ReleaseView.From).ToList();
    }

    /// <summary>Approves a digest so nodes may run it (a privileged action; the caller has verified TOTP step-up).</summary>
    public async Task<ReleaseView> ApproveAsync(string releaseId, string actor, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var release = await db.Releases.FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken)
            ?? throw new FleetValidationException(new ValidationIssue("releaseId", "release_not_found", "No such release."));
        if (!release.Approved)
        {
            release.Approved = true;
            release.ApprovedAt = _time.GetUtcNow();
            release.ApprovedBy = actor;
            await db.SaveChangesAsync(cancellationToken);
            await _audit.AppendAsync(
                actor,
                "release.approved",
                release.Id,
                new Dictionary<string, object?> { ["sha"] = release.Sha, ["digest"] = release.AgentDigest },
                cancellationToken);
        }

        return ReleaseView.From(release);
    }

    /// <summary>The newest approved release (the image new nodes receive), or null.</summary>
    public async Task<ReleaseEntity?> GetCurrentApprovedAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Releases.AsNoTracking()
            .Where(r => r.Approved)
            .OrderByDescending(r => r.ApprovedAt)
            .ThenByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ReleaseEntity?> FindApprovedByDigestAsync(string digest, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Releases.AsNoTracking()
            .Where(r => r.Approved && r.AgentDigest == digest)
            .OrderByDescending(r => r.ApprovedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Current approved digest plus the previous two (the rollback window), at most 8.</summary>
    public async Task<AgentImagePolicy> AgentImagePolicyAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var approved = await db.Releases.AsNoTracking()
            .Where(r => r.Approved)
            .OrderByDescending(r => r.ApprovedAt)
            .ThenByDescending(r => r.RecordedAt)
            .Select(r => r.AgentDigest)
            .Take(10)
            .ToListAsync(cancellationToken);
        var window = approved.Distinct(StringComparer.Ordinal).Take(3).ToList();
        return new AgentImagePolicy(window, window.Count > 0 ? window[0] : null, _options.Value.Image.MinAgentVersion);
    }

    /// <summary>
    /// The CI sync job: records the release and hands the manager a job-scoped registry token that lives
    /// in memory for at most 60 minutes. Operations waiting in <c>ImageAwaitingSync</c> are woken.
    /// </summary>
    public async Task<ReleaseView> SyncAsync(SyncPayload payload, string actor, CancellationToken cancellationToken)
    {
        var view = await IngestAsync(payload.Record, actor, cancellationToken);
        if (!string.IsNullOrEmpty(payload.RegistryToken))
        {
            if (payload.RegistryUsername is null || !RegistryUserPattern.IsMatch(payload.RegistryUsername))
            {
                throw new FleetValidationException(new ValidationIssue("registryUsername", "sync_invalid", "registryUsername is missing or invalid."));
            }

            if (payload.RegistryToken.Length > 400)
            {
                throw new FleetValidationException(new ValidationIssue("registryToken", "sync_invalid", "registryToken is too long."));
            }

            _tokens.Set(
                payload.RegistryUsername,
                SecretBuffer.FromUtf8(payload.RegistryToken),
                TimeSpan.FromMinutes(_options.Value.Timing.RolloutTokenMinutes));
        }

        await _audit.AppendAsync(
            actor,
            "release.sync",
            view.Id,
            new Dictionary<string, object?> { ["sha"] = view.Sha, ["tokenSupplied"] = !string.IsNullOrEmpty(payload.RegistryToken) },
            cancellationToken);
        _signal.Kick();
        return view;
    }
}
