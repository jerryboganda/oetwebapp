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

    // ════════════════════════════════════════════
    //  AI Evaluation Config
    // ════════════════════════════════════════════

    public async Task<object> GetAIConfigListAsync(string? status, CancellationToken ct)
    {
        var query = db.AIConfigVersions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var parsedStatus = Enum.Parse<AIConfigStatus>(status, true);
            query = query.Where(a => a.Status == parsedStatus);
        }

        var configs = await query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var providerCodes = configs.Select(a => a.Provider.Trim().ToLowerInvariant()).Distinct().ToList();
        var providers = await db.AiProviders
            .AsNoTracking()
            .Where(p => providerCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, p => p.Name, ct);

        var items = configs.Select(a =>
        {
            var code = a.Provider.Trim().ToLowerInvariant();
            var providerName = providers.TryGetValue(code, out var name) ? name : a.Provider;
            return new
            {
                a.Id,
                model = a.Model,
                provider = a.Provider,
                providerName,
                taskType = a.TaskType,
                status = a.Status.ToString().ToLowerInvariant(),
                accuracy = a.Accuracy,
                confidenceThreshold = a.ConfidenceThreshold,
                routingRule = a.RoutingRule,
                experimentFlag = a.ExperimentFlag,
                promptLabel = a.PromptLabel,
            confidencePolicy = JsonSupport.Deserialize<Dictionary<string, object>>(a.ConfidencePolicyJson, new Dictionary<string, object>())
            };
        });

        return items;
    }

    public async Task<object> CreateAIConfigAsync(string adminId, string adminName,
        AdminAIConfigCreateRequest request, CancellationToken ct)
    {
        var providerCode = request.Provider.Trim().ToLowerInvariant();
        var providerExists = await db.AiProviders.AnyAsync(p => p.Code == providerCode && p.IsActive, ct);
        if (!providerExists)
            throw ApiException.Validation("invalid_provider", "The selected provider is not registered or inactive. Register it at /admin/ai-providers first.");

        var id = $"AIC-{Guid.NewGuid():N}"[..12];
        var createdAt = DateTimeOffset.UtcNow;
        var config = new AIConfigVersion
        {
            Id = id,
            Model = request.Model,
            Provider = request.Provider,
            TaskType = request.TaskType,
            Status = !string.IsNullOrWhiteSpace(request.Status)
                ? Enum.Parse<AIConfigStatus>(request.Status, true)
                : AIConfigStatus.Testing,
            Accuracy = request.Accuracy,
            ConfidenceThreshold = request.ConfidenceThreshold,
            RoutingRule = request.RoutingRule ?? "",
            ExperimentFlag = request.ExperimentFlag ?? "",
            PromptLabel = request.PromptLabel ?? "",
            ConfidencePolicyJson = request.ConfidencePolicy is not null
                ? JsonSupport.Serialize(request.ConfidencePolicy)
                : "{}",
            CreatedBy = adminName,
            CreatedAt = createdAt
        };
        db.AIConfigVersions.Add(config);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "AIConfig", id, $"Created AI config: {request.Model}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            id,
            createdAt.UtcDateTime.Ticks.ToString(),
            $"AI config {config.Model} for {config.TaskType} was created.",
            ct);
        return new { id, status = "testing" };
    }

    public async Task<object> UpdateAIConfigAsync(string adminId, string adminName,
        string configId, AdminAIConfigUpdateRequest request, CancellationToken ct)
    {
        var a = await db.AIConfigVersions.FirstOrDefaultAsync(x => x.Id == configId, ct)
                ?? throw ApiException.NotFound("ai_config_not_found", "AI config not found.");

        if (request.Provider is not null)
        {
            var providerCode = request.Provider.Trim().ToLowerInvariant();
            var providerExists = await db.AiProviders.AnyAsync(p => p.Code == providerCode && p.IsActive, ct);
            if (!providerExists)
                throw ApiException.Validation("invalid_provider", "The selected provider is not registered or inactive. Register it at /admin/ai-providers first.");
            a.Provider = request.Provider;
        }

        if (request.Model is not null) a.Model = request.Model;
        if (request.TaskType is not null) a.TaskType = request.TaskType;
        if (request.Status is not null) a.Status = Enum.Parse<AIConfigStatus>(request.Status, true);
        if (request.Accuracy.HasValue) a.Accuracy = request.Accuracy.Value;
        if (request.ConfidenceThreshold.HasValue) a.ConfidenceThreshold = request.ConfidenceThreshold.Value;
        if (request.RoutingRule is not null) a.RoutingRule = request.RoutingRule;
        if (request.ExperimentFlag is not null) a.ExperimentFlag = request.ExperimentFlag;
        if (request.PromptLabel is not null) a.PromptLabel = request.PromptLabel;
        if (request.ConfidencePolicy is not null)
            a.ConfidencePolicyJson = JsonSupport.Serialize(request.ConfidencePolicy);
        var updatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Updated", "AIConfig", configId, $"Updated AI config: {a.Model}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            configId,
            updatedAt.UtcDateTime.Ticks.ToString(),
            $"AI config {a.Model} for {a.TaskType} was updated.",
            ct);
        return new { id = configId, status = a.Status.ToString().ToLowerInvariant() };
    }

    // ════════════════════════════════════════════
    //  Feature Flags
    // ════════════════════════════════════════════

    public async Task<object> GetFlagListAsync(string? flagType, CancellationToken ct)
    {
        var query = db.FeatureFlags.AsQueryable();

        if (!string.IsNullOrWhiteSpace(flagType) && flagType != "all")
        {
            var parsedType = Enum.Parse<FeatureFlagType>(flagType, true);
            query = query.Where(f => f.FlagType == parsedType);
        }

        var flags = await ToOrderedListDescendingAsync(query, f => f.UpdatedAt, ct);
        var items = flags.Select(f => new
        {
            f.Id,
            f.Name,
            f.Key,
            enabled = f.Enabled,
            type = f.FlagType.ToString().ToLowerInvariant(),
            rolloutPercentage = f.RolloutPercentage,
            description = f.Description,
            owner = f.Owner
        });

        return items;
    }

    public async Task<object> CreateFlagAsync(string adminId, string adminName,
        AdminFlagCreateRequest request, CancellationToken ct)
    {
        if (await db.FeatureFlags.AnyAsync(f => f.Key == request.Key, ct))
            throw ApiException.Conflict("flag_key_duplicate", "A flag with this key already exists.");

        var id = $"FLG-{Guid.NewGuid():N}"[..12];
        var now = DateTimeOffset.UtcNow;

        var flag = new FeatureFlag
        {
            Id = id,
            Name = request.Name,
            Key = request.Key,
            FlagType = !string.IsNullOrWhiteSpace(request.FlagType)
                ? Enum.Parse<FeatureFlagType>(request.FlagType, true)
                : FeatureFlagType.Release,
            Enabled = request.Enabled,
            RolloutPercentage = request.RolloutPercentage,
            Description = request.Description,
            Owner = request.Owner,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.FeatureFlags.Add(flag);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Created", "Flag", id, $"Created flag: {request.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            id,
            now.UtcDateTime.Ticks.ToString(),
            $"Feature flag {flag.Name} was created.",
            ct);
        return new { id, enabled = request.Enabled };
    }

    public async Task<object> UpdateFlagAsync(string adminId, string adminName,
        string flagId, AdminFlagUpdateRequest request, CancellationToken ct)
    {
        var f = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == flagId, ct)
                ?? throw ApiException.NotFound("flag_not_found", "Feature flag not found.");

        if (request.Name is not null) f.Name = request.Name;
        if (request.Key is not null) f.Key = request.Key;
        if (request.FlagType is not null) f.FlagType = Enum.Parse<FeatureFlagType>(request.FlagType, true);
        if (request.Enabled.HasValue) f.Enabled = request.Enabled.Value;
        if (request.RolloutPercentage.HasValue) f.RolloutPercentage = request.RolloutPercentage.Value;
        if (request.Description is not null) f.Description = request.Description;
        if (request.Owner is not null) f.Owner = request.Owner;
        f.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var action = request.Enabled.HasValue ? (request.Enabled.Value ? "Enabled" : "Disabled") : "Updated";
        await LogAuditAsync(adminId, adminName, action, "Flag", flagId, $"{action} flag: {f.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            flagId,
            f.UpdatedAt.UtcDateTime.Ticks.ToString(),
            $"Feature flag {f.Name} was {action.ToLowerInvariant()}.",
            ct);
        return new { id = flagId, enabled = f.Enabled };
    }

    // ════════════════════════════════════════════
    //  AI Config Activate  (B4)
    // ════════════════════════════════════════════

    public async Task<object> ActivateAIConfigAsync(string adminId, string adminName, string configId, CancellationToken ct)
    {
        var config = await db.AIConfigVersions.FirstOrDefaultAsync(x => x.Id == configId, ct)
                     ?? throw ApiException.NotFound("ai_config_not_found", "AI config not found.");

        if (config.Status == AIConfigStatus.Active)
            return new { id = configId, status = "active", message = "Already active." };

        // Deactivate other configs of the same task type
        var sameTask = await db.AIConfigVersions
            .Where(a => a.TaskType == config.TaskType && a.Status == AIConfigStatus.Active)
            .ToListAsync(ct);
        foreach (var other in sameTask)
            other.Status = AIConfigStatus.Deprecated;

        config.Status = AIConfigStatus.Active;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Activated", "AIConfig", configId,
            $"Activated AI config: {config.Model} for {config.TaskType}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            configId,
            DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
            $"AI config {config.Model} for {config.TaskType} was activated.",
            ct);
        return new { id = configId, status = "active" };
    }

    public async Task<object> DeleteAIConfigAsync(string adminId, string adminName, string configId, CancellationToken ct)
    {
        var config = await db.AIConfigVersions.FirstOrDefaultAsync(x => x.Id == configId, ct)
                     ?? throw ApiException.NotFound("ai_config_not_found", "AI config not found.");

        db.AIConfigVersions.Remove(config);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Deleted", "AIConfig", configId,
            $"Deleted AI config: {config.Model} for {config.TaskType}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminAiConfigChanged,
            "ai_config",
            configId,
            DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
            $"AI config {config.Model} for {config.TaskType} was deleted.",
            ct);
        return new { id = configId, deleted = true };
    }

    // ════════════════════════════════════════════
    //  Flag Activate / Deactivate  (B5)
    // ════════════════════════════════════════════

    public async Task<object> ActivateFlagAsync(string adminId, string adminName, string flagId, CancellationToken ct)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == flagId, ct)
                   ?? throw ApiException.NotFound("flag_not_found", "Feature flag not found.");

        flag.Enabled = true;
        flag.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Activated", "Flag", flagId, $"Activated flag: {flag.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            flagId,
            flag.UpdatedAt.UtcDateTime.Ticks.ToString(),
            $"Feature flag {flag.Name} was activated.",
            ct);
        return new { id = flagId, enabled = true };
    }

    public async Task<object> DeactivateFlagAsync(string adminId, string adminName, string flagId, CancellationToken ct)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == flagId, ct)
                   ?? throw ApiException.NotFound("flag_not_found", "Feature flag not found.");

        flag.Enabled = false;
        flag.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Deactivated", "Flag", flagId, $"Deactivated flag: {flag.Name}", ct);
        await NotifyAdminsAsync(
            NotificationEventKey.AdminFeatureFlagChanged,
            "feature_flag",
            flagId,
            flag.UpdatedAt.UtcDateTime.Ticks.ToString(),
            $"Feature flag {flag.Name} was deactivated.",
            ct);
        return new { id = flagId, enabled = false };
    }
}
