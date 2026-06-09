using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class BillingSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapBillingSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/v1/subscriptions/me").RequireAuthorization();

        me.MapGet("/", GetMySubscription);
        me.MapGet("/list", ListMySubscriptions);
        me.MapGet("/invoices", GetMyInvoices);
        me.MapPost("/portal-session", CreatePortalSession);
        me.MapPost("/cancel", CancelLatestSubscription);
        me.MapPost("/pause", RequestFreezeLatestSubscription);
        me.MapPost("/resume", ResumeLatestSubscription);

        var subs = app.MapGroup("/v1/subscriptions").RequireAuthorization();
        subs.MapPost("/{subscriptionId}/request-freeze", RequestFreeze);
        subs.MapPost("/{subscriptionId}/resume", Resume);

        return app;
    }

    private static string GetUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static async Task<Results<Ok<SubscriptionMeDto>, NotFound>> GetMySubscription(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var sub = await QueryUserSubscriptions(db, GetUserId(http))
            .FirstOrDefaultAsync(ct);
        return sub is null ? TypedResults.NotFound() : TypedResults.Ok(await ProjectAsync(db, sub, ct));
    }

    private static async Task<Ok<object>> ListMySubscriptions(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var rows = await QueryUserSubscriptions(db, GetUserId(http)).ToListAsync(ct);
        var items = new List<SubscriptionMeDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await ProjectAsync(db, row, ct));
        }
        return TypedResults.Ok((object)new { items });
    }

    private static async Task<Ok<IEnumerable<SubscriptionInvoiceDto>>> GetMyInvoices(
        HttpContext http,
        ISubscriptionService svc,
        CancellationToken ct)
    {
        var invoices = await svc.ListInvoicesAsync(GetUserId(http), ct);
        return TypedResults.Ok(invoices);
    }

    private static async Task<Results<Ok<object>, BadRequest<string>>> CreatePortalSession(
        HttpContext http,
        PortalSessionRequest request,
        ISubscriptionService svc,
        CancellationToken ct)
    {
        try
        {
            var url = await svc.CreatePortalSessionAsync(GetUserId(http), request.ReturnUrl, ct);
            return TypedResults.Ok((object)new { url });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> CancelLatestSubscription(
        HttpContext http,
        CancelRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        var sub = await QueryUserSubscriptions(db, userId).FirstOrDefaultAsync(ct);
        if (sub is null) return TypedResults.BadRequest("No subscription found.");

        var now = DateTimeOffset.UtcNow;
        CloseOpenFreezeRows(db, sub, now, "cancelled");
        SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Cancelled, "learner_cancel_subscription");
        sub.ExpiresAt = now;
        sub.ChangedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ProjectAsync(db, sub, ct));
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> RequestFreezeLatestSubscription(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var sub = await QueryUserSubscriptions(db, GetUserId(http)).FirstOrDefaultAsync(ct);
        if (sub is null) return TypedResults.BadRequest("No subscription found.");
        return await RequestFreezeCore(db, sub, "candidate_requested_freeze", ct);
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> ResumeLatestSubscription(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var sub = await QueryUserSubscriptions(db, GetUserId(http)).FirstOrDefaultAsync(ct);
        if (sub is null) return TypedResults.BadRequest("No subscription found.");
        return await ResumeCore(db, sub, ct);
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> RequestFreeze(
        string subscriptionId,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == GetUserId(http), ct);
        if (sub is null) return TypedResults.BadRequest("Subscription not found.");
        return await RequestFreezeCore(db, sub, "candidate_requested_freeze", ct);
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> Resume(
        string subscriptionId,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == GetUserId(http), ct);
        if (sub is null) return TypedResults.BadRequest("Subscription not found.");
        return await ResumeCore(db, sub, ct);
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> RequestFreezeCore(
        LearnerDbContext db,
        Subscription sub,
        string reason,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (sub.Status != SubscriptionStatus.Active && sub.Status != SubscriptionStatus.Trial)
        {
            return TypedResults.BadRequest("Only active subscriptions can request a freeze.");
        }
        var remaining = CalculateRemainingDays(sub, now);
        if (remaining <= 0) return TypedResults.BadRequest("A subscription with no remaining days cannot be frozen.");
        if (sub.TotalFreezeDaysUsed >= sub.MaxFreezeDaysAllowed) return TypedResults.BadRequest("Freeze allowance has already been used.");
        var hasPending = await db.SubscriptionFreezes.AnyAsync(f => f.SubscriptionId == sub.Id && f.RequestStatus == "pending", ct);
        if (hasPending) return TypedResults.BadRequest("A freeze request is already pending.");

        var freeze = new SubscriptionFreeze
        {
            Id = $"subfreeze-{Guid.NewGuid():N}",
            SubscriptionId = sub.Id,
            UserId = sub.UserId,
            RequestedBy = "candidate",
            RequestStatus = "pending",
            FreezeRequestDate = now,
            FreezeReason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SubscriptionFreezes.Add(freeze);
        SubscriptionStateMachine.Transition(sub, SubscriptionStatus.FreezeRequested, "candidate_request_freeze");
        sub.PendingFreezeRequestDate = now;
        sub.ChangedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ProjectAsync(db, sub, ct));
    }

    private static async Task<Results<Ok<SubscriptionMeDto>, BadRequest<string>>> ResumeCore(
        LearnerDbContext db,
        Subscription sub,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (sub.Status != SubscriptionStatus.Frozen)
        {
            return TypedResults.BadRequest("Only frozen subscriptions can be resumed.");
        }

        var open = await db.SubscriptionFreezes
            .Where(f => f.SubscriptionId == sub.Id && f.RequestStatus == "approved" && f.FreezeEndDate == null)
            .OrderByDescending(f => f.FreezeStartDate)
            .FirstOrDefaultAsync(ct);
        if (open is null || open.FreezeStartDate is null)
        {
            return TypedResults.BadRequest("Open freeze record was not found.");
        }

        var used = Math.Max(1, (int)Math.Ceiling((now - open.FreezeStartDate.Value).TotalDays));
        var allowanceRemaining = Math.Max(0, sub.MaxFreezeDaysAllowed - sub.TotalFreezeDaysUsed);
        used = Math.Min(used, allowanceRemaining);
        var preserved = Math.Max(0, sub.PreservedRemainingDays ?? open.PreservedRemainingDaysAtFreeze ?? 0);

        open.FreezeEndDate = now;
        open.FreezeDaysUsed = used;
        open.UpdatedAt = now;
        sub.TotalFreezeDaysUsed += used;
        sub.ExpiresAt = now.AddDays(preserved);
        sub.PreservedRemainingDays = null;
        sub.FrozenSince = null;
        sub.PendingFreezeRequestDate = null;
        SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Active, "subscription_resume");
        sub.ChangedAt = now;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ProjectAsync(db, sub, ct));
    }

    private static IQueryable<Subscription> QueryUserSubscriptions(LearnerDbContext db, string userId)
        => db.Subscriptions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.ChangedAt)
            .ThenByDescending(s => s.StartedAt);

    private static async Task<SubscriptionMeDto> ProjectAsync(LearnerDbContext db, Subscription sub, CancellationToken ct)
    {
        var plan = await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == sub.PlanId || p.Id == sub.PlanId, ct);
        var now = DateTimeOffset.UtcNow;
        var remaining = CalculateRemainingDays(sub, now);
        var status = NormalizeStatusForAccess(sub, now);
        var duration = Math.Max(1, sub.AccessDurationDays);
        var freezeAllowanceRemaining = Math.Max(0, sub.MaxFreezeDaysAllowed - sub.TotalFreezeDaysUsed);

        return new SubscriptionMeDto(
            SubscriptionId: sub.Id,
            Status: status,
            PlanCode: plan?.Code ?? sub.PlanId,
            PlanName: plan?.Name ?? sub.PlanId,
            Price: sub.PriceAmount,
            Currency: sub.Currency,
            Interval: sub.Interval,
            StartedAt: sub.StartedAt == default ? null : sub.StartedAt,
            NextRenewalAt: sub.NextRenewalAt,
            CancelledAt: sub.Status == SubscriptionStatus.Cancelled ? sub.ChangedAt : null,
            PausedUntil: sub.PausedUntil,
            CancelAtPeriodEnd: false,
            TrialEndsAt: sub.Status == SubscriptionStatus.Trial ? sub.ExpiresAt : null,
            ProductCategory: plan?.ProductCategory,
            StartDate: sub.StartedAt == default ? null : sub.StartedAt,
            EndDate: sub.ExpiresAt,
            DurationDays: duration,
            RemainingDays: remaining,
            ExpiringSoon: (status is "active" or "trial" or "freeze_requested") && remaining < 14,
            TotalFreezeDaysUsed: sub.TotalFreezeDaysUsed,
            MaxFreezeDays: sub.MaxFreezeDaysAllowed,
            FreezeAllowanceRemaining: freezeAllowanceRemaining,
            PreservedRemainingDays: sub.PreservedRemainingDays,
            PendingFreezeRequestDate: sub.PendingFreezeRequestDate,
            FrozenSince: sub.FrozenSince);
    }

    private static int CalculateRemainingDays(Subscription sub, DateTimeOffset now)
    {
        if (sub.Status == SubscriptionStatus.Frozen)
        {
            return Math.Max(0, sub.PreservedRemainingDays ?? 0);
        }
        if (sub.ExpiresAt is null) return Math.Max(1, sub.AccessDurationDays);
        return Math.Max(0, (int)Math.Ceiling((sub.ExpiresAt.Value - now).TotalDays));
    }

    private static string NormalizeStatusForAccess(Subscription sub, DateTimeOffset now)
    {
        if (sub.Status != SubscriptionStatus.Frozen && sub.ExpiresAt is { } expiry && expiry <= now)
        {
            return "expired";
        }
        return sub.Status switch
        {
            SubscriptionStatus.PastDue => "past_due",
            SubscriptionStatus.FreezeRequested => "freeze_requested",
            _ => sub.Status.ToString().ToLowerInvariant(),
        };
    }

    private static void CloseOpenFreezeRows(LearnerDbContext db, Subscription sub, DateTimeOffset now, string reason)
    {
        foreach (var freeze in db.SubscriptionFreezes.Where(f => f.SubscriptionId == sub.Id && f.FreezeEndDate == null))
        {
            freeze.FreezeEndDate = now;
            freeze.RequestStatus = "completed";
            freeze.UpdatedAt = now;
        }
        sub.PendingFreezeRequestDate = null;
        sub.FrozenSince = null;
        sub.PreservedRemainingDays = null;
    }

    private sealed record PortalSessionRequest(string ReturnUrl);
    private sealed record CancelRequest(bool AtPeriodEnd = true);

    public sealed record SubscriptionMeDto(
        string SubscriptionId,
        string Status,
        string PlanCode,
        string PlanName,
        decimal Price,
        string Currency,
        string Interval,
        DateTimeOffset? StartedAt,
        DateTimeOffset? NextRenewalAt,
        DateTimeOffset? CancelledAt,
        DateTimeOffset? PausedUntil,
        bool CancelAtPeriodEnd,
        DateTimeOffset? TrialEndsAt,
        string? ProductCategory,
        DateTimeOffset? StartDate,
        DateTimeOffset? EndDate,
        int DurationDays,
        int RemainingDays,
        bool ExpiringSoon,
        int TotalFreezeDaysUsed,
        int MaxFreezeDays,
        int FreezeAllowanceRemaining,
        int? PreservedRemainingDays,
        DateTimeOffset? PendingFreezeRequestDate,
        DateTimeOffset? FrozenSince);
}
