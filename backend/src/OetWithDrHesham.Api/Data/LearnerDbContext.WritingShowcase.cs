using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingShowcasePost> WritingShowcasePosts => Set<WritingShowcasePost>();

    partial void OnModelCreatingWritingShowcase(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingShowcasePost>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Status, x.PublishedAt })
                .IsDescending(false, true);
            e.HasIndex(x => new { x.Profession, x.LetterType, x.Status });
            e.HasIndex(x => x.SubmissionId).IsUnique();
        });
    }
}
