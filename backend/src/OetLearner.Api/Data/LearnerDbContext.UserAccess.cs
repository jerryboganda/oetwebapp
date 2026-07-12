using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// Per-user access overrides: module enable/disable, Materials folder allow-list,
/// and Recall-set allow-list. These are the per-USER layer on top of the per-PLAN
/// entitlements, resolved in the entitlement resolver / content services.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<UserModuleOverride> UserModuleOverrides => Set<UserModuleOverride>();
    public DbSet<UserMaterialFolderAccess> UserMaterialFolderAccesses => Set<UserMaterialFolderAccess>();
    public DbSet<UserRecallSetAccess> UserRecallSetAccesses => Set<UserRecallSetAccess>();

    partial void OnModelCreatingUserAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserModuleOverride>(e =>
        {
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<UserMaterialFolderAccess>(e =>
        {
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<UserRecallSetAccess>(e =>
        {
            e.HasIndex(x => x.UserId);
        });
    }
}
