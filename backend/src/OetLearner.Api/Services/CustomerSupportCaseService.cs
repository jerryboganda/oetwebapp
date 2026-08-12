using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class CustomerSupportCaseService(LearnerDbContext db)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CustomerSupportCaseSummary>> ListAsync(
        string? ticketId,
        CancellationToken ct)
    {
        var normalizedTicketId = NormalizeRequired(ticketId, "ticketId", 128);
        return await db.CustomerSupportCases
            .AsNoTracking()
            .Where(x => x.ExternalTicketId == normalizedTicketId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CustomerSupportCaseSummary(
                x.Id,
                x.ExternalTicketId,
                x.CandidateUserId,
                x.Subject,
                x.Status,
                x.OpenedByAdminId,
                x.OpenedByAdminName,
                x.ExpiresAt,
                x.ClosedAt,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<CustomerSupportCaseSummary> CreateAsync(
        string actorId,
        string actorName,
        CustomerSupportCaseCreateRequest request,
        CancellationToken ct)
    {
        var ticketId = NormalizeRequired(request.ExternalTicketId, "externalTicketId", 128);
        var candidateId = NormalizeRequired(request.CandidateUserId, "candidateUserId", 64);
        var subject = NormalizeRequired(request.Subject, "subject", 256);
        var now = DateTimeOffset.UtcNow;
        if (request.ExpiresAt <= now)
        {
            throw ApiException.Validation(
                "support_case_expiry_invalid",
                "Support access must expire in the future.",
                [new ApiFieldError("expiresAt", "future_required", "Choose a future expiry time.")]);
        }

        var candidate = await db.Users.AsNoTracking()
            .Where(x => x.Id == candidateId
                && x.Role == ApplicationUserRoles.Learner
                && x.AccountStatus != "deleted")
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(ct);
        if (candidate is null)
        {
            throw ApiException.NotFound("support_candidate_not_found", "The linked learner was not found.");
        }

        if (await db.CustomerSupportCases.AnyAsync(
                x => x.ExternalTicketId == ticketId && x.CandidateUserId == candidateId,
                ct))
        {
            throw ApiException.Conflict(
                "support_case_exists",
                "A support case already exists for this ticket and candidate.");
        }

        var supportCase = new CustomerSupportCase
        {
            Id = Guid.NewGuid().ToString("N"),
            ExternalTicketId = ticketId,
            CandidateUserId = candidate.Id,
            Subject = subject,
            Status = CustomerSupportCaseStatuses.Open,
            OpenedByAdminId = actorId,
            OpenedByAdminName = actorName,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CustomerSupportCases.Add(supportCase);
        AddAudit(actorId, actorName, "support.case.created", supportCase, new
        {
            supportCase.ExternalTicketId,
            supportCase.CandidateUserId,
            supportCase.ExpiresAt,
        });
        await db.SaveChangesAsync(ct);
        return ToSummary(supportCase);
    }

    public async Task<CustomerSupportCandidateProjection> GetCandidateAsync(
        string actorId,
        string actorName,
        string caseId,
        CancellationToken ct)
    {
        var normalizedCaseId = NormalizeRequired(caseId, "caseId", 64);
        var now = DateTimeOffset.UtcNow;
        var supportCase = await db.CustomerSupportCases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == normalizedCaseId
                && x.Status == CustomerSupportCaseStatuses.Open
                && x.ExpiresAt > now, ct);
        if (supportCase is null)
        {
            throw ApiException.NotFound("support_case_not_found", "The support case is missing, closed, or expired.");
        }

        var learner = await (
            from user in db.Users.AsNoTracking()
            join account in db.ApplicationUserAccounts.AsNoTracking()
                on user.AuthAccountId equals account.Id into accounts
            from account in accounts.DefaultIfEmpty()
            where user.Id == supportCase.CandidateUserId
                && user.Role == ApplicationUserRoles.Learner
                && user.AccountStatus != "deleted"
            select new
            {
                user.Id,
                user.DisplayName,
                Email = account != null && account.Email != "" ? account.Email : user.Email,
                user.AccountStatus,
                user.ActiveProfessionId,
            }).SingleOrDefaultAsync(ct);
        if (learner is null)
        {
            throw ApiException.NotFound("support_candidate_not_found", "The linked learner was not found.");
        }

        AddAudit(actorId, actorName, "support.case.candidate_read", supportCase, new
        {
            supportCase.ExternalTicketId,
            supportCase.CandidateUserId,
            supportCase.ExpiresAt,
        });
        await db.SaveChangesAsync(ct);
        return new CustomerSupportCandidateProjection(
            supportCase.Id,
            supportCase.ExternalTicketId,
            learner.Id,
            learner.DisplayName,
            learner.Email,
            learner.AccountStatus,
            learner.ActiveProfessionId,
            supportCase.ExpiresAt);
    }

    public async Task<CustomerSupportCaseSummary> CloseAsync(
        string actorId,
        string actorName,
        string caseId,
        CustomerSupportCaseCloseRequest request,
        CancellationToken ct)
    {
        var normalizedCaseId = NormalizeRequired(caseId, "caseId", 64);
        var supportCase = await db.CustomerSupportCases
            .SingleOrDefaultAsync(x => x.Id == normalizedCaseId, ct);
        if (supportCase is null)
        {
            throw ApiException.NotFound("support_case_not_found", "The support case was not found.");
        }

        if (supportCase.Status == CustomerSupportCaseStatuses.Open)
        {
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason is { Length: > 512 })
            {
                throw ApiException.Validation(
                    "support_case_reason_invalid",
                    "The close reason must be 512 characters or fewer.");
            }

            var now = DateTimeOffset.UtcNow;
            supportCase.Status = CustomerSupportCaseStatuses.Closed;
            supportCase.ClosedAt = now;
            supportCase.UpdatedAt = now;
            AddAudit(actorId, actorName, "support.case.closed", supportCase, new
            {
                supportCase.ExternalTicketId,
                supportCase.CandidateUserId,
                reason,
            });
            await db.SaveChangesAsync(ct);
        }

        return ToSummary(supportCase);
    }

    private void AddAudit(
        string actorId,
        string actorName,
        string action,
        CustomerSupportCase supportCase,
        object details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorAuthAccountId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "CustomerSupportCase",
            ResourceId = supportCase.Id,
            Details = JsonSerializer.Serialize(details, AuditJsonOptions),
        });
    }

    private static CustomerSupportCaseSummary ToSummary(CustomerSupportCase supportCase)
        => new(
            supportCase.Id,
            supportCase.ExternalTicketId,
            supportCase.CandidateUserId,
            supportCase.Subject,
            supportCase.Status,
            supportCase.OpenedByAdminId,
            supportCase.OpenedByAdminName,
            supportCase.ExpiresAt,
            supportCase.ClosedAt,
            supportCase.CreatedAt,
            supportCase.UpdatedAt);

    private static string NormalizeRequired(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw ApiException.Validation(
                "support_case_field_invalid",
                $"{field} is required and must be {maxLength} characters or fewer.",
                [new ApiFieldError(field, "invalid", $"Enter a value of 1 to {maxLength} characters.")]);
        }

        return normalized;
    }
}
