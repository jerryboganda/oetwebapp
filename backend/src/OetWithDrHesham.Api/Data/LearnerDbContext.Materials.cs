using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<MaterialFolder> MaterialFolders => Set<MaterialFolder>();
    public DbSet<MaterialFile> MaterialFiles => Set<MaterialFile>();
    public DbSet<MaterialFolderAudience> MaterialFolderAudiences => Set<MaterialFolderAudience>();

    partial void OnModelCreatingMaterials(ModelBuilder modelBuilder)
    {
        // Self-reference: folder → parent folder (Restrict so you can't orphan subfolders)
        modelBuilder.Entity<MaterialFolder>()
            .HasOne(x => x.ParentFolder)
            .WithMany()
            .HasForeignKey(x => x.ParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // MaterialFile → MediaAsset: Restrict (SHA-deduped shared asset; never auto-delete)
        modelBuilder.Entity<MaterialFile>()
            .HasOne(x => x.MediaAsset)
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // MaterialFile → MaterialFolder: Restrict (folder must be empty before deleting)
        modelBuilder.Entity<MaterialFile>()
            .HasOne(x => x.Folder)
            .WithMany()
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        // MaterialFolderAudience → MaterialFolder: Cascade (audience rows are owned by the folder)
        modelBuilder.Entity<MaterialFolderAudience>()
            .HasOne(x => x.Folder)
            .WithMany(x => x.Audiences)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
