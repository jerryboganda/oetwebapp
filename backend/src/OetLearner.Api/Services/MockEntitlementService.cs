using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks Module Phase 1 — credit-based entitlement gate for mock attempts.
///
/// Credits are granted by purchasing <see cref="BillingAddOn"/>s whose
/// <c>GrantEntitlementsJson</c> contains per-mock-type counts (e.g.
/// <c>{ "mockFull": 5, "mockWriting": 3 }</c>). Each consumed credit is
/// recorded as a <see cref="MockEntitlementLedger"/> row. Remaining =
/// granted - consumed.
///
/// Premium subscribers (per <see cref="OetLearner.Api.Services.Entitlements.IEffectiveEntitlementResolver"/>)
/// bypass the credit ledger and are reported as unlimited.
///
/// Mock-type tokens used here mirror the canonical entitlement JSON keys
/// (camelCase) and are normalised to a stable snake_case ledger value via
/// <see cref="MockEntitlementKeys"/>.
/// </summary>
public interface IMockEntitlementService
{
    Task<MockEntitlementCheck> CheckAsync(string userId, string mockType, CancellationToken ct);
    Task<MockEntitlementDebit> DebitAsync(string userId, string mockType, string mockAttemptId, CancellationToken ct);
    Task<MockEntitlementSummary> SummariseAsync(string userId, CancellationToken ct);
}

