using System;
using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain.AiAssistant;

public class AiRolePermissionMatrix
{
    [Key]
    public Guid Id { get; set; }

    // Mirrors AdminPermissionType key. Admin-only feature for now, but
    // matrix lets system_admin tighten/loosen per sub-admin.
    [Required, MaxLength(64)]
    public string RoleKey { get; set; } = string.Empty;

    public bool CanUseAssistant { get; set; }
    public bool CanManageAssistant { get; set; }
    public bool CanUseUnrestricted { get; set; } // bypass approval prompts
    public bool CanRunCommands { get; set; }
    public bool CanWriteFiles { get; set; }
    public bool CanReindex { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
