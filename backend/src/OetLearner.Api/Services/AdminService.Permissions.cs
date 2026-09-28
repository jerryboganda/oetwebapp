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
    //  Admin Permissions (RBAC)
    // ════════════════════════════════════════════

    public async Task<object> GetAdminPermissionsAsync(string userId, CancellationToken ct)
    {
        var user = await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.NotFound("user_not_found", "User not found.");

        if (user.Role != ApplicationUserRoles.Admin)
            throw ApiException.Validation("not_admin", "User is not an admin.");

        var grants = await db.AdminPermissionGrants
            .AsNoTracking()
            .Where(g => g.AdminUserId == userId)
            .OrderBy(g => g.Permission)
            .ToListAsync(ct);

        return new
        {
            userId,
            permissions = grants.Select(g => new
            {
                permission = g.Permission,
                grantedBy = g.GrantedBy,
                grantedAt = g.GrantedAt
            }),
            allPermissions = AdminPermissions.All
        };
    }

    public async Task<object> UpdateAdminPermissionsAsync(
        string actorId, string actorName, string userId,
        AdminPermissionUpdateRequest request, CancellationToken ct)
    {
        var user = await db.ApplicationUserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.NotFound("user_not_found", "User not found.");

        if (user.Role != ApplicationUserRoles.Admin)
            throw ApiException.Validation("not_admin", "User is not an admin.");

        EnsureOwnerAccountMutationAllowed(actorId, user.Id, "change permissions");

        var invalid = request.Permissions.Except(AdminPermissions.All).ToArray();
        if (invalid.Length > 0)
            throw ApiException.Validation("invalid_permissions", $"Invalid permissions: {string.Join(", ", invalid)}");

        var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            var existing = await db.AdminPermissionGrants
                .Where(g => g.AdminUserId == userId)
                .ToListAsync(ct);

            db.AdminPermissionGrants.RemoveRange(existing);

            var now = timeProvider.GetUtcNow();
            foreach (var perm in request.Permissions.Distinct())
            {
                db.AdminPermissionGrants.Add(new AdminPermissionGrant
                {
                    Id = $"APG-{Guid.NewGuid():N}",
                    AdminUserId = userId,
                    Permission = perm,
                    GrantedBy = actorId,
                    GrantedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        await LogAuditAsync(actorId, actorName, "UpdatePermissions", "AdminPermission", userId,
            $"Set permissions: [{string.Join(", ", request.Permissions)}]", ct);

        return new { userId, permissions = request.Permissions, updated = true };
    }

    // ════════════════════════════════════════════
    //  Permission Templates
    // ════════════════════════════════════════════

    public object GetAllPermissions()
    {
        return new
        {
            permissions = AdminPermissions.All.Select(p => new { key = p }).ToArray()
        };
    }

    public async Task<object> GetPermissionTemplatesAsync(CancellationToken ct)
    {
        var templates = await db.PermissionTemplates
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return new
        {
            templates = templates.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description,
                permissions = System.Text.Json.JsonSerializer.Deserialize<string[]>(t.Permissions) ?? [],
                createdBy = t.CreatedBy,
                createdAt = t.CreatedAt
            })
        };
    }

    public async Task<object> CreatePermissionTemplateAsync(
        string actorId, string actorName,
        CreatePermissionTemplateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw ApiException.Validation("name_required", "Template name is required.");

        var invalid = request.Permissions.Except(AdminPermissions.All).ToArray();
        if (invalid.Length > 0)
            throw ApiException.Validation("invalid_permissions", $"Invalid permissions: {string.Join(", ", invalid)}");

        var exists = await db.PermissionTemplates.AnyAsync(t => t.Name == request.Name, ct);
        if (exists)
            throw ApiException.Validation("duplicate_name", "A template with this name already exists.");

        var template = new PermissionTemplate
        {
            Id = $"PT-{Guid.NewGuid():N}",
            Name = request.Name,
            Description = request.Description,
            Permissions = System.Text.Json.JsonSerializer.Serialize(request.Permissions.Distinct().ToArray()),
            CreatedBy = actorId,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.PermissionTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "CreatePermissionTemplate", "PermissionTemplate", template.Id,
            $"Created template '{request.Name}' with [{string.Join(", ", request.Permissions)}]", ct);

        return new
        {
            id = template.Id,
            name = template.Name,
            description = template.Description,
            permissions = request.Permissions,
            createdBy = template.CreatedBy,
            createdAt = template.CreatedAt
        };
    }

    public async Task<object> DeletePermissionTemplateAsync(
        string actorId, string actorName, string templateId, CancellationToken ct)
    {
        var template = await db.PermissionTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
                       ?? throw ApiException.NotFound("template_not_found", "Permission template not found.");

        db.PermissionTemplates.Remove(template);
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(actorId, actorName, "DeletePermissionTemplate", "PermissionTemplate", templateId,
            $"Deleted template '{template.Name}'", ct);

        return new { deleted = true, id = templateId };
    }

    public async Task<object> ApplyPermissionTemplateAsync(
        string actorId, string actorName, string userId, string templateId, CancellationToken ct)
    {
        var user = await db.ApplicationUserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw ApiException.NotFound("user_not_found", "User not found.");

        if (user.Role != ApplicationUserRoles.Admin)
            throw ApiException.Validation("not_admin", "User is not an admin.");

        EnsureOwnerAccountMutationAllowed(actorId, user.Id, "change permissions");

        var template = await db.PermissionTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct)
                       ?? throw ApiException.NotFound("template_not_found", "Permission template not found.");

        var permissions = System.Text.Json.JsonSerializer.Deserialize<string[]>(template.Permissions) ?? [];

        var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            var existing = await db.AdminPermissionGrants
                .Where(g => g.AdminUserId == userId)
                .ToListAsync(ct);

            db.AdminPermissionGrants.RemoveRange(existing);

            var now = timeProvider.GetUtcNow();
            foreach (var perm in permissions.Distinct())
            {
                db.AdminPermissionGrants.Add(new AdminPermissionGrant
                {
                    Id = $"APG-{Guid.NewGuid():N}",
                    AdminUserId = userId,
                    Permission = perm,
                    GrantedBy = actorId,
                    GrantedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        await LogAuditAsync(actorId, actorName, "ApplyPermissionTemplate", "AdminPermission", userId,
            $"Applied template '{template.Name}' → [{string.Join(", ", permissions)}]", ct);

        return new { userId, templateId, templateName = template.Name, permissions, applied = true };
    }
}
