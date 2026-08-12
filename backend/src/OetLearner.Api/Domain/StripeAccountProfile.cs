using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// An admin-managed Stripe account (spec 2026-08 §8 — multiple Stripe account
/// support). The admin can register several Stripe accounts (test/live, different
/// businesses), mark exactly one as the default used for checkout + webhooks, and
/// rotate credentials without a redeploy.
///
/// Secrets (<see cref="SecretKeyEncrypted"/>, <see cref="WebhookSecretEncrypted"/>)
/// are encrypted at rest with ASP.NET Data Protection via
/// <c>IRuntimeSettingsProvider.Protect</c> — the same scheme as the legacy single
/// RuntimeSettings Stripe key — and are NEVER returned to clients; list endpoints
/// expose only presence flags and a last-4 hint. The publishable key is
/// client-side by design and may be shown in full.
///
/// Resolution order (RuntimeSettingsProvider.Merge): default active profile →
/// legacy RuntimeSettings override → env/appsettings.
/// </summary>
[Index(nameof(IsDefault))]
[Index(nameof(IsActive))]
public class StripeAccountProfile
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Admin-facing label, e.g. "UK Ltd — live" or "Test sandbox".</summary>
    [MaxLength(128)]
    public string Label { get; set; } = default!;

    /// <summary>"test" | "live" — informational badge; the key itself decides.</summary>
    [MaxLength(8)]
    public string Mode { get; set; } = "test";

    /// <summary>pk_… — safe for the browser, returned in full to admins.</summary>
    [MaxLength(256)]
    public string? PublishableKey { get; set; }

    /// <summary>sk_… encrypted with Data Protection. Never returned to clients.</summary>
    public string SecretKeyEncrypted { get; set; } = default!;

    /// <summary>whsec_… encrypted with Data Protection. Never returned to clients.</summary>
    public string? WebhookSecretEncrypted { get; set; }

    /// <summary>acct_… captured by the Save &amp; Test Connection check.</summary>
    [MaxLength(64)]
    public string? StripeAccountId { get; set; }

    /// <summary>Optional routing hint (comma-separated ISO country codes) for future
    /// per-region account routing. Informational for now — the default account wins.</summary>
    [MaxLength(256)]
    public string? RoutingCountriesCsv { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Exactly one profile should be default; enforced by the set-default endpoint.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Result of the last test-connection attempt ("ok" | error text | null = never tested).</summary>
    [MaxLength(512)]
    public string? LastTestResult { get; set; }

    public DateTimeOffset? LastTestedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
