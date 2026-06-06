using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingShowcasePostView(
    Guid Id,
    Guid SubmissionId,
    string AnonymizedLetterContent,
    string Profession,
    string LetterType,
    string Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

public interface IWritingShowcaseService
{
    Task<IReadOnlyList<WritingShowcasePostView>> ListPublishedAsync(string viewerUserId, string? profession, string? letterType, int take, CancellationToken ct);
    Task<WritingShowcasePostView> SubmitForModerationAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingShowcasePostView> ApproveAsync(string adminId, Guid postId, CancellationToken ct);
    Task<WritingShowcasePostView> RejectAsync(string adminId, Guid postId, string? reason, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingShowcaseListResponse> ListShowcasePostsAsync(string viewerUserId, string? profession, string? letterType, int page, int pageSize, CancellationToken ct);
    Task<WritingShowcasePostResponse?> PublishToShowcaseAsync(string userId, Guid submissionId, CancellationToken ct);
}

public sealed class WritingShowcaseService(LearnerDbContext db, TimeProvider clock) : IWritingShowcaseService
{
    private static readonly Regex NameRegex = new(@"\b(Mr|Mrs|Ms|Dr|Prof)\.?\s+[A-Z][a-zA-Z'\-]+(?:\s+[A-Z][a-zA-Z'\-]+)*", RegexOptions.Compiled);
    private static readonly Regex UntitledNameRegex = new(@"\b[A-Z][a-zA-Z'\-]+\s+[A-Z][a-zA-Z'\-]+\b", RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"\b(\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{2,4}|\d{4}-\d{2}-\d{2}|(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DobRegex = new(@"\b(?:DOB|D\.O\.B\.|date of birth)\s*[:\-]?\s*\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{2,4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PhoneRegex = new(@"\b(?:\+?\d{1,3}[\s\-])?(?:\d{3,4}[\s\-]){2,3}\d{2,4}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex AddressRegex = new(@"\b\d+\s+[A-Z][a-zA-Z]+\s+(?:Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Drive|Dr|Court|Ct|Place|Pl|Square|Sq|Crescent|Cres)\b", RegexOptions.Compiled);
    private static readonly Regex MedicalIdentifierRegex = new(@"\b(?:MRN|NHS|hospital number|patient id)\s*[:#\-]?\s*[A-Z0-9\-]{5,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<IReadOnlyList<WritingShowcasePostView>> ListPublishedAsync(string viewerUserId, string? profession, string? letterType, int take, CancellationToken ct)
    {
        _ = viewerUserId;
        var query = db.WritingShowcasePosts.AsNoTracking().Where(p => p.Status == "published" && p.PublishedAt != null);
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(p => p.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(p => p.LetterType == letterType);
        var rows = await query.OrderByDescending(p => p.PublishedAt).Take(Math.Clamp(take, 1, 50)).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<WritingShowcasePostView> SubmitForModerationAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_submission_not_found", "Submission was not found.");
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile?.OptInCommunity != true)
        {
            throw ApiException.Validation("writing_showcase_opt_in_required", "Enable Writing community sharing before submitting a showcase post.");
        }
        var grade = await db.WritingGrades.AsNoTracking()
            .Where(g => g.SubmissionId == submissionId)
            .OrderByDescending(g => g.AppealedByGradeId != null || g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("writing_showcase_grade_missing", "Only graded submissions can be submitted to the showcase.");
        if (!string.Equals(grade.BandLabel, "A", StringComparison.OrdinalIgnoreCase) && grade.RawTotal < 38)
        {
            throw ApiException.Validation("writing_showcase_a_grade_required", "Only A-grade letters can be submitted to the showcase.");
        }
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submission.ScenarioId, ct);
        var anonymized = Anonymize(submission.LetterContent);
        var status = ContainsSensitiveResidue(anonymized) ? "needs_redaction" : "pending";
        var existing = await db.WritingShowcasePosts.FirstOrDefaultAsync(p => p.SubmissionId == submissionId, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.AuthorUserId, userId, StringComparison.Ordinal))
            {
                throw ApiException.Forbidden("writing_showcase_forbidden", "Showcase post belongs to another learner.");
            }
            existing.AnonymizedLetterContent = anonymized;
            existing.Status = status;
            existing.PublishedAt = null;
            await db.SaveChangesAsync(ct);
            return ToView(existing);
        }
        var now = clock.GetUtcNow();
        var post = new WritingShowcasePost
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            AuthorUserId = userId,
            AnonymizedLetterContent = anonymized,
            Profession = scenario?.Profession ?? "medicine",
            LetterType = scenario?.LetterType ?? "LT-RR",
            Status = status,
            CreatedAt = now,
        };
        db.WritingShowcasePosts.Add(post);
        await db.SaveChangesAsync(ct);
        return ToView(post);
    }

    public async Task<WritingShowcasePostView> ApproveAsync(string adminId, Guid postId, CancellationToken ct)
    {
        var post = await db.WritingShowcasePosts.FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw ApiException.NotFound("writing_showcase_not_found", "Showcase post was not found.");
        if (ContainsSensitiveResidue(post.AnonymizedLetterContent))
        {
            post.Status = "needs_redaction";
            post.PublishedAt = null;
            await db.SaveChangesAsync(ct);
            throw ApiException.Validation("writing_showcase_privacy_review_required", "Showcase post still contains possible sensitive information.");
        }
        post.Status = "published";
        post.ApprovedById = adminId;
        post.PublishedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToView(post);
    }

    public async Task<WritingShowcasePostView> RejectAsync(string adminId, Guid postId, string? reason, CancellationToken ct)
    {
        _ = reason;
        var post = await db.WritingShowcasePosts.FirstOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw ApiException.NotFound("writing_showcase_not_found", "Showcase post was not found.");
        post.Status = "rejected";
        post.ApprovedById = adminId;
        post.PublishedAt = null;
        await db.SaveChangesAsync(ct);
        return ToView(post);
    }

    internal static string Anonymize(string letterContent)
    {
        if (string.IsNullOrWhiteSpace(letterContent)) return string.Empty;
        var redacted = NameRegex.Replace(letterContent, "[NAME]");
        redacted = UntitledNameRegex.Replace(redacted, "[NAME]");
        redacted = DobRegex.Replace(redacted, "[DATE]");
        redacted = DateRegex.Replace(redacted, "[DATE]");
        redacted = PhoneRegex.Replace(redacted, "[PHONE]");
        redacted = EmailRegex.Replace(redacted, "[EMAIL]");
        redacted = AddressRegex.Replace(redacted, "[ADDRESS]");
        redacted = MedicalIdentifierRegex.Replace(redacted, "[ID]");
        return redacted;
    }

    private static bool ContainsSensitiveResidue(string text)
        => EmailRegex.IsMatch(text)
           || PhoneRegex.IsMatch(text)
           || DateRegex.IsMatch(text)
              || DobRegex.IsMatch(text)
              || AddressRegex.IsMatch(text)
              || MedicalIdentifierRegex.IsMatch(text);

    private static WritingShowcasePostView ToView(WritingShowcasePost post)
        => new(post.Id, post.SubmissionId, post.AnonymizedLetterContent, post.Profession, post.LetterType, post.Status, post.PublishedAt, post.CreatedAt);

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingShowcaseListResponse> ListShowcasePostsAsync(string viewerUserId, string? profession, string? letterType, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingShowcasePosts.AsNoTracking().Where(p => p.Status == "published" && p.PublishedAt != null);
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(p => p.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(p => p.LetterType == letterType);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(p => p.PublishedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(ToView).Select(WritingV2ResponseMapper.ToResponse).ToList();
        _ = viewerUserId;
        return new WritingShowcaseListResponse(items, total);
    }

    public async Task<WritingShowcasePostResponse?> PublishToShowcaseAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        try
        {
            var view = await SubmitForModerationAsync(userId, submissionId, ct);
            return WritingV2ResponseMapper.ToResponse(view);
        }
        catch (ApiException ex) when (ex.ErrorCode == "writing_submission_not_found")
        {
            return null;
        }
    }
}
