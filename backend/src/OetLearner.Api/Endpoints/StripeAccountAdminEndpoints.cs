using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;
using Stripe;
using System.Security.Claims;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin CRUD for multiple Stripe accounts (spec 2026-08 §8). Secrets are
/// encrypted at rest via <see cref="IRuntimeSettingsProvider.Protect"/> (Data
/// Protection — same scheme as the legacy RuntimeSettings Stripe key) and are
/// NEVER returned to clients: list responses carry only a presence flag and a
/// masked hint. The default active account's keys are overlaid onto effective
/// billing settings by RuntimeSettingsProvider.Merge, so checkout and webhook
/// verification pick up an account switch within the 30s settings cache window
/// (writes call Invalidate() for an immediate refresh).
/// </summary>
public static class StripeAccountAdminEndpoints
{
    public static IEndpointRouteBuilder MapStripeAccountAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/billing/stripe-accounts");

        group.MapGet("/", ListAccounts).RequireAuthorization("AdminBillingRead");
        group.MapPost("/", CreateAccount).WithAdminWrite("AdminBillingCatalogWrite");
        group.MapPut("/{id}", UpdateAccount).WithAdminWrite("AdminBillingCatalogWrite");
        group.MapPost("/{id}/set-default", SetDefault).WithAdminWrite("AdminBillingCatalogWrite");
        group.MapPost("/{id}/test-connection", TestConnection).WithAdminWrite("AdminBillingCatalogWrite");
        group.MapDelete("/{id}", DeleteAccount).WithAdminWrite("AdminBillingCatalogWrite");

