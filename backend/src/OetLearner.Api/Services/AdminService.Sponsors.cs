using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    // ══════════════════════════════════════════════════════
    // B3 · Enterprise / Sponsor Channel
    // ══════════════════════════════════════════════════════

    public async Task<object> GetSponsorsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SponsorAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync(ct);
        var sponsors = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = sponsors.Select(s => new
            {
                id = s.Id, name = s.Name, type = s.Type, contactEmail = s.ContactEmail,
                organizationName = s.OrganizationName, status = s.Status, createdAt = s.CreatedAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> CreateSponsorAsync(string actorId, string actorName, SponsorCreateRequest req, CancellationToken ct)
    {
        var sponsor = new SponsorAccount
        {
            Id = $"spon-{Guid.NewGuid():N}",
            AuthAccountId = $"auth-{Guid.NewGuid():N}",
            Name = req.Name,
            Type = req.Type,
            ContactEmail = req.ContactEmail,
            OrganizationName = req.OrganizationName,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.SponsorAccounts.Add(sponsor);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "CreateSponsor", "SponsorAccount", sponsor.Id, $"Created sponsor: {req.Name}", ct);
        return new { id = sponsor.Id, name = sponsor.Name, status = sponsor.Status };
    }

    public async Task<object> UpdateSponsorAsync(string actorId, string actorName, string sponsorId, SponsorUpdateRequest req, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts.FindAsync([sponsorId], ct)
            ?? throw ApiException.NotFound("SPONSOR_NOT_FOUND", "Sponsor not found.");

        if (req.Name is not null) sponsor.Name = req.Name;
        if (req.ContactEmail is not null) sponsor.ContactEmail = req.ContactEmail;
        if (req.OrganizationName is not null) sponsor.OrganizationName = req.OrganizationName;
        if (req.Status is not null) sponsor.Status = req.Status;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "UpdateSponsor", "SponsorAccount", sponsorId, "Updated sponsor", ct);
        return new { id = sponsor.Id, name = sponsor.Name, status = sponsor.Status };
    }

    public async Task<object> GetCohortsAsync(string? sponsorId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Cohorts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(sponsorId))
            query = query.Where(c => c.SponsorId == sponsorId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status == status);

        var total = await query.CountAsync(ct);
        var cohorts = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = cohorts.Select(c => new
            {
                id = c.Id, sponsorId = c.SponsorId, name = c.Name, examTypeCode = c.ExamTypeCode,
                startDate = c.StartDate, endDate = c.EndDate, maxSeats = c.MaxSeats,
                enrolledCount = c.EnrolledCount, status = c.Status, createdAt = c.CreatedAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> CreateCohortAsync(string actorId, string actorName, CohortCreateRequest req, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts.FindAsync([req.SponsorId], ct)
            ?? throw ApiException.NotFound("SPONSOR_NOT_FOUND", "Sponsor not found.");

        var cohort = new Cohort
        {
            Id = $"coh-{Guid.NewGuid():N}",
            SponsorId = req.SponsorId,
            Name = req.Name,
            ExamTypeCode = req.ExamTypeCode,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            MaxSeats = req.MaxSeats,
            EnrolledCount = 0,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Cohorts.Add(cohort);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "CreateCohort", "Cohort", cohort.Id, $"Created cohort: {req.Name}", ct);
        return new { id = cohort.Id, name = cohort.Name, status = cohort.Status };
    }

    public async Task<object> UpdateCohortAsync(string actorId, string actorName, string cohortId, CohortUpdateRequest req, CancellationToken ct)
    {
        var cohort = await db.Cohorts.FindAsync([cohortId], ct)
            ?? throw ApiException.NotFound("COHORT_NOT_FOUND", "Cohort not found.");

        if (req.Name is not null) cohort.Name = req.Name;
        if (req.StartDate is not null) cohort.StartDate = req.StartDate;
        if (req.EndDate is not null) cohort.EndDate = req.EndDate;
        if (req.MaxSeats is not null) cohort.MaxSeats = req.MaxSeats.Value;
        if (req.Status is not null) cohort.Status = req.Status;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "UpdateCohort", "Cohort", cohortId, "Updated cohort", ct);
        return new { id = cohort.Id, name = cohort.Name, status = cohort.Status };
    }

    public async Task<object> GetCohortMembersAsync(string cohortId, int page, int pageSize, CancellationToken ct)
    {
        var members = await db.CohortMembers
            .Where(m => m.CohortId == cohortId)
            .OrderByDescending(m => m.EnrolledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var total = await db.CohortMembers.CountAsync(m => m.CohortId == cohortId, ct);

        return new
        {
            items = members.Select(m => new
            {
                id = m.Id, cohortId = m.CohortId, learnerId = m.LearnerId,
                status = m.Status, enrolledAt = m.EnrolledAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> AddCohortMemberAsync(string actorId, string actorName, string cohortId, string learnerId, CancellationToken ct)
    {
        var cohort = await db.Cohorts.FindAsync([cohortId], ct)
            ?? throw ApiException.NotFound("COHORT_NOT_FOUND", "Cohort not found.");

        if (cohort.EnrolledCount >= cohort.MaxSeats)
            throw ApiException.Validation("COHORT_FULL", "Cohort is at maximum capacity.");

        var existing = await db.CohortMembers
            .AnyAsync(m => m.CohortId == cohortId && m.LearnerId == learnerId, ct);
        if (existing) throw ApiException.Validation("ALREADY_ENROLLED", "Learner is already enrolled.");

        var member = new CohortMember
        {
            Id = Guid.NewGuid(),
            CohortId = cohortId,
            LearnerId = learnerId,
            Status = "active",
            EnrolledAt = DateTimeOffset.UtcNow
        };
        db.CohortMembers.Add(member);
        cohort.EnrolledCount++;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "AddCohortMember", "CohortMember", member.Id.ToString(),
            $"Added learner {learnerId} to cohort {cohortId}", ct);

        return new { id = member.Id, cohortId, learnerId, status = member.Status };
    }

    public async Task<object> GetSponsorLearnersAsync(string sponsorId, int page, int pageSize, CancellationToken ct)
    {
        var links = await db.SponsorLearnerLinks
            .Where(l => l.SponsorId == sponsorId)
            .OrderByDescending(l => l.LinkedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var total = await db.SponsorLearnerLinks.CountAsync(l => l.SponsorId == sponsorId, ct);

        return new
        {
            items = links.Select(l => new
            {
                id = l.Id, sponsorId = l.SponsorId, learnerId = l.LearnerId,
                learnerConsented = l.LearnerConsented, linkedAt = l.LinkedAt, consentedAt = l.ConsentedAt
            }).ToList(),
            total, page, pageSize
        };
    }

    public async Task<object> LinkSponsorLearnerAsync(string actorId, string actorName, string sponsorId, string learnerId, CancellationToken ct)
    {
        var sponsor = await db.SponsorAccounts.FindAsync([sponsorId], ct)
            ?? throw ApiException.NotFound("SPONSOR_NOT_FOUND", "Sponsor not found.");

        var existing = await db.SponsorLearnerLinks
            .AnyAsync(l => l.SponsorId == sponsorId && l.LearnerId == learnerId, ct);
        if (existing)
            throw ApiException.Validation("ALREADY_LINKED", "Learner is already linked to this sponsor.");

        var link = new SponsorLearnerLink
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            LearnerId = learnerId,
            LearnerConsented = false,
            LinkedAt = DateTimeOffset.UtcNow
        };
        db.SponsorLearnerLinks.Add(link);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "LinkSponsorLearner", "SponsorLearnerLink", link.Id.ToString(),
            $"Linked learner {learnerId} to sponsor {sponsorId}", ct);

        return new { id = link.Id, sponsorId, learnerId, learnerConsented = false };
    }
}
