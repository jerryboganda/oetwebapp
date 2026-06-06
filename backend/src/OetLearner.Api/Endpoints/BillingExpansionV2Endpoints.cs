using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Phase 5-10 endpoints that didn't fit in BillingExpansionEndpoints:
///  - pause / resume / cancellation intent (Phase 6)
///  - card-update self-serve token redeem (Phase 5)
///  - bank account config CRUD (Phase 4)
///  - notification template CRUD + dispatch log (Phase 9)
///  - metrics CSV export (Phase 10)
/// </summary>
public static class BillingExpansionV2Endpoints
{
    public static IEndpointRouteBuilder MapBillingExpansionV2Endpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        var billing = v1.MapGroup("/billing").RequireAuthorization();
        billing.MapPost("/subscription/pause", PauseSubscription);
        billing.MapPost("/subscription/resume", ResumeSubscription);
        billing.MapPost("/subscription/cancel-intent", CreateCancelIntent);
        billing.MapPost("/subscription/cancel-intent/{id}/confirm", ConfirmCancelIntent);
        billing.MapPost("/subscription/cancel-intent/{id}/retain", RetainCancelIntent);
        billing.MapGet("/update-card/{token}", RedeemUpdateCardLink).AllowAnonymous();
        // "/me" must be before "/{region}" or the literal "me" is treated as a region
        billing.MapGet("/bank-accounts/me", GetMyBankAccounts);
        billing.MapGet("/bank-accounts/{region}", GetBankAccountsForRegion);

        var admin = v1.MapGroup("/admin/billing");
        admin.MapGet("/bank-accounts", ListBankAccounts).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/bank-accounts", UpsertBankAccount).RequireAuthorization("AdminBillingCatalogWrite");
        admin.MapDelete("/bank-accounts/{id}", DeleteBankAccount).RequireAuthorization("AdminBillingCatalogWrite");

        admin.MapGet("/notification-templates", ListNotificationTemplates).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/notification-templates", UpsertNotificationTemplate).RequireAuthorization("AdminBillingCatalogWrite");
        admin.MapDelete("/notification-templates/{id}", DeleteNotificationTemplate).RequireAuthorization("AdminBillingCatalogWrite");
        admin.MapGet("/notification-dispatch-log", ListDispatchLog).RequireAuthorization("AdminBillingRead");

        admin.MapGet("/metrics.csv", ExportMetricsCsv).RequireAuthorization("AdminBillingRead");

        admin.MapGet("/cancellation-intents", ListCancellationIntents).RequireAuthorization("AdminBillingRead");
        admin.MapGet("/deflection-rules", ListDeflectionRules).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/deflection-rules", UpsertDeflectionRule).RequireAuthorization("AdminBillingCatalogWrite");
        admin.MapDelete("/deflection-rules/{id}", DeleteDeflectionRule).RequireAuthorization("AdminBillingCatalogWrite");

