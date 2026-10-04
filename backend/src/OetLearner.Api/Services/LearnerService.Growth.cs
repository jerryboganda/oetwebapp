using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    // ── Engagement ──

    public async Task<object> GetEngagementAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw ApiException.NotFound("user_not_found", "User not found.");

        var weeklyActivity = JsonSupport.Deserialize<bool[]>(user.WeeklyActivityJson ?? "[]", []);
        var daysLabels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        return new
        {
            currentStreak = user.CurrentStreak,
            longestStreak = user.LongestStreak,
            lastPracticeDate = user.LastPracticeDate,
            totalPracticeMinutes = user.TotalPracticeMinutes,
            totalPracticeSessions = user.TotalPracticeSessions,
            avgSessionMinutes = user.TotalPracticeSessions > 0 ? user.TotalPracticeMinutes / user.TotalPracticeSessions : 0,
            weeklyActivity = weeklyActivity.Select((active, index) => new
            {
                day = index < daysLabels.Length ? daysLabels[index] : $"Day {index + 1}",
                active
            }),
            streakFreezeAvailable = true,
            streakFreezeUsedThisWeek = false
        };
    }

    // ── Exam Family Reference ──

    public async Task<object> GetExamFamiliesAsync(CancellationToken ct)
    {
        var families = await db.ExamFamilies.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                code = x.Code,
                label = x.Label,
                scoringModel = x.ScoringModel,
                description = x.Description,
                subtests = x.SubtestConfigJson,
                criteria = x.CriteriaConfigJson,
                isActive = x.IsActive
            })
            .ToListAsync(ct);

        return new { examFamilies = families };
    }

    // ════════════════════════════════════════════
    //  Score Guarantee
    // ════════════════════════════════════════════

    public async Task<object> GetScoreGuaranteeAsync(string userId, CancellationToken ct)
    {
        var pledge = await db.ScoreGuaranteePledges
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        if (pledge is null)
            return new { active = false, eligible = true };

        return new
        {
            active = pledge.Status == "active",
            id = pledge.Id,
            pledgeId = pledge.Id,
            userId = pledge.UserId,
            subscriptionId = pledge.SubscriptionId,
            baselineScore = pledge.BaselineScore,
            guaranteedImprovement = pledge.GuaranteedImprovement,
            status = pledge.Status,
            proofDocumentUrl = pledge.ProofDocumentUrl,
            claimNote = pledge.ClaimNote,
            reviewNote = pledge.ReviewNote,
            activatedAt = pledge.ActivatedAt,
            expiresAt = pledge.ExpiresAt,
            actualScore = pledge.ActualScore,
            claimSubmittedAt = pledge.ClaimSubmittedAt,
            reviewedAt = pledge.ReviewedAt
        };
    }

    public async Task<object> ActivateScoreGuaranteeAsync(string userId, ScoreGuaranteeActivateRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var existing = await db.ScoreGuaranteePledges
            .AnyAsync(p => p.UserId == userId && p.Status == "active", ct);
        if (existing)
            throw ApiException.Conflict("already_active", "Score guarantee is already active.");

        if (request.BaselineScore < 0 || request.BaselineScore > 500)
            throw ApiException.Validation("invalid_score", "Baseline score must be between 0 and 500.");

        var sub = await db.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Active, ct);
        if (sub is null)
            throw ApiException.Validation("no_subscription", "Active subscription required for score guarantee.");

        var pledge = new ScoreGuaranteePledge
        {
            Id = $"SGP-{Guid.NewGuid():N}",
            UserId = userId,
            SubscriptionId = sub.Id,
            BaselineScore = request.BaselineScore,
            GuaranteedImprovement = 50,
            Status = "active",
            ActivatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(180)
        };

        db.ScoreGuaranteePledges.Add(pledge);
        await db.SaveChangesAsync(ct);

        return new { id = pledge.Id, pledgeId = pledge.Id, status = "active", expiresAt = pledge.ExpiresAt };
    }

    public async Task<object> SubmitScoreGuaranteeClaimAsync(string userId, ScoreGuaranteeClaimRequest request, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var pledge = await db.ScoreGuaranteePledges
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "active", ct)
            ?? throw ApiException.NotFound("no_pledge", "No active score guarantee found.");

        if (request.ActualScore < 0 || request.ActualScore > 500)
            throw ApiException.Validation("invalid_score", "Actual score must be between 0 and 500.");

        pledge.ActualScore = request.ActualScore;
        pledge.ProofDocumentUrl = request.ProofDocumentUrl;
        pledge.ClaimNote = request.Note;
        pledge.Status = "claim_submitted";
        pledge.ClaimSubmittedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new { id = pledge.Id, pledgeId = pledge.Id, status = "claim_submitted" };
    }

    // ════════════════════════════════════════════
    //  Score Cross-Reference Calculator
    // ════════════════════════════════════════════

    public Task<object> GetScoreEquivalencesAsync(CancellationToken ct)
    {
        // Official OET equivalence table (publicly available from OET website)
        var equivalences = new[]
        {
            // Grade bands as OET reports them (owner spec 4 Oct 2026): ten-point scores, no "B+".
            new { oetGrade = "A",   oetScoreMin = 450, oetScoreMax = 500, ielts = 9.0,  pte = 88,  cefr = "C2" },
            new { oetGrade = "B",   oetScoreMin = 350, oetScoreMax = 440, ielts = 7.5,  pte = 73,  cefr = "C1" },
            new { oetGrade = "C+",  oetScoreMin = 300, oetScoreMax = 340, ielts = 7.0,  pte = 65,  cefr = "B2+" },
            new { oetGrade = "C",   oetScoreMin = 200, oetScoreMax = 290, ielts = 6.0,  pte = 50,  cefr = "B1+" },
            new { oetGrade = "D",   oetScoreMin = 100, oetScoreMax = 190, ielts = 5.0,  pte = 36,  cefr = "A2+" },
            new { oetGrade = "E",   oetScoreMin = 0,   oetScoreMax = 90,  ielts = 4.5,  pte = 30,  cefr = "A2" }
        };

        var commonRequirements = new[]
        {
            new { country = "Australia", body = "AHPRA (Nursing)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.0 },
            new { country = "Australia", body = "AHPRA (Medicine)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.0 },
            new { country = "UK", body = "NMC (Nursing)", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 7.0 },
            new { country = "UK", body = "GMC (Medicine)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.5 },
            new { country = "New Zealand", body = "NCNZ (Nursing)", oetMinGrade = "B", oetMinScore = 350, ieltsMin = 7.0 },
            new { country = "Ireland", body = "NMBI (Nursing)", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 6.5 },
            new { country = "Singapore", body = "SNB (Nursing)", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 6.5 },
            new { country = "USA", body = "Various State Boards", oetMinGrade = "C+", oetMinScore = 300, ieltsMin = 6.5 }
        };

        return Task.FromResult<object>(new { equivalences, commonRequirements });
    }

    // ════════════════════════════════════════════
    //  Referral Program
    // ════════════════════════════════════════════

    public async Task<object> GetReferralInfoAsync(string userId, CancellationToken ct)
    {
        var myCode = await db.ReferralRecords
            .AsNoTracking()
            .Where(r => r.ReferrerUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var referralsMade = await db.ReferralRecords
            .AsNoTracking()
            .CountAsync(r => r.ReferrerUserId == userId && r.Status != "pending", ct);

        var creditsEarned = await db.ReferralRecords
            .AsNoTracking()
            .Where(r => r.ReferrerUserId == userId && r.Status == "rewarded")
            .SumAsync(r => r.ReferrerCreditAmount, ct);

        return new
        {
            referralCode = myCode?.ReferralCode,
            referralsMade,
            creditsEarned,
            referrerCreditAmount = 10m,
            referredDiscountPercent = 10m
        };
    }

    public async Task<object> GenerateReferralCodeAsync(string userId, CancellationToken ct)
    {
        await EnsureLearnerMutationAllowedAsync(userId, ct);
        var existing = await db.ReferralRecords
            .FirstOrDefaultAsync(r => r.ReferrerUserId == userId && r.Status == "pending", ct);

        if (existing is not null)
            return new { referralCode = existing.ReferralCode };

        var code = $"REF-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var record = new ReferralRecord
        {
            Id = $"RR-{Guid.NewGuid():N}",
            ReferrerUserId = userId,
            ReferralCode = code,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ReferralRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return new { referralCode = code };
    }

    // ── L12: Peer Review Exchange ────────────────────────────

    public async Task<object> GetPeerReviewPoolAsync(string userId, CancellationToken ct)
    {
        // Available peer review requests from other learners (not mine, not claimed)
        var available = await db.PeerReviewRequests
            .Where(r => r.SubmitterUserId != userId && r.Status == "open")
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        // My submissions
        var mySubmissions = await db.PeerReviewRequests
            .Where(r => r.SubmitterUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        // My reviews (claimed by me)
        var myReviews = await db.PeerReviewRequests
            .Where(r => r.ReviewerUserId == userId)
            .OrderByDescending(r => r.ClaimedAt)
            .Take(10)
            .ToListAsync(ct);

        // Get feedback for my submissions
        var mySubmissionIds = mySubmissions.Select(s => s.Id).ToList();
        var feedbackForMe = await db.PeerReviewFeedbacks
            .Where(f => mySubmissionIds.Contains(f.PeerReviewRequestId))
            .ToListAsync(ct);

        return new
        {
            availableToReview = available.Select(r => new { id = r.Id, subtestCode = r.SubtestCode, attemptId = r.AttemptId, createdAt = r.CreatedAt }),
            mySubmissions = mySubmissions.Select(s => new { id = s.Id, subtestCode = s.SubtestCode, status = s.Status, createdAt = s.CreatedAt, feedback = feedbackForMe.Where(f => f.PeerReviewRequestId == s.Id).Select(f => new { rating = f.OverallRating, comments = f.Comments, strengths = f.StrengthNotes, improvements = f.ImprovementNotes }) }),
            myReviews = myReviews.Select(r => new { id = r.Id, subtestCode = r.SubtestCode, status = r.Status, claimedAt = r.ClaimedAt, completedAt = r.CompletedAt }),
            stats = new
            {
                reviewsGiven = myReviews.Count(r => r.Status == "completed"),
                reviewsReceived = feedbackForMe.Count,
                averageHelpfulness = feedbackForMe.Where(f => f.HelpfulnessRating > 0).Select(f => (double)f.HelpfulnessRating).DefaultIfEmpty(0).Average()
            }
        };
    }

    public async Task<object> SubmitForPeerReviewAsync(string userId, string attemptId, string subtestCode, CancellationToken ct)
    {
        var attempt = await db.Attempts.FindAsync([attemptId], ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        if (attempt.UserId != userId) throw new InvalidOperationException("Not your attempt.");

        var existing = await db.PeerReviewRequests.AnyAsync(r => r.AttemptId == attemptId && r.Status != "expired", ct);
        if (existing) throw new InvalidOperationException("Already submitted for peer review.");

        var request = new PeerReviewRequest
        {
            Id = $"pr-{Guid.NewGuid():N}",
            SubmitterUserId = userId,
            AttemptId = attemptId,
            SubtestCode = subtestCode.ToLowerInvariant(),
            Status = "open",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PeerReviewRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return new { id = request.Id, status = "open" };
    }

    public async Task<object> ClaimPeerReviewAsync(string userId, string peerReviewId, CancellationToken ct)
    {
        var request = await db.PeerReviewRequests.FindAsync([peerReviewId], ct)
            ?? throw new InvalidOperationException("Peer review request not found.");

        if (request.SubmitterUserId == userId) throw new InvalidOperationException("Cannot review your own submission.");
        if (request.Status != "open") throw new InvalidOperationException("Already claimed or completed.");

        request.ReviewerUserId = userId;
        request.Status = "claimed";
        request.ClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { id = request.Id, status = "claimed" };
    }

    public async Task<object> SubmitPeerFeedbackAsync(string userId, string peerReviewId, int overallRating, string comments, string? strengths, string? improvements, CancellationToken ct)
    {
        var request = await db.PeerReviewRequests.FindAsync([peerReviewId], ct)
            ?? throw new InvalidOperationException("Peer review request not found.");

        if (request.ReviewerUserId != userId) throw new InvalidOperationException("Not assigned to you.");
        if (request.Status != "claimed") throw new InvalidOperationException("Not in claimable state.");

        var feedback = new PeerReviewFeedback
        {
            Id = $"prf-{Guid.NewGuid():N}",
            PeerReviewRequestId = peerReviewId,
            ReviewerUserId = userId,
            OverallRating = Math.Clamp(overallRating, 1, 5),
            Comments = comments.Trim(),
            StrengthNotes = strengths?.Trim(),
            ImprovementNotes = improvements?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PeerReviewFeedbacks.Add(feedback);
        request.Status = "completed";
        request.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { feedbackId = feedback.Id, status = "completed" };
    }

    // ── Learner Escalation / Dispute ────────────────────────────

    public async Task<object> SubmitEscalationAsync(
        string userId, string submissionId, string reason, string details, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
            throw new InvalidOperationException("Submission ID is required.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Reason is required.");
        if (string.IsNullOrWhiteSpace(details))
            throw new InvalidOperationException("Details are required.");

        var existing = await db.LearnerEscalations
            .AnyAsync(e => e.UserId == userId && e.SubmissionId == submissionId && e.Status == "Pending", ct);
        if (existing)
            throw new InvalidOperationException("An escalation for this submission is already pending.");

        var escalation = new LearnerEscalation
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SubmissionId = submissionId,
            Reason = reason,
            Details = details,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.LearnerEscalations.Add(escalation);
        await db.SaveChangesAsync(ct);
        return new { escalationId = escalation.Id, status = escalation.Status };
    }

    public async Task<object> GetMyEscalationsAsync(string userId, CancellationToken ct)
    {
        var items = await db.LearnerEscalations
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .Select(e => new
            {
                id = e.Id,
                submissionId = e.SubmissionId,
                reason = e.Reason,
                status = e.Status,
                createdAt = e.CreatedAt,
                updatedAt = e.UpdatedAt
            })
            .ToListAsync(ct);

        return new { items, total = items.Count };
    }

    public async Task<object> GetEscalationDetailsAsync(string userId, string escalationId, CancellationToken ct)
    {
        var esc = await db.LearnerEscalations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == escalationId && e.UserId == userId, ct)
            ?? throw new InvalidOperationException("Escalation not found.");

        return new
        {
            id = esc.Id,
            submissionId = esc.SubmissionId,
            reason = esc.Reason,
            details = esc.Details,
            status = esc.Status,
            createdAt = esc.CreatedAt,
            updatedAt = esc.UpdatedAt
        };
    }
}
