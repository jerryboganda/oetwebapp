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
    public DbSet<AccountFreezePolicy> AccountFreezePolicies => Set<AccountFreezePolicy>();
    public DbSet<AccountFreezeRecord> AccountFreezeRecords => Set<AccountFreezeRecord>();
    public DbSet<AccountFreezeEntitlement> AccountFreezeEntitlements => Set<AccountFreezeEntitlement>();
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
    public DbSet<ApplicationUserAccount> ApplicationUserAccounts => Set<ApplicationUserAccount>();
    public DbSet<RefreshTokenRecord> RefreshTokenRecords => Set<RefreshTokenRecord>();
    public DbSet<EmailOtpChallenge> EmailOtpChallenges => Set<EmailOtpChallenge>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<ExternalIdentityLink> ExternalIdentityLinks => Set<ExternalIdentityLink>();
    public DbSet<LearnerRegistrationProfile> LearnerRegistrationProfiles => Set<LearnerRegistrationProfile>();
    public DbSet<SignupExamTypeCatalog> SignupExamTypeCatalog => Set<SignupExamTypeCatalog>();
    public DbSet<SignupProfessionCatalog> SignupProfessionCatalog => Set<SignupProfessionCatalog>();
    public DbSet<SignupSessionCatalog> SignupSessionCatalog => Set<SignupSessionCatalog>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<NotificationInboxItem> NotificationInboxItems => Set<NotificationInboxItem>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationPolicyOverride> NotificationPolicyOverrides => Set<NotificationPolicyOverride>();
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<MobilePushToken> MobilePushTokens => Set<MobilePushToken>();
    public DbSet<SubscriptionItem> SubscriptionItems => Set<SubscriptionItem>();
    public DbSet<BillingAddOn> BillingAddOns => Set<BillingAddOn>();
    public DbSet<BillingCoupon> BillingCoupons => Set<BillingCoupon>();
    public DbSet<BillingCouponRedemption> BillingCouponRedemptions => Set<BillingCouponRedemption>();
    public DbSet<BillingQuote> BillingQuotes => Set<BillingQuote>();
    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();

    // Multi-exam reference entities
    public DbSet<ExamType> ExamTypes => Set<ExamType>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<ExamFamily> ExamFamilies => Set<ExamFamily>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();

    // Expert Console entities
    public DbSet<ExpertUser> ExpertUsers => Set<ExpertUser>();
    public DbSet<ExpertReviewAssignment> ExpertReviewAssignments => Set<ExpertReviewAssignment>();
    public DbSet<ExpertReviewDraft> ExpertReviewDrafts => Set<ExpertReviewDraft>();
    public DbSet<ExpertCalibrationCase> ExpertCalibrationCases => Set<ExpertCalibrationCase>();
    public DbSet<ExpertCalibrationResult> ExpertCalibrationResults => Set<ExpertCalibrationResult>();
    public DbSet<ExpertCalibrationNote> ExpertCalibrationNotes => Set<ExpertCalibrationNote>();
    public DbSet<ExpertAvailability> ExpertAvailabilities => Set<ExpertAvailability>();
    public DbSet<ExpertMetricSnapshot> ExpertMetricSnapshots => Set<ExpertMetricSnapshot>();

    // Gamification entities
    public DbSet<LearnerStreak> LearnerStreaks => Set<LearnerStreak>();
    public DbSet<LearnerXP> LearnerXPs => Set<LearnerXP>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<LearnerAchievement> LearnerAchievements => Set<LearnerAchievement>();
    public DbSet<LeaderboardEntry> LeaderboardEntries => Set<LeaderboardEntry>();

    // Spaced repetition entities
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();

    // Vocabulary entities
    public DbSet<VocabularyTerm> VocabularyTerms => Set<VocabularyTerm>();
    public DbSet<LearnerVocabulary> LearnerVocabularies => Set<LearnerVocabulary>();
    public DbSet<VocabularyQuizResult> VocabularyQuizResults => Set<VocabularyQuizResult>();

    // Adaptive difficulty entities
    public DbSet<LearnerSkillProfile> LearnerSkillProfiles => Set<LearnerSkillProfile>();

    // AI features entities
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<WritingCoachSession> WritingCoachSessions => Set<WritingCoachSession>();
    public DbSet<WritingCoachSuggestion> WritingCoachSuggestions => Set<WritingCoachSuggestion>();
    public DbSet<PronunciationAssessment> PronunciationAssessments => Set<PronunciationAssessment>();
    public DbSet<PronunciationDrill> PronunciationDrills => Set<PronunciationDrill>();
    public DbSet<LearnerPronunciationProgress> LearnerPronunciationProgress => Set<LearnerPronunciationProgress>();
    public DbSet<PredictionSnapshot> PredictionSnapshots => Set<PredictionSnapshot>();

    // Learning content entities
    public DbSet<GrammarLesson> GrammarLessons => Set<GrammarLesson>();
    public DbSet<LearnerGrammarProgress> LearnerGrammarProgress => Set<LearnerGrammarProgress>();
    public DbSet<VideoLesson> VideoLessons => Set<VideoLesson>();
    public DbSet<LearnerVideoProgress> LearnerVideoProgress => Set<LearnerVideoProgress>();
    public DbSet<StrategyGuide> StrategyGuides => Set<StrategyGuide>();

    // Community entities
    public DbSet<ForumCategory> ForumCategories => Set<ForumCategory>();
    public DbSet<ForumThread> ForumThreads => Set<ForumThread>();
    public DbSet<ForumReply> ForumReplies => Set<ForumReply>();
    public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
    public DbSet<StudyGroupMember> StudyGroupMembers => Set<StudyGroupMember>();
    public DbSet<PeerReviewRequest> PeerReviewRequests => Set<PeerReviewRequest>();
    public DbSet<PeerReviewFeedback> PeerReviewFeedbacks => Set<PeerReviewFeedback>();

    // Tutoring entities
    public DbSet<TutoringSession> TutoringSessions => Set<TutoringSession>();
    public DbSet<TutoringAvailability> TutoringAvailabilities => Set<TutoringAvailability>();

    // Private Speaking session entities
    public DbSet<PrivateSpeakingConfig> PrivateSpeakingConfigs => Set<PrivateSpeakingConfig>();
    public DbSet<PrivateSpeakingTutorProfile> PrivateSpeakingTutorProfiles => Set<PrivateSpeakingTutorProfile>();
    public DbSet<PrivateSpeakingAvailabilityRule> PrivateSpeakingAvailabilityRules => Set<PrivateSpeakingAvailabilityRule>();
    public DbSet<PrivateSpeakingAvailabilityOverride> PrivateSpeakingAvailabilityOverrides => Set<PrivateSpeakingAvailabilityOverride>();
    public DbSet<PrivateSpeakingBooking> PrivateSpeakingBookings => Set<PrivateSpeakingBooking>();
    public DbSet<PrivateSpeakingAuditLog> PrivateSpeakingAuditLogs => Set<PrivateSpeakingAuditLog>();

    // Social / achievement entities
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<SponsorAccount> SponsorAccounts => Set<SponsorAccount>();
    public DbSet<SponsorLearnerLink> SponsorLearnerLinks => Set<SponsorLearnerLink>();
    public DbSet<Cohort> Cohorts => Set<Cohort>();
    public DbSet<CohortMember> CohortMembers => Set<CohortMember>();
    public DbSet<Sponsorship> Sponsorships => Set<Sponsorship>();

    // Marketplace / booking entities
    public DbSet<ExamBooking> ExamBookings => Set<ExamBooking>();
    public DbSet<ContentContributor> ContentContributors => Set<ContentContributor>();
    public DbSet<ContentSubmission> ContentSubmissions => Set<ContentSubmission>();

    // Admin / CMS entities
    public DbSet<ContentGenerationJob> ContentGenerationJobs => Set<ContentGenerationJob>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<AIConfigVersion> AIConfigVersions => Set<AIConfigVersion>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // AI usage accounting. See docs/AI-USAGE-POLICY.md. Every AI call made
    // through AiGatewayService produces exactly one AiUsageRecord row.
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    // AI quota + policy (Slice 2). Admin-configurable surfaces for the
    // policies documented in docs/AI-USAGE-POLICY.md §3, §4, §7.
    public DbSet<AiQuotaPlan> AiQuotaPlans => Set<AiQuotaPlan>();
    public DbSet<AiQuotaCounter> AiQuotaCounters => Set<AiQuotaCounter>();
    public DbSet<AiUserQuotaOverride> AiUserQuotaOverrides => Set<AiUserQuotaOverride>();
    public DbSet<AiGlobalPolicy> AiGlobalPolicies => Set<AiGlobalPolicy>();

    // BYOK (Slice 3). Encrypted via ASP.NET Data Protection; KeyHint is the
    // only display-safe field.
    public DbSet<UserAiCredential> UserAiCredentials => Set<UserAiCredential>();
    public DbSet<UserAiPreferences> UserAiPreferences => Set<UserAiPreferences>();

    // Provider registry (Slice 5). DB-backed replacement for the previous
    // config-only provider registration.
    public DbSet<AiProvider> AiProviders => Set<AiProvider>();

    // Credit ledger (Slice 6). Append-only; balance = SUM(TokensDelta).
    public DbSet<AiCreditLedgerEntry> AiCreditLedger => Set<AiCreditLedgerEntry>();

    // Content Paper subsystem (Content Upload, Slice 1). Curatorial papers
    // that bundle typed assets pointing at MediaAsset rows.
    public DbSet<ContentPaper> ContentPapers => Set<ContentPaper>();
    public DbSet<ContentPaperAsset> ContentPaperAssets => Set<ContentPaperAsset>();

    // Content Upload chunked session (Slice 2).
    public DbSet<AdminUploadSession> AdminUploadSessions => Set<AdminUploadSession>();

    // Reading Authoring subsystem (Reading Slice R1).
    public DbSet<ReadingPart> ReadingParts => Set<ReadingPart>();
    public DbSet<ReadingText> ReadingTexts => Set<ReadingText>();
    public DbSet<ReadingQuestion> ReadingQuestions => Set<ReadingQuestion>();
    public DbSet<ReadingAttempt> ReadingAttempts => Set<ReadingAttempt>();
    public DbSet<ReadingAnswer> ReadingAnswers => Set<ReadingAnswer>();
    public DbSet<ReadingPolicy> ReadingPolicies => Set<ReadingPolicy>();
    public DbSet<ReadingUserPolicyOverride> ReadingUserPolicyOverrides => Set<ReadingUserPolicyOverride>();
    public DbSet<BillingPlan> BillingPlans => Set<BillingPlan>();
    public DbSet<AdminPermissionGrant> AdminPermissionGrants => Set<AdminPermissionGrant>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<PermissionTemplate> PermissionTemplates => Set<PermissionTemplate>();
    public DbSet<ContentPublishRequest> ContentPublishRequests => Set<ContentPublishRequest>();
    public DbSet<ReviewEscalation> ReviewEscalations => Set<ReviewEscalation>();
    public DbSet<LearnerEscalation> LearnerEscalations => Set<LearnerEscalation>();
    public DbSet<ScoreGuaranteePledge> ScoreGuaranteePledges => Set<ScoreGuaranteePledge>();
    public DbSet<ReferralRecord> ReferralRecords => Set<ReferralRecord>();
    public DbSet<ExpertAnnotationTemplate> ExpertAnnotationTemplates => Set<ExpertAnnotationTemplate>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<StudyCommitment> StudyCommitments => Set<StudyCommitment>();
    public DbSet<LearnerCertificate> LearnerCertificates => Set<LearnerCertificate>();

    // Content hierarchy entities
    public DbSet<ContentProgram> ContentPrograms => Set<ContentProgram>();
    public DbSet<ContentTrack> ContentTracks => Set<ContentTrack>();
    public DbSet<ContentModule> ContentModules => Set<ContentModule>();
    public DbSet<ContentLesson> ContentLessons => Set<ContentLesson>();
    public DbSet<ContentReference> ContentReferences => Set<ContentReference>();

    // Package / entitlement entities
    public DbSet<ContentPackage> ContentPackages => Set<ContentPackage>();
    public DbSet<PackageContentRule> PackageContentRules => Set<PackageContentRule>();

    // Media asset entities
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    // Testimonial / marketing entities
    public DbSet<TestimonialAsset> TestimonialAssets => Set<TestimonialAsset>();
    public DbSet<MarketingAsset> MarketingAssets => Set<MarketingAsset>();
    public DbSet<FreePreviewAsset> FreePreviewAssets => Set<FreePreviewAsset>();

    // Foundation / remediation entities
    public DbSet<FoundationResource> FoundationResources => Set<FoundationResource>();

    // Content cohort overlay entities
    public DbSet<ContentCohortOverlay> ContentCohortOverlays => Set<ContentCohortOverlay>();

    // Content import entities
    public DbSet<ContentImportBatch> ContentImportBatches => Set<ContentImportBatch>();

    // Admin content management entities
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<ConversationTemplate> ConversationTemplates => Set<ConversationTemplate>();
    public DbSet<FreeTierConfig> FreeTierConfigs => Set<FreeTierConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContentItem>().HasIndex(x => new { x.SubtestCode, x.Status });
        modelBuilder.Entity<Attempt>().HasIndex(x => new { x.UserId, x.SubtestCode, x.State });
        modelBuilder.Entity<Evaluation>().HasIndex(x => new { x.AttemptId, x.State });
        modelBuilder.Entity<ReviewRequest>().HasIndex(x => new { x.AttemptId, x.State });
        modelBuilder.Entity<AccountFreezePolicy>().HasIndex(x => x.Version);
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => new { x.UserId, x.Status });
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsCurrent\" = TRUE");
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => new { x.Status, x.ScheduledStartAt });
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => new { x.Status, x.EndedAt });
        modelBuilder.Entity<AccountFreezeEntitlement>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<StudyPlanItem>().HasIndex(x => new { x.StudyPlanId, x.Section, x.Status });
        modelBuilder.Entity<BackgroundJobItem>().HasIndex(x => new { x.State, x.AvailableAt });
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.UserId, x.IssuedAt });
        modelBuilder.Entity<AnalyticsEventRecord>().HasIndex(x => new { x.UserId, x.EventName, x.OccurredAt });
        modelBuilder.Entity<IdempotencyRecord>().HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        modelBuilder.Entity<ApplicationUserAccount>().HasIndex(x => new { x.NormalizedEmail, x.Role });
        modelBuilder.Entity<ApplicationUserAccount>().HasIndex(x => x.DeletedAt);
        modelBuilder.Entity<RefreshTokenRecord>().HasIndex(x => new { x.ApplicationUserAccountId, x.ExpiresAt });
        modelBuilder.Entity<EmailOtpChallenge>().HasIndex(x => new { x.ApplicationUserAccountId, x.Purpose, x.ExpiresAt });
        modelBuilder.Entity<MfaRecoveryCode>().HasIndex(x => x.ApplicationUserAccountId);
        modelBuilder.Entity<ExternalIdentityLink>().HasIndex(x => new { x.ApplicationUserAccountId, x.Provider });
        modelBuilder.Entity<LearnerRegistrationProfile>().HasIndex(x => x.ApplicationUserAccountId).IsUnique();
        modelBuilder.Entity<LearnerRegistrationProfile>().HasIndex(x => x.LearnerUserId).IsUnique();
        modelBuilder.Entity<SignupExamTypeCatalog>().HasIndex(x => new { x.IsActive, x.SortOrder });
        modelBuilder.Entity<SignupProfessionCatalog>().HasIndex(x => new { x.IsActive, x.SortOrder });
        modelBuilder.Entity<SignupSessionCatalog>().HasIndex(x => new { x.IsActive, x.SortOrder });
        modelBuilder.Entity<NotificationEvent>().Property(x => x.EventKey).HasMaxLength(128);
        modelBuilder.Entity<NotificationInboxItem>().Property(x => x.EventKey).HasMaxLength(128);
        modelBuilder.Entity<NotificationPolicyOverride>().Property(x => x.EventKey).HasMaxLength(128);

        // Learner lookup indexes (frequently queried by UserId)
        modelBuilder.Entity<LearnerGoal>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerSettings>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Subscription>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerUser>().Property(x => x.AccountStatus).IsConcurrencyToken();
        modelBuilder.Entity<ExpertUser>().Property(x => x.IsActive).IsConcurrencyToken();
        modelBuilder.Entity<Wallet>().Property(x => x.LastUpdatedAt).IsConcurrencyToken();
        modelBuilder.Entity<Wallet>().HasIndex(x => x.UserId);
        modelBuilder.Entity<ReviewRequest>().Property(x => x.State).IsConcurrencyToken();
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

        // AiUsageRecord: keep rows even if the auth account is later deleted
        // (we still need historical usage / cost data). Indexes declared via
        // [Index] on the entity cover the common access paths; this just
        // pins the delete behaviour so a user delete does not cascade into
        // usage history.
        modelBuilder.Entity<AiUsageRecord>()
            .HasOne(x => x.AuthAccount)
            .WithMany()
            .HasForeignKey(x => x.AuthAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        // Content Paper subsystem (Slice 1). Cascade delete: removing a paper
        // also removes its asset links, but NOT the underlying MediaAsset
        // (same file may be referenced by other papers under SHA dedup).
        modelBuilder.Entity<ContentPaperAsset>()
            .HasOne(x => x.Paper)
            .WithMany(p => p.Assets)
            .HasForeignKey(x => x.PaperId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContentPaperAsset>()
            .HasOne(x => x.MediaAsset)
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reading Authoring (R1). A ReadingPart belongs to a ContentPaper.
        // Cascade: archive a paper → archive its structure. Answers and
        // attempts stay (historical record of a user's study).
        modelBuilder.Entity<ReadingText>()
            .HasOne(x => x.Part)
            .WithMany(p => p.Texts)
            .HasForeignKey(x => x.ReadingPartId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadingQuestion>()
            .HasOne(x => x.Part)
            .WithMany(p => p.Questions)
            .HasForeignKey(x => x.ReadingPartId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadingQuestion>()
            .HasOne(x => x.Text)
            .WithMany()
            .HasForeignKey(x => x.ReadingTextId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ReadingAnswer>()
            .HasOne(x => x.Attempt)
            .WithMany(a => a.Answers)
            .HasForeignKey(x => x.ReadingAttemptId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadingAnswer>()
            .HasOne(x => x.Question)
            .WithMany()
            .HasForeignKey(x => x.ReadingQuestionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationPreference>().HasIndex(x => x.AuthAccountId).IsUnique();
        modelBuilder.Entity<NotificationPolicyOverride>().HasIndex(x => new { x.AudienceRole, x.EventKey }).IsUnique();
        modelBuilder.Entity<NotificationDeliveryAttempt>().HasIndex(x => new { x.NotificationEventId, x.Channel, x.AttemptedAt });
        modelBuilder.Entity<PushSubscription>().HasIndex(x => x.Endpoint).IsUnique();
        modelBuilder.Entity<SubscriptionItem>().HasIndex(x => new { x.SubscriptionId, x.Status });
        modelBuilder.Entity<SubscriptionItem>().HasIndex(x => new { x.ItemCode, x.SubscriptionId });
        modelBuilder.Entity<BillingPlan>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<BillingPlan>().HasIndex(x => new { x.Status, x.DisplayOrder });
        modelBuilder.Entity<BillingAddOn>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<BillingAddOn>().HasIndex(x => new { x.Status, x.DisplayOrder });
        modelBuilder.Entity<BillingCoupon>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<BillingCoupon>().HasIndex(x => new { x.Status, x.EndsAt });
        modelBuilder.Entity<BillingCouponRedemption>().HasIndex(x => new { x.CouponCode, x.UserId, x.RedeemedAt });
        modelBuilder.Entity<BillingQuote>().HasIndex(x => new { x.UserId, x.CreatedAt });
        modelBuilder.Entity<BillingQuote>().HasIndex(x => new { x.Status, x.ExpiresAt });
        modelBuilder.Entity<BillingEvent>().HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
        modelBuilder.Entity<BillingEvent>().HasIndex(x => new { x.UserId, x.OccurredAt });

        // Multi-exam indexes
        modelBuilder.Entity<ExamFamily>().HasIndex(x => new { x.IsActive, x.SortOrder });
        modelBuilder.Entity<ExamType>().HasIndex(x => new { x.Status, x.SortOrder });
        modelBuilder.Entity<TaskType>().HasIndex(x => new { x.ExamTypeCode, x.SubtestCode, x.Status });
        modelBuilder.Entity<ContentItem>().HasIndex(x => x.ExamFamilyCode);
        modelBuilder.Entity<ContentItem>().HasIndex(x => x.ExamTypeCode);
        modelBuilder.Entity<Attempt>().HasIndex(x => x.ExamFamilyCode);
        modelBuilder.Entity<Attempt>().HasIndex(x => x.ExamTypeCode);

        // Gamification indexes
        modelBuilder.Entity<LearnerAchievement>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerAchievement>().HasIndex(x => new { x.UserId, x.AchievementId }).IsUnique();
        modelBuilder.Entity<Achievement>().HasIndex(x => new { x.Status, x.SortOrder });
        modelBuilder.Entity<LeaderboardEntry>().HasIndex(x => new { x.ExamTypeCode, x.Period, x.PeriodStart, x.Rank });
        modelBuilder.Entity<LeaderboardEntry>().HasIndex(x => new { x.UserId, x.Period });

        // Spaced repetition indexes
        modelBuilder.Entity<ReviewItem>().HasIndex(x => new { x.UserId, x.DueDate, x.Status });
        modelBuilder.Entity<ReviewItem>().HasIndex(x => new { x.UserId, x.ExamTypeCode, x.Status });

        // Vocabulary indexes
        modelBuilder.Entity<VocabularyTerm>().HasIndex(x => new { x.ExamTypeCode, x.Status, x.Category });
        modelBuilder.Entity<LearnerVocabulary>().HasIndex(x => new { x.UserId, x.NextReviewDate });
        modelBuilder.Entity<LearnerVocabulary>().HasIndex(x => new { x.UserId, x.TermId }).IsUnique();

        // Adaptive difficulty indexes
        modelBuilder.Entity<LearnerSkillProfile>().HasIndex(x => new { x.UserId, x.ExamTypeCode, x.SubtestCode });

        // Conversation indexes
        modelBuilder.Entity<ConversationSession>().HasIndex(x => new { x.UserId, x.State });
        modelBuilder.Entity<ConversationTurn>().HasIndex(x => new { x.SessionId, x.TurnNumber });

        // Writing coach indexes
        modelBuilder.Entity<WritingCoachSession>().HasIndex(x => x.AttemptId);
        modelBuilder.Entity<WritingCoachSuggestion>().HasIndex(x => new { x.AttemptId, x.Resolution });

        // Pronunciation indexes
        modelBuilder.Entity<PronunciationAssessment>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerPronunciationProgress>().HasIndex(x => new { x.UserId, x.PhonemeCode });

        // Prediction indexes
        modelBuilder.Entity<PredictionSnapshot>().HasIndex(x => new { x.UserId, x.ExamTypeCode, x.SubtestCode, x.ComputedAt });

        // Learning content indexes
        modelBuilder.Entity<GrammarLesson>().HasIndex(x => new { x.ExamTypeCode, x.Category, x.Status });
        modelBuilder.Entity<LearnerGrammarProgress>().HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
        modelBuilder.Entity<VideoLesson>().HasIndex(x => new { x.ExamTypeCode, x.Category, x.Status });
        modelBuilder.Entity<LearnerVideoProgress>().HasIndex(x => new { x.UserId, x.VideoLessonId }).IsUnique();
        modelBuilder.Entity<StrategyGuide>().HasIndex(x => new { x.ExamTypeCode, x.Category, x.Status });

        // Community indexes
        modelBuilder.Entity<ForumThread>().HasIndex(x => new { x.CategoryId, x.LastActivityAt });
        modelBuilder.Entity<ForumReply>().HasIndex(x => x.ThreadId);
        modelBuilder.Entity<StudyGroupMember>().HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
        modelBuilder.Entity<StudyGroupMember>().HasIndex(x => x.UserId);
        modelBuilder.Entity<PeerReviewRequest>().HasIndex(x => x.SubmitterUserId);
        modelBuilder.Entity<PeerReviewRequest>().HasIndex(x => new { x.Status, x.CreatedAt });
        modelBuilder.Entity<PeerReviewFeedback>().HasIndex(x => x.PeerReviewRequestId);

        // Tutoring indexes
        modelBuilder.Entity<TutoringSession>().HasIndex(x => new { x.LearnerUserId, x.ScheduledAt });
        modelBuilder.Entity<TutoringSession>().HasIndex(x => new { x.ExpertUserId, x.ScheduledAt });
        modelBuilder.Entity<TutoringAvailability>().HasIndex(x => x.ExpertUserId);

        // Private Speaking indexes
        modelBuilder.Entity<PrivateSpeakingTutorProfile>().HasIndex(x => x.ExpertUserId).IsUnique();
        modelBuilder.Entity<PrivateSpeakingAvailabilityRule>().HasIndex(x => new { x.TutorProfileId, x.DayOfWeek });
        modelBuilder.Entity<PrivateSpeakingAvailabilityOverride>().HasIndex(x => new { x.TutorProfileId, x.Date });
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => new { x.TutorProfileId, x.SessionStartUtc });
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => new { x.LearnerUserId, x.SessionStartUtc });
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.Status);
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.IdempotencyKey).IsUnique();
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.StripeCheckoutSessionId);
        modelBuilder.Entity<PrivateSpeakingAuditLog>().HasIndex(x => x.BookingId);
        modelBuilder.Entity<PrivateSpeakingAuditLog>().HasIndex(x => x.CreatedAt);

        // Social indexes
        modelBuilder.Entity<Certificate>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Certificate>().HasIndex(x => x.VerificationCode).IsUnique();
        modelBuilder.Entity<ReferralCode>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<ReferralCode>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Referral>().HasIndex(x => x.ReferrerUserId);
        modelBuilder.Entity<SponsorLearnerLink>().HasIndex(x => new { x.SponsorId, x.LearnerId }).IsUnique();
        modelBuilder.Entity<CohortMember>().HasIndex(x => new { x.CohortId, x.LearnerId }).IsUnique();

        // Marketplace indexes
        modelBuilder.Entity<ExamBooking>().HasIndex(x => new { x.UserId, x.ExamDate });
        modelBuilder.Entity<ContentSubmission>().HasIndex(x => new { x.ContributorId, x.Status });

        // Admin content generation indexes
        modelBuilder.Entity<ContentGenerationJob>().HasIndex(x => new { x.State, x.CreatedAt });
        modelBuilder.Entity<ContentGenerationJob>().HasIndex(x => x.RequestedBy);

        // Wallet transaction indexes
        modelBuilder.Entity<WalletTransaction>().HasIndex(x => new { x.WalletId, x.CreatedAt });

        // Payment indexes
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => new { x.LearnerUserId, x.CreatedAt });
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => x.GatewayTransactionId).IsUnique();
        modelBuilder.Entity<PaymentWebhookEvent>().HasIndex(x => x.GatewayEventId).IsUnique();
        modelBuilder.Entity<PaymentWebhookEvent>().HasIndex(x => new { x.ProcessingStatus, x.ReceivedAt });

        // ── Content hierarchy indexes ──
        modelBuilder.Entity<ContentProgram>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ContentItem>().HasIndex(x => x.InstructionLanguage);
        modelBuilder.Entity<ContentItem>().HasIndex(x => x.SourceProvenance);
        modelBuilder.Entity<ContentItem>().HasIndex(x => x.DuplicateGroupId);
        modelBuilder.Entity<ContentItem>().HasIndex(x => x.ImportBatchId);
        modelBuilder.Entity<ContentItem>().HasIndex(x => new { x.IsPreviewEligible, x.Status });
        modelBuilder.Entity<ContentImportBatch>().HasIndex(x => x.CreatedBy);

        // ── Phase 0: Foundation entities ──
        modelBuilder.Entity<AdminPermissionGrant>().HasIndex(x => new { x.AdminUserId, x.Permission }).IsUnique();
        modelBuilder.Entity<PermissionTemplate>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<ContentPublishRequest>().HasIndex(x => new { x.ContentItemId, x.Status });
        modelBuilder.Entity<ContentPublishRequest>().HasIndex(x => x.RequestedBy);
        modelBuilder.Entity<ContentPublishRequest>().HasIndex(x => x.Stage);
        modelBuilder.Entity<ReviewEscalation>().HasIndex(x => new { x.ReviewRequestId, x.Status });
        modelBuilder.Entity<ReviewEscalation>().HasIndex(x => x.SecondReviewerId);

        // ── Learner Escalations ──
        modelBuilder.Entity<LearnerEscalation>().HasIndex(x => new { x.UserId, x.Status });
        modelBuilder.Entity<LearnerEscalation>().HasIndex(x => x.SubmissionId);
    }
}
