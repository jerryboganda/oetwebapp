using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>Admin Documentation Center — see <c>Domain/DocumentationCenterEntities.cs</c>.</summary>
public partial class LearnerDbContext
{
    public DbSet<DocumentationModule> DocumentationModules => Set<DocumentationModule>();
    public DbSet<DocumentationVersion> DocumentationVersions => Set<DocumentationVersion>();
    public DbSet<DocumentationEvidenceItem> DocumentationEvidenceItems => Set<DocumentationEvidenceItem>();
    public DbSet<DocumentationExport> DocumentationExports => Set<DocumentationExport>();

    partial void OnModelCreatingDocumentationCenter(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentationModule>(entity =>
        {
            entity.HasIndex(x => x.SortOrder).HasDatabaseName("IX_DocumentationModules_SortOrder");
        });

        modelBuilder.Entity<DocumentationVersion>(entity =>
        {
            entity.Property(x => x.ContentJson).HasColumnType("jsonb");
            entity.HasOne(x => x.Module).WithMany(m => m.Versions)
                .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ModuleId, x.IsCurrent })
                .HasDatabaseName("IX_DocumentationVersions_ModuleId_IsCurrent");
            entity.HasIndex(x => new { x.ModuleId, x.VersionNumber })
                .IsUnique()
                .HasDatabaseName("IX_DocumentationVersions_ModuleId_VersionNumber");
        });

        modelBuilder.Entity<DocumentationEvidenceItem>(entity =>
        {
            entity.HasOne(x => x.Module).WithMany(m => m.EvidenceItems)
                .HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.EvidenceId).IsUnique()
                .HasDatabaseName("IX_DocumentationEvidenceItems_EvidenceId");
            entity.HasIndex(x => x.ModuleId).HasDatabaseName("IX_DocumentationEvidenceItems_ModuleId");
        });

        modelBuilder.Entity<DocumentationExport>(entity =>
        {
            entity.Property(x => x.IncludedVersionIdsJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.GeneratedAt).HasDatabaseName("IX_DocumentationExports_GeneratedAt");
        });
    }
}
