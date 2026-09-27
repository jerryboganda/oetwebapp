using System.Buffers;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Services.OwnerAgent;

/// <summary>CONTRACT.md §7 audit actions (<c>AuditEvent.ResourceType = "OwnerAgent"</c>).</summary>
public static class OwnerAgentAuditActions
{
    public const string Unlock = "unlock";
    public const string UnlockFailed = "unlock_failed";
    public const string Lock = "lock";
    public const string EngineConnect = "engine_connect";
    public const string EngineLogout = "engine_logout";
    public const string GithubTokensUpdated = "github_tokens_updated";
    public const string SessionCreated = "session_created";
    public const string MessageSent = "message_sent";
    public const string ApprovalDecided = "approval_decided";
    public const string ModeChanged = "mode_changed";
    public const string ShipStarted = "ship_started";
    public const string KillSwitch = "kill_switch";
    public const string ApplyUpdate = "apply_update";
    public const string Resume = "resume";
}

/// <summary>
/// One audit row as returned by <c>GET /v1/owner-agent/audit</c>. <see cref="Details"/> is the
/// sanitized payload as a JSON object (null when the stored row is unreadable);
/// <see cref="Hash"/>/<see cref="PreviousHash"/> expose the chain.
/// </summary>
public sealed record OwnerAgentAuditEntry(
    string Id,
    DateTimeOffset OccurredAt,
    string ActorId,
    string ActorName,
    string Action,
    string ResourceType,
    string? ResourceId,
    JsonElement? Details,
    string? Hash,
    string? PreviousHash,
    bool HashValid);

/// <summary>
/// <c>GET /v1/owner-agent/audit</c> body (<c>{ items, chainIntact }</c>, the shape
/// lib/owner-agent/api.ts reads): newest first; <see cref="ChainIntact"/> is false when any
/// row in the window fails its own hash or does not link to the next older row.
/// </summary>
public sealed record OwnerAgentAuditPage(IReadOnlyList<OwnerAgentAuditEntry> Items, bool ChainIntact);

public interface IOwnerAgentAuditService
{
    /// <summary>
    /// Appends one hash-chained audit row. <paramref name="details"/> is sanitized
    /// (secret patterns redacted, strings capped at 200 chars) before it is stored.
    /// Never throws for a storage failure — the relayed action has already happened.
    /// </summary>
    Task WriteAsync(
        ClaimsPrincipal actor,
        string action,
        string? resourceId,
        IReadOnlyDictionary<string, object?>? details,
        CancellationToken cancellationToken);

    Task<OwnerAgentAuditPage> ListAsync(int take, CancellationToken cancellationToken);
}

