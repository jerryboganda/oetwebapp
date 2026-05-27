using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingDiagnosticSession> WritingDiagnosticSessions => Set<WritingDiagnosticSession>();

    partial void OnModelCreatingWritingDiagnosticSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingDiagnosticSession>(e =>
        {
            e.HasKey(x => x.Id);
            // Lookup pattern: WHERE UserId = ? AND Id = ? (every read path).
            e.HasIndex(x => new { x.UserId, x.Id });
            // Cleanup cron scans on ExpiresAt.
            e.HasIndex(x => x.ExpiresAt);
            // Reverse-lookup from a submission row back to the originating session.
            e.HasIndex(x => x.SubmissionId);
        });
    }
}