        return app;
    }

    // ── Pause / Resume ────────────────────────────────────────────

    private static async Task<Results<Ok<Subscription>, BadRequest<string>>> PauseSubscription(HttpContext http, PauseRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (sub is null) return TypedResults.BadRequest("No subscription found.");

        DateTimeOffset? until = request.Days is > 0 ? DateTimeOffset.UtcNow.AddDays(request.Days.Value) : null;
        try
        {
            SubscriptionStateMachine.Pause(sub, until, request.Reason ?? "learner_requested_pause");
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(sub);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<Subscription>, BadRequest<string>>> ResumeSubscription(HttpContext http, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (sub is null) return TypedResults.BadRequest("No subscription found.");
        try
        {
            SubscriptionStateMachine.Resume(sub, "learner_requested_resume");
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(sub);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    // ── Cancellation intent (deflection flow) ─────────────────────

    private static async Task<Ok<CancellationIntentResponse>> CreateCancelIntent(HttpContext http, CancelIntentCreateRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct)
            ?? throw new InvalidOperationException("No subscription found.");

        var now = DateTimeOffset.UtcNow;
        var intent = new CancellationIntent
        {
            Id = Guid.NewGuid().ToString("N"),
            SubscriptionId = sub.Id,
            UserId = userId,
            Reason = request.Reason,
            ReasonDetail = request.ReasonDetail,
            Status = "started",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var rule = await db.DeflectionRules
            .Where(r => r.IsActive && r.TriggerReason == request.Reason)
            .FirstOrDefaultAsync(ct);

        string? offeredCoupon = null;
        if (rule is not null)
        {
            var priorOffers = await db.CancellationIntents
                .CountAsync(i => i.UserId == userId && i.OfferedCouponCode == rule.OfferedCouponCode, ct);
            var tenureDays = (now - sub.StartedAt).TotalDays;
            if (priorOffers < rule.MaxOffersPerUser && tenureDays >= rule.MinTenureDays)
            {
                offeredCoupon = rule.OfferedCouponCode;
                intent.OfferedCouponCode = offeredCoupon;
                intent.Status = "offered_coupon";
            }
        }

        db.CancellationIntents.Add(intent);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CancellationIntentResponse(intent.Id, intent.Status, offeredCoupon));
    }

    private static async Task<Results<Ok<Subscription>, BadRequest<string>>> ConfirmCancelIntent(HttpContext http, string id, LearnerDbContext db, CancellationToken ct)
    {
        // Object-level authorization: scope the intent (and its subscription) to the
        // caller so a learner can only confirm-cancel their OWN intent. Without this,
        // any authenticated user could cancel another user's subscription by id.
        var userId = http.UserId();
        var intent = await db.CancellationIntents.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
        if (intent is null) return TypedResults.BadRequest("Intent not found.");
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == intent.SubscriptionId && s.UserId == userId, ct);
        if (sub is null) return TypedResults.BadRequest("Subscription not found.");

        SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Cancelled, "learner_confirmed_cancel");
        intent.Status = "confirmed_cancel";
        intent.ResolvedAt = DateTimeOffset.UtcNow;
        intent.UpdatedAt = intent.ResolvedAt.Value;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(sub);
    }

    private static async Task<Results<Ok<CancellationIntent>, BadRequest<string>>> RetainCancelIntent(HttpContext http, string id, LearnerDbContext db, CancellationToken ct)
    {
        // Object-level authorization: only the owner may retain their own intent.
        var userId = http.UserId();
        var intent = await db.CancellationIntents.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);
        if (intent is null) return TypedResults.BadRequest("Intent not found.");
        intent.Status = "retained";
        intent.ResolvedAt = DateTimeOffset.UtcNow;
        intent.UpdatedAt = intent.ResolvedAt.Value;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(intent);
    }

    // ── Card update token redeem ──────────────────────────────────

    private static async Task<Results<Ok<UpdateCardRedeemResponse>, NotFound>> RedeemUpdateCardLink(string token, LearnerDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var link = await db.PaymentMethodUpdateLinks
            .FirstOrDefaultAsync(l => l.Token == token && l.ExpiresAt > now && l.UsedAt == null, ct);
        if (link is null) return TypedResults.NotFound();
        link.UsedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new UpdateCardRedeemResponse(link.UserId, link.SubscriptionId));
    }

    // ── Bank accounts ─────────────────────────────────────────────

    private static async Task<Ok<List<BankAccountConfig>>> GetMyBankAccounts(HttpContext http, LearnerDbContext db, CancellationToken ct)
    {
        var userId = http.UserId();
        var region = await db.ApplicationUserAccounts
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredRegion)
            .FirstOrDefaultAsync(ct) ?? "ROW";
        var rows = await db.BankAccountConfigs
            .Where(b => b.IsActive && (b.Region == region || b.Region == "ROW"))
            .OrderBy(b => b.Region == "ROW" ? 1 : 0)
            .ThenBy(b => b.BankName)
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Ok<List<BankAccountConfig>>> GetBankAccountsForRegion(string region, LearnerDbContext db, CancellationToken ct)
    {
        var normalized = region.ToUpperInvariant();
        var rows = await db.BankAccountConfigs
            .Where(b => b.IsActive && b.Region == normalized)
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Ok<List<BankAccountConfig>>> ListBankAccounts(LearnerDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.BankAccountConfigs.OrderBy(b => b.Region).ThenBy(b => b.Currency).ToListAsync(ct));

    private static async Task<Ok<BankAccountConfig>> UpsertBankAccount(BankAccountConfigUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.BankAccountConfigs.FirstOrDefaultAsync(b => b.Region == request.Region.ToUpperInvariant() && b.Currency == request.Currency.ToUpperInvariant(), ct);
        if (row is null)
        {
            row = new BankAccountConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Region = request.Region.ToUpperInvariant(),
                Currency = request.Currency.ToUpperInvariant(),
                BankName = request.BankName,
                AccountHolderName = request.AccountHolderName,
                Iban = request.Iban,
                SwiftBic = request.SwiftBic,
                AccountNumber = request.AccountNumber,
                RoutingOrSortCode = request.RoutingOrSortCode,
                InstructionsMarkdown = request.InstructionsMarkdown,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.BankAccountConfigs.Add(row);
        }
        else
        {
            row.BankName = request.BankName;
            row.AccountHolderName = request.AccountHolderName;
            row.Iban = request.Iban;
            row.SwiftBic = request.SwiftBic;
            row.AccountNumber = request.AccountNumber;
            row.RoutingOrSortCode = request.RoutingOrSortCode;
            row.InstructionsMarkdown = request.InstructionsMarkdown;
            row.IsActive = request.IsActive;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(row);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteBankAccount(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.BankAccountConfigs.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.BankAccountConfigs.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── Notification templates ────────────────────────────────────

    private static async Task<Ok<List<BillingNotificationTemplate>>> ListNotificationTemplates(LearnerDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.BillingNotificationTemplates.OrderBy(t => t.Code).ThenBy(t => t.Channel).ToListAsync(ct));

    private static async Task<Ok<BillingNotificationTemplate>> UpsertNotificationTemplate(NotificationTemplateUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.BillingNotificationTemplates.FirstOrDefaultAsync(t =>
            t.Code == request.Code && t.Channel == request.Channel && t.LocaleTag == (request.LocaleTag ?? "en"), ct);
        if (row is null)
        {
            row = new BillingNotificationTemplate
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = request.Code,
                Channel = request.Channel,
                LocaleTag = request.LocaleTag ?? "en",
                Subject = request.Subject,
                BodyTemplate = request.BodyTemplate,
                VariablesJson = request.VariablesJson ?? "[]",
                Version = 1,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.BillingNotificationTemplates.Add(row);
        }
        else
        {
            row.Subject = request.Subject;
            row.BodyTemplate = request.BodyTemplate;
            row.VariablesJson = request.VariablesJson ?? "[]";
            row.IsActive = request.IsActive;
            row.Version += 1;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(row);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteNotificationTemplate(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.BillingNotificationTemplates.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.BillingNotificationTemplates.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<BillingNotificationDispatchLog>>> ListDispatchLog(LearnerDbContext db, [FromQuery] string? userId, CancellationToken ct)
    {
        var q = db.BillingNotificationDispatchLogs.AsQueryable();
        if (!string.IsNullOrEmpty(userId)) q = q.Where(l => l.UserId == userId);
        return TypedResults.Ok(await q.OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync(ct));
    }

    // ── Cancellation intent + deflection admin ────────────────────

    private static async Task<Ok<List<CancellationIntent>>> ListCancellationIntents(LearnerDbContext db, [FromQuery] string? status, CancellationToken ct)
    {
        var q = db.CancellationIntents.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(i => i.Status == status);
        return TypedResults.Ok(await q.OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(ct));
    }

    private static async Task<Ok<List<DeflectionRule>>> ListDeflectionRules(LearnerDbContext db, CancellationToken ct)
        => TypedResults.Ok(await db.DeflectionRules.OrderBy(r => r.TriggerReason).ToListAsync(ct));

    private static async Task<Ok<DeflectionRule>> UpsertDeflectionRule(DeflectionRuleUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.DeflectionRules.FirstOrDefaultAsync(r => r.TriggerReason == request.TriggerReason, ct);
        if (row is null)
        {
            row = new DeflectionRule
            {
                Id = Guid.NewGuid().ToString("N"),
                TriggerReason = request.TriggerReason,
                OfferedCouponCode = request.OfferedCouponCode,
                MinTenureDays = request.MinTenureDays,
                MaxOffersPerUser = request.MaxOffersPerUser,
                IsActive = request.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.DeflectionRules.Add(row);
        }
        else
        {
            row.OfferedCouponCode = request.OfferedCouponCode;
            row.MinTenureDays = request.MinTenureDays;
            row.MaxOffersPerUser = request.MaxOffersPerUser;
            row.IsActive = request.IsActive;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(row);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteDeflectionRule(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.DeflectionRules.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.DeflectionRules.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── CSV export ────────────────────────────────────────────────

    private static async Task<IResult> ExportMetricsCsv(LearnerDbContext db, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? code, [FromQuery] string? region, CancellationToken ct)
    {
        var rows = await db.BillingMetricDailies
            .Where(m => m.MetricDate >= from && m.MetricDate <= to
                && (code == null || m.MetricCode == code)
                && (region == null || m.Region == region))
            .OrderBy(m => m.MetricDate)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("date,metric_code,region,currency,value,computed_at");
        foreach (var r in rows)
        {
            sb.Append(r.MetricDate.ToString("yyyy-MM-dd")).Append(',')
              .Append(EscapeCsv(r.MetricCode)).Append(',')
              .Append(EscapeCsv(r.Region)).Append(',')
              .Append(EscapeCsv(r.Currency)).Append(',')
              .Append(r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(r.ComputedAt.ToString("O")).AppendLine();
        }
        return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"billing-metrics-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

// ── DTOs ──────────────────────────────────────────────────────────

public sealed record PauseRequest(int? Days, string? Reason);
public sealed record CancelIntentCreateRequest(string Reason, string? ReasonDetail);
public sealed record CancellationIntentResponse(string Id, string Status, string? OfferedCouponCode);
public sealed record UpdateCardRedeemResponse(string UserId, string SubscriptionId);

public sealed record BankAccountConfigUpsertRequest(
    string Region,
    string Currency,
    string BankName,
    string AccountHolderName,
    string? Iban,
    string? SwiftBic,
    string? AccountNumber,
    string? RoutingOrSortCode,
    string? InstructionsMarkdown,
    bool IsActive);

public sealed record NotificationTemplateUpsertRequest(
    string Code,
    string Channel,
    string? LocaleTag,
    string? Subject,
    string BodyTemplate,
    string? VariablesJson,
    bool IsActive);

public sealed record DeflectionRuleUpsertRequest(
    string TriggerReason,
    string OfferedCouponCode,
    int MinTenureDays,
    int MaxOffersPerUser,
    bool IsActive);

file static class BillingExpansionV2HttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