/// <summary>
/// API-side, hash-chained audit for the Owner Agent Console. The agent can alter
/// application tables through its DB role, so each row carries
/// <c>prev</c> (previous row's hash) and <c>hash</c> = SHA-256 over
/// (prev, id, occurredAt, actor, action, resourceId, canonical details) inside
/// <see cref="AuditEvent.Details"/> — no schema change. Any edit/deletion of an
/// earlier row breaks the chain, which <see cref="ListAsync"/> reports.
/// Writes are serialized per process (single active API slot).
/// </summary>
public sealed class OwnerAgentAuditService(
    LearnerDbContext db,
    TimeProvider timeProvider,
    ILogger<OwnerAgentAuditService> logger) : IOwnerAgentAuditService
{
    public const string ResourceType = "OwnerAgent";
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private const int MaxTake = 500;
    private static readonly SemaphoreSlim ChainGate = new(1, 1);

    public async Task WriteAsync(
        ClaimsPrincipal actor,
        string action,
        string? resourceId,
        IReadOnlyDictionary<string, object?>? details,
        CancellationToken cancellationToken)
    {
        var actorId = Truncate(
            actor.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? OwnerAgentIdentity.GetAuthAccountId(actor)
                ?? "unknown",
            64);
        var actorAccountId = OwnerAgentIdentity.GetAuthAccountId(actor);
        var actorName = Truncate(actor.FindFirstValue(ClaimTypes.Name) ?? actorId, 128);
        var dataJson = OwnerAgentAuditSanitizer.ToCanonicalJson(details);
        var safeResourceId = resourceId is null ? null : (resourceId.Length > 64 ? resourceId[..64] : resourceId);

        // The relayed action already happened; losing the audit row must not turn a
        // successful call into a 500, so the gate/save below use CancellationToken.None.
        await ChainGate.WaitAsync(CancellationToken.None);
        AuditEvent? entity = null;
        try
        {
            var (previousHash, headOccurredAt) = await ReadChainHeadAsync(CancellationToken.None);
            var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            // Strictly increasing timestamps keep "newest first" == chain order even
            // when two actions land in the same millisecond.
            if (headOccurredAt is { } head && occurredAt <= head)
            {
                occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(head.ToUnixTimeMilliseconds() + 1);
            }

            var id = $"AUD-{Guid.NewGuid():N}";
            var hash = ComputeHash(previousHash, id, occurredAt, actorId, action, safeResourceId, dataJson);

            entity = new AuditEvent
            {
                Id = id,
                OccurredAt = occurredAt,
                ActorId = actorId,
                ActorAuthAccountId = actorAccountId,
                ActorName = actorName,
                Action = action,
                ResourceType = ResourceType,
                ResourceId = safeResourceId,
                Details = BuildDetails(dataJson, previousHash, hash),
            };
            db.AuditEvents.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Owner agent audit write failed for action {Action}.", action);
            if (entity is not null)
            {
                // Do not leave a failed row queued on the request's context.
                db.Entry(entity).State = EntityState.Detached;
            }
        }
        finally
        {
            ChainGate.Release();
        }
    }

    public async Task<OwnerAgentAuditPage> ListAsync(int take, CancellationToken cancellationToken)
    {
        var bounded = Math.Clamp(take, 1, MaxTake);
        var rows = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.ResourceType == ResourceType)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(bounded)
            .Select(e => new { e.Id, e.OccurredAt, e.ActorId, e.ActorName, e.Action, e.ResourceId, e.Details })
            .ToListAsync(cancellationToken);

        var items = new List<OwnerAgentAuditEntry>(rows.Count);
        var chainIntact = true;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var parsed = ParseDetails(row.Details);
            var hashValid = parsed.Hash is not null
                && parsed.PreviousHash is not null
                && parsed.DataJson is not null
                && string.Equals(
                    ComputeHash(parsed.PreviousHash, row.Id, row.OccurredAt, row.ActorId, row.Action, row.ResourceId, parsed.DataJson),
                    parsed.Hash,
                    StringComparison.Ordinal);

            // Rows are newest-first: this row's prev must equal the next (older) row's hash.
            if (index + 1 < rows.Count)
            {
                var older = ParseDetails(rows[index + 1].Details);
                if (!string.Equals(parsed.PreviousHash, older.Hash, StringComparison.Ordinal))
                {
                    chainIntact = false;
                }
            }

            chainIntact &= hashValid;
            items.Add(new OwnerAgentAuditEntry(
                row.Id,
                row.OccurredAt,
                row.ActorId,
                row.ActorName,
                row.Action,
                ResourceType,
                row.ResourceId,
                ToElement(parsed.DataJson),
                parsed.Hash,
                parsed.PreviousHash,
                hashValid));
        }

        return new OwnerAgentAuditPage(items, chainIntact);
    }

    internal static string ComputeHash(
        string previousHash,
        string id,
        DateTimeOffset occurredAt,
        string actorId,
        string action,
        string? resourceId,
        string dataJson)
    {
        var material = string.Join('\n',
            previousHash,
            id,
            occurredAt.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            actorId,
            action,
            resourceId ?? string.Empty,
            dataJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private async Task<(string Hash, DateTimeOffset? OccurredAt)> ReadChainHeadAsync(CancellationToken cancellationToken)
    {
        var head = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.ResourceType == ResourceType)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.Details, e.OccurredAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (head is null)
        {
            return (GenesisHash, null);
        }

        var parsed = ParseDetails(head.Details);
        // A head row without a readable hash (tampered) still chains deterministically:
        // hash its raw text so the break stays visible instead of silently resetting.
        return (parsed.Hash
                ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(head.Details ?? string.Empty))).ToLowerInvariant(),
            head.OccurredAt);
    }

    private static string BuildDetails(string dataJson, string previousHash, string hash)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);
            writer.WritePropertyName("data");
            writer.WriteRawValue(dataJson, skipInputValidation: false);
            writer.WriteString("prev", previousHash);
            writer.WriteString("hash", hash);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static ParsedDetails ParseDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return new ParsedDetails(null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(details);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ParsedDetails(null, null, null);
            }

            var dataJson = root.TryGetProperty("data", out var raw) ? raw.GetRawText() : null;
            var previous = root.TryGetProperty("prev", out var prevElement) && prevElement.ValueKind == JsonValueKind.String
                ? prevElement.GetString()
                : null;
            var hash = root.TryGetProperty("hash", out var hashElement) && hashElement.ValueKind == JsonValueKind.String
                ? hashElement.GetString()
                : null;
            return new ParsedDetails(dataJson, previous, hash);
        }
        catch (JsonException)
        {
            return new ParsedDetails(null, null, null);
        }
    }

    private static JsonElement? ToElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length > maxLength ? value[..maxLength] : value;

    private sealed record ParsedDetails(string? DataJson, string? PreviousHash, string? Hash);
}

/// <summary>
/// Audit/log detail sanitizer: CONTRACT.md §7 — "Details never contains secrets or
/// message bodies beyond the first 200 chars". Values are flattened to primitives,
/// strings are capped at <see cref="MaxStringLength"/> and scrubbed of common
/// credential shapes (JWTs, GitHub/OpenAI/Slack/AWS tokens, connection strings,
/// PEM blocks) before they are persisted.
/// </summary>
public static partial class OwnerAgentAuditSanitizer
{
    public const int MaxStringLength = 200;
    public const string Redacted = "[redacted]";

    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    [GeneratedRegex(
        @"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}"
        + @"|github_pat_[A-Za-z0-9_]{10,}"
        + @"|gh[pousr]_[A-Za-z0-9]{16,}"
        + @"|sk-[A-Za-z0-9_-]{12,}"
        + @"|xox[abprs]-[A-Za-z0-9-]{8,}"
        + @"|AKIA[0-9A-Z]{16}"
        + @"|(?:postgres(?:ql)?|mysql|mongodb(?:\+srv)?|redis|amqps?)://\S+"
        + @"|-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    /// <summary>Redacts credential shapes, then caps the length.</summary>
    public static string Scrub(string value, int maxLength = MaxStringLength)
    {
        var redacted = SecretPattern().Replace(value, Redacted);
        return redacted.Length > maxLength ? redacted[..maxLength] : redacted;
    }

    /// <summary>Sorted-key, primitive-only JSON used both for storage and hashing.</summary>
    public static string ToCanonicalJson(IReadOnlyDictionary<string, object?>? details)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (details is not null)
        {
            foreach (var (key, value) in details)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                sorted[key.Length > 64 ? key[..64] : key] = Normalize(value);
            }
        }

        return JsonSerializer.Serialize(sorted, CanonicalOptions);
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        string text => Scrub(text),
        bool or int or long or short or byte or double or float or decimal => value,
        DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        Enum enumValue => enumValue.ToString(),
        _ => Scrub(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
    };
}
