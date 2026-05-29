using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public interface INotificationCampaignService
{
    Task<NotificationCampaign> CreateAsync(string adminId, CreateCampaignRequest request, CancellationToken ct);
    Task<NotificationCampaign> UpdateAsync(string adminId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct);
    Task<NotificationCampaign?> GetAsync(Guid campaignId, CancellationToken ct);
    Task<CampaignListResponse> ListAsync(int page, int pageSize, NotificationCampaignStatus? status, CancellationToken ct);
    Task<NotificationCampaign> ApproveAsync(string adminId, Guid campaignId, CancellationToken ct);
    Task CancelAsync(string adminId, Guid campaignId, CancellationToken ct);
    Task<int> EvaluateSegmentAsync(Guid campaignId, CancellationToken ct);
    Task<CampaignSendResult> SendAsync(Guid campaignId, CancellationToken ct);
}

public sealed class NotificationCampaignService(
    LearnerDbContext db,
    TimeProvider clock,
    ILogger<NotificationCampaignService> logger) : INotificationCampaignService
{
    public async Task<NotificationCampaign> CreateAsync(string adminId, CreateCampaignRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Body);

        var now = clock.GetUtcNow();
        var campaign = new NotificationCampaign
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Subject = request.Subject,
            Body = request.Body,
            HtmlBody = request.HtmlBody,
            Channel = request.Channel ?? NotificationChannel.Email,
            Status = NotificationCampaignStatus.Draft,
            SegmentJson = request.SegmentJson ?? "{}",
            ScheduledAt = request.ScheduledAt,
            VariantLabel = request.VariantLabel,
            ParentCampaignId = request.ParentCampaignId,
            CreatedByAdminId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.NotificationCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Campaign {CampaignId} created by admin {AdminId}", campaign.Id, adminId);
        return campaign;
    }

    public async Task<NotificationCampaign> UpdateAsync(string adminId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct)
    {
        var campaign = await db.NotificationCampaigns.FindAsync([campaignId], ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        if (campaign.Status is not (NotificationCampaignStatus.Draft or NotificationCampaignStatus.Scheduled))
            throw new InvalidOperationException($"Cannot edit campaign in {campaign.Status} status.");

        var now = clock.GetUtcNow();
        if (request.Name is not null) campaign.Name = request.Name;
        if (request.Subject is not null) campaign.Subject = request.Subject;
        if (request.Body is not null) campaign.Body = request.Body;
        if (request.HtmlBody is not null) campaign.HtmlBody = request.HtmlBody;
        if (request.SegmentJson is not null) campaign.SegmentJson = request.SegmentJson;
        if (request.ScheduledAt.HasValue) campaign.ScheduledAt = request.ScheduledAt.Value;
        if (request.Channel.HasValue) campaign.Channel = request.Channel.Value;
        campaign.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return campaign;
    }

    public async Task<NotificationCampaign?> GetAsync(Guid campaignId, CancellationToken ct)
    {
        return await db.NotificationCampaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);
    }

    public async Task<CampaignListResponse> ListAsync(int page, int pageSize, NotificationCampaignStatus? status, CancellationToken ct)
    {
        var query = db.NotificationCampaigns.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(c => c.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new CampaignListResponse(total, items);
    }

    public async Task<NotificationCampaign> ApproveAsync(string adminId, Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.NotificationCampaigns.FindAsync([campaignId], ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        if (campaign.Status != NotificationCampaignStatus.Draft)
            throw new InvalidOperationException($"Only draft campaigns can be approved (current: {campaign.Status}).");

        var now = clock.GetUtcNow();
        campaign.ApprovedByAdminId = adminId;
        campaign.ApprovedAt = now;
        campaign.Status = campaign.ScheduledAt.HasValue
            ? NotificationCampaignStatus.Scheduled
            : NotificationCampaignStatus.Sending;
        campaign.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Campaign {CampaignId} approved by {AdminId}", campaignId, adminId);
        return campaign;
    }

    public async Task CancelAsync(string adminId, Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.NotificationCampaigns.FindAsync([campaignId], ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        if (campaign.Status is NotificationCampaignStatus.Sent or NotificationCampaignStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel campaign in {campaign.Status} status.");

        campaign.Status = NotificationCampaignStatus.Cancelled;
        campaign.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Campaign {CampaignId} cancelled by {AdminId}", campaignId, adminId);
    }

    public async Task<int> EvaluateSegmentAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.NotificationCampaigns.FindAsync([campaignId], ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        // Segment evaluation: parse SegmentJson and count matching users.
        // For now, a simplified implementation counting all users with consent.
        var count = await db.NotificationConsents
            .Where(c => c.IsGranted && c.Channel == campaign.Channel)
            .Select(c => c.AuthAccountId)
            .Distinct()
            .CountAsync(ct);

        campaign.RecipientCount = count;
        campaign.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return count;
    }

    public async Task<CampaignSendResult> SendAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.NotificationCampaigns.FindAsync([campaignId], ct)
            ?? throw new InvalidOperationException("Campaign not found.");

        if (campaign.Status is not (NotificationCampaignStatus.Sending or NotificationCampaignStatus.Scheduled))
            throw new InvalidOperationException($"Campaign must be approved before sending (current: {campaign.Status}).");

        var now = clock.GetUtcNow();
        campaign.Status = NotificationCampaignStatus.Sending;
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // Resolve recipients from segment (simplified: all consented users for channel)
        var recipients = await db.NotificationConsents
            .Where(c => c.IsGranted && c.Channel == campaign.Channel)
            .Select(c => new { c.AuthAccountId })
            .Distinct()
            .ToListAsync(ct);

        var recipientRows = recipients.Select(r => new NotificationCampaignRecipient
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            RecipientUserId = r.AuthAccountId,
            RecipientEmail = "", // Will be resolved by the delivery pipeline
            DeliveryStatus = NotificationDeliveryStatus.Pending,
            CreatedAt = now,
        }).ToList();

        db.NotificationCampaignRecipients.AddRange(recipientRows);

        campaign.RecipientCount = recipientRows.Count;
        campaign.SentAt = now;
        campaign.Status = NotificationCampaignStatus.Sent;
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Campaign {CampaignId} sent to {Count} recipients", campaignId, recipientRows.Count);
        return new CampaignSendResult(recipientRows.Count, 0);
    }
}

// ─── Contracts ───────────────────────────────────────────────────────────────
public record CreateCampaignRequest(
    string Name,
    string Subject,
    string Body,
    string? HtmlBody = null,
    NotificationChannel? Channel = null,
    string? SegmentJson = null,
    DateTimeOffset? ScheduledAt = null,
    string? VariantLabel = null,
    Guid? ParentCampaignId = null);

public record UpdateCampaignRequest(
    string? Name = null,
    string? Subject = null,
    string? Body = null,
    string? HtmlBody = null,
    string? SegmentJson = null,
    DateTimeOffset? ScheduledAt = null,
    NotificationChannel? Channel = null);

public record CampaignListResponse(int Total, List<NotificationCampaign> Items);
public record CampaignSendResult(int Delivered, int Failed);
