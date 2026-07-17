using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Mocks Module Phase 1 — per-user ledger of mock-credit consumption.
///
/// Each row records that one credit granted by an active <see cref="BillingAddOn"/>
/// (resolved via <c>BillingAddOn.GrantEntitlementsJson</c>) has been spent against a
/// particular <see cref="MockAttempt"/>. The <see cref="OetWithDrHesham.Api.Services.MockEntitlementService"/>
/// is the only writer; it sums the granted entitlements minus rows in this ledger
/// to determine remaining mock credits per <see cref="MockType"/>.
///
/// Mock types map 1-to-1 with entitlement keys emitted in
/// <c>BillingAddOn.GrantEntitlementsJson</c> (e.g. <c>"mockFull": 5</c>,
/// <c>"mockWriting": 3</c>, <c>"mockSpeakingSession": 1</c>).
///
/// IDs are 64-char strings to match the existing convention used across
/// <see cref="MockAttempt.Id"/>, <see cref="BillingAddOn.Id"/>, etc.
/// </summary>
[Index(nameof(UserId), nameof(MockType))]
[Index(nameof(UserId), nameof(ConsumedAt))]
[Index(nameof(AddOnId))]
public class MockEntitlementLedger
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>References <see cref="BillingAddOn.Id"/> that granted the credit.</summary>
    [MaxLength(64)]
    public string AddOnId { get; set; } = default!;

    /// <summary>
    /// Canonical mock-type token consumed. Values mirror the entitlement keys in
    /// <see cref="BillingAddOn.GrantEntitlementsJson"/> — e.g. <c>mock_full</c>,
    /// <c>mock_writing</c>, <c>mock_speaking_session</c>.
    /// </summary>
    [MaxLength(32)]
    public string MockType { get; set; } = default!;

    public DateTimeOffset ConsumedAt { get; set; }

    /// <summary>The <see cref="MockAttempt.Id"/> that consumed the credit, if known.</summary>
    [MaxLength(64)]
    public string? MockAttemptId { get; set; }
}
