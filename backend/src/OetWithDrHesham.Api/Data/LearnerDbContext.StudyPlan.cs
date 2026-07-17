using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<StudyPlanTemplate> StudyPlanTemplates => Set<StudyPlanTemplate>();
    public DbSet<StudyPlanTemplateTier> StudyPlanTemplateTiers => Set<StudyPlanTemplateTier>();

    partial void OnModelCreatingStudyPlan(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudyPlan>().HasIndex(x => new { x.UserId, x.IsActive });
        modelBuilder.Entity<StudyPlan>().HasIndex(x => x.TemplateId);
        modelBuilder.Entity<StudyPlanItem>().HasIndex(x => new { x.StudyPlanId, x.WeekIndex });
        modelBuilder.Entity<StudyPlanItem>().HasIndex(x => x.LinkedReviewItemId);
        modelBuilder.Entity<StudyPlanItem>().HasIndex(x => x.ReplacedById);

        modelBuilder.Entity<StudyPlanTemplate>().HasIndex(x => new { x.IsActive, x.MinWeeks, x.MaxWeeks });
        modelBuilder.Entity<StudyPlanTemplate>().HasIndex(x => x.ProfessionId);

        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<StudyPlanTemplate>().Property(x => x.TemplateBodyJson).HasColumnType("jsonb");
            modelBuilder.Entity<StudyPlanTemplate>().Property(x => x.FocusTagsJson).HasColumnType("jsonb");
            modelBuilder.Entity<StudyPlan>().Property(x => x.SubtestWeightsJson).HasColumnType("jsonb");
        }
    }
}
