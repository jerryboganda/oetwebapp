using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingMock> WritingMocks => Set<WritingMock>();
    public DbSet<WritingMockSession> WritingMockSessions => Set<WritingMockSession>();

    partial void OnModelCreatingWritingMocks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingMock>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ScenarioId);
        });

        modelBuilder.Entity<WritingMockSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.StartedAt });
            e.HasIndex(x => x.MockId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubmissionId);
        });
    }
}
