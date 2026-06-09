using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext(DbContextOptions<LearnerDbContext> options) : DbContext(options)
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
    public DbSet<SubscriptionFreeze> SubscriptionFreezes => Set<SubscriptionFreeze>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<BackgroundJobItem> BackgroundJobs => Set<BackgroundJobItem>();
    public DbSet<DiagnosticSession> DiagnosticSessions => Set<DiagnosticSession>();
    public DbSet<DiagnosticSubtestStatus> DiagnosticSubtests => Set<DiagnosticSubtestStatus>();
    public DbSet<MockAttempt> MockAttempts => Set<MockAttempt>();
    public DbSet<MockReport> MockReports => Set<MockReport>();
    public DbSet<MockBundle> MockBundles => Set<MockBundle>();
    public DbSet<MockBundleSection> MockBundleSections => Set<MockBundleSection>();
    public DbSet<MockSectionAttempt> MockSectionAttempts => Set<MockSectionAttempt>();
    public DbSet<MockReviewReservation> MockReviewReservations => Set<MockReviewReservation>();
    public DbSet<MockBooking> MockBookings => Set<MockBooking>();
    public DbSet<MockLiveRoomTransition> MockLiveRoomTransitions => Set<MockLiveRoomTransition>();
    public DbSet<MockContentReview> MockContentReviews => Set<MockContentReview>();
    public DbSet<MockProctoringEvent> MockProctoringEvents => Set<MockProctoringEvent>();
    public DbSet<MockItemAnalysisSnapshot> MockItemAnalysisSnapshots => Set<MockItemAnalysisSnapshot>();
    public DbSet<MockEntitlementLedger> MockEntitlementLedgers => Set<MockEntitlementLedger>();
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();
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
    public DbSet<NotificationConsent> NotificationConsents => Set<NotificationConsent>();
    public DbSet<NotificationSuppression> NotificationSuppressions => Set<NotificationSuppression>();
    public DbSet<SubscriptionItem> SubscriptionItems => Set<SubscriptionItem>();
    public DbSet<BillingAddOn> BillingAddOns => Set<BillingAddOn>();
    public DbSet<TutorBookUpdate> TutorBookUpdates => Set<TutorBookUpdate>();
    public DbSet<TutorBookAudioScript> TutorBookAudioScripts => Set<TutorBookAudioScript>();
    public DbSet<BillingAddOnVersion> BillingAddOnVersions => Set<BillingAddOnVersion>();
    public DbSet<BillingContentString> BillingContentStrings => Set<BillingContentString>();
    public DbSet<BillingCoupon> BillingCoupons => Set<BillingCoupon>();
    public DbSet<BillingCouponVersion> BillingCouponVersions => Set<BillingCouponVersion>();
    public DbSet<BillingCouponRedemption> BillingCouponRedemptions => Set<BillingCouponRedemption>();
    public DbSet<BillingQuote> BillingQuotes => Set<BillingQuote>();
    public DbSet<BillingEvent> BillingEvents => Set<BillingEvent>();
    public DbSet<NativeIapProductMapping> NativeIapProductMappings => Set<NativeIapProductMapping>();
    public DbSet<AiPackageCreditAccount> AiPackageCreditAccounts => Set<AiPackageCreditAccount>();
    public DbSet<AiPackageCreditTransaction> AiPackageCreditTransactions => Set<AiPackageCreditTransaction>();
    public DbSet<LearnerExamOutcome> LearnerExamOutcomes => Set<LearnerExamOutcome>();

    // Multi-exam reference entities
    public DbSet<ExamType> ExamTypes => Set<ExamType>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<ExamFamily> ExamFamilies => Set<ExamFamily>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    // Slice B (billing-hardening) — refund + dispute lifecycle.
    public DbSet<OrderRefund> OrderRefunds => Set<OrderRefund>();
    public DbSet<PaymentDispute> PaymentDisputes => Set<PaymentDispute>();

    // Expert Console entities
    public DbSet<ExpertUser> ExpertUsers => Set<ExpertUser>();
    public DbSet<ExpertReviewAssignment> ExpertReviewAssignments => Set<ExpertReviewAssignment>();
    public DbSet<ExpertReviewDraft> ExpertReviewDrafts => Set<ExpertReviewDraft>();
    public DbSet<ExpertCalibrationCase> ExpertCalibrationCases => Set<ExpertCalibrationCase>();
    public DbSet<ExpertCalibrationResult> ExpertCalibrationResults => Set<ExpertCalibrationResult>();
    public DbSet<ExpertCalibrationNote> ExpertCalibrationNotes => Set<ExpertCalibrationNote>();
    public DbSet<ExpertAvailability> ExpertAvailabilities => Set<ExpertAvailability>();
    public DbSet<ExpertMetricSnapshot> ExpertMetricSnapshots => Set<ExpertMetricSnapshot>();
    public DbSet<ExpertOnboardingProgress> ExpertOnboardingProgresses => Set<ExpertOnboardingProgress>();

    // Per-user onboarding / product-tour progress (learner, expert, admin).
    public DbSet<LearnerOnboardingTour> LearnerOnboardingTours => Set<LearnerOnboardingTour>();

    // Rulebook authoring (admin-managed)
    public DbSet<RulebookVersion> RulebookVersions => Set<RulebookVersion>();
    public DbSet<RulebookSectionRow> RulebookSectionRows => Set<RulebookSectionRow>();
    public DbSet<RulebookRuleRow> RulebookRuleRows => Set<RulebookRuleRow>();

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
    public DbSet<ConversationEvaluation> ConversationEvaluations => Set<ConversationEvaluation>();
    public DbSet<ConversationTurnAnnotation> ConversationTurnAnnotations => Set<ConversationTurnAnnotation>();
    public DbSet<ConversationSessionResumeToken> ConversationSessionResumeTokens => Set<ConversationSessionResumeToken>();
    public DbSet<ConversationSettingsRow> ConversationSettings => Set<ConversationSettingsRow>();
    public DbSet<RuntimeSettingsRow> RuntimeSettings => Set<RuntimeSettingsRow>();
    public DbSet<WritingCoachSession> WritingCoachSessions => Set<WritingCoachSession>();
    public DbSet<WritingCoachSuggestion> WritingCoachSuggestions => Set<WritingCoachSuggestion>();
    public DbSet<WritingRuleViolation> WritingRuleViolations => Set<WritingRuleViolation>();
    public DbSet<PronunciationAssessment> PronunciationAssessments => Set<PronunciationAssessment>();
    public DbSet<PronunciationAttempt> PronunciationAttempts => Set<PronunciationAttempt>();
    public DbSet<PronunciationDrill> PronunciationDrills => Set<PronunciationDrill>();
    public DbSet<LearnerPronunciationProgress> LearnerPronunciationProgress => Set<LearnerPronunciationProgress>();
    public DbSet<LearnerPronunciationDiscriminationAttempt> LearnerPronunciationDiscriminationAttempts => Set<LearnerPronunciationDiscriminationAttempt>();
    public DbSet<PredictionSnapshot> PredictionSnapshots => Set<PredictionSnapshot>();

    // Learning content entities
    public DbSet<GrammarLesson> GrammarLessons => Set<GrammarLesson>();
    public DbSet<LearnerGrammarProgress> LearnerGrammarProgress => Set<LearnerGrammarProgress>();
    public DbSet<VideoLesson> VideoLessons => Set<VideoLesson>();
    public DbSet<LearnerVideoProgress> LearnerVideoProgress => Set<LearnerVideoProgress>();
    public DbSet<StrategyGuide> StrategyGuides => Set<StrategyGuide>();
    public DbSet<LearnerStrategyProgress> LearnerStrategyProgress => Set<LearnerStrategyProgress>();

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
    public DbSet<PrivateSpeakingTutorCalendarConnection> PrivateSpeakingTutorCalendarConnections => Set<PrivateSpeakingTutorCalendarConnection>();
    public DbSet<PrivateSpeakingAvailabilityRule> PrivateSpeakingAvailabilityRules => Set<PrivateSpeakingAvailabilityRule>();
    public DbSet<PrivateSpeakingAvailabilityOverride> PrivateSpeakingAvailabilityOverrides => Set<PrivateSpeakingAvailabilityOverride>();
    public DbSet<PrivateSpeakingBooking> PrivateSpeakingBookings => Set<PrivateSpeakingBooking>();
    public DbSet<PrivateSpeakingAuditLog> PrivateSpeakingAuditLogs => Set<PrivateSpeakingAuditLog>();

    // Billing V2 — Stripe-synced product catalog, cart, checkout, and subscription management.
    public DbSet<BillingProduct> BillingProducts => Set<BillingProduct>();
    public DbSet<BillingPrice> BillingPrices => Set<BillingPrice>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<AppliedPromoCode> AppliedPromoCodes => Set<AppliedPromoCode>();
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();
    public DbSet<CustomerSubscription> CustomerSubscriptions => Set<CustomerSubscription>();
    public DbSet<CrossSellRule> CrossSellRules => Set<CrossSellRule>();

    // Zoom-backed live classes
    public DbSet<LiveClass> LiveClasses => Set<LiveClass>();
    public DbSet<LiveClassSession> LiveClassSessions => Set<LiveClassSession>();
    public DbSet<LiveClassEnrollment> LiveClassEnrollments => Set<LiveClassEnrollment>();
    public DbSet<LiveClassAttendance> LiveClassAttendances => Set<LiveClassAttendance>();
    public DbSet<LiveClassRecording> LiveClassRecordings => Set<LiveClassRecording>();
    public DbSet<LiveClassWaitlistEntry> LiveClassWaitlistEntries => Set<LiveClassWaitlistEntry>();
    public DbSet<LiveClassWebhookEvent> LiveClassWebhookEvents => Set<LiveClassWebhookEvent>();
    public DbSet<ClassRecordingEmbedding> ClassRecordingEmbeddings => Set<ClassRecordingEmbedding>();

    // Wave A1 — Zoom tutor stack: principal tutor record, weekly availability
    // schedule, per-class materials, and learner feedback rows.
    public DbSet<OetLearner.Api.Domain.Classes.Tutor> Tutors => Set<OetLearner.Api.Domain.Classes.Tutor>();
    public DbSet<OetLearner.Api.Domain.Classes.TutorAvailability> TutorAvailabilities => Set<OetLearner.Api.Domain.Classes.TutorAvailability>();
    public DbSet<OetLearner.Api.Domain.Classes.ClassMaterial> ClassMaterials => Set<OetLearner.Api.Domain.Classes.ClassMaterial>();
    public DbSet<OetLearner.Api.Domain.Classes.ClassFeedback> ClassFeedbacks => Set<OetLearner.Api.Domain.Classes.ClassFeedback>();

    // Social / achievement entities
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<SponsorAccount> SponsorAccounts => Set<SponsorAccount>();
    public DbSet<SponsorLearnerLink> SponsorLearnerLinks => Set<SponsorLearnerLink>();
    public DbSet<Cohort> Cohorts => Set<Cohort>();
    public DbSet<CohortMember> CohortMembers => Set<CohortMember>();
    public DbSet<Sponsorship> Sponsorships => Set<Sponsorship>();
    public DbSet<SponsorSeatPack> SponsorSeatPacks => Set<SponsorSeatPack>();
    public DbSet<SponsorSeatAssignment> SponsorSeatAssignments => Set<SponsorSeatAssignment>();
    public DbSet<SponsorBillingEvent> SponsorBillingEvents => Set<SponsorBillingEvent>();

    // Marketplace / booking entities
    public DbSet<ExamBooking> ExamBookings => Set<ExamBooking>();
    public DbSet<ContentContributor> ContentContributors => Set<ContentContributor>();
    public DbSet<ContentSubmission> ContentSubmissions => Set<ContentSubmission>();

    // Admin / CMS entities
    public DbSet<ContentGenerationJob> ContentGenerationJobs => Set<ContentGenerationJob>();
    public DbSet<ContentRevision> ContentRevisions => Set<ContentRevision>();
    public DbSet<AIConfigVersion> AIConfigVersions => Set<AIConfigVersion>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<LaunchReadinessSettings> LaunchReadinessSettings => Set<LaunchReadinessSettings>();
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

    // Multi-account pool (GitHub Copilot integration Phase 2). One row per
    // PAT / quota slot under a provider. Pick + increment is atomic; see
    // AiProviderAccountRegistry.PickAndReserveAsync.
    public DbSet<AiProviderAccount> AiProviderAccounts => Set<AiProviderAccount>();

    // Per-feature routing overrides (Phase 7). One row per FeatureCode that
    // pins a provider; consulted by AiFeatureRouteResolver before the
    // global failover-priority default.
    public DbSet<AiFeatureRoute> AiFeatureRoutes => Set<AiFeatureRoute>();

    // Credit ledger (Slice 6). Append-only; balance = SUM(TokensDelta).
    public DbSet<AiCreditLedgerEntry> AiCreditLedger => Set<AiCreditLedgerEntry>();

    // Tool calling (Phase 5). Deny-by-default catalog + per-feature opt-in
    // grants + per-call audit. See docs/AI-COPILOT-TOOLS-PRD.md.
    public DbSet<AiTool> AiTools => Set<AiTool>();
    public DbSet<AiFeatureToolGrant> AiFeatureToolGrants => Set<AiFeatureToolGrant>();
    public DbSet<AiToolInvocation> AiToolInvocations => Set<AiToolInvocation>();

    // AI Assistant (Phase A–H). Conversational multi-role chat.
    public DbSet<AiAssistantThread> AiAssistantThreads => Set<AiAssistantThread>();
    public DbSet<AiAssistantMessage> AiAssistantMessages => Set<AiAssistantMessage>();
    public DbSet<AiFileBackup> AiFileBackups => Set<AiFileBackup>();
    public DbSet<AiCodebaseChunk> AiCodebaseChunks => Set<AiCodebaseChunk>();

    // User-scoped items written by the two write-category tools (Phase 5).
    public DbSet<UserNote> UserNotes => Set<UserNote>();
    public DbSet<RecallBookmark> RecallBookmarks => Set<RecallBookmark>();

    // Content Paper subsystem (Content Upload, Slice 1). Curatorial papers
    // that bundle typed assets pointing at MediaAsset rows.
    public DbSet<ContentPaper> ContentPapers => Set<ContentPaper>();
    public DbSet<ContentPaperAsset> ContentPaperAssets => Set<ContentPaperAsset>();

    // Content Upload chunked session (Slice 2).
    public DbSet<AdminUploadSession> AdminUploadSessions => Set<AdminUploadSession>();

    // Reading Authoring subsystem (Reading Slice R1).
    public DbSet<ReadingPart> ReadingParts => Set<ReadingPart>();
    public DbSet<ReadingSection> ReadingSections => Set<ReadingSection>();
    public DbSet<ReadingText> ReadingTexts => Set<ReadingText>();
    public DbSet<ReadingQuestion> ReadingQuestions => Set<ReadingQuestion>();
    public DbSet<ReadingAttempt> ReadingAttempts => Set<ReadingAttempt>();
    public DbSet<ReadingAnswer> ReadingAnswers => Set<ReadingAnswer>();
    public DbSet<ReadingPaperAnnotation> ReadingPaperAnnotations => Set<ReadingPaperAnnotation>();
    public DbSet<ReadingPolicy> ReadingPolicies => Set<ReadingPolicy>();
    public DbSet<ReadingUserPolicyOverride> ReadingUserPolicyOverrides => Set<ReadingUserPolicyOverride>();
    public DbSet<ReadingErrorBankEntry> ReadingErrorBankEntries => Set<ReadingErrorBankEntry>();
    public DbSet<ReadingQuestionReviewLog> ReadingQuestionReviewLogs => Set<ReadingQuestionReviewLog>();
    public DbSet<ReadingExtractionDraft> ReadingExtractionDrafts => Set<ReadingExtractionDraft>();

    // Reading Wave 1 — review feedback, changed-answer history, assignments.
    public DbSet<ReadingAttemptFeedback> ReadingAttemptFeedbacks => Set<ReadingAttemptFeedback>();
    public DbSet<ReadingAnswerRevision> ReadingAnswerRevisions => Set<ReadingAnswerRevision>();
    public DbSet<ReadingAssignment> ReadingAssignments => Set<ReadingAssignment>();

    // Reading Pathway subsystem (Reading Module Pathway Plan).
    // New 5-stage learning pathway: onboarding, diagnostic, foundation, practice, mastery.
    public DbSet<LearnerReadingProfile> LearnerReadingProfiles => Set<LearnerReadingProfile>();
    public DbSet<LearnerReadingPathway> LearnerReadingPathways => Set<LearnerReadingPathway>();
    public DbSet<LearnerSkillScore> LearnerSkillScores => Set<LearnerSkillScore>();
    public DbSet<ReadingDailyPlanItem> ReadingDailyPlanItems => Set<ReadingDailyPlanItem>();
    public DbSet<ReadingLesson> ReadingLessons => Set<ReadingLesson>();
    public DbSet<LearnerLessonProgress> LearnerLessonProgresses => Set<LearnerLessonProgress>();
    public DbSet<ReadingStrategy> ReadingStrategies => Set<ReadingStrategy>();
    public DbSet<ReadingStrategyProgress> ReadingStrategyProgresses => Set<ReadingStrategyProgress>();
    public DbSet<VocabularyWord> VocabularyWords => Set<VocabularyWord>();
    public DbSet<LearnerVocabularyItem> LearnerVocabularyItems => Set<LearnerVocabularyItem>();
    public DbSet<VocabularyList> VocabularyLists => Set<VocabularyList>();
    public DbSet<ReadingPracticeSession> ReadingPracticeSessions => Set<ReadingPracticeSession>();
    public DbSet<ReadingQuestionAttempt> ReadingQuestionAttempts => Set<ReadingQuestionAttempt>();
    public DbSet<ReadingMockTemplate> ReadingMockTemplates => Set<ReadingMockTemplate>();
    public DbSet<StreakRecord> StreakRecords => Set<StreakRecord>();
    public DbSet<LearnerXp> LearnerXps => Set<LearnerXp>();
    public DbSet<LearnerBadge> LearnerBadges => Set<LearnerBadge>();
    public DbSet<ReadingQuestionDiscussionComment> ReadingQuestionDiscussionComments => Set<ReadingQuestionDiscussionComment>();

    // Writing Pathway subsystem — additive orchestration over existing Writing attempts/evaluations.
    public DbSet<LearnerWritingProfile> LearnerWritingProfiles => Set<LearnerWritingProfile>();
    public DbSet<LearnerWritingPathway> LearnerWritingPathways => Set<LearnerWritingPathway>();
    public DbSet<WritingDailyPlanItem> WritingDailyPlanItems => Set<WritingDailyPlanItem>();
    public DbSet<WritingLesson> WritingLessons => Set<WritingLesson>();
    public DbSet<LearnerWritingLessonProgress> LearnerWritingLessonProgresses => Set<LearnerWritingLessonProgress>();

    // Listening Module subsystem (Phase 2 of LISTENING-MODULE-PLAN.md).
    // Additive to the existing JSON-backed ContentPaper.ExtractedTextJson
    // runtime — these tables are populated by the next-generation authoring
    // path while learner grading continues to read the JSON blob until the
    // backfill ships.
    public DbSet<ListeningPart> ListeningParts => Set<ListeningPart>();
    public DbSet<ListeningExtract> ListeningExtracts => Set<ListeningExtract>();
    public DbSet<ListeningQuestion> ListeningQuestions => Set<ListeningQuestion>();
    public DbSet<ListeningQuestionOption> ListeningQuestionOptions => Set<ListeningQuestionOption>();
    public DbSet<ListeningAttempt> ListeningAttempts => Set<ListeningAttempt>();
    public DbSet<ListeningAnswer> ListeningAnswers => Set<ListeningAnswer>();
    public DbSet<ListeningPolicy> ListeningPolicies => Set<ListeningPolicy>();
    public DbSet<ListeningUserPolicyOverride> ListeningUserPolicyOverrides => Set<ListeningUserPolicyOverride>();
    public DbSet<ListeningExtractionDraft> ListeningExtractionDrafts => Set<ListeningExtractionDraft>();
    // Listening V2 — pathway + cross-skill teacher classes + per-attempt notes.
    public DbSet<ListeningPathwayProgress> ListeningPathwayProgress => Set<ListeningPathwayProgress>();
    public DbSet<TeacherClass> TeacherClasses => Set<TeacherClass>();
    public DbSet<TeacherClassMember> TeacherClassMembers => Set<TeacherClassMember>();
    public DbSet<ListeningAttemptNote> ListeningAttemptNotes => Set<ListeningAttemptNote>();
    public DbSet<ListeningExpertFeedback> ListeningExpertFeedbacks => Set<ListeningExpertFeedback>();
    public DbSet<ListeningTtsJob> ListeningTtsJobs => Set<ListeningTtsJob>();

    // Listening Pathway subsystem (Listening Module Pathway Plan Phase 1).
    // 5-stage learning pathway parallel to ReadingPathway — onboarding, audio_check,
    // diagnostic, foundation, practice, mastery. Coexists with the existing V2
    // attempt-level path (ListeningAttempt/ListeningPathwayProgress) which serves
    // a different layer.
    public DbSet<LearnerListeningProfile> LearnerListeningProfiles => Set<LearnerListeningProfile>();
    public DbSet<LearnerListeningPathway> LearnerListeningPathways => Set<LearnerListeningPathway>();
    public DbSet<LearnerListeningSkillScore> LearnerListeningSkillScores => Set<LearnerListeningSkillScore>();
    public DbSet<LearnerAccentProgress> LearnerAccentProgresses => Set<LearnerAccentProgress>();
    public DbSet<ListeningPracticeSession> ListeningPracticeSessions => Set<ListeningPracticeSession>();
    public DbSet<ListeningQuestionAttempt> ListeningQuestionAttempts => Set<ListeningQuestionAttempt>();
    public DbSet<ListeningPracticeNote> ListeningPracticeNotes => Set<ListeningPracticeNote>();

    // Listening Pathway subsystem (Phase 2–5).
    public DbSet<ListeningLesson> ListeningLessons => Set<ListeningLesson>();
    public DbSet<LearnerListeningLessonProgress> LearnerListeningLessonProgresses => Set<LearnerListeningLessonProgress>();
    public DbSet<ListeningDailyPlanItem> ListeningDailyPlanItems => Set<ListeningDailyPlanItem>();
    public DbSet<ListeningStrategy> ListeningStrategies => Set<ListeningStrategy>();
    public DbSet<LearnerListeningStrategyProgress> LearnerListeningStrategyProgresses => Set<LearnerListeningStrategyProgress>();
    public DbSet<PronunciationCard> PronunciationCards => Set<PronunciationCard>();
    public DbSet<LearnerPronunciationCard> LearnerPronunciationCards => Set<LearnerPronunciationCard>();
    public DbSet<DictationDrill> DictationDrills => Set<DictationDrill>();
    public DbSet<LearnerDictationProgress> LearnerDictationProgresses => Set<LearnerDictationProgress>();
    public DbSet<ListeningMockTemplate> ListeningMockTemplates => Set<ListeningMockTemplate>();

    public DbSet<BillingPlan> BillingPlans => Set<BillingPlan>();
    public DbSet<BillingPlanVersion> BillingPlanVersions => Set<BillingPlanVersion>();
    public DbSet<WalletTopUpTierConfig> WalletTopUpTierConfigs => Set<WalletTopUpTierConfig>();
    public DbSet<AdminPermissionGrant> AdminPermissionGrants => Set<AdminPermissionGrant>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<PermissionTemplate> PermissionTemplates => Set<PermissionTemplate>();
    public DbSet<ContentPublishRequest> ContentPublishRequests => Set<ContentPublishRequest>();
    public DbSet<ReviewEscalation> ReviewEscalations => Set<ReviewEscalation>();
    public DbSet<LearnerEscalation> LearnerEscalations => Set<LearnerEscalation>();
    public DbSet<ScoreGuaranteePledge> ScoreGuaranteePledges => Set<ScoreGuaranteePledge>();
    public DbSet<ReferralRecord> ReferralRecords => Set<ReferralRecord>();
    public DbSet<ExpertAnnotationTemplate> ExpertAnnotationTemplates => Set<ExpertAnnotationTemplate>();
    public DbSet<ExpertReviewerPayout> ExpertReviewerPayouts => Set<ExpertReviewerPayout>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<ExpertReviewAmend> ExpertReviewAmends => Set<ExpertReviewAmend>();
    public DbSet<ExpertMessageThread> ExpertMessageThreads => Set<ExpertMessageThread>();
    public DbSet<ExpertMessageReply> ExpertMessageReplies => Set<ExpertMessageReply>();
    public DbSet<ExpertCompensationRate> ExpertCompensationRates => Set<ExpertCompensationRate>();
    public DbSet<ExpertEarning> ExpertEarnings => Set<ExpertEarning>();
    public DbSet<ExpertPayout> ExpertPayouts => Set<ExpertPayout>();
    public DbSet<ExpertSlaSnapshot> ExpertSlaSnapshots => Set<ExpertSlaSnapshot>();
    public DbSet<StudyCommitment> StudyCommitments => Set<StudyCommitment>();
    public DbSet<LearnerCertificate> LearnerCertificates => Set<LearnerCertificate>();

    // Voice Design — bulk regeneration tracking
    public DbSet<AudioRegenerationBatch> AudioRegenerationBatches => Set<AudioRegenerationBatch>();

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
    public DbSet<NotificationCampaign> NotificationCampaigns => Set<NotificationCampaign>();
    public DbSet<NotificationCampaignRecipient> NotificationCampaignRecipients => Set<NotificationCampaignRecipient>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<ConversationTemplate> ConversationTemplates => Set<ConversationTemplate>();
    public DbSet<FreeTierConfig> FreeTierConfigs => Set<FreeTierConfig>();

    // Wave 3 of docs/SPEAKING-MODULE-PLAN.md - Speaking mock-set composition
    // (two role-plays as one mock) and per-learner mock-session tracking
    // (free-tier cap enforcement + combined readiness band snapshot).
    public DbSet<SpeakingMockSet> SpeakingMockSets => Set<SpeakingMockSet>();
    public DbSet<SpeakingMockSession> SpeakingMockSessions => Set<SpeakingMockSession>();

    // Wave 4 of docs/SPEAKING-MODULE-PLAN.md - tutor calibration drift +
    // inline transcript comments. Calibration samples store gold rubric
    // scores; calibration scores capture each tutor's submitted rubric
    // for the same sample. Feedback comments are line-keyed so the
    // learner can see them inline beneath the transcript.
    public DbSet<SpeakingCalibrationSample> SpeakingCalibrationSamples => Set<SpeakingCalibrationSample>();
    public DbSet<SpeakingCalibrationScore> SpeakingCalibrationScores => Set<SpeakingCalibrationScore>();
    public DbSet<SpeakingFeedbackComment> SpeakingFeedbackComments => Set<SpeakingFeedbackComment>();

    // Writing module runtime-mutable settings (singleton row, id="global").
    // See WritingOptionsProvider; bootstrapped lazily on first read.
    public DbSet<WritingOptions> WritingOptions => Set<WritingOptions>();
    public DbSet<WritingAttemptAsset> WritingAttemptAssets => Set<WritingAttemptAsset>();
    public DbSet<ReviewVoiceNote> ReviewVoiceNotes => Set<ReviewVoiceNote>();
    public DbSet<SpeakingReviewVoiceNote> SpeakingReviewVoiceNotes => Set<SpeakingReviewVoiceNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContentItem>().HasIndex(x => new { x.SubtestCode, x.Status });
        modelBuilder.Entity<Attempt>().HasIndex(x => new { x.UserId, x.SubtestCode, x.State });
        modelBuilder.Entity<Attempt>().HasIndex(x => x.ContentId);
        modelBuilder.Entity<Evaluation>().HasIndex(x => new { x.AttemptId, x.State });
        // Worker polls for pending evaluations without filtering by attempt.
        modelBuilder.Entity<Evaluation>().HasIndex(x => new { x.State, x.LastTransitionAt });
        modelBuilder.Entity<ReviewRequest>().HasIndex(x => new { x.AttemptId, x.State });
        modelBuilder.Entity<ReviewRequest>().HasIndex(x => new { x.State, x.CreatedAt });
        modelBuilder.Entity<AccountFreezePolicy>().HasIndex(x => x.Version);
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => new { x.UserId, x.Status });
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsCurrent\" = TRUE");
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => new { x.Status, x.ScheduledStartAt });
        modelBuilder.Entity<AccountFreezeRecord>().HasIndex(x => new { x.Status, x.EndedAt });
        modelBuilder.Entity<AccountFreezeEntitlement>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<SubscriptionFreeze>().HasIndex(x => x.SubscriptionId);
        modelBuilder.Entity<SubscriptionFreeze>().HasIndex(x => x.UserId);
        modelBuilder.Entity<SubscriptionFreeze>()
            .HasIndex(x => x.SubscriptionId)
            .IsUnique()
            .HasFilter("\"RequestStatus\" = 'pending'");
        modelBuilder.Entity<StudyPlanItem>().HasIndex(x => new { x.StudyPlanId, x.Section, x.Status });
        modelBuilder.Entity<BackgroundJobItem>().HasIndex(x => new { x.State, x.AvailableAt });
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.UserId, x.IssuedAt });
        modelBuilder.Entity<Invoice>()
            .HasIndex(x => new { x.UserId, x.Number })
            .IsUnique()
            .HasFilter("\"Number\" IS NOT NULL");
        modelBuilder.Entity<AnalyticsEventRecord>().HasIndex(x => new { x.UserId, x.EventName, x.OccurredAt });
        // Platform-wide funnels (no user filter) need leading EventName.
        modelBuilder.Entity<AnalyticsEventRecord>().HasIndex(x => new { x.EventName, x.OccurredAt });
        modelBuilder.Entity<IdempotencyRecord>().HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        // NOTE: dropped redundant HasIndex(NormalizedEmail, Role) — UNIQUE(NormalizedEmail) from
        // the entity [Index] attribute already satisfies any filter on NormalizedEmail (max 1 row).
        modelBuilder.Entity<ApplicationUserAccount>().HasIndex(x => x.DeletedAt);
        modelBuilder.Entity<ApplicationUserAccount>().HasIndex(x => x.LastLoginAt);
        modelBuilder.Entity<RefreshTokenRecord>().HasIndex(x => new { x.ApplicationUserAccountId, x.ExpiresAt });
        // Active sessions / revocation sweeps use a partial index to skip revoked tokens.
        modelBuilder.Entity<RefreshTokenRecord>()
            .HasIndex(x => x.ApplicationUserAccountId)
            .HasFilter("\"RevokedAt\" IS NULL")
            .HasDatabaseName("IX_RefreshTokenRecord_Active");
        modelBuilder.Entity<EmailOtpChallenge>().HasIndex(x => new { x.ApplicationUserAccountId, x.Purpose, x.ExpiresAt });
        modelBuilder.Entity<EmailOtpChallenge>().HasIndex(x => x.ExpiresAt);
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
        modelBuilder.Entity<NotificationSuppression>().Property(x => x.EventKey).HasMaxLength(128);
        modelBuilder.Entity<NotificationTemplate>().Property(x => x.MetadataJson).HasDefaultValue("{}");

        // Rulebook authoring (admin-managed)
        modelBuilder.Entity<RulebookVersion>().HasIndex(x => new { x.Kind, x.Profession, x.Status });
        modelBuilder.Entity<RulebookVersion>().HasIndex(x => x.ReferencePdfAssetId);
        modelBuilder.Entity<RulebookVersion>().Property(x => x.Status).HasMaxLength(16);
        modelBuilder.Entity<RulebookVersion>().Property(x => x.Kind).HasMaxLength(32);
        modelBuilder.Entity<RulebookVersion>().Property(x => x.Profession).HasMaxLength(32);
        modelBuilder.Entity<RulebookVersion>().Property(x => x.ReferencePdfAssetId).HasMaxLength(64);
        modelBuilder.Entity<RulebookVersion>()
            .HasOne(x => x.ReferencePdfAsset)
            .WithMany()
            .HasForeignKey(x => x.ReferencePdfAssetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RulebookSectionRow>().HasIndex(x => new { x.RulebookVersionId, x.Code }).IsUnique();
        modelBuilder.Entity<RulebookRuleRow>().HasIndex(x => new { x.RulebookVersionId, x.Code }).IsUnique();
        modelBuilder.Entity<RulebookRuleRow>().HasIndex(x => new { x.RulebookVersionId, x.SectionCode });

        // Learner lookup indexes (frequently queried by UserId)
        modelBuilder.Entity<LearnerGoal>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerSettings>().HasIndex(x => x.UserId);
        modelBuilder.Entity<Subscription>().HasIndex(x => x.UserId);
        modelBuilder.Entity<LearnerUser>().HasIndex(x => x.Email);
        modelBuilder.Entity<LearnerUser>().HasIndex(x => x.LastActiveAt);
        modelBuilder.Entity<LearnerUser>().HasIndex(x => x.AccountStatus);
        modelBuilder.Entity<LearnerUser>().Property(x => x.AccountStatus).IsConcurrencyToken();
        modelBuilder.Entity<ExpertUser>().Property(x => x.IsActive).IsConcurrencyToken();
        modelBuilder.Entity<Wallet>().Property(x => x.LastUpdatedAt).IsConcurrencyToken();
        // Slice A (May 2026 billing hardening) — cross-DB rowversion token so
        // SQLite/in-memory tests get the same optimistic-concurrency
        // semantics as production Postgres (which already gets xmin via the
        // ConfigureXminToken path further down). Nullable; tolerates legacy
        // NULLs from the additive 20260512100000_AddWalletRowVersion
        // migration. ValueGeneratedOnAddOrUpdate so EF assigns a fresh
        // shadow value on every write.
        modelBuilder.Entity<Wallet>()
            .Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();

        // Optimistic concurrency via Postgres system column xmin on high-risk
        // billing + evaluation entities. SQLite/in-memory test providers do
        // not have that system column, so keep the mapping Npgsql-only.
        if (Database.IsNpgsql())
        {
            ConfigureXminToken<Subscription>(modelBuilder);
            ConfigureXminToken<Invoice>(modelBuilder);
            ConfigureXminToken<SubscriptionItem>(modelBuilder);
            ConfigureXminToken<Evaluation>(modelBuilder);
            // Gap W1: optimistic concurrency on AI-extraction drafts so two
            // admins racing to Approve/Reject the same Pending draft cannot
            // both succeed. xmin is a Postgres system column → no DDL.
            ConfigureXminToken<ListeningExtractionDraft>(modelBuilder);

            // pgvector — used by WritingExemplarEmbedding /
            // WritingScenarioEmbedding for HNSW cosine nearest-neighbour
            // queries. The CREATE EXTENSION DDL is emitted by migration
            // 20260612120000_AddPgvectorEmbeddingColumns; declaring it here
            // keeps the model snapshot honest and survives `EnsureCreated`
            // paths that bypass migrations.
            modelBuilder.HasPostgresExtension("vector");
        }

        // Hot JSON columns stored as jsonb for smaller on-disk size, native
        // validation, and future GIN-index capability. Paired with migration
        // 20260424170000_ConvertHotJsonColumnsToJsonb. SQLite/in-memory
        // providers used by tests do not support jsonb, so scope to Npgsql.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<AnalyticsEventRecord>().Property(x => x.PayloadJson).HasColumnType("jsonb");
            modelBuilder.Entity<Attempt>().Property(x => x.AnalysisJson).HasColumnType("jsonb");
            modelBuilder.Entity<Evaluation>().Property(x => x.StrengthsJson).HasColumnType("jsonb");
            modelBuilder.Entity<Evaluation>().Property(x => x.IssuesJson).HasColumnType("jsonb");
            modelBuilder.Entity<Evaluation>().Property(x => x.CriterionScoresJson).HasColumnType("jsonb");
            modelBuilder.Entity<Evaluation>().Property(x => x.FeedbackItemsJson).HasColumnType("jsonb");
            modelBuilder.Entity<PaymentWebhookEvent>().Property(x => x.PayloadJson).HasColumnType("jsonb");
            modelBuilder.Entity<NotificationTemplate>().Property(x => x.MetadataJson).HasColumnType("jsonb");

            // Listening V2 — additive jsonb columns on ListeningAttempt
            // (NavigationStateJson, AudioCueTimelineJson, TechReadinessJson,
            // AnnotationsJson, HumanScoreOverridesJson, LastQuestionVersionMapJson).
            // Per repo memory ef-core-sqlite-translation.md: NEVER LINQ-into
            // any of these columns. Reads must materialise the row first then
            // parse client-side. SQLite test harness keeps them as TEXT.
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.RowVersion).IsConcurrencyToken();
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.NavigationStateJson).HasColumnType("jsonb");
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.AudioCueTimelineJson).HasColumnType("jsonb");
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.TechReadinessJson).HasColumnType("jsonb");
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.AnnotationsJson).HasColumnType("jsonb");
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.HumanScoreOverridesJson).HasColumnType("jsonb");
            modelBuilder.Entity<ListeningAttempt>().Property(x => x.LastQuestionVersionMapJson).HasColumnType("jsonb");

            // Reading — R08 parity: rule-out / highlight annotations stored as
            // jsonb (same hot-JSON convention as the ListeningAttempt columns
            // above). Read the whole row then parse client-side; never LINQ
            // into the JSON. RowVersion stays a [ConcurrencyCheck] annotation;
            // the annotations autosave path uses a targeted ExecuteUpdateAsync
            // that deliberately does not touch it (see ReadingAttemptService).
            modelBuilder.Entity<ReadingAttempt>().Property(x => x.AnnotationsJson).HasColumnType("jsonb");

            // Gap N5: AuditEvent.Details routinely carries before/after JSON
            // snapshots that exceed the legacy varchar(1024) cap. Promote the
            // column to TEXT so full payloads survive (paired migration:
            // WidenAuditEventDetailsToText).
            modelBuilder.Entity<AuditEvent>().Property(x => x.Details).HasColumnType("text");

            // Composite primary keys on the range-partitioned append-only
            // tables. Postgres requires every UNIQUE/PK on a partitioned
            // table to include the partition key, so the PK is widened from
            // (Id) to (partitionCol, Id). Id alone remains effectively unique
            // (ULID), and existing equality-on-Id queries keep working — at
            // worst they scan across partitions for the handful of audit/lookup
            // paths that do so. Scoped to Npgsql because the SQLite test
            // harness keeps these as plain heap tables with a single-col PK.
            //
            // Paired with migration 20260424190000_ConvertAppendOnlyTablesToPartitioned.
            // If the migration's opt-in GUC (oet.enable_partition_conversion)
            // is left false, the DB retains its single-col PK — the composite
            // HasKey declaration below is still harmless because inserts and
            // equality lookups on Id continue to work unchanged.
            modelBuilder.Entity<AnalyticsEventRecord>().HasKey(x => new { x.OccurredAt, x.Id });
            modelBuilder.Entity<AuditEvent>().HasKey(x => new { x.OccurredAt, x.Id });
            modelBuilder.Entity<AiUsageRecord>().HasKey(x => new { x.CreatedAt, x.Id });
        }
        modelBuilder.Entity<Wallet>().HasIndex(x => x.UserId);
        modelBuilder.Entity<ReviewRequest>().Property(x => x.State).IsConcurrencyToken();
        modelBuilder.Entity<StudyPlan>().HasIndex(x => x.UserId);
        modelBuilder.Entity<ReadinessSnapshot>().HasIndex(x => x.UserId);
        modelBuilder.Entity<DiagnosticSession>().HasIndex(x => new { x.UserId, x.State });
        // Dropped solo HasIndex(UserId) — strict prefix of the composite (UserId, State) below.
        modelBuilder.Entity<MockAttempt>().HasIndex(x => new { x.UserId, x.State });
        modelBuilder.Entity<MockAttempt>()
            .HasOne(x => x.MockBundle)
            .WithMany()
            .HasForeignKey(x => x.MockBundleId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MockBundleSection>()
            .HasOne(x => x.MockBundle)
            .WithMany(x => x.Sections)
            .HasForeignKey(x => x.MockBundleId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MockBundleSection>()
            .HasOne(x => x.ContentPaper)
            .WithMany()
            .HasForeignKey(x => x.ContentPaperId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MockSectionAttempt>()
            .HasOne(x => x.MockAttempt)
            .WithMany(x => x.SectionAttempts)
            .HasForeignKey(x => x.MockAttemptId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MockSectionAttempt>()
            .HasOne(x => x.MockBundleSection)
            .WithMany()
            .HasForeignKey(x => x.MockBundleSectionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MockReviewReservation>()
            .HasOne(x => x.MockAttempt)
            .WithOne()
            .HasForeignKey<MockReviewReservation>(x => x.MockAttemptId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MockBooking>()
            .HasOne(x => x.MockBundle)
            .WithMany()
            .HasForeignKey(x => x.MockBundleId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MockBooking>()
            .HasOne(x => x.MockAttempt)
            .WithMany()
            .HasForeignKey(x => x.MockAttemptId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MockLiveRoomTransition>()
            .HasOne(x => x.Booking)
            .WithMany(x => x.LiveRoomTransitions)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MockLiveRoomTransition>()
            .HasIndex(x => new { x.BookingId, x.ClientTransitionId })
            .IsUnique()
            .HasFilter("\"ClientTransitionId\" IS NOT NULL");
        modelBuilder.Entity<MockContentReview>()
            .HasOne(x => x.MockBundle)
            .WithMany()
            .HasForeignKey(x => x.MockBundleId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MockContentReview>()
            .HasOne(x => x.MockAttempt)
            .WithMany()
            .HasForeignKey(x => x.MockAttemptId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<UploadSession>().HasIndex(x => x.AttemptId);

        // Expert indexes
        modelBuilder.Entity<ExpertReviewAssignment>().HasIndex(x => new { x.ReviewRequestId, x.ClaimState });
        modelBuilder.Entity<ExpertReviewAssignment>().HasIndex(x => x.AssignedReviewerId);
        modelBuilder.Entity<ExpertReviewDraft>().HasIndex(x => new { x.ReviewRequestId, x.ReviewerId });
        modelBuilder.Entity<WritingAttemptAsset>()
            .HasOne(x => x.Attempt)
            .WithMany()
            .HasForeignKey(x => x.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WritingAttemptAsset>()
            .HasOne(x => x.MediaAsset)
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReviewVoiceNote>()
            .HasOne(x => x.ReviewRequest)
            .WithMany()
            .HasForeignKey(x => x.ReviewRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReviewVoiceNote>()
            .HasOne(x => x.MediaAsset)
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity<ContentPaper>()
            .Property(e => e.RowVersion).IsConcurrencyToken();
        // ListeningSequenceJson (WS4 — admin sequence builder) deliberately
        // has NO explicit HasColumnType: it follows the same convention as the
        // sibling ExtractedTextJson column on ContentPaper, which maps to plain
        // `text` (the ContentPaper JSON columns are NOT in the jsonb block
        // above — only ListeningAttempt's hot columns are). Nullable ⇒ the FSM
        // falls back to the derived canonical sequence when absent.
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
        // P0 hardening 2026-05: enforce FK ReadingPart → ContentPaper at the
        // DB level (was a comment-only relationship before, no referential
        // integrity). Paper hard-delete blocked while parts exist; archive
        // instead.
        modelBuilder.Entity<ReadingPart>()
            .HasOne(x => x.Paper)
            .WithMany()
            .HasForeignKey(x => x.PaperId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReadingSection>()
            .HasOne(x => x.Part)
            .WithMany(p => p.Sections)
            .HasForeignKey(x => x.ReadingPartId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadingSection>()
            .HasOne(x => x.ContentPaperAsset)
            .WithMany()
            .HasForeignKey(x => x.ContentPaperAssetId)
            .OnDelete(DeleteBehavior.SetNull);
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
            .HasOne(x => x.Section)
            .WithMany(s => s.Questions)
            .HasForeignKey(x => x.ReadingSectionId)
            .OnDelete(DeleteBehavior.SetNull);
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
        modelBuilder.Entity<ReadingPaperAnnotation>()
            .HasOne(x => x.Paper)
            .WithMany()
            .HasForeignKey(x => x.PaperId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadingPaperAnnotation>()
            .HasOne(x => x.ContentPaperAsset)
            .WithMany()
            .HasForeignKey(x => x.ContentPaperAssetId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReadingQuestionReviewLog>()
            .HasOne(x => x.Question)
            .WithMany()
            .HasForeignKey(x => x.ReadingQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // P0 hardening 2026-05: filtered unique index preventing a learner
        // from holding two in-progress Exam attempts on the same paper. The
        // existing service-level check (`ReadingAttemptService.StartAsync`)
        // has a race window between the SELECT and the INSERT under
        // concurrent requests; the DB now closes it deterministically.
        // Practice modes (Learning / Drill / MiniTest / ErrorBank) are
        // deliberately excluded so a learner may run them alongside an exam
        // attempt (matches existing service comment).
        modelBuilder.Entity<ReadingAttempt>()
            .HasIndex(a => new { a.UserId, a.PaperId, a.Mode, a.Status })
            .HasDatabaseName("UX_ReadingAttempt_UserPaperExam_InProgress")
            .IsUnique()
            .HasFilter("\"Mode\" = 0 AND \"Status\" = 0");

        modelBuilder.Entity<ReadingPolicy>()
            .Property(x => x.AllowPaperReadingMode)
            .HasDefaultValue(true);
        modelBuilder.Entity<NotificationPreference>().HasIndex(x => x.AuthAccountId).IsUnique();
        modelBuilder.Entity<NotificationPolicyOverride>().HasIndex(x => new { x.AudienceRole, x.EventKey }).IsUnique();
        // NOTE: dropped fluent (NotificationEventId, Channel, AttemptedAt) and Endpoint-unique here
        // — both are already declared via [Index] attributes on the entities (NotificationEntities.cs).
        modelBuilder.Entity<NotificationDeliveryAttempt>().HasIndex(x => new { x.Status, x.AttemptedAt });
        modelBuilder.Entity<SubscriptionItem>().HasIndex(x => new { x.SubscriptionId, x.Status });
        modelBuilder.Entity<SubscriptionItem>().HasIndex(x => new { x.ItemCode, x.SubscriptionId });
        modelBuilder.Entity<SubscriptionItem>().HasIndex(x => x.AddOnVersionId);
        modelBuilder.Entity<BillingPlan>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<BillingPlan>().HasIndex(x => new { x.Status, x.DisplayOrder });
        modelBuilder.Entity<WalletTopUpTierConfig>().HasIndex(x => new { x.Amount, x.Currency }).IsUnique();
        modelBuilder.Entity<WalletTopUpTierConfig>().HasIndex(x => new { x.IsActive, x.DisplayOrder });
        modelBuilder.Entity<WalletTopUpTierConfig>().HasIndex(x => x.Slug).IsUnique().HasFilter("\"Slug\" IS NOT NULL");
        modelBuilder.Entity<BillingPlanVersion>().HasIndex(x => new { x.PlanId, x.VersionNumber }).IsUnique();
        modelBuilder.Entity<BillingPlanVersion>().HasIndex(x => x.Code);
        modelBuilder.Entity<BillingAddOn>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<BillingAddOn>().HasIndex(x => new { x.Status, x.DisplayOrder });
        modelBuilder.Entity<BillingAddOnVersion>().HasIndex(x => new { x.AddOnId, x.VersionNumber }).IsUnique();
        modelBuilder.Entity<BillingAddOnVersion>().HasIndex(x => x.Code);
        modelBuilder.Entity<BillingCoupon>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<BillingCoupon>().HasIndex(x => new { x.Status, x.EndsAt });
        modelBuilder.Entity<BillingCouponVersion>().HasIndex(x => new { x.CouponId, x.VersionNumber }).IsUnique();
        modelBuilder.Entity<BillingCouponVersion>().HasIndex(x => x.Code);
        modelBuilder.Entity<BillingCouponRedemption>().HasIndex(x => new { x.CouponCode, x.UserId, x.RedeemedAt });
        modelBuilder.Entity<BillingCouponRedemption>().HasIndex(x => new { x.CouponId, x.UserId, x.RedeemedAt });
        modelBuilder.Entity<BillingCouponRedemption>().HasIndex(x => x.CouponVersionId);
        modelBuilder.Entity<BillingQuote>().HasIndex(x => new { x.UserId, x.CreatedAt });
        modelBuilder.Entity<BillingQuote>().HasIndex(x => new { x.Status, x.ExpiresAt });
        modelBuilder.Entity<BillingQuote>().HasIndex(x => x.PlanVersionId);
        modelBuilder.Entity<BillingQuote>().HasIndex(x => x.CouponVersionId);
        modelBuilder.Entity<BillingEvent>().HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
        modelBuilder.Entity<BillingEvent>().HasIndex(x => new { x.UserId, x.OccurredAt });
        modelBuilder.Entity<NativeIapProductMapping>().HasIndex(x => new { x.Platform, x.StoreProductId }).IsUnique().HasFilter("\"IsActive\" = TRUE");
        modelBuilder.Entity<NativeIapProductMapping>().HasIndex(x => new { x.Platform, x.IsActive });
        modelBuilder.Entity<NativeIapProductMapping>().HasIndex(x => new { x.TargetType, x.TargetId });
        modelBuilder.Entity<Subscription>().HasIndex(x => x.PlanVersionId);
        modelBuilder.Entity<Invoice>().HasIndex(x => x.PlanVersionId);
        modelBuilder.Entity<Invoice>().HasIndex(x => x.QuoteId);
        modelBuilder.Entity<Invoice>().HasIndex(x => x.CheckoutSessionId);
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => x.QuoteId);
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => x.PlanVersionId);
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => x.CouponVersionId);

        // Multi-exam indexes
        modelBuilder.Entity<ExamFamily>().HasIndex(x => new { x.IsActive, x.SortOrder });
        modelBuilder.Entity<ExamType>().HasIndex(x => new { x.Status, x.SortOrder });
        modelBuilder.Entity<TaskType>().HasIndex(x => new { x.ExamTypeCode, x.SubtestCode, x.Status });
        // NOTE: dropped single-column indexes on ContentItem.ExamFamilyCode,
        // ContentItem.ExamTypeCode, Attempt.ExamFamilyCode, Attempt.ExamTypeCode.
        // Every row currently stores "oet" for both columns, so the indexes
        // were ~0-selectivity and only added write amplification. Re-introduce
        // as partial or composite indexes once a second exam family ships.

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
        modelBuilder.Entity<VocabularyTerm>().HasIndex(x => new { x.ProfessionId, x.Category, x.Status });
        modelBuilder.Entity<VocabularyTerm>().HasIndex(x => new { x.Term, x.ExamTypeCode, x.ProfessionId });
        modelBuilder.Entity<VocabularyTerm>().HasIndex(x => new { x.ExamTypeCode, x.Status, x.IsFreePreview });
        modelBuilder.Entity<LearnerVocabulary>().HasIndex(x => new { x.UserId, x.NextReviewDate });
        modelBuilder.Entity<LearnerVocabulary>().HasIndex(x => new { x.UserId, x.TermId }).IsUnique();
        modelBuilder.Entity<LearnerVocabulary>().HasIndex(x => new { x.UserId, x.Starred });
        modelBuilder.Entity<VocabularyQuizResult>().HasIndex(x => new { x.UserId, x.CompletedAt });

        // Adaptive difficulty indexes
        modelBuilder.Entity<LearnerSkillProfile>().HasIndex(x => new { x.UserId, x.ExamTypeCode, x.SubtestCode });

        // Conversation indexes
        modelBuilder.Entity<ConversationSession>().HasIndex(x => new { x.UserId, x.State });
        modelBuilder.Entity<ConversationSession>().HasIndex(x => new { x.UserId, x.CreatedAt });
        modelBuilder.Entity<ConversationSession>().HasIndex(x => x.TemplateId);
        modelBuilder.Entity<ConversationTurn>().HasIndex(x => new { x.SessionId, x.TurnNumber });
        modelBuilder.Entity<ConversationTurn>().HasIndex(x => new { x.SessionId, x.TurnClientId }).IsUnique();
        modelBuilder.Entity<ConversationTurn>().HasIndex(x => new { x.SessionId, x.ProviderEventId }).IsUnique();
        modelBuilder.Entity<ConversationEvaluation>().HasIndex(x => x.SessionId).IsUnique();
        modelBuilder.Entity<ConversationEvaluation>().HasIndex(x => new { x.UserId, x.CreatedAt });
        modelBuilder.Entity<ConversationTurnAnnotation>().HasIndex(x => new { x.SessionId, x.TurnNumber });
        modelBuilder.Entity<ConversationTurnAnnotation>().HasIndex(x => x.EvaluationId);
        modelBuilder.Entity<ConversationSessionResumeToken>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<ConversationSessionResumeToken>().HasIndex(x => new { x.UserId, x.SessionId });
        modelBuilder.Entity<ConversationSessionResumeToken>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<ConversationTemplate>().HasIndex(x => new { x.Status, x.TaskTypeCode, x.ProfessionId });
        modelBuilder.Entity<ConversationTemplate>().HasIndex(x => new { x.Status, x.Difficulty });

        // Runtime infrastructure-settings singleton row (Id="default").
        // See Domain/RuntimeSettingsRow.cs for the DB-override contract used
        // by IRuntimeSettingsProvider.
        modelBuilder.Entity<RuntimeSettingsRow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
        });

        // Writing coach indexes
        modelBuilder.Entity<WritingCoachSession>().HasIndex(x => x.AttemptId);
        modelBuilder.Entity<WritingCoachSuggestion>().HasIndex(x => new { x.AttemptId, x.Resolution });

        // Audit P2-2 — Writing rule-violation analytics indexes.
        // Top dashboard queries:
        //   • "violations in window grouped by ruleId" → (RuleId, GeneratedAt)
        //   • "profession breakdown for window" → (Profession, GeneratedAt)
        //   • "attempt drill-down" → (AttemptId)
        modelBuilder.Entity<WritingRuleViolation>().HasIndex(x => new { x.RuleId, x.GeneratedAt });
        modelBuilder.Entity<WritingRuleViolation>().HasIndex(x => new { x.Profession, x.GeneratedAt });
        modelBuilder.Entity<WritingRuleViolation>().HasIndex(x => x.AttemptId);

        // Pronunciation indexes (base indexes declared via [Index] attributes on entities)
        // Compound attempt index added below for fast drill-list lookups
        modelBuilder.Entity<PronunciationAttempt>().HasIndex(x => new { x.UserId, x.DrillId, x.CreatedAt });

        // Prediction indexes
        modelBuilder.Entity<PredictionSnapshot>().HasIndex(x => new { x.UserId, x.ExamTypeCode, x.SubtestCode, x.ComputedAt });

        // Wave 3 of docs/SPEAKING-MODULE-PLAN.md - mock-set lookups by
        // status (admin list / learner published list) and rolling window
        // counting per learner for the free-tier cap.
        modelBuilder.Entity<SpeakingMockSet>().HasIndex(x => new { x.Status, x.SortOrder });
        modelBuilder.Entity<SpeakingMockSession>().HasIndex(x => new { x.UserId, x.StartedAt });
        modelBuilder.Entity<SpeakingMockSession>().HasIndex(x => x.MockSetId);

        // Wave 4 of docs/SPEAKING-MODULE-PLAN.md - calibration drift +
        // inline transcript comments. Composite uniqueness on
        // (SampleId, TutorId) so each tutor submits exactly one rubric
        // per sample (re-submits update in place).
        modelBuilder.Entity<SpeakingCalibrationSample>().HasIndex(x => new { x.Status, x.PublishedAt });
        modelBuilder.Entity<SpeakingCalibrationScore>().HasIndex(x => new { x.SampleId, x.TutorId }).IsUnique();
        modelBuilder.Entity<SpeakingCalibrationScore>().HasIndex(x => x.TutorId);
        modelBuilder.Entity<SpeakingFeedbackComment>().HasIndex(x => new { x.AttemptId, x.TranscriptLineIndex });

        // Learning content indexes
        modelBuilder.Entity<GrammarLesson>().HasIndex(x => new { x.ExamTypeCode, x.Category, x.Status });
        modelBuilder.Entity<LearnerGrammarProgress>().HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
        modelBuilder.Entity<VideoLesson>().HasIndex(x => new { x.ExamTypeCode, x.Category, x.Status });
        modelBuilder.Entity<LearnerVideoProgress>().HasIndex(x => new { x.UserId, x.VideoLessonId }).IsUnique();
        modelBuilder.Entity<StrategyGuide>().HasIndex(x => new { x.ExamTypeCode, x.Category, x.Status });
        modelBuilder.Entity<StrategyGuide>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<StrategyGuide>().HasIndex(x => x.ContentLessonId);
        modelBuilder.Entity<LearnerStrategyProgress>().HasIndex(x => new { x.UserId, x.StrategyGuideId }).IsUnique();

        // Community indexes
        modelBuilder.Entity<ForumThread>().HasIndex(x => new { x.CategoryId, x.LastActivityAt });
        // Paginated thread view orders by CreatedAt; include it so the ORDER BY is indexed.
        modelBuilder.Entity<ForumReply>().HasIndex(x => new { x.ThreadId, x.CreatedAt });
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
        modelBuilder.Entity<PrivateSpeakingTutorCalendarConnection>().HasIndex(x => x.TutorProfileId).IsUnique();
        modelBuilder.Entity<PrivateSpeakingTutorCalendarConnection>().HasIndex(x => x.ExpertUserId);
        modelBuilder.Entity<PrivateSpeakingAvailabilityRule>().HasIndex(x => new { x.TutorProfileId, x.DayOfWeek });
        modelBuilder.Entity<PrivateSpeakingAvailabilityOverride>().HasIndex(x => new { x.TutorProfileId, x.Date });
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => new { x.TutorProfileId, x.SessionStartUtc });
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => new { x.LearnerUserId, x.SessionStartUtc });
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.EntitlementSubscriptionId);
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.GoogleCalendarEventId);
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.Status);
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.IdempotencyKey).IsUnique();
        modelBuilder.Entity<PrivateSpeakingBooking>().HasIndex(x => x.StripeCheckoutSessionId);
        modelBuilder.Entity<PrivateSpeakingAuditLog>().HasIndex(x => x.BookingId);
        modelBuilder.Entity<PrivateSpeakingAuditLog>().HasIndex(x => x.CreatedAt);

        // Live class indexes and relationships
        modelBuilder.Entity<LiveClass>().Property(x => x.PriceUsd).HasPrecision(10, 2);
        modelBuilder.Entity<LiveClass>()
            .HasOne(x => x.TutorProfile)
            .WithMany()
            .HasForeignKey(x => x.TutorProfileId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<LiveClassSession>()
            .HasOne(x => x.LiveClass)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.LiveClassId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LiveClassSession>()
            .HasOne(x => x.Recording)
            .WithOne(x => x.ClassSession)
            .HasForeignKey<LiveClassRecording>(x => x.ClassSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LiveClassEnrollment>()
            .HasOne(x => x.ClassSession)
            .WithMany(x => x.Enrollments)
            .HasForeignKey(x => x.ClassSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LiveClassAttendance>()
            .HasOne(x => x.ClassSession)
            .WithMany()
            .HasForeignKey(x => x.ClassSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Wave A2 — recording transcript embeddings (RAG store).
        modelBuilder.Entity<ClassRecordingEmbedding>()
            .HasOne(x => x.Recording)
            .WithMany()
            .HasForeignKey(x => x.ClassRecordingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Wave A1 — Tutor + class extras (Zoom tutor stack).
        modelBuilder.Entity<OetLearner.Api.Domain.Classes.Tutor>().Property(x => x.HourlyRateUsd).HasPrecision(10, 2);
        modelBuilder.Entity<OetLearner.Api.Domain.Classes.TutorAvailability>()
            .HasOne(x => x.Tutor)
            .WithMany(x => x.Availability)
            .HasForeignKey(x => x.TutorId)
            .OnDelete(DeleteBehavior.Cascade);

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
        modelBuilder.Entity<WalletTransaction>()
            .HasIndex(x => new { x.WalletId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        modelBuilder.Entity<WalletTransaction>()
            .HasIndex(x => new { x.WalletId, x.TransactionType, x.ReferenceType, x.ReferenceId })
            .IsUnique()
            .HasFilter("\"ReferenceId\" IS NOT NULL AND ((\"TransactionType\" = 'top_up' AND \"ReferenceType\" = 'payment') OR (\"TransactionType\" = 'plan_grant' AND \"ReferenceType\" = 'subscription') OR (\"TransactionType\" = 'credit_purchase' AND \"ReferenceType\" = 'addon'))");

        // AI renewal idempotence: one monthly plan-renewal grant per reference key.
        modelBuilder.Entity<AiCreditLedgerEntry>()
            .HasIndex(x => x.ReferenceId)
            .IsUnique()
            .HasDatabaseName("UX_AiCreditLedger_PlanRenewal_ReferenceId")
            .HasFilter("\"ReferenceId\" IS NOT NULL AND \"Source\" = 0");

        // AI usage idempotence: one debit entry per persisted usage row.
        modelBuilder.Entity<AiCreditLedgerEntry>()
            .HasIndex(x => new { x.ReferenceId, x.Source })
            .IsUnique()
            .HasDatabaseName("UX_AiCreditLedger_UsageDebit_ReferenceId")
            .HasFilter("\"ReferenceId\" IS NOT NULL AND \"Source\" = 4");

        // AI purchase idempotence: one paid credit grant per checkout / grant reference.
        modelBuilder.Entity<AiCreditLedgerEntry>()
            .HasIndex(x => new { x.UserId, x.ReferenceId, x.Source })
            .IsUnique()
            .HasDatabaseName("UX_AiCreditLedger_Purchase_ReferenceId")
            .HasFilter("\"ReferenceId\" IS NOT NULL AND \"Source\" = 2");

        // AI expiration idempotence: one expiration row per original grant.
        modelBuilder.Entity<AiCreditLedgerEntry>()
            .HasIndex(x => new { x.Source, x.ReferenceId })
            .IsUnique()
            .HasDatabaseName("UX_AiCreditLedger_Expiration_ReferenceId")
            .HasFilter("\"ReferenceId\" IS NOT NULL AND \"Source\" = 5");

        // AI refund reversal idempotence: one admin-adjustment reversal per purchase reference.
        modelBuilder.Entity<AiCreditLedgerEntry>()
            .HasIndex(x => new { x.Source, x.UserId, x.ReferenceId })
            .IsUnique()
            .HasDatabaseName("UX_AiCreditLedger_RefundAdjustment_ReferenceId")
            .HasFilter("\"ReferenceId\" IS NOT NULL AND \"Source\" = 3 AND (\"ReferenceId\" LIKE 'addon-refund:%' OR \"ReferenceId\" LIKE 'plan-refund:%')");

        modelBuilder.Entity<AiPackageCreditTransaction>()
            .HasIndex(x => x.StripeSessionId)
            .IsUnique()
            .HasDatabaseName("UX_AiPackageCreditTransactions_StripeSessionId")
            .HasFilter("\"StripeSessionId\" IS NOT NULL");

        modelBuilder.Entity<AiPackageCreditTransaction>()
            .HasIndex(x => new { x.ReferenceId, x.Reason })
            .IsUnique()
            .HasDatabaseName("UX_AiPackageCreditTransactions_Reference_Reason")
            .HasFilter("\"ReferenceId\" IS NOT NULL");

        // Payment indexes
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => new { x.LearnerUserId, x.CreatedAt });
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => new { x.Status, x.CreatedAt });
        modelBuilder.Entity<PaymentTransaction>().HasIndex(x => x.GatewayTransactionId).IsUnique();
        modelBuilder.Entity<PaymentWebhookEvent>().HasIndex(x => x.GatewayEventId).IsUnique();
        modelBuilder.Entity<PaymentWebhookEvent>().HasIndex(x => new { x.ProcessingStatus, x.ReceivedAt });
        modelBuilder.Entity<PaymentWebhookEvent>().HasIndex(x => new { x.VerificationStatus, x.ProcessingStatus });

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

        // ── Listening AI extraction drafts (gap B7) ──
        // Cascade-delete drafts when the parent ContentPaper is removed so we
        // never leave orphan draft rows behind.
        modelBuilder.Entity<ListeningExtractionDraft>()
            .HasOne(d => d.Paper)
            .WithMany()
            .HasForeignKey(d => d.PaperId)
            .HasPrincipalKey(p => p.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Listening V2 — pathway + teacher classes + attempt notes ──
        // Notes follow attempt lifecycle (cascade). Class members follow
        // their class (cascade). Pathway progress NEVER cascades from User
        // (Restrict) — anonymisation worker writes a tombstone instead.
        modelBuilder.Entity<ListeningAttemptNote>()
            .HasOne<ListeningAttempt>()
            .WithMany()
            .HasForeignKey(n => n.ListeningAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── ListeningExpertFeedback — FK to ListeningAttempt (cascade) ──
        modelBuilder.Entity<ListeningExpertFeedback>()
            .HasOne(f => f.Attempt)
            .WithMany()
            .HasForeignKey(f => f.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeacherClassMember>()
            .HasOne(m => m.Class)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.TeacherClassId)
            .OnDelete(DeleteBehavior.Cascade);

        // ListeningPathwayProgress: no FK to User (UserId is a free string id
        // matching the project-wide convention) — leaves cascade implicit and
        // safe under account-anonymisation. The unique (UserId, StageCode)
        // index already exists via the [Index] attribute on the entity.

        // Reading Pathway subsystem indexes
        modelBuilder.Entity<LearnerSkillScore>().HasIndex(x => new { x.UserId, x.SkillCode }).IsUnique();
        modelBuilder.Entity<ReadingDailyPlanItem>().HasIndex(x => new { x.UserId, x.PlanDate });
        modelBuilder.Entity<LearnerVocabularyItem>().HasIndex(x => new { x.UserId, x.NextReviewAt });
        modelBuilder.Entity<LearnerVocabularyItem>().HasIndex(x => new { x.UserId, x.VocabularyWordId }).IsUnique();
        modelBuilder.Entity<ReadingQuestionAttempt>().HasIndex(x => new { x.UserId, x.AttemptedAt });
        modelBuilder.Entity<ReadingQuestionAttempt>().HasIndex(x => new { x.UserId, x.InReviewQueue, x.NextReviewAt });
        modelBuilder.Entity<StreakRecord>().HasIndex(x => new { x.UserId, x.Date }).IsUnique();
        modelBuilder.Entity<ReadingQuestionDiscussionComment>().HasIndex(x => x.ReadingQuestionId);
        modelBuilder.Entity<LearnerReadingProfile>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<LearnerLessonProgress>().HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
        modelBuilder.Entity<ReadingStrategyProgress>().HasIndex(x => new { x.UserId, x.StrategyId }).IsUnique();
        modelBuilder.Entity<LearnerXp>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<LearnerBadge>().HasIndex(x => new { x.UserId, x.BadgeCode }).IsUnique();

        // Writing Pathway subsystem indexes
        modelBuilder.Entity<LearnerWritingProfile>(entity =>
        {
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.Property(x => x.AccommodationProfileJson).HasColumnType("jsonb").HasDefaultValue("{}");
        });
        modelBuilder.Entity<LearnerWritingPathway>(entity =>
        {
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.Property(x => x.WeaknessVectorJson).HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(x => x.SubSkillMasteryJson).HasColumnType("jsonb").HasDefaultValue("{}");
        });
        modelBuilder.Entity<WritingDailyPlanItem>().HasIndex(x => new { x.UserId, x.PlanDate });
        modelBuilder.Entity<WritingDailyPlanItem>().HasIndex(x => new { x.UserId, x.Status });
        modelBuilder.Entity<WritingLesson>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<WritingLesson>().HasIndex(x => new { x.SkillCode, x.OrderIndex });
        modelBuilder.Entity<LearnerWritingLessonProgress>().HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();

        // Listening Pathway subsystem indexes
        modelBuilder.Entity<LearnerListeningProfile>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<LearnerListeningSkillScore>().HasIndex(x => new { x.UserId, x.SkillCode }).IsUnique();
        modelBuilder.Entity<LearnerAccentProgress>().HasIndex(x => new { x.UserId, x.Accent }).IsUnique();
        modelBuilder.Entity<ListeningPracticeSession>().HasIndex(x => new { x.UserId, x.StartedAt });
        modelBuilder.Entity<ListeningQuestionAttempt>().HasIndex(x => new { x.UserId, x.AttemptedAt });
        modelBuilder.Entity<ListeningQuestionAttempt>().HasIndex(x => new { x.UserId, x.InReviewQueue, x.NextReviewAt });
        modelBuilder.Entity<ListeningPracticeNote>().HasIndex(x => new { x.UserId, x.PracticeSessionId, x.ListeningQuestionId });

        // Listening Pathway Phase 2–5 indexes
        modelBuilder.Entity<ListeningLesson>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<ListeningLesson>().HasIndex(x => new { x.SkillCode, x.OrderIndex });
        modelBuilder.Entity<LearnerListeningLessonProgress>().HasIndex(x => new { x.UserId, x.LessonId }).IsUnique();
        modelBuilder.Entity<ListeningDailyPlanItem>().HasIndex(x => new { x.UserId, x.PlanDate });
        modelBuilder.Entity<ListeningStrategy>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<LearnerListeningStrategyProgress>().HasIndex(x => new { x.UserId, x.StrategyId }).IsUnique();
        modelBuilder.Entity<PronunciationCard>().HasIndex(x => x.Word).IsUnique();
        modelBuilder.Entity<LearnerPronunciationCard>().HasIndex(x => new { x.UserId, x.NextReviewAt });
        modelBuilder.Entity<LearnerPronunciationCard>().HasIndex(x => new { x.UserId, x.PronunciationCardId }).IsUnique();
        modelBuilder.Entity<DictationDrill>().HasIndex(x => new { x.DrillType, x.Accent, x.IsPublished });
        modelBuilder.Entity<LearnerDictationProgress>().HasIndex(x => new { x.UserId, x.NextReviewAt });
        modelBuilder.Entity<LearnerDictationProgress>().HasIndex(x => new { x.UserId, x.DictationDrillId }).IsUnique();

        // Scoring Policy table (partial; see LearnerDbContext.ScoringPolicy.cs).
        OnModelCreatingScoringPolicy(modelBuilder);

        // Result Template Assets table (partial; see LearnerDbContext.ResultTemplates.cs).
        OnModelCreatingResultTemplates(modelBuilder);

        // Speaking Shared Resources table (partial; see LearnerDbContext.SpeakingSharedResources.cs).
        OnModelCreatingSpeakingSharedResources(modelBuilder);

        // Role-Play Cards + Interlocutor Scripts (partial; see LearnerDbContext.RolePlayCards.cs).
        OnModelCreatingRolePlayCards(modelBuilder);

        // Speaking Sessions / Recordings / Transcripts (partial; see LearnerDbContext.SpeakingSessions.cs).
        OnModelCreatingSpeakingSessions(modelBuilder);

        // Speaking AI + Tutor Assessments + Timestamped Comments (partial; see LearnerDbContext.SpeakingAssessments.cs).
        OnModelCreatingSpeakingAssessments(modelBuilder);

        // Speaking double-marking + senior moderation (partial; see LearnerDbContext.SpeakingModeration.cs).
        OnModelCreatingSpeakingModeration(modelBuilder);

        // Speaking Live Rooms + Tokens (partial; see LearnerDbContext.SpeakingLiveRooms.cs).
        OnModelCreatingSpeakingLiveRooms(modelBuilder);

        // Speaking Drill Items + Attempts (partial; see LearnerDbContext.SpeakingDrills.cs).
        OnModelCreatingSpeakingDrills(modelBuilder);

        // Interlocutor Training Modules + Progress (partial; see LearnerDbContext.InterlocutorTraining.cs).
        OnModelCreatingInterlocutorTraining(modelBuilder);

        // Recall Set Tags table (partial; see LearnerDbContext.RecallSetTags.cs).
        OnModelCreatingRecallSetTags(modelBuilder);

        // Billing region pricing + gateway routing (partial; see LearnerDbContext.BillingRegion.cs).
        OnModelCreatingBillingRegion(modelBuilder);

        // Study planner engine + templates (partial; see LearnerDbContext.StudyPlan.cs).
        OnModelCreatingStudyPlan(modelBuilder);

        // Churn risk + usage forecast + FX rates + pricing experiments
        // (partial; see LearnerDbContext.ChurnAndFx.cs).
        OnModelCreatingChurnAndFx(modelBuilder);

        // Readiness snapshot + weekly history (partial; see LearnerDbContext.Readiness.cs).
        OnModelCreatingReadiness(modelBuilder);

        // Writing Module V2 — scenarios, exemplars, submissions, grades, canon,
        // drills, lessons, mocks, readiness, mistakes, tutor, OCR, showcase.
        // Each partial lives in LearnerDbContext.WritingScenarios.cs etc.
        OnModelCreatingWritingScenarios(modelBuilder);
        OnModelCreatingWritingExemplars(modelBuilder);
        OnModelCreatingWritingSubmissions(modelBuilder);
        OnModelCreatingWritingCanon(modelBuilder);
        OnModelCreatingWritingDrills(modelBuilder);
        OnModelCreatingWritingLessons(modelBuilder);
        OnModelCreatingWritingMocks(modelBuilder);
        OnModelCreatingWritingReadiness(modelBuilder);
        OnModelCreatingWritingMistakes(modelBuilder);
        OnModelCreatingWritingTutor(modelBuilder);
        OnModelCreatingWritingOcr(modelBuilder);
        OnModelCreatingWritingShowcase(modelBuilder);
        OnModelCreatingWritingDiagnosticSessions(modelBuilder);

        // Writing Module V2 post-launch additions — Buddy System (spec §23.5)
        // and 50-letter calibration harness (spec §33). Partial classes in
        // LearnerDbContext.WritingBuddy.cs and LearnerDbContext.WritingCalibration.cs.
        OnModelCreatingWritingBuddy(modelBuilder);
        OnModelCreatingWritingCalibration(modelBuilder);

        // OET Writing exam-faithful closure — authored task fields, content
        // checklists, attempt events, span annotations, double-marking/moderation,
        // result-visibility. Partial class in LearnerDbContext.WritingExam.cs.
        OnModelCreatingWritingExam(modelBuilder);

        // Materials library — nestable folders, files, and per-folder audience
        // assignment. Partial class in LearnerDbContext.Materials.cs.
        OnModelCreatingMaterials(modelBuilder);
    }

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.ScoringPolicy.cs (partial).
    /// </summary>
    partial void OnModelCreatingScoringPolicy(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.ResultTemplates.cs (partial).
    /// </summary>
    partial void OnModelCreatingResultTemplates(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.SpeakingSharedResources.cs (partial).
    /// </summary>
    partial void OnModelCreatingSpeakingSharedResources(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.RolePlayCards.cs (partial).
    /// </summary>
    partial void OnModelCreatingRolePlayCards(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.SpeakingSessions.cs (partial).
    /// </summary>
    partial void OnModelCreatingSpeakingSessions(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.SpeakingAssessments.cs (partial).
    /// </summary>
    partial void OnModelCreatingSpeakingAssessments(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.SpeakingModeration.cs (partial).
    /// </summary>
    partial void OnModelCreatingSpeakingModeration(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.SpeakingLiveRooms.cs (partial).
    /// </summary>
    partial void OnModelCreatingSpeakingLiveRooms(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.SpeakingDrills.cs (partial).
    /// </summary>
    partial void OnModelCreatingSpeakingDrills(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.InterlocutorTraining.cs (partial).
    /// </summary>
    partial void OnModelCreatingInterlocutorTraining(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.RecallSetTags.cs (partial).
    /// </summary>
    partial void OnModelCreatingRecallSetTags(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.BillingRegion.cs (partial).
    /// </summary>
    partial void OnModelCreatingBillingRegion(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.ChurnAndFx.cs (partial).
    /// </summary>
    partial void OnModelCreatingChurnAndFx(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.StudyPlan.cs (partial).
    /// </summary>
    partial void OnModelCreatingStudyPlan(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.Readiness.cs (partial).
    /// </summary>
    partial void OnModelCreatingReadiness(ModelBuilder modelBuilder);

    // Writing Module V2 — partial method declarations. Implementations live in
    // LearnerDbContext.Writing*.cs (one file per logical entity grouping).
    partial void OnModelCreatingWritingScenarios(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingExemplars(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingSubmissions(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingCanon(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingDrills(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingLessons(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingMocks(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingReadiness(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingMistakes(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingTutor(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingOcr(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingShowcase(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingDiagnosticSessions(ModelBuilder modelBuilder);

    // Writing Module V2 post-launch additions — Buddy System (spec §23.5)
    // and 50-letter calibration harness (spec §33).
    partial void OnModelCreatingWritingBuddy(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingCalibration(ModelBuilder modelBuilder);
    partial void OnModelCreatingWritingExam(ModelBuilder modelBuilder);

    /// <summary>
    /// Defined in <see cref="LearnerDbContext"/>.Materials.cs (partial).
    /// </summary>
    partial void OnModelCreatingMaterials(ModelBuilder modelBuilder);

    /// <summary>
    /// Resolves a candidate audit actor id to a value safe to store in
    /// <see cref="Domain.AuditEvent.ActorAuthAccountId"/>, which carries a
    /// foreign key to <c>ApplicationUserAccounts.Id</c>
    /// (<c>FK_AuditEvents_ApplicationUserAccounts_ActorAuthAccountId</c>).
    ///
    /// Returns <paramref name="candidateActorId"/> only when a matching
    /// account row exists, otherwise <c>null</c>. The caller id frequently is
    /// NOT an account row — e.g. under <c>DevelopmentAuthHandler</c> it is the
    /// raw <c>X-Debug-UserId</c> header, and seeded/legacy admin ids never had
    /// an <c>ApplicationUserAccount</c> — and writing such a dangling id into
    /// the FK column makes <c>SaveChanges</c> throw a 500 ("An error occurred
    /// while saving the entity changes"). Audit-writing services must resolve
    /// the FK through this helper while keeping the raw caller id in the
    /// non-FK <c>AuditEvent.ActorId</c> / <c>ActorName</c> columns for
    /// traceability. See <c>ContentPaperService.WriteAuditAsync</c> for the
    /// canonical pattern (it simply leaves the FK null).
    /// </summary>
    public async Task<string?> ResolveActorAuthAccountIdAsync(
        string? candidateActorId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidateActorId))
        {
            return null;
        }

        return await ApplicationUserAccounts
            .AnyAsync(a => a.Id == candidateActorId, ct)
            ? candidateActorId
            : null;
    }

    /// <summary>
    /// Configures the Postgres system column <c>xmin</c> as an optimistic
    /// concurrency token for <typeparamref name="TEntity"/>. xmin is always
    /// present on every row, so this is purely a model-level opt-in — no DDL.
    /// </summary>
    private static void ConfigureXminToken<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>()
            .Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
