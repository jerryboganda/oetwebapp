using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingOcrJob> WritingOcrJobs => Set<WritingOcrJob>();

    partial void OnModelCreatingWritingOcr(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingOcrJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ImageUrlsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .IsDescending(false, true);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubmissionId);
        });
    }
}
