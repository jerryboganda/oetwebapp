using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Security;

/// <summary>
/// <see cref="SecurityEvent.Kind"/> values used by the authenticator step-up,
/// hardened re-enrolment and the Owner Agent Console unlock lifecycle.
///
/// <para>
/// These rows double as durable state (no new table / migration): the latest
/// <see cref="StepUpSucceeded"/> row carries the last accepted TOTP time-step
/// (replay guard), <see cref="OwnerAgentLocked"/> and
/// <see cref="AuthenticatorReenrolled"/> rows are the unlock-ticket revocation
/// watermark, and a recent <see cref="AuthenticatorReenrolled"/> row blocks a
/// console unlock for <see cref="ReenrolmentUnlockCooldown"/>. Because they are
/// load-bearing they are written through the caller's tracked
/// <see cref="LearnerDbContext"/> (atomic, fails loudly) rather than the
/// best-effort <see cref="ISecurityEventLogger"/>.
/// </para>
/// </summary>
public static class OwnerAgentSecurityEventKinds
{
    // Registered in the canonical SecurityEventKinds whitelist; aliased here.
    public const string StepUpSucceeded = SecurityEventKinds.AuthStepUpSucceeded;
    public const string AuthenticatorReenrolled = SecurityEventKinds.AuthAuthenticatorReenrolled;
    public const string OwnerAgentUnlocked = SecurityEventKinds.OwnerAgentUnlocked;
    public const string OwnerAgentLocked = SecurityEventKinds.OwnerAgentLocked;

    /// <summary>Re-enrolling an authenticator blocks console unlock for this long.</summary>
    public static readonly TimeSpan ReenrolmentUnlockCooldown = TimeSpan.FromHours(72);

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        StepUpSucceeded, AuthenticatorReenrolled, OwnerAgentUnlocked, OwnerAgentLocked,
    };

    /// <summary>Kinds that revoke every unlock ticket issued before them.</summary>
    public static readonly IReadOnlyCollection<string> UnlockRevocationKinds = [OwnerAgentLocked, AuthenticatorReenrolled];

    public static string DefaultSeverity(string kind) => kind switch
    {
        AuthenticatorReenrolled => "warning",
        OwnerAgentUnlocked or OwnerAgentLocked => "warning",
        _ => "info",
    };
}

/// <summary>Small query/factory helpers shared by AuthService and the Owner Agent services.</summary>
internal static class OwnerAgentSecurityEvents
{
    public sealed record Row(string Kind, DateTimeOffset OccurredAt, string? DetailsJson);

    private const int MaxRows = 500;

    /// <summary>
    /// Builds a <see cref="SecurityEvent"/> with the same request context fields
    /// <see cref="SecurityEventLogger"/> records, for callers that must persist the
    /// row atomically with their own change.
    /// </summary>
    public static SecurityEvent Create(
        string authAccountId,
        string kind,
        DateTimeOffset occurredAt,
        HttpContext? httpContext,
        object? details = null,
        string? severity = null,
        Guid? sessionFamilyId = null)
    {
        string? ip = null, userAgent = null, platform = null, country = null;
        if (httpContext is not null)
        {
            ip = httpContext.Request.Headers["CF-Connecting-IP"].ToString();
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = httpContext.Connection.RemoteIpAddress?.ToString();
            }

            userAgent = httpContext.Request.Headers.UserAgent.ToString();
            platform = httpContext.Request.Headers["X-OET-Client-Platform"].ToString();
            country = httpContext.Request.Headers["CF-IPCountry"].ToString();
        }

        return new SecurityEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            AuthAccountId = authAccountId,
            Kind = kind,
            Severity = severity ?? OwnerAgentSecurityEventKinds.DefaultSeverity(kind),
            IpAddress = Truncate(ip, 64),
            UserAgent = Truncate(userAgent, 256),
            Platform = Truncate(platform, 32),
            CountryCode = Truncate(country, 8),
            SessionFamilyId = sessionFamilyId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
        };
    }

    /// <summary>
    /// Recent rows of the given kinds for one account, newest first. Server-side
    /// filtered on every provider that can translate a <see cref="DateTimeOffset"/>
    /// comparison; the SQLite provider (offline desktop only) cannot, so it falls
    /// back to filtering the account's rows of those kinds client-side.
    /// </summary>
    public static async Task<IReadOnlyList<Row>> RecentAsync(
        LearnerDbContext db,
        string authAccountId,
        IReadOnlyCollection<string> kinds,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var kindList = kinds.ToArray();
        try
        {
            return await db.SecurityEvents
                .AsNoTracking()
                .Where(e => e.AuthAccountId == authAccountId && kindList.Contains(e.Kind) && e.OccurredAt > since)
                .OrderByDescending(e => e.OccurredAt)
                .Take(MaxRows)
                .Select(e => new Row(e.Kind, e.OccurredAt, e.DetailsJson))
                .ToListAsync(cancellationToken);
        }
        catch (InvalidOperationException) when (db.Database.IsSqlite())
        {
            var rows = await db.SecurityEvents
                .AsNoTracking()
                .Where(e => e.AuthAccountId == authAccountId && kindList.Contains(e.Kind))
                .Select(e => new Row(e.Kind, e.OccurredAt, e.DetailsJson))
                .ToListAsync(cancellationToken);
            return rows
                .Where(r => r.OccurredAt > since)
                .OrderByDescending(r => r.OccurredAt)
                .Take(MaxRows)
                .ToList();
        }
    }

    public static long? ReadLongDetail(string? detailsJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(propertyName, out var value)
                   && value.ValueKind == JsonValueKind.Number
                   && value.TryGetInt64(out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length > maxLength ? value[..maxLength] : value;
    }
}
