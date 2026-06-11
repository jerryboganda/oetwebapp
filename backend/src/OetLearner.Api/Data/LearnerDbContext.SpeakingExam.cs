using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingCardType> SpeakingCardTypes => Set<SpeakingCardType>();
    public DbSet<SpeakingExamSession> SpeakingExamSessions => Set<SpeakingExamSession>();

    partial void OnModelCreatingSpeakingExam(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingCardType>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // The hidden card type on RolePlayCard. Restrict delete so a card type
        // in use cannot be removed out from under its cards (admin soft-deletes
        // via IsActive instead).
        modelBuilder.Entity<RolePlayCard>(e =>
        {
            e.HasOne(x => x.CardType)
                .WithMany()
                .HasForeignKey(x => x.CardTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingExamSession>(e =>
        {
            e.HasKey(x => x.Id);
        });
    }
}
