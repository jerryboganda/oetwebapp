using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    partial void OnModelCreatingAnswerKeyReports(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssessmentAnswerKeyReport>(entity =>
        {
            entity.Property(x => x.Assessment).IsRequired();
            entity.Property(x => x.AttemptId).IsRequired();
            entity.Property(x => x.PaperId).IsRequired();
            entity.Property(x => x.QuestionId).IsRequired();
            entity.Property(x => x.PartCode).IsRequired();
            entity.Property(x => x.QuestionStemSnapshot).IsRequired();
            entity.Property(x => x.PaperTitleSnapshot).IsRequired();
            entity.Property(x => x.ReporterUserId).IsRequired();
            entity.Property(x => x.LearnerAnswerSnapshot).IsRequired();
            entity.Property(x => x.OfficialAnswerSnapshot).IsRequired();
            entity.Property(x => x.ReasonCode).IsRequired();
            entity.Property(x => x.Status).IsRequired();
            entity.HasIndex(x => new { x.Status, x.CreatedAt })
                .HasDatabaseName("IX_AssessmentAnswerKeyReports_Status_CreatedAt");
            entity.HasIndex(x => new { x.Assessment, x.Status })
                .HasDatabaseName("IX_AssessmentAnswerKeyReports_Assessment_Status");
            entity.HasIndex(x => new { x.ReporterUserId, x.Assessment, x.QuestionId, x.AttemptId })
                .HasDatabaseName("IX_AssessmentAnswerKeyReports_Reporter_Assessment_Question_Attempt");
        });
    }
}
