using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class CommunityEndpoints
{
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var community = v1.MapGroup("/community");

        // ── Forum categories ─────────────────────────────────────────────
        community.MapGet("/categories", async (
            [FromQuery] string? examTypeCode,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.ForumCategories.Where(c => c.Status == "active");
            if (!string.IsNullOrEmpty(examTypeCode))
                query = query.Where(c => c.ExamTypeCode == null || c.ExamTypeCode == examTypeCode);
            var cats = await query.OrderBy(c => c.SortOrder).ToListAsync(ct);
            return Results.Ok(cats.Select(c => new { id = c.Id, examTypeCode = c.ExamTypeCode, name = c.Name, description = c.Description, sortOrder = c.SortOrder }));
        });

        // ── Forum threads ────────────────────────────────────────────────
        community.MapGet("/threads", async (
            [FromQuery] string? categoryId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var query = db.ForumThreads.AsQueryable();
            if (!string.IsNullOrEmpty(categoryId)) query = query.Where(t => t.CategoryId == categoryId);
            var total = await query.CountAsync(ct);
            var threads = await query.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.LastActivityAt)
                .Skip(((page <= 0 ? 1 : page) - 1) * (pageSize <= 0 ? 20 : pageSize))
                .Take(pageSize <= 0 ? 20 : pageSize)
                .ToListAsync(ct);
            return Results.Ok(new { total, threads = threads.Select(t => new { id = t.Id, categoryId = t.CategoryId, title = t.Title, authorDisplayName = t.AuthorDisplayName, authorRole = t.AuthorRole, isPinned = t.IsPinned, isLocked = t.IsLocked, replyCount = t.ReplyCount, viewCount = t.ViewCount, likeCount = t.LikeCount, createdAt = t.CreatedAt, lastActivityAt = t.LastActivityAt }) });
        });

        community.MapGet("/threads/{threadId}", async (string threadId, LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            thread.ViewCount++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = thread.Id, categoryId = thread.CategoryId, authorUserId = thread.AuthorUserId, authorDisplayName = thread.AuthorDisplayName, authorRole = thread.AuthorRole, title = thread.Title, body = thread.Body, isPinned = thread.IsPinned, isLocked = thread.IsLocked, replyCount = thread.ReplyCount, viewCount = thread.ViewCount, likeCount = thread.LikeCount, createdAt = thread.CreatedAt, lastActivityAt = thread.LastActivityAt });
        });

        community.MapPost("/threads", async (HttpContext http, CreateThreadRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([http.UserId()], ct);
            var thread = new ForumThread
            {
                Id = $"ft-{Guid.NewGuid():N}",
                CategoryId = req.CategoryId,
                AuthorUserId = http.UserId(),
                AuthorDisplayName = user?.DisplayName ?? "Learner",
                AuthorRole = user?.Role ?? "learner",
                Title = req.Title,
                Body = req.Body,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActivityAt = DateTimeOffset.UtcNow
            };
            db.ForumThreads.Add(thread);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = thread.Id });
        });

        // ── Forum replies ────────────────────────────────────────────────
        community.MapGet("/threads/{threadId}/replies", async (
            string threadId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var ps = pageSize <= 0 ? 20 : pageSize;
            var pg = page <= 0 ? 1 : page;
            var total = await db.ForumReplies.CountAsync(r => r.ThreadId == threadId, ct);
            var replies = await db.ForumReplies.Where(r => r.ThreadId == threadId)
                .OrderBy(r => r.CreatedAt).Skip((pg - 1) * ps).Take(ps).ToListAsync(ct);
            return Results.Ok(new { total, replies = replies.Select(r => new { id = r.Id, authorDisplayName = r.AuthorDisplayName, authorRole = r.AuthorRole, body = r.Body, isExpertVerified = r.IsExpertVerified, likeCount = r.LikeCount, createdAt = r.CreatedAt, editedAt = r.EditedAt }) });
        });

        community.MapPost("/threads/{threadId}/replies", async (HttpContext http, string threadId, CreateReplyRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var thread = await db.ForumThreads.FindAsync([threadId], ct);
            if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });
            if (thread.IsLocked) return Results.Json(new { error = "THREAD_LOCKED" }, statusCode: 403);

            var user = await db.Users.FindAsync([http.UserId()], ct);
            var reply = new ForumReply
            {
                Id = $"fr-{Guid.NewGuid():N}",
                ThreadId = threadId,
                AuthorUserId = http.UserId(),
                AuthorDisplayName = user?.DisplayName ?? "Learner",
                AuthorRole = user?.Role ?? "learner",
                Body = req.Body,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ForumReplies.Add(reply);
            thread.ReplyCount++;
            thread.LastActivityAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = reply.Id });
        });

        // ── Study groups ─────────────────────────────────────────────────
        community.MapGet("/study-groups", async (
            [FromQuery] string? examTypeCode,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            LearnerDbContext db, CancellationToken ct) =>
        {
            var ps = pageSize <= 0 ? 20 : pageSize;
            var pg = page <= 0 ? 1 : page;
            var query = db.StudyGroups.Where(g => g.IsPublic && g.Status == "active");
            if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(g => g.ExamTypeCode == examTypeCode);
            var total = await query.CountAsync(ct);
            var groups = await query.OrderByDescending(g => g.MemberCount).Skip((pg - 1) * ps).Take(ps).ToListAsync(ct);
            return Results.Ok(new { total, groups = groups.Select(g => new { id = g.Id, name = g.Name, description = g.Description, examTypeCode = g.ExamTypeCode, memberCount = g.MemberCount, maxMembers = g.MaxMembers, createdAt = g.CreatedAt }) });
        });

        community.MapPost("/study-groups", async (HttpContext http, CreateStudyGroupRequest req, LearnerDbContext db, CancellationToken ct) =>
        {
            var group = new StudyGroup
            {
                Id = $"sg-{Guid.NewGuid():N}",
                Name = req.Name,
                Description = req.Description,
                ExamTypeCode = req.ExamTypeCode,
                CreatorUserId = http.UserId(),
                MaxMembers = 20,
                MemberCount = 1,
                IsPublic = req.IsPublic,
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.StudyGroups.Add(group);
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                UserId = http.UserId(),
                Role = "creator",
                JoinedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = group.Id });
        });

        community.MapPost("/study-groups/{groupId}/join", async (HttpContext http, string groupId, LearnerDbContext db, CancellationToken ct) =>
        {
            var group = await db.StudyGroups.FindAsync([groupId], ct);
            if (group == null) return Results.NotFound(new { error = "GROUP_NOT_FOUND" });
            if (group.MemberCount >= group.MaxMembers) return Results.BadRequest(new { error = "GROUP_FULL" });

            var already = await db.StudyGroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == http.UserId(), ct);
            if (!already)
            {
                db.StudyGroupMembers.Add(new StudyGroupMember { Id = Guid.NewGuid(), GroupId = groupId, UserId = http.UserId(), Role = "member", JoinedAt = DateTimeOffset.UtcNow });
                group.MemberCount++;
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { joined = !already });
        });

        return app;
    }
}

public record CreateThreadRequest(string CategoryId, string Title, string Body);
public record CreateReplyRequest(string Body);
public record CreateStudyGroupRequest(string Name, string Description, string ExamTypeCode, bool IsPublic);

file static class CommunityHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
