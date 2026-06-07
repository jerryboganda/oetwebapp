using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.TutorBook;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner + admin endpoints for The Tutor Book — First Edition 2026.
///
/// <para>Learner auth: a buyer must have an active Subscription with
/// <c>TutorBookUnlocked=true</c> (set on purchase of <c>tutor-book</c>,
/// <c>tutor-book-addon</c>, or <c>full-condensed-medicine-tbook</c>).</para>
///
/// <para>Admin auth: <c>AdminBillingCatalogWrite</c> for write endpoints,
/// <c>AdminBillingRead</c> for read-only audit endpoints.</para>
/// </summary>
public static class TutorBookEndpoints
{
    public static IEndpointRouteBuilder MapTutorBookEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1/tutor-book").RequireAuthorization();
        v1.MapGet("/download", DownloadWatermarked);
        v1.MapGet("/audio-scripts", ListLearnerAudioScripts);
        v1.MapGet("/updates", ListLearnerUpdates);
        v1.MapGet("/telegram", GetTelegramLink);

        var admin = app.MapGroup("/v1/admin/tutor-book").RequireAuthorization("AdminBillingCatalogWrite");
        admin.MapGet("/updates", AdminListUpdates);
        admin.MapPost("/updates", AdminUpsertUpdate).RequireRateLimiting("PerUserWrite");
        admin.MapDelete("/updates/{id}", AdminDeleteUpdate).RequireRateLimiting("PerUserWrite");
        admin.MapGet("/audio-scripts", AdminListAudioScripts);
        admin.MapPost("/audio-scripts", AdminUpsertAudioScript).RequireRateLimiting("PerUserWrite");
        admin.MapDelete("/audio-scripts/{id}", AdminDeleteAudioScript).RequireRateLimiting("PerUserWrite");