        return app;
    }

    private static async Task<Ok<List<StripeAccountProfileDto>>> ListAccounts(
        LearnerDbContext db, IRuntimeSettingsProvider settings, CancellationToken ct)
    {
        var rows = await db.StripeAccountProfiles.AsNoTracking()
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(r => StripeAccountProfileDto.FromEntity(r, settings)).ToList());
    }

    private static async Task<Results<Ok<StripeAccountProfileDto>, BadRequest<string>>> CreateAccount(
        HttpContext http, StripeAccountUpsertRequest request, LearnerDbContext db,
        IRuntimeSettingsProvider settings, CancellationToken ct)
    {
        var label = request.Label?.Trim();
        if (string.IsNullOrWhiteSpace(label))
            return TypedResults.BadRequest("Label is required.");
        if (string.IsNullOrWhiteSpace(request.SecretKey))
            return TypedResults.BadRequest("Secret key is required.");
        if (!request.SecretKey.Trim().StartsWith("sk_", StringComparison.Ordinal)
            && !request.SecretKey.Trim().StartsWith("rk_", StringComparison.Ordinal))
            return TypedResults.BadRequest("Secret key must start with sk_ (or rk_ for a restricted key).");

        var now = DateTimeOffset.UtcNow;
        var makeDefault = request.IsDefault
                          || !await db.StripeAccountProfiles.AnyAsync(p => p.IsDefault, ct);
        if (makeDefault)
        {
            await ClearDefaultsAsync(db, ct);
        }

        var row = new StripeAccountProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            Mode = NormalizeMode(request.Mode, request.SecretKey),
            PublishableKey = NullIfEmpty(request.PublishableKey),
            SecretKeyEncrypted = settings.Protect(request.SecretKey.Trim()),
            WebhookSecretEncrypted = string.IsNullOrWhiteSpace(request.WebhookSecret)
                ? null
                : settings.Protect(request.WebhookSecret.Trim()),
            RoutingCountriesCsv = NormalizeCountriesCsv(request.RoutingCountriesCsv),
            IsActive = request.IsActive ?? true,
            IsDefault = makeDefault,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.StripeAccountProfiles.Add(row);
        AddAudit(db, http, "stripe_account.create", row.Id,
            $"Created Stripe account \"{row.Label}\" ({row.Mode}){(row.IsDefault ? " and set default" : string.Empty)}.");
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return TypedResults.Ok(StripeAccountProfileDto.FromEntity(row, settings));
    }

    private static async Task<Results<Ok<StripeAccountProfileDto>, NotFound, BadRequest<string>>> UpdateAccount(
        HttpContext http, string id, StripeAccountUpsertRequest request, LearnerDbContext db,
        IRuntimeSettingsProvider settings, CancellationToken ct)
    {
        var row = await db.StripeAccountProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return TypedResults.NotFound();

        if (!string.IsNullOrWhiteSpace(request.Label)) row.Label = request.Label.Trim();
        if (!string.IsNullOrWhiteSpace(request.Mode)) row.Mode = NormalizeMode(request.Mode, null);
        if (request.PublishableKey is not null) row.PublishableKey = NullIfEmpty(request.PublishableKey);
        if (request.RoutingCountriesCsv is not null) row.RoutingCountriesCsv = NormalizeCountriesCsv(request.RoutingCountriesCsv);

        // Rotate-only semantics: an omitted/blank secret keeps the stored one.
        var rotatedSecret = false;
        if (!string.IsNullOrWhiteSpace(request.SecretKey))
        {
            var secret = request.SecretKey.Trim();
            if (!secret.StartsWith("sk_", StringComparison.Ordinal) && !secret.StartsWith("rk_", StringComparison.Ordinal))
                return TypedResults.BadRequest("Secret key must start with sk_ (or rk_ for a restricted key).");
            row.SecretKeyEncrypted = settings.Protect(secret);
            row.Mode = NormalizeMode(request.Mode, secret);
            row.StripeAccountId = null; // stale until re-tested
            row.LastTestResult = null;
            row.LastTestedAt = null;
            rotatedSecret = true;
        }
        if (request.WebhookSecret is not null)
        {
            row.WebhookSecretEncrypted = string.IsNullOrWhiteSpace(request.WebhookSecret)
                ? null
                : settings.Protect(request.WebhookSecret.Trim());
        }

        if (request.IsActive is not null)
        {
            if (request.IsActive == false && row.IsDefault)
                return TypedResults.BadRequest("Deactivate is blocked for the default account — set another account as default first.");
            row.IsActive = request.IsActive.Value;
        }

        if (request.IsDefault == true && !row.IsDefault)
        {
            if (!row.IsActive) return TypedResults.BadRequest("Only an active account can be the default.");
            await ClearDefaultsAsync(db, ct);
            row.IsDefault = true;
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(db, http, "stripe_account.update", row.Id,
            $"Updated Stripe account \"{row.Label}\"{(rotatedSecret ? " (secret key rotated)" : string.Empty)}.");
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return TypedResults.Ok(StripeAccountProfileDto.FromEntity(row, settings));
    }

    private static async Task<Results<Ok<StripeAccountProfileDto>, NotFound, BadRequest<string>>> SetDefault(
        HttpContext http, string id, LearnerDbContext db, IRuntimeSettingsProvider settings, CancellationToken ct)
    {
        var row = await db.StripeAccountProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        if (!row.IsActive) return TypedResults.BadRequest("Only an active account can be the default.");

        await ClearDefaultsAsync(db, ct);
        row.IsDefault = true;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(db, http, "stripe_account.set_default", row.Id,
            $"Set Stripe account \"{row.Label}\" ({row.Mode}) as the default for checkout and webhooks.");
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return TypedResults.Ok(StripeAccountProfileDto.FromEntity(row, settings));
    }

    /// <summary>Save &amp; Test Connection — calls Stripe <c>GET /v1/account</c> with the
    /// stored key (per-request ApiKey, never the global) and records the acct_… id.</summary>
    private static async Task<Results<Ok<StripeAccountProfileDto>, NotFound, BadRequest<string>>> TestConnection(
        HttpContext http, string id, LearnerDbContext db, IRuntimeSettingsProvider settings, CancellationToken ct)
    {
        var row = await db.StripeAccountProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return TypedResults.NotFound();

        var secret = settings.Unprotect(row.SecretKeyEncrypted);
        if (string.IsNullOrWhiteSpace(secret))
            return TypedResults.BadRequest("Stored secret key could not be decrypted — rotate the key and try again.");

        row.LastTestedAt = DateTimeOffset.UtcNow;
        try
        {
            var account = await new AccountService().GetSelfAsync(
                requestOptions: new RequestOptions { ApiKey = secret },
                cancellationToken: ct);
            row.StripeAccountId = account.Id;
            row.LastTestResult = "ok";
            AddAudit(db, http, "stripe_account.test_connection", row.Id,
                $"Stripe connection test for \"{row.Label}\" succeeded ({account.Id}).");
        }
        catch (StripeException ex)
        {
            row.LastTestResult = Truncate($"failed: {ex.StripeError?.Message ?? ex.Message}", 512);
            AddAudit(db, http, "stripe_account.test_connection", row.Id,
                $"Stripe connection test for \"{row.Label}\" failed: {ex.StripeError?.Code ?? ex.Message}");
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(StripeAccountProfileDto.FromEntity(row, settings));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> DeleteAccount(
        HttpContext http, string id, LearnerDbContext db, IRuntimeSettingsProvider settings, CancellationToken ct)
    {
        var row = await db.StripeAccountProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        if (row.IsDefault)
            return TypedResults.BadRequest("The default account cannot be deleted — set another account as default first.");

        db.StripeAccountProfiles.Remove(row);
        AddAudit(db, http, "stripe_account.delete", row.Id,
            $"Deleted Stripe account \"{row.Label}\" ({row.Mode}).");
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return TypedResults.NoContent();
    }

    // ── helpers ─────────────────────────────────────────────────────

    private static Task<int> ClearDefaultsAsync(LearnerDbContext db, CancellationToken ct)
        => db.StripeAccountProfiles.Where(p => p.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false), ct);

    private static void AddAudit(LearnerDbContext db, HttpContext http, string action, string resourceId, string details)
        => db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = http.AdminId(),
            ActorName = http.AdminName(),
            Action = action,
            ResourceType = "StripeAccountProfile",
            ResourceId = resourceId,
            Details = details,
        });

    /// <summary>"live"/"test" from the explicit request value, else inferred from the key prefix.</summary>
    private static string NormalizeMode(string? requested, string? secretKey)
    {
        var mode = requested?.Trim().ToLowerInvariant();
        if (mode is "live" or "test") return mode;
        if (!string.IsNullOrWhiteSpace(secretKey))
            return secretKey.Contains("_live_", StringComparison.Ordinal) ? "live" : "test";
        return "test";
    }

    private static string? NormalizeCountriesCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToUpperInvariant())
            .Where(p => p.Length is >= 2 and <= 3)
            .Distinct()
            .ToList();
        return parts.Count == 0 ? null : string.Join(',', parts);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}

