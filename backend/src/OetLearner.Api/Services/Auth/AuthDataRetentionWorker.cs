using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Auth;

/// <summary>
/// Background sweeper that prunes auth rows which have no ongoing value and
/// would otherwise bloat indexes forever:
///
///   * <c>EmailOtpChallenges</c> older than <see cref="OtpRetention"/> past
///     their <c>ExpiresAt</c> (they cannot be redeemed after expiry and
///     retaining them long-term adds no forensic value — the related sign-in
///     event is recorded in <c>AuditEvents</c>).
///   * <c>RefreshTokenRecords</c> that have been revoked for longer than
///     <see cref="RevokedRefreshRetention"/> (kept briefly so reuse-detection
///     still works for the grace period, deleted after).
///
/// Hard deletes only — active tokens and unexpired challenges are never
/// touched. Batch size is capped so a very large backlog is cleared over
/// several sweeps rather than locking the tables.
/// </summary>
public sealed class AuthDataRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AuthDataRetentionWorker> logger) : BackgroundService
{
    /// <summary>Keep expired OTPs around for 30 days before deletion.</summary>
    public static readonly TimeSpan OtpRetention = TimeSpan.FromDays(30);

    /// <summary>Keep revoked refresh tokens around for 14 days so reuse
    /// detection still flags replayed tokens during the grace period.</summary>
    public static readonly TimeSpan RevokedRefreshRetention = TimeSpan.FromDays(14);

    private const int BatchSize = 500;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a minute after startup so we don't collide with migrations / warm-up.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auth-data retention sweep failed");
            }
            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var now = DateTimeOffset.UtcNow;
        var otpCutoff = now - OtpRetention;
        var refreshCutoff = now - RevokedRefreshRetention;

        var otpDeleted = await db.EmailOtpChallenges
            .Where(c => c.ExpiresAt < otpCutoff)
            .Take(BatchSize)
            .ExecuteDeleteAsync(ct);

        var refreshDeleted = await db.RefreshTokenRecords
            .Where(r => r.RevokedAt != null && r.RevokedAt < refreshCutoff)
            .Take(BatchSize)
            .ExecuteDeleteAsync(ct);

        if (otpDeleted > 0 || refreshDeleted > 0)
        {
            logger.LogInformation(
                "Auth-data retention swept: {Otp} expired OTP challenges, {Refresh} revoked refresh tokens.",
                otpDeleted,
                refreshDeleted);
        }
    }
}