        return app;
    }

    private sealed record AudioScriptDto(string Id, string Chapter, string Title, string AudioUrl, string? TranscriptUrl, int DisplayOrder, bool IsPublished);
    private sealed record TutorBookUpdateDto(string Id, string Title, string BodyMarkdown, DateTimeOffset PublishedAt, string Audience, bool IsPublished);
    private sealed record TelegramResponse(string? InviteUrl);

    private sealed record AudioScriptUpsertRequest(string? Id, string Chapter, string Title, string AudioUrl, string? TranscriptUrl, int DisplayOrder, bool IsPublished = true);
    private sealed record TutorBookUpdateUpsertRequest(string? Id, string Title, string BodyMarkdown, string Audience = "all", bool IsPublished = true, DateTimeOffset? PublishedAt = null);

    // ── Learner: watermarked PDF download ────────────────────────────────

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

    // ── Learner: audio scripts + updates + telegram ──────────────────────

    private static async Task<Results<Ok<List<AudioScriptDto>>, ForbidHttpResult>> ListLearnerAudioScripts(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();
        var unlocked = await UserHasTutorBookAsync(db, userId, ct);
        if (!unlocked) return TypedResults.Forbid();

        var rows = await db.TutorBookAudioScripts.AsNoTracking()
            .Where(s => s.IsPublished)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Chapter)
            .Select(s => new AudioScriptDto(s.Id, s.Chapter, s.Title, s.AudioUrl, s.TranscriptUrl, s.DisplayOrder, s.IsPublished))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<List<TutorBookUpdateDto>>, ForbidHttpResult>> ListLearnerUpdates(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();

        var subscription = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId
                && s.TutorBookUnlocked
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .Join(db.BillingPlans.AsNoTracking(), s => s.PlanId, p => p.Code, (s, p) => new { p.Profession })
            .FirstOrDefaultAsync(ct);
        if (subscription is null) return TypedResults.Forbid();

        var profession = string.IsNullOrEmpty(subscription.Profession) ? "all" : subscription.Profession;
        var rows = await db.TutorBookUpdates.AsNoTracking()
            .Where(u => u.IsPublished && (u.Audience == "all" || u.Audience == profession))
            .OrderByDescending(u => u.PublishedAt)
            .Select(u => new TutorBookUpdateDto(u.Id, u.Title, u.BodyMarkdown, u.PublishedAt, u.Audience, u.IsPublished))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<TelegramResponse>, ForbidHttpResult>> GetTelegramLink(
        HttpContext http,
        LearnerDbContext db,
        IConfiguration config,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return TypedResults.Forbid();
        var unlocked = await UserHasTutorBookAsync(db, userId, ct);
        if (!unlocked) return TypedResults.Forbid();

        var url = config["TutorBook:TelegramInviteUrl"];
        return TypedResults.Ok(new TelegramResponse(url));
    }

    // ── Admin: updates CRUD ──────────────────────────────────────────────

    private static async Task<Ok<List<TutorBookUpdateDto>>> AdminListUpdates(
        LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.TutorBookUpdates.AsNoTracking()
            .OrderByDescending(u => u.PublishedAt)
            .Select(u => new TutorBookUpdateDto(u.Id, u.Title, u.BodyMarkdown, u.PublishedAt, u.Audience, u.IsPublished))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Ok<TutorBookUpdateDto>> AdminUpsertUpdate(
        HttpContext http,
        TutorBookUpdateUpsertRequest req,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var adminName = http.User.Identity?.Name;
        var now = DateTimeOffset.UtcNow;

        TutorBookUpdate row;
        if (!string.IsNullOrWhiteSpace(req.Id))
        {
            row = await db.TutorBookUpdates.FirstOrDefaultAsync(u => u.Id == req.Id, ct)
                ?? new TutorBookUpdate { Id = req.Id!, CreatedAt = now, CreatedByAdminId = adminId, CreatedByAdminName = adminName };
            if (row.CreatedAt == default)
            {
                row.CreatedAt = now;
                row.CreatedByAdminId = adminId;
                row.CreatedByAdminName = adminName;
                db.TutorBookUpdates.Add(row);
            }
        }
        else
        {
            row = new TutorBookUpdate
            {
                Id = $"tbu_{Guid.NewGuid():N}"[..32],
                CreatedAt = now,
                CreatedByAdminId = adminId,
                CreatedByAdminName = adminName,
            };
            db.TutorBookUpdates.Add(row);
        }

        row.Title = req.Title;
        row.BodyMarkdown = req.BodyMarkdown;
        row.Audience = string.IsNullOrWhiteSpace(req.Audience) ? "all" : req.Audience.Trim().ToLowerInvariant();
        row.IsPublished = req.IsPublished;
        row.PublishedAt = req.PublishedAt ?? now;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TutorBookUpdateDto(row.Id, row.Title, row.BodyMarkdown, row.PublishedAt, row.Audience, row.IsPublished));
    }

    private static async Task<Results<NoContent, NotFound>> AdminDeleteUpdate(
        string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.TutorBookUpdates.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        db.TutorBookUpdates.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── Admin: audio scripts CRUD ────────────────────────────────────────

    private static async Task<Ok<List<AudioScriptDto>>> AdminListAudioScripts(
        LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.TutorBookAudioScripts.AsNoTracking()
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Chapter)
            .Select(s => new AudioScriptDto(s.Id, s.Chapter, s.Title, s.AudioUrl, s.TranscriptUrl, s.DisplayOrder, s.IsPublished))
            .ToListAsync(ct);
        return TypedResults.Ok(rows);
    }

    private static async Task<Ok<AudioScriptDto>> AdminUpsertAudioScript(
        HttpContext http,
        AudioScriptUpsertRequest req,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var adminName = http.User.Identity?.Name;
        var now = DateTimeOffset.UtcNow;

        TutorBookAudioScript row;
        if (!string.IsNullOrWhiteSpace(req.Id))
        {
            row = await db.TutorBookAudioScripts.FirstOrDefaultAsync(s => s.Id == req.Id, ct)
                ?? new TutorBookAudioScript { Id = req.Id!, CreatedAt = now, CreatedByAdminId = adminId, CreatedByAdminName = adminName };
            if (row.CreatedAt == default)
            {
                row.CreatedAt = now;
                row.CreatedByAdminId = adminId;
                row.CreatedByAdminName = adminName;
                db.TutorBookAudioScripts.Add(row);
            }
        }
        else
        {
            row = new TutorBookAudioScript
            {
                Id = $"tba_{Guid.NewGuid():N}"[..32],
                CreatedAt = now,
                CreatedByAdminId = adminId,
                CreatedByAdminName = adminName,
            };
            db.TutorBookAudioScripts.Add(row);
        }

        row.Chapter = req.Chapter;
        row.Title = req.Title;
        row.AudioUrl = req.AudioUrl;
        row.TranscriptUrl = req.TranscriptUrl;
        row.DisplayOrder = req.DisplayOrder;
        row.IsPublished = req.IsPublished;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new AudioScriptDto(row.Id, row.Chapter, row.Title, row.AudioUrl, row.TranscriptUrl, row.DisplayOrder, row.IsPublished));
    }

    private static async Task<Results<NoContent, NotFound>> AdminDeleteAudioScript(
        string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.TutorBookAudioScripts.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        db.TutorBookAudioScripts.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Task<bool> UserHasTutorBookAsync(LearnerDbContext db, string userId, CancellationToken ct) =>
        db.Subscriptions.AsNoTracking().AnyAsync(s => s.UserId == userId
            && s.TutorBookUnlocked
            && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial), ct);
}