public sealed class MockEntitlementService(
    LearnerDbContext db,
    OetLearner.Api.Services.Entitlements.IEffectiveEntitlementResolver entitlementResolver,
    ILogger<MockEntitlementService>? logger = null) : IMockEntitlementService
{
    private const string ReasonAllowedSubscription = "subscription_unlimited";
    private const string ReasonAllowedCredits = "credits_available";
    private const string ReasonNoCredits = "no_credits";
    private const string ReasonInvalidMockType = "invalid_mock_type";
    private const string ReasonAnonymous = "anonymous";

    public async Task<MockEntitlementCheck> CheckAsync(string userId, string mockType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new MockEntitlementCheck(false, mockType, 0, 0, ReasonAnonymous, "Sign in to use mock credits.");
        }

        var normalised = MockEntitlementKeys.NormaliseLedgerType(mockType);
        if (string.IsNullOrWhiteSpace(normalised))
        {
            return new MockEntitlementCheck(false, mockType, 0, 0, ReasonInvalidMockType,
                $"Unknown mock type '{mockType}'.");
        }

        // Premium / Trial subscribers bypass the credit ledger.
        var resolved = await entitlementResolver.ResolveAsync(userId, ct);
        if (resolved.HasEligibleSubscription)
        {
            return new MockEntitlementCheck(true, normalised, int.MaxValue, int.MaxValue,
                ReasonAllowedSubscription,
                resolved.IsTrial
                    ? "Trial subscription — unlimited mock attempts."
                    : "Active subscription — unlimited mock attempts.");
        }

        var (granted, _) = await SumGrantedAsync(userId, normalised, ct);
        var consumed = await db.MockEntitlementLedgers.AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.MockType == normalised, ct);
        var remaining = Math.Max(0, granted - consumed);

        if (remaining <= 0)
        {
            return new MockEntitlementCheck(false, normalised, 0, granted, ReasonNoCredits,
                "No remaining mock credits — purchase a mock add-on to continue.");
        }

        return new MockEntitlementCheck(true, normalised, remaining, granted, ReasonAllowedCredits,
            $"{remaining} of {granted} mock credits remaining.");
    }

    public async Task<MockEntitlementDebit> DebitAsync(string userId, string mockType, string mockAttemptId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new MockEntitlementDebit(false, mockType, 0, 0, null, ReasonAnonymous,
                "Sign in to use mock credits.");
        }

        var normalised = MockEntitlementKeys.NormaliseLedgerType(mockType);
        if (string.IsNullOrWhiteSpace(normalised))
        {
            return new MockEntitlementDebit(false, mockType, 0, 0, null, ReasonInvalidMockType,
                $"Unknown mock type '{mockType}'.");
        }

        // Premium subscribers do not consume credits. The debit becomes a no-op
        // but still reports success so callers can treat the result uniformly.
        var resolved = await entitlementResolver.ResolveAsync(userId, ct);
        if (resolved.HasEligibleSubscription)
        {
            return new MockEntitlementDebit(true, normalised, int.MaxValue, int.MaxValue, null,
                ReasonAllowedSubscription, "Subscription covers mock attempt — no credit consumed.");
        }

        var (granted, sourceAddOnId) = await SumGrantedAsync(userId, normalised, ct);
        var consumed = await db.MockEntitlementLedgers
            .CountAsync(r => r.UserId == userId && r.MockType == normalised, ct);
        var remaining = Math.Max(0, granted - consumed);
        if (remaining <= 0)
        {
            return new MockEntitlementDebit(false, normalised, 0, granted, null, ReasonNoCredits,
                "No remaining mock credits — purchase a mock add-on to continue.");
        }

        var entry = new MockEntitlementLedger
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            AddOnId = sourceAddOnId ?? string.Empty,
            MockType = normalised,
            ConsumedAt = DateTimeOffset.UtcNow,
            MockAttemptId = string.IsNullOrWhiteSpace(mockAttemptId) ? null : mockAttemptId,
        };
        db.MockEntitlementLedgers.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger?.LogError(ex,
                "MockEntitlementService.Debit failed userId={UserId} mockType={MockType} attemptId={AttemptId}",
                userId, normalised, mockAttemptId);
            return new MockEntitlementDebit(false, normalised, remaining, granted, null, "persist_error",
                "Could not record mock-credit consumption.");
        }

        var nowRemaining = Math.Max(0, remaining - 1);
        return new MockEntitlementDebit(true, normalised, nowRemaining, granted, entry.Id,
            ReasonAllowedCredits, $"{nowRemaining} of {granted} mock credits remaining after this attempt.");
    }

    public async Task<MockEntitlementSummary> SummariseAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new MockEntitlementSummary(
                Tier: "anonymous",
                HasEligibleSubscription: false,
                IsTrial: false,
                Items: Array.Empty<MockEntitlementSummaryItem>());
        }

        var resolved = await entitlementResolver.ResolveAsync(userId, ct);

        // Aggregate granted credits across the user's active add-on items.
        var grantedByType = await AggregateGrantedByTypeAsync(userId, ct);

        // Aggregate consumed credits from the ledger.
        var consumedRows = await db.MockEntitlementLedgers.AsNoTracking()
            .Where(r => r.UserId == userId)
            .GroupBy(r => r.MockType)
            .Select(g => new { MockType = g.Key, Consumed = g.Count() })
            .ToListAsync(ct);

        var consumedByType = consumedRows.ToDictionary(x => x.MockType, x => x.Consumed, StringComparer.OrdinalIgnoreCase);

        // Surface a row per known mock-type so the client can render even
        // when nothing has been granted yet ("0 of 0 Writing mocks used").
        var items = new List<MockEntitlementSummaryItem>();
        foreach (var token in MockEntitlementKeys.AllLedgerTypes)
        {
            grantedByType.TryGetValue(token, out var granted);
            consumedByType.TryGetValue(token, out var consumed);
            var remaining = resolved.HasEligibleSubscription
                ? int.MaxValue
                : Math.Max(0, granted - consumed);
            items.Add(new MockEntitlementSummaryItem(
                MockType: token,
                Label: MockEntitlementKeys.Label(token),
                Granted: resolved.HasEligibleSubscription ? int.MaxValue : granted,
                Consumed: consumed,
                Remaining: remaining,
                Unlimited: resolved.HasEligibleSubscription));
        }

        return new MockEntitlementSummary(
            Tier: resolved.HasEligibleSubscription ? (resolved.IsTrial ? "trial" : "paid") : "free",
            HasEligibleSubscription: resolved.HasEligibleSubscription,
            IsTrial: resolved.IsTrial,
            Items: items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sums entitlement counts granted by the user's active add-on subscription
    /// items for a single mock-type ledger token. Returns the AddOnId of an
    /// arbitrary contributing add-on so debit rows can be attributed.
    /// </summary>
    private async Task<(int Granted, string? SourceAddOnId)> SumGrantedAsync(
        string userId, string ledgerType, CancellationToken ct)
    {
        var grants = await LoadActiveGrantsAsync(userId, ct);
        var camelKey = MockEntitlementKeys.CamelKeyForLedger(ledgerType);
        var snakeKey = ledgerType;

        var total = 0;
        string? source = null;
        foreach (var g in grants)
        {
            var contribution = ReadGrantValue(g.GrantEntitlementsJson, camelKey, snakeKey);
            if (contribution <= 0) continue;
            total += contribution * Math.Max(1, g.Quantity);
            source ??= g.AddOnId;
        }
        return (total, source);
    }

    private async Task<Dictionary<string, int>> AggregateGrantedByTypeAsync(string userId, CancellationToken ct)
    {
        var grants = await LoadActiveGrantsAsync(userId, ct);
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in grants)
        {
            if (string.IsNullOrWhiteSpace(g.GrantEntitlementsJson)) continue;
            Dictionary<string, JsonElement>? map;
            try
            {
                map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    g.GrantEntitlementsJson, JsonSupport.Options);
            }
            catch
            {
                continue;
            }
            if (map is null) continue;

            foreach (var (key, value) in map)
            {
                var ledgerType = MockEntitlementKeys.LedgerTypeForGrantKey(key);
                if (ledgerType is null) continue;
                var count = TryReadInt(value);
                if (count <= 0) continue;
                var contribution = count * Math.Max(1, g.Quantity);
                totals[ledgerType] = (totals.TryGetValue(ledgerType, out var existing) ? existing : 0) + contribution;
            }
        }
        return totals;
    }

    /// <summary>
    /// Loads the active add-on subscription items for the user, paired with the
    /// catalog row's <c>GrantEntitlementsJson</c>. Mirrors the active-item join
    /// used by <see cref="OetLearner.Api.Services.Entitlements.EffectiveEntitlementResolver"/>.
    /// </summary>
    private async Task<IReadOnlyList<ActiveAddOnGrant>> LoadActiveGrantsAsync(string userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Only honour grants whose parent subscription is actually live —
        // Pending/Cancelled/Expired parents must not confer mock credits.
        var subscriptionIds = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (subscriptionIds.Count == 0)
        {
            return Array.Empty<ActiveAddOnGrant>();
        }

        var query = from item in db.SubscriptionItems.AsNoTracking()
                    join addOn in db.BillingAddOns.AsNoTracking() on item.ItemCode equals addOn.Code
                    where subscriptionIds.Contains(item.SubscriptionId)
                        && item.Status == SubscriptionItemStatus.Active
                        && item.StartsAt <= now
                        && (item.EndsAt == null || item.EndsAt > now)
                    select new ActiveAddOnGrant
                    {
                        AddOnId = addOn.Id,
                        AddOnCode = addOn.Code,
                        Quantity = item.Quantity,
                        GrantEntitlementsJson = addOn.GrantEntitlementsJson,
                    };

        return await query.ToListAsync(ct);
    }

    private static int ReadGrantValue(string? grantsJson, string camelKey, string snakeKey)
    {
        if (string.IsNullOrWhiteSpace(grantsJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(grantsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;
            if (doc.RootElement.TryGetProperty(camelKey, out var camelEl))
            {
                return TryReadInt(camelEl);
            }
            if (doc.RootElement.TryGetProperty(snakeKey, out var snakeEl))
            {
                return TryReadInt(snakeEl);
            }
            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int TryReadInt(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => Math.Max(0, i),
            JsonValueKind.String when int.TryParse(el.GetString(), out var s) => Math.Max(0, s),
            JsonValueKind.True => 1,
            _ => 0,
        };
    }

    private sealed class ActiveAddOnGrant
    {
        public string AddOnId { get; set; } = default!;
        public string AddOnCode { get; set; } = default!;
        public int Quantity { get; set; }
        public string GrantEntitlementsJson { get; set; } = "{}";
    }
}

/// <summary>
/// Canonical mock-credit entitlement keys. Maps the camelCase keys used inside
/// <see cref="BillingAddOn.GrantEntitlementsJson"/> to the snake_case tokens
/// stored in <see cref="MockEntitlementLedger.MockType"/>.
/// </summary>
public static class MockEntitlementKeys
{
    public const string MockFull = "mock_full";
    public const string MockLrw = "mock_lrw";
    public const string MockSub = "mock_sub";
    public const string MockPart = "mock_part";
    public const string MockDiagnostic = "mock_diagnostic";
    public const string MockFinalReadiness = "mock_final_readiness";
    public const string MockRemedial = "mock_remedial";
    public const string MockWriting = "mock_writing";
    public const string MockSpeakingSession = "mock_speaking_session";

    public static readonly IReadOnlyList<string> AllLedgerTypes = new[]
    {
        MockFull, MockLrw, MockSub, MockPart, MockDiagnostic, MockFinalReadiness,
        MockRemedial, MockWriting, MockSpeakingSession,
    };

    private static readonly Dictionary<string, string> _ledgerToCamel = new(StringComparer.OrdinalIgnoreCase)
    {
        [MockFull] = "mockFull",
        [MockLrw] = "mockLrw",
        [MockSub] = "mockSub",
        [MockPart] = "mockPart",
        [MockDiagnostic] = "mockDiagnostic",
        [MockFinalReadiness] = "mockFinalReadiness",
        [MockRemedial] = "mockRemedial",
        [MockWriting] = "mockWriting",
        [MockSpeakingSession] = "mockSpeakingSession",
    };

    private static readonly Dictionary<string, string> _camelToLedger = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mockFull"] = MockFull,
        ["mockLrw"] = MockLrw,
        ["mockSub"] = MockSub,
        ["mockPart"] = MockPart,
        ["mockDiagnostic"] = MockDiagnostic,
        ["mockFinalReadiness"] = MockFinalReadiness,
        ["mockRemedial"] = MockRemedial,
        ["mockWriting"] = MockWriting,
        ["mockSpeakingSession"] = MockSpeakingSession,
    };

    private static readonly Dictionary<string, string> _labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [MockFull] = "Full Mock",
        [MockLrw] = "LRW Mock",
        [MockSub] = "Sub-test Mock",
        [MockPart] = "Part Mock",
        [MockDiagnostic] = "Diagnostic Mock",
        [MockFinalReadiness] = "Final Readiness Mock",
        [MockRemedial] = "Remedial Mock",
        [MockWriting] = "Writing Mock",
        [MockSpeakingSession] = "Speaking Mock Session",
    };

    /// <summary>
    /// Accepts either a camelCase grant key ("mockFull") or the snake_case
    /// ledger token ("mock_full") or a MockTypes.* token ("full") and returns
    /// the canonical ledger string, or null if unknown.
    /// </summary>
    public static string? NormaliseLedgerType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        // Already a canonical ledger token? Return the literal constant (matches case).
        foreach (var ledgerKey in _ledgerToCamel.Keys)
        {
            if (string.Equals(ledgerKey, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return ledgerKey;
            }
        }
        if (_camelToLedger.TryGetValue(trimmed, out var ledger)) return ledger;
        // Map MockTypes.* tokens (e.g. "full") to mock_<token>.
        return trimmed.ToLowerInvariant() switch
        {
            "full" => MockFull,
            "lrw" => MockLrw,
            "sub" => MockSub,
            "part" => MockPart,
            "diagnostic" => MockDiagnostic,
            "final_readiness" => MockFinalReadiness,
            "remedial" => MockRemedial,
            "writing" => MockWriting,
            "speaking" or "speaking_session" => MockSpeakingSession,
            _ => null,
        };
    }

    /// <summary>Returns the camelCase grant-JSON key for a ledger token.</summary>
    public static string CamelKeyForLedger(string ledgerType)
        => _ledgerToCamel.TryGetValue(ledgerType, out var key) ? key : ledgerType;

    /// <summary>Returns the ledger token for a camelCase grant-JSON key, or null if unknown.</summary>
    public static string? LedgerTypeForGrantKey(string? camelKey)
    {
        if (string.IsNullOrWhiteSpace(camelKey)) return null;
        return _camelToLedger.TryGetValue(camelKey.Trim(), out var ledger) ? ledger : null;
    }

    public static string Label(string ledgerType)
        => _labels.TryGetValue(ledgerType, out var label) ? label : ledgerType;
}

/// <summary>Result of <see cref="MockEntitlementService.CheckAsync"/>.</summary>
public sealed record MockEntitlementCheck(
    bool Allowed,
    string MockType,
    int Remaining,
    int Granted,
    string Reason,
    string Message);

/// <summary>Result of <see cref="MockEntitlementService.DebitAsync"/>.</summary>
public sealed record MockEntitlementDebit(
    bool Success,
    string MockType,
    int Remaining,
    int Granted,
    string? LedgerEntryId,
    string Reason,
    string Message);

/// <summary>Result of <see cref="MockEntitlementService.SummariseAsync"/>.</summary>
public sealed record MockEntitlementSummary(
    string Tier,
    bool HasEligibleSubscription,
    bool IsTrial,
    IReadOnlyList<MockEntitlementSummaryItem> Items);

/// <summary>Per-mock-type roll-up rendered by the dashboard / paywall CTA.</summary>
public sealed record MockEntitlementSummaryItem(
    string MockType,
    string Label,
    int Granted,
    int Consumed,
    int Remaining,
    bool Unlimited);
