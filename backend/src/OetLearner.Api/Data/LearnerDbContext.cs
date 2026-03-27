using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public class LearnerDbContext(DbContextOptions<LearnerDbContext> options) : DbContext(options)
{
    public DbSet<LearnerUser> Users => Set<LearnerUser>();
    public DbSet<LearnerGoal> Goals => Set<LearnerGoal>();
    public DbSet<LearnerSettings> Settings => Set<LearnerSettings>();
    public DbSet<ProfessionReference> Professions => Set<ProfessionReference>();
    public DbSet<SubtestReference> Subtests => Set<SubtestReference>();
    public DbSet<CriterionReference> Criteria => Set<CriterionReference>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<ReadinessSnapshot> ReadinessSnapshots => Set<ReadinessSnapshot>();
    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();
    public DbSet<StudyPlanItem> StudyPlanItems => Set<StudyPlanItem>();
    public DbSet<ReviewRequest> ReviewRequests => Set<ReviewRequest>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<BackgroundJobItem> BackgroundJobs => Set<BackgroundJobItem>();
    public DbSet<DiagnosticSession> DiagnosticSessions => Set<DiagnosticSession>();
    public DbSet<DiagnosticSubtestStatus> DiagnosticSubtests => Set<DiagnosticSubtestStatus>();
    public DbSet<MockAttempt> MockAttempts => Set<MockAttempt>();
    public DbSet<MockReport> MockReports => Set<MockReport>();
    public DbSet<AnalyticsEventRecord> AnalyticsEvents => Set<AnalyticsEventRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuthAccount> AuthAccounts => Set<AuthAccount>();

    // Expert Console entities
    public DbSet<ExpertUser> ExpertUsers => Set<ExpertUser>();
    public DbSet<ExpertReviewAssignment> ExpertReviewAssignments => Set<ExpertReviewAssignment>();
    public DbSet<ExpertReviewDraft> ExpertReviewDrafts => Set<ExpertReviewDraft>();
    public DbSet<ExpertCalibrationCase> ExpertCalibrationCases => Set<ExpertCalibrationCase>();
    public DbSet<ExpertCalibrationResult> ExpertCalibrationResults => Set<ExpertCalibrationResult>();
    public DbSet<ExpertCalibrationNote> ExpertCalibrationNotes => Set<ExpertCalibrationNote>();
    public DbSet<ExpertAvailability> ExpertAvailabilities => Set<ExpertAvailability>();
    public DbSet<ExpertMetricSnapshot> ExpertMetricSnapshots => Set<ExpertMetricSnapshot>();

    // Admin / CMS entities
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<AIConfigVersion> AIConfigVersions => Set<AIConfigVersion>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<BillingPlan> BillingPlans => Set<BillingPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContentItem>().HasIndex(x => new { x.SubtestCode, x.Status });
        modelBuilder.Entity<Attempt>().HasIndex(x => new { x.UserId, x.SubtestCode, x.State });
        modelBuilder.Entity<Evaluation>().HasIndex(x => new { x.AttemptId, x.State });
        modelBuilder.Entity<ReviewRequest>().HasIndex(x => new { x.AttemptId, x.State });
        modelBuilder.Entity<StudyPlanItem>().HasIndex(x => new { x.StudyPlanId, x.Section, x.Status });
        modelBuilder.Entity<BackgroundJobItem>().HasIndex(x => new { x.State, x.AvailableAt });
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.UserId, x.IssuedAt });
        modelBuilder.Entity<AnalyticsEventRecord>().HasIndex(x => new { x.UserId, x.EventName, x.OccurredAt });
        modelBuilder.Entity<IdempotencyRecord>().HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        modelBuilder.Entity<AuthAccount>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<AuthAccount>().HasIndex(x => new { x.SubjectId, x.Role }).IsUnique();

        // Learner lookup indexes (frequently queried by UserId)
        modelBuilder.Entity<LearnerGoal>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerSettings>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Subscription>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Wallet>().HasIndex(x => x.UserId);
        modelBuilder.Entity<StudyPlan>().HasIndex(x => x.UserId);
        modelBuilder.Entity<ReadinessSnapshot>().HasIndex(x => x.UserId);
        modelBuilder.Entity<DiagnosticSession>().HasIndex(x => new { x.UserId, x.State });
        modelBuilder.Entity<MockAttempt>().HasIndex(x => x.UserId);
        modelBuilder.Entity<UploadSession>().HasIndex(x => x.AttemptId);

        // Expert indexes
        modelBuilder.Entity<ExpertReviewAssignment>().HasIndex(x => new { x.ReviewRequestId, x.ClaimState });
        modelBuilder.Entity<ExpertReviewAssignment>().HasIndex(x => x.AssignedReviewerId);
        modelBuilder.Entity<ExpertReviewDraft>().HasIndex(x => new { x.ReviewRequestId, x.ReviewerId });
        modelBuilder.Entity<ExpertCalibrationResult>().HasIndex(x => new { x.CalibrationCaseId, x.ReviewerId });
        modelBuilder.Entity<ExpertAvailability>().HasIndex(x => x.ReviewerId);
        modelBuilder.Entity<ExpertMetricSnapshot>().HasIndex(x => new { x.ReviewerId, x.WindowStart });

        // Admin indexes
        modelBuilder.Entity<ContentRevision>().HasIndex(x => new { x.ContentItemId, x.RevisionNumber });
        modelBuilder.Entity<AIConfigVersion>().HasIndex(x => new { x.TaskType, x.Status });
        modelBuilder.Entity<FeatureFlag>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<AuditEvent>().HasIndex(x => x.OccurredAt);
        modelBuilder.Entity<AuditEvent>().HasIndex(x => x.ActorId);
        modelBuilder.Entity<AuditEvent>().HasIndex(x => new { x.ResourceType, x.ResourceId });
    }
}
