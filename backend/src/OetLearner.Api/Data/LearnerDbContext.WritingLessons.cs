using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingLessonV2> WritingLessonsV2 => Set<WritingLessonV2>();
    public DbSet<WritingLessonCompletionV2> WritingLessonCompletionsV2 => Set<WritingLessonCompletionV2>();

    partial void OnModelCreatingWritingLessons(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingLessonV2>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.QuizQuestionsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.HasIndex(x => new { x.SubSkill, x.OrderInCourse });
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<WritingLessonCompletionV2>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
        });
    }
}
