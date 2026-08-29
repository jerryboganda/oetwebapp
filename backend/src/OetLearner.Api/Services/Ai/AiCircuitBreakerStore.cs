using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// durable circuit breaker over credentials and providers.
///
/// <para>
/// <see cref="AllowAsync"/> is fail-closed: a store outage denies the call
/// rather than sending traffic at a credential/provider we can no longer
/// prove is healthy.
/// </para>
/// </summary>
public interface IAiCircuitBreakerStore
{
    Task<bool> AllowAsync(string kind, string key, CancellationToken ct);
    Task RecordSuccessAsync(string kind, string key, CancellationToken ct);
    Task RecordFailureAsync(string kind, string key, string? failureCode, CancellationToken ct);
    Task ResetAsync(string kind, string key, CancellationToken ct);
    Task<IReadOnlyList<AiCircuitState>> ListAsync(CancellationToken ct);
}

public sealed class AiCircuitBreakerStore(
    IServiceScopeFactory scopeFactory,
    ILogger<AiCircuitBreakerStore> logger) : IAiCircuitBreakerStore
{
    public const string KindCredential = "credential";
    public const string KindProvider = "provider";
    public const string StateClosed = "closed";
    public const string StateOpen = "open";
    public const string StateHalfOpen = "half_open";

    public static readonly TimeSpan OpenDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ProviderFailureWindow = TimeSpan.FromSeconds(60);
    public const int ProviderFailureThreshold = 5;

    private static readonly HashSet<string> ImmediateOpenCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "401", "403", "402", "invalid_model", "invalid_config",
    };

    public async Task<bool> AllowAsync(string kind, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(key)) return false;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            var normalizedKind = NormalizeKind(kind);
            var normalizedKey = key.Trim();

            var row = await db.AiCircuitStates.FirstOrDefaultAsync(
                s => s.Kind == normalizedKind && s.Key == normalizedKey, ct);
            if (row is null) return true;

            if (string.Equals(row.State, StateClosed, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(row.State, StateOpen, StringComparison.Ordinal))
            {
                if (row.OpenUntil is { } until && until > now)
                {
                    return false;
                }

                // Cool-down elapsed: exactly one probe may proceed.
                row.State = StateHalfOpen;
                row.ProbeInFlight = true;
                row.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                return true;
            }

            if (string.Equals(row.State, StateHalfOpen, StringComparison.Ordinal))
            {
                if (row.ProbeInFlight) return false;

                row.ProbeInFlight = true;
                row.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiCircuitBreakerStore.AllowAsync failed for {Kind}/{Key}; denying the call.", kind, key);
            return false;
        }
    }

    public async Task RecordSuccessAsync(string kind, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(key)) return;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            var normalizedKind = NormalizeKind(kind);
            var normalizedKey = key.Trim();

            var row = await db.AiCircuitStates.FirstOrDefaultAsync(
                s => s.Kind == normalizedKind && s.Key == normalizedKey, ct);
            if (row is null) return;

            row.State = StateClosed;
            row.FailureCount = 0;
            row.OpenedAt = null;
            row.OpenUntil = null;
            row.ProbeInFlight = false;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiCircuitBreakerStore.RecordSuccessAsync failed for {Kind}/{Key}.", kind, key);
        }
    }

    public async Task RecordFailureAsync(string kind, string key, string? failureCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(key)) return;

        try
        {
            await using var dbScope = scopeFactory.CreateAsyncScope();
            var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            var normalizedKind = NormalizeKind(kind);
            var normalizedKey = key.Trim();
            var code = string.IsNullOrWhiteSpace(failureCode) ? null : failureCode.Trim();

            var row = await db.AiCircuitStates.FirstOrDefaultAsync(
                s => s.Kind == normalizedKind && s.Key == normalizedKey, ct);
            if (row is null)
            {
                row = new AiCircuitState
                {
                    Id = NewCircuitId(),
                    Kind = normalizedKind,
                    Key = normalizedKey,
                    State = StateClosed,
                    FailureCount = 0,
                    ProbeInFlight = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.AiCircuitStates.Add(row);
            }

            var openImmediately = string.Equals(normalizedKind, KindCredential, StringComparison.Ordinal)
                || IsImmediateOpenCode(code);

            if (openImmediately)
            {
                Open(row, now, code);
            }
            else
            {
                if (row.LastFailureAt is { } last && now - last > ProviderFailureWindow)
                {
                    row.FailureCount = 0;
                }

                row.FailureCount += 1;
                row.LastFailureAt = now;
                row.LastFailureCode = TruncateCode(code);
                row.ProbeInFlight = false;
                row.UpdatedAt = now;

                if (row.FailureCount >= ProviderFailureThreshold)
                {
                    Open(row, now, code);
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AiCircuitBreakerStore.RecordFailureAsync failed for {Kind}/{Key}.", kind, key);
        }
    }

    public async Task ResetAsync(string kind, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var dbScope = scopeFactory.CreateAsyncScope();
        var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var normalizedKind = NormalizeKind(kind);
        var normalizedKey = key.Trim();

        var row = await db.AiCircuitStates.FirstOrDefaultAsync(
            s => s.Kind == normalizedKind && s.Key == normalizedKey, ct);
        if (row is null)
        {
            db.AiCircuitStates.Add(new AiCircuitState
            {
                Id = NewCircuitId(),
                Kind = normalizedKind,
                Key = normalizedKey,
                State = StateClosed,
                FailureCount = 0,
                ProbeInFlight = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            row.State = StateClosed;
            row.FailureCount = 0;
            row.OpenedAt = null;
            row.OpenUntil = null;
            row.LastFailureCode = null;
            row.ProbeInFlight = false;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AiCircuitState>> ListAsync(CancellationToken ct)
    {
        await using var dbScope = scopeFactory.CreateAsyncScope();
        var db = dbScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.AiCircuitStates.AsNoTracking()
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Key)
            .ToListAsync(ct);
    }

    private static void Open(AiCircuitState row, DateTimeOffset now, string? code)
    {
        row.State = StateOpen;
        row.OpenedAt = now;
        row.OpenUntil = now + OpenDuration;
        row.LastFailureAt = now;
        row.LastFailureCode = TruncateCode(code);
        row.ProbeInFlight = false;
        row.UpdatedAt = now;
    }

    private static bool IsImmediateOpenCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (ImmediateOpenCodes.Contains(code)) return true;
        return code.Contains("invalid_model", StringComparison.OrdinalIgnoreCase)
            || code.Contains("invalid_config", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKind(string kind) => kind.Trim().ToLowerInvariant();

    private static string NewCircuitId() => Guid.NewGuid().ToString("N");

    private static string? TruncateCode(string? code)
        => code is null ? null : (code.Length <= 64 ? code : code[..64]);
}
