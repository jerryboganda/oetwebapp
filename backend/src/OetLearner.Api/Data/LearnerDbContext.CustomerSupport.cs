using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    partial void OnModelCreatingCustomerSupport(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerSupportCase>(entity =>
        {
            entity.Property(x => x.ExternalTicketId).IsRequired();
            entity.Property(x => x.CandidateUserId).IsRequired();
            entity.Property(x => x.Subject).IsRequired();
            entity.Property(x => x.Status).IsRequired();
            entity.Property(x => x.OpenedByAdminId).IsRequired();
            entity.Property(x => x.OpenedByAdminName).IsRequired();
            entity.HasIndex(x => new { x.ExternalTicketId, x.CandidateUserId })
                .IsUnique()
                .HasDatabaseName("UX_CustomerSupportCases_Ticket_Candidate");
            entity.HasIndex(x => new { x.CandidateUserId, x.Status, x.ExpiresAt })
                .HasDatabaseName("IX_CustomerSupportCases_Candidate_Status_Expiry");
        });
    }
}
