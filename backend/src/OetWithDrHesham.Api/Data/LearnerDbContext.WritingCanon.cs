using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingCanonRule> WritingCanonRules => Set<WritingCanonRule>();
    public DbSet<WritingCanonViolation> WritingCanonViolations => Set<WritingCanonViolation>();

    partial void OnModelCreatingWritingCanon(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingCanonRule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AppliesToLetterTypesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.AppliesToProfessionsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.CorrectExamplesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.IncorrectExamplesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.DetectionConfigJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasIndex(x => new { x.Category, x.Active });
            e.HasIndex(x => x.DetectionType);
        });

        modelBuilder.Entity<WritingCanonViolation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => x.RuleId);
            e.HasIndex(x => new { x.SubmissionId, x.RuleId });
        });
    }
}