// AdminId/AdminName mirrors the file-scoped helpers in BillingExpansionEndpoints.cs
// (they are `file static`, so each endpoints file carries its own copy).
file static class StripeAccountAdminHttpContextExtensions
{
    internal static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    internal static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}

public sealed record StripeAccountUpsertRequest(
    string? Label,
    string? Mode,
    string? PublishableKey,
    // Full sk_… key on create/rotate; omit or blank on update to keep the stored key.
    string? SecretKey,
    // whsec_…; null = keep, empty string = clear.
    string? WebhookSecret,
    string? RoutingCountriesCsv,
    bool? IsActive,
    bool IsDefault = false);

/// <summary>Masked admin view — secrets never leave the server. <see cref="SecretKeyHint"/>
/// is "sk_live_••••1234"-style, derived server-side from the decrypted key.</summary>
public sealed record StripeAccountProfileDto(
    string Id,
    string Label,
    string Mode,
    string? PublishableKey,
    bool HasSecretKey,
    string? SecretKeyHint,
    bool HasWebhookSecret,
    string? StripeAccountId,
    string? RoutingCountriesCsv,
    bool IsActive,
    bool IsDefault,
    string? LastTestResult,
    DateTimeOffset? LastTestedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static StripeAccountProfileDto FromEntity(StripeAccountProfile r, IRuntimeSettingsProvider settings) => new(
        r.Id,
        r.Label,
        r.Mode,
        r.PublishableKey,
        !string.IsNullOrEmpty(r.SecretKeyEncrypted),
        MaskSecret(settings.Unprotect(r.SecretKeyEncrypted)),
        !string.IsNullOrEmpty(r.WebhookSecretEncrypted),
        r.StripeAccountId,
        r.RoutingCountriesCsv,
        r.IsActive,
        r.IsDefault,
        r.LastTestResult,
        r.LastTestedAt,
        r.CreatedAt,
        r.UpdatedAt);

    private static string? MaskSecret(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;
        var prefixEnd = plain.LastIndexOf('_');
        var prefix = prefixEnd > 0 && prefixEnd <= 8 ? plain[..(prefixEnd + 1)] : "sk_";
        var last4 = plain.Length >= 4 ? plain[^4..] : plain;
        return $"{prefix}\u2022\u2022\u2022\u2022{last4}";
    }
}
