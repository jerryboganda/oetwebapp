using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.TutorBook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing endpoints for The Tutor Book — First Edition 2026.
///
/// <para>Auth: a buyer must have an active Subscription with
/// <c>TutorBookUnlocked=true</c> (set on purchase of <c>tutor-book</c>,
/// <c>tutor-book-addon</c>, or <c>full-condensed-medicine-tbook</c>).</para>
/// </summary>
public static class TutorBookEndpoints
{
    public static IEndpointRouteBuilder MapTutorBookEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1/tutor-book").RequireAuthorization();
        v1.MapGet("/download", DownloadWatermarked);
        v1.MapGet("/audio-scripts", ListAudioScripts);
        v1.MapGet("/updates", ListUpdates);
        v1.MapGet("/telegram", GetTelegramLink);
        return app;
    }

    private sealed record AudioScript(string Chapter, string Title, string AudioUrl, string? TranscriptUrl);
    private sealed record TutorBookUpdateDto(string Id, string Title, string BodyMarkdown, DateTimeOffset PublishedAt, string Audience);
    private sealed record TelegramResponse(string? InviteUrl);

    private static async Task<Results<FileContentHttpResult, ForbidHttpResult, NotFound>> DownloadWatermarked(
        HttpContext http,
        LearnerDbContext db,
        ITutorBookWatermarkService watermark,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();

        var subscription = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && s.TutorBookUnlocked
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (subscription is null) return TypedResults.Forbid();

        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var account = await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var buyerName = learner?.DisplayName ?? account?.Email ?? "OET Learner";
        var buyerEmail = learner?.Email ?? account?.Email ?? "learner@oetwithdrhesham.co.uk";

        var (bytes, filename) = await watermark.GetWatermarkedAsync(buyerName, buyerEmail, subscription.StartedAt, ct);
        return TypedResults.File(bytes, "application/pdf", filename);
    }

    private static async Task<Results<Ok<List<AudioScript>>, ForbidHttpResult>> ListAudioScripts(
        HttpContext http,
        LearnerDbContext db,
        IConfiguration config,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();

        var unlocked = await db.Subscriptions.AsNoTracking()
            .AnyAsync(s => s.UserId == userId
                && s.TutorBookUnlocked
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial), ct);
        if (!unlocked) return TypedResults.Forbid();

        // Admin-configurable JSON list under TutorBook:AudioScripts
        var scripts = config.GetSection("TutorBook:AudioScripts").Get<List<AudioScript>>() ?? new List<AudioScript>();
        return TypedResults.Ok(scripts);
    }

    private static async Task<Results<Ok<List<TutorBookUpdateDto>>, ForbidHttpResult>> ListUpdates(
        HttpContext http,
        LearnerDbContext db,
        IConfiguration config,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();

        var subscription = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && s.TutorBookUnlocked
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .Join(db.BillingPlans.AsNoTracking(), s => s.PlanId, p => p.Code, (s, p) => new { s.Id, p.Profession })
            .FirstOrDefaultAsync(ct);
        if (subscription is null) return TypedResults.Forbid();

        // Filter updates by buyer profession ("all" matches everything)
        var profession = subscription.Profession ?? "all";
        var updates = config.GetSection("TutorBook:Updates").Get<List<TutorBookUpdateDto>>() ?? new List<TutorBookUpdateDto>();
        var filtered = updates
            .Where(u => u.Audience == "all" || u.Audience.Equals(profession, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(u => u.PublishedAt)
            .ToList();
        return TypedResults.Ok(filtered);
    }

    private static async Task<Results<Ok<TelegramResponse>, ForbidHttpResult>> GetTelegramLink(
        HttpContext http,
        LearnerDbContext db,
        IConfiguration config,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();

        var unlocked = await db.Subscriptions.AsNoTracking()
            .AnyAsync(s => s.UserId == userId
                && s.TutorBookUnlocked
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial), ct);
        if (!unlocked) return TypedResults.Forbid();

        var url = config["TutorBook:TelegramInviteUrl"];
        return TypedResults.Ok(new TelegramResponse(url));
    }
}
