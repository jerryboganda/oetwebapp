using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingCommonMistake> WritingCommonMistakes => Set<WritingCommonMistake>();
    public DbSet<WritingLearnerMistakeStat> WritingLearnerMistakeStats => Set<WritingLearnerMistakeStat>();

    partial void OnModelCreatingWritingMistakes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingCommonMistake>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.CanonRuleId);
            e.HasIndex(x => x.RelatedSubSkill);
        });

        modelBuilder.Entity<WritingLearnerMistakeStat>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.MistakeId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.LastOccurredAt })
                .IsDescending(false, true);
        });
    }
}
