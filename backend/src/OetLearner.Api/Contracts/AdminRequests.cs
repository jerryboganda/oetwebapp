namespace OetLearner.Api.Contracts;

// ── Content ──

public record AdminContentCreateRequest(
    string Title,
    string ContentType,
    string SubtestCode,
    string? ProfessionId,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    string? Description,
    string? ModelAnswer,
    string? CriteriaFocus,
    string? CaseNotes,
    string? SourceType,
    string? QaStatus);

public record AdminContentUpdateRequest(
    string? Title,
    string? ContentType,
    string? SubtestCode,
    string? ProfessionId,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    string? Description,
    string? ModelAnswer,
    string? CriteriaFocus,
    string? CaseNotes,
    string? SourceType,
    string? QaStatus,
    string? ChangeNote);

public record AdminContentStatusRequest(string? Reason);

// ── Speaking mock sets (Wave 3 of docs/SPEAKING-MODULE-PLAN.md) ──
//
// A mock set is the curatorial pairing of two speaking role-plays. Both
// referenced ContentItem rows MUST have SubtestCode = "speaking" and the
// admin service refuses to publish until that holds.
public record AdminSpeakingMockSetCreateRequest(
    string Title,
    string RolePlay1ContentId,
    string RolePlay2ContentId,
    string? ProfessionId = null,
    string? Description = null,
    string? Difficulty = null,
    string? CriteriaFocus = null,
    string? Tags = null,
    int? SortOrder = null);

public record AdminSpeakingMockSetUpdateRequest(
    string? Title = null,
    string? RolePlay1ContentId = null,
    string? RolePlay2ContentId = null,
    string? ProfessionId = null,
    string? Description = null,
    string? Difficulty = null,
    string? CriteriaFocus = null,
    string? Tags = null,
    int? SortOrder = null);

// ── Speaking calibration (Wave 4 of docs/SPEAKING-MODULE-PLAN.md) ──
//
// Calibration samples are admin-curated reference attempts with the
// canonical 9-criterion rubric. Tutors then submit their rubric for the
// same sample; drift = mean absolute error across criteria. Scores are
// validated server-side against `OetScoring.SpeakingCriterionScores`
// limits (linguistic 0–6, clinical 0–3) — see
// `AdminService.SpeakingCalibration.cs` and
// `ExpertService.SpeakingCalibration.cs`.

public record AdminSpeakingCalibrationSampleCreateRequest(
    string Title,
    string SourceAttemptId,
    SpeakingCriterionScoresPayload GoldScores,
    string? Description = null,
    string? ProfessionId = null,
    string? Difficulty = null,
    string? CalibrationNotes = null);

public record AdminSpeakingCalibrationSampleUpdateRequest(
    string? Title = null,
    string? Description = null,
    SpeakingCriterionScoresPayload? GoldScores = null,
    string? ProfessionId = null,
    string? Difficulty = null,
    string? CalibrationNotes = null);

public record SpeakingCriterionScoresPayload(
    int Intelligibility,
    int Fluency,
    int Appropriateness,
    int GrammarExpression,
    int RelationshipBuilding,
    int PatientPerspective,
    int Structure,
    int InformationGathering,
    int InformationGiving);

public record TutorSpeakingCalibrationSubmitRequest(
    SpeakingCriterionScoresPayload Scores,
    string? Notes = null);

// Phase 7 — admin-side drift report. One row per tutor with at least
// `MinSamples` published calibration submissions; `OverallMAE` is the
// mean absolute error across all 9 OET speaking criteria, and
// `CriterionMAE` is the per-criterion breakdown that drives the radar
// chart in the admin drift tab.
public record TutorCalibrationDriftRow(
    string TutorId,
    string DisplayName,
    int Samples,
    double OverallMAE,
    string CriterionMAEJson,
    IReadOnlyDictionary<string, double> CriterionMAE,
    DateTimeOffset? LastSubmittedAt);

public record TutorCalibrationDriftReport(
    IReadOnlyList<TutorCalibrationDriftRow> Tutors,
    int SampleSize,
    int SamplesPublished,
    int MinSamples);

public record ExpertSpeakingFeedbackCommentRequest(
    int TranscriptLineIndex,
    string Body,
    string? CriterionCode = null);

// ── Taxonomy ──

public record AdminTaxonomyCreateRequest(
    string Label,
    string Code,
    string? Type,
    string? Description);

public record AdminTaxonomyUpdateRequest(
    string? Label,
    string? Code,
    string? Type,
    string? Status);

// ── Signup Catalog ──

public record AdminSignupExamTypeCatalogRequest(
    string? Id,
    string? Code,
    string? Label,
    string? Description,
    int? SortOrder,
    bool? IsActive);

public record AdminSignupProfessionCatalogRequest(
    string? Id,
    string? Label,
    string? Description,
    IReadOnlyList<string>? ExamTypeIds,
    IReadOnlyList<string>? CountryTargets,
    int? SortOrder,
    bool? IsActive);

// ── Criteria ──

public record AdminCriterionCreateRequest(
    string Name,
    string SubtestCode,
    int Weight,
    string? Description);

public record AdminCriterionUpdateRequest(
    string? Name,
    int? Weight,
    string? Status,
    string? Description);

// ── AI Config ──

public record AdminAIConfigCreateRequest(
    string Model,
    string Provider,
    string TaskType,
    string? Status,
    double Accuracy,
    double ConfidenceThreshold,
    string? RoutingRule,
    string? ExperimentFlag,
    string? PromptLabel,
    AdminAIConfidencePolicyRequest? ConfidencePolicy = null);

public record AdminAIConfigUpdateRequest(
    string? Model,
    string? Provider,
    string? TaskType,
    string? Status,
    double? Accuracy,
    double? ConfidenceThreshold,
    string? RoutingRule,
    string? ExperimentFlag,
    string? PromptLabel,
    AdminAIConfidencePolicyRequest? ConfidencePolicy = null);

public record AdminAIConfidencePolicyRequest(
    string Band,
    double MinThreshold,
    double MaxThreshold,
    bool RecommendsHumanReview,
    string LearnerLabel,
    string ProvenanceLabel,
    string Disclaimer);

// ── Feature Flags ──

public record AdminFlagCreateRequest(
    string Name,
    string Key,
    string? FlagType,
    bool Enabled,
    int RolloutPercentage,
    string? Description,
    string? Owner);

public record AdminFlagUpdateRequest(
    string? Name,
    string? Key,
    string? FlagType,
    bool? Enabled,
    int? RolloutPercentage,
    string? Description,
    string? Owner);

// ── Users ──

public record AdminUserInviteRequest(
    string Name,
    string Email,
    string Role,
    string? ProfessionId);

public record AdminUserStatusRequest(string Status, string? Reason);

public record AdminUserCreditsRequest(int Amount, string? Reason);

public record AdminUserSetPasswordRequest(string Password);

public record AdminUserLifecycleRequest(string? Reason);

/// <summary>
/// Admin edit of a user's captured registration/profile data. Every field is optional;
/// only provided (non-null) values are applied. Email is intentionally absent — it is the
/// immutable auth identity and cannot be changed through this endpoint.
/// </summary>
public record AdminUserProfileUpdateRequest(
    string? DisplayName,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? ProfessionId,
    string? ExamTypeId,
    string? CountryTarget,
    string? Timezone,
    string? Locale,
    bool? MarketingOptIn,
    bool? AgreeToTerms,
    bool? AgreeToPrivacy,
    string[]? Specialties,
    string? Reason);

/// <summary>
/// Admin manual "Add User" — create a learner (or expert/admin) directly. When
/// <paramref name="Password"/> is set the account is created ready-to-use with that
/// password and no email challenge; otherwise (or when <paramref name="SendInvite"/>
/// is true) the existing invite/OTP email flow runs. Phone is persisted to the
/// learner registration profile.
/// </summary>
public record AdminUserCreateRequest(
    string Name,
    string Email,
    string Role,
    string? ProfessionId,
    string? MobileNumber,
    string? Password,
    bool SendInvite);

// ── Per-user access allocation (Add-User feature) ──

/// <summary>Grant one package (billing plan) to a user as a new subscription row.
/// Multiple packages per user are allowed. <paramref name="StartsAt"/> backdates or
/// postdates the grant (defaults to now) and anchors the expiry; <paramref name="ExpiresAt"/>
/// overrides the plan's access-duration expiry when provided. <paramref name="MakePrimary"/>
/// points the learner's CurrentPlanId at this package. <paramref name="OverrideProfessionMismatch"/>
/// lets an admin attach a profession-specific plan to a learner registered under a
/// different profession (audited either way).</summary>
public record AdminUserAccessPackageRequest(
    string PlanCode,
    DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt,
    bool MakePrimary,
    bool GrantIncludedCredits,
    bool OverrideProfessionMismatch);

/// <summary>Grant an add-on to a user, applied to a target subscription (defaults to the
/// user's primary/latest). Idempotent per (user, addon, subscription).</summary>
public record AdminUserAccessAddonRequest(
    string AddonCode,
    string? SubscriptionId,
    int Quantity);

/// <summary>A single per-user module override (see ModuleKeys).</summary>
public record AdminModuleOverrideDto(string ModuleKey, bool Enabled);

/// <summary>Declarative overwrite of a user's per-user scope: module overrides, the
/// Materials folder allow-list, the Recall-set allow-list, and the master access-expiry
/// login gate. Each list REPLACES the existing rows. Set <paramref name="ClearAccessExpiry"/>
/// to remove the master expiry; otherwise <paramref name="AccessExpiresAt"/> (when provided)
/// sets it.</summary>
public record AdminUserAccessScopeRequest(
    List<AdminModuleOverrideDto>? Modules,
    List<string>? MaterialFolderIds,
    List<string>? RecallSetCodes,
    DateTimeOffset? AccessExpiresAt,
    bool ClearAccessExpiry,
    List<string>? VideoIds = null);

// ── Billing ──

public record AdminBillingPlanCreateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
    int DurationMonths,
    int IncludedCredits,
    int DisplayOrder,
    bool IsVisible,
    bool IsRenewable,
    int TrialDays,
    string? DiagnosticMockEntitlement = null,
    string? Status = null,
    string? IncludedSubtestsJson = null,
    string? EntitlementsJson = null,
    // ── OET 2026 catalog fields (optional — backward compatible) ──
    decimal? OriginalPriceGbp = null,
    int? AccessDurationDays = null,
    bool? WritingAddonsEnabled = null,
    bool? SpeakingAddonsEnabled = null,
    bool? SpeakingPracticeAccessEnabled = null,
    bool? TutorBookDiscountEnabled = null,
    string? Profession = null,
    string? ProductCategory = null,
    string? DashboardModulesJson = null,
    int? BundledWritingAssessments = null,
    int? BundledSpeakingSessions = null,
    int? BundledAiCredits = null,
    bool? BundledTutorBook = null,
    bool? BundledBasicEnglish = null,
    bool? IsDraft = null,
    bool? ExtensionAllowed = null,
    bool? RecallUpdatesEnabled = null,
    // "What's included" bullet list — persisted on the linked ContentPackage.
    string? ComparisonFeaturesJson = null,
    // ── Delivery + content scoping (access & payment spec 2026-07-15) ──
    // Empty string clears TelegramInviteUrl / DeliveryInstructions / ContentOverridesJson;
    // null leaves the stored value untouched.
    string? DeliveryMethod = null,
    string? TelegramInviteUrl = null,
    string? DeliveryInstructions = null,
    string? ContentOverridesJson = null);

public record AdminBillingPlanUpdateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
    int DurationMonths,
    int IncludedCredits,
    int DisplayOrder,
    bool IsVisible,
    bool IsRenewable,
    int TrialDays,
    string? DiagnosticMockEntitlement = null,
    string? Status = null,
    string? IncludedSubtestsJson = null,
    string? EntitlementsJson = null,
    decimal? OriginalPriceGbp = null,
    int? AccessDurationDays = null,
    bool? WritingAddonsEnabled = null,
    bool? SpeakingAddonsEnabled = null,
    bool? SpeakingPracticeAccessEnabled = null,
    bool? TutorBookDiscountEnabled = null,
    string? Profession = null,
    string? ProductCategory = null,
    string? DashboardModulesJson = null,
    int? BundledWritingAssessments = null,
    int? BundledSpeakingSessions = null,
    int? BundledAiCredits = null,
    bool? BundledTutorBook = null,
    bool? BundledBasicEnglish = null,
    bool? IsDraft = null,
    bool? ExtensionAllowed = null,
    bool? RecallUpdatesEnabled = null,
    string? ComparisonFeaturesJson = null,
    // ── Delivery + content scoping (access & payment spec 2026-07-15) ──
    // Empty string clears TelegramInviteUrl / DeliveryInstructions / ContentOverridesJson;
    // null leaves the stored value untouched.
    string? DeliveryMethod = null,
    string? TelegramInviteUrl = null,
    string? DeliveryInstructions = null,
    string? ContentOverridesJson = null);

public record AdminBillingAddOnCreateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
    int DurationDays,
    int GrantCredits,
    int DisplayOrder,
    bool IsRecurring,
    bool AppliesToAllPlans,
    bool IsStackable,
    int QuantityStep,
    int? MaxQuantity,
    string? Status = null,
    string? CompatiblePlanCodesJson = null,
    string? GrantEntitlementsJson = null,
    decimal? OriginalPriceGbp = null,
    string? AddonKind = null,
    bool? RequiresEligibleParent = null,
    string? EligibilityFlag = null,
    int? LettersGranted = null,
    int? SessionsGranted = null,
    string? AiPackageGroup = null,
    string? AiFeaturesJson = null);

public record AdminBillingAddOnUpdateRequest(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string Interval,
    int DurationDays,
    int GrantCredits,
    int DisplayOrder,
    bool IsRecurring,
    bool AppliesToAllPlans,
    bool IsStackable,
    int QuantityStep,
    int? MaxQuantity,
    string? Status = null,
    string? CompatiblePlanCodesJson = null,
    string? GrantEntitlementsJson = null,
    decimal? OriginalPriceGbp = null,
    string? AddonKind = null,
    bool? RequiresEligibleParent = null,
    string? EligibilityFlag = null,
    int? LettersGranted = null,
    int? SessionsGranted = null,
    string? AiPackageGroup = null,
    string? AiFeaturesJson = null);

/// <summary>A single learner-billing copy override (key → value, with optional section/description for the admin editor).</summary>
public record AdminBillingContentEntry(
    string Key,
    string Value,
    string? Section = null,
    string? Description = null);

/// <summary>Atomic upsert of learner-billing copy overrides. Unknown/malformed keys are rejected server-side.</summary>
public record AdminBillingContentReplaceRequest(
    IReadOnlyList<AdminBillingContentEntry> Entries);

public record AdminBillingCouponCreateRequest(
    string Code,
    string Name,
    string Description,
    string DiscountType,
    decimal DiscountValue,
    string Currency,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerUser,
    decimal? MinimumSubtotal,
    bool IsStackable,
    string? Status = null,
    string? ApplicablePlanCodesJson = null,
    string? ApplicableAddOnCodesJson = null,
    string? Notes = null);

public record AdminBillingCouponUpdateRequest(
    string Code,
    string Name,
    string Description,
    string DiscountType,
    decimal DiscountValue,
    string Currency,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerUser,
    decimal? MinimumSubtotal,
    bool IsStackable,
    string? Status = null,
    string? ApplicablePlanCodesJson = null,
    string? ApplicableAddOnCodesJson = null,
    string? Notes = null);

// ── Subscription Lifecycle (Admin manual actions) ──

/// <summary>
/// Request to manually change a learner's subscription plan from the admin console.
/// Switches the active subscription to <paramref name="PlanCode"/> (resolved by code or id).
/// When <paramref name="ResetRenewalDate"/> is true the renewal date is recomputed from the
/// target plan's duration; otherwise the existing renewal anchor is preserved. When
/// <paramref name="GrantIncludedCredits"/> is true any plan-included review credits are
/// granted as a wallet credit ledger entry attributed to this admin action.
/// </summary>
public record AdminSubscriptionChangePlanRequest(
    string PlanCode,
    bool ResetRenewalDate = true,
    bool GrantIncludedCredits = false,
    string? Reason = null);

/// <summary>
/// Request to extend (or shorten) a subscription's next renewal date.
/// Exactly one of <paramref name="AddDays"/>, <paramref name="AddMonths"/> or
/// <paramref name="NewRenewalAt"/> must be provided. Negative add values shorten the term.
/// </summary>
public record AdminSubscriptionExtendRequest(
    int? AddDays = null,
    int? AddMonths = null,
    DateTimeOffset? NewRenewalAt = null,
    string? Reason = null);

/// <summary>
/// Request to cancel a subscription. <paramref name="Immediate"/> = true marks the
/// subscription cancelled and ends entitlement now; otherwise cancellation is scheduled
/// to take effect at the existing renewal date (subscription stays active until then).
/// </summary>
public record AdminSubscriptionCancelRequest(
    bool Immediate = false,
    string? Reason = null);

/// <summary>
/// Request to reactivate a cancelled or expired subscription. Renewal is bumped to at
/// least one duration unit ahead of "now" so the learner is not immediately re-expired.
/// </summary>
public record AdminSubscriptionReactivateRequest(
    bool ResetRenewalDate = true,
    string? Reason = null);

/// <summary>
/// Request to set a subscription's status to an explicit value (administrative override
/// for trial/active/past_due/suspended/cancelled/expired/pending).
/// </summary>
public record AdminSubscriptionStatusRequest(
    string Status,
    string? Reason = null);

/// <summary>
/// Request to inspect-and-correct a subscription's OET 2026 entitlement counters and
/// unlock flags from the admin console. Each field is an <b>absolute SET</b>: a non-null
/// value overwrites the stored value (counters are clamped to &gt;= 0); a null value
/// leaves that field unchanged. <paramref name="Reason"/> is required and recorded with
/// a before/after snapshot in the audit log.
/// </summary>
public record AdminSubscriptionEntitlementAdjustRequest(
    int? WritingAssessmentsRemaining,
    int? SpeakingSessionsRemaining,
    int? AiCreditsRemaining,
    bool? TutorBookUnlocked,
    bool? BasicEnglishUnlocked,
    string Reason);

/// <summary>
/// Request to create a brand-new subscription for a learner who does not currently have
/// one. Useful for onboarding sponsored, comp'd, or migrated accounts.
/// </summary>
public record AdminSubscriptionCreateRequest(
    string UserId,
    string PlanCode,
    bool GrantIncludedCredits = false,
    string? Reason = null);

// ── Review Ops ──

public record AdminReviewAssignRequest(string ExpertId, string? Reason);

public record AdminReviewCancelRequest(string Reason);

public record AdminReviewReopenRequest(string? Reason);

// ── Bulk Actions ──

public record AdminBulkActionRequest(
    string Action,
    string[] ContentIds,
    bool DryRun = false);

// ── RBAC Permissions ──

public record AdminPermissionUpdateRequest(string[] Permissions);

public record CreatePermissionTemplateRequest(string Name, string? Description, string[] Permissions);

// ── Content Publishing Workflow ──

public record AdminPublishRequestPayload(string? Note);

public record AdminPublishReviewPayload(string? Note);

public record AdminEditorReviewPayload(string? Notes);

public record AdminEditorRejectPayload(string Reason);

public record AdminPublisherApprovePayload(string? Notes);

public record AdminPublisherRejectPayload(string Reason);

// ── Review Escalation ──

public record AdminEscalationAssignRequest(string SecondReviewerId);

public record AdminEscalationResolveRequest(int FinalScore, string? ResolutionNote);

// ── Phase 1 DTOs ─────────────────────────────────────

public record ScoreGuaranteeActivateRequest(int BaselineScore);

public record ScoreGuaranteeClaimRequest(int ActualScore, string? ProofDocumentUrl, string? Note);

public record AdminScoreGuaranteeReviewRequest(string Decision, string? Note);
// Decision: approved | rejected

public record StudyCommitmentRequest(int DailyMinutes);

public record ExpertAnnotationTemplateRequest(string SubtestCode, string CriterionCode, string Label, string TemplateText, bool IsShared);

// ── Phase 2 DTOs ─────────────────────────────────────

// L3 Profession Learning Paths
public record LearningPathRequest(string ProfessionId, string ExamTypeCode);

// L5 Weak-Area Remediation
public record RemediationStartRequest(string SubtestCode, string? CriterionCode);

// A4 Content Quality Scoring
public record ContentQualityScoreRequest(string ContentId);

// A6 Bulk Learner Operations
public record AdminBulkCreditRequest(string[] UserIds, int CreditAmount, string Reason);
public record AdminBulkNotificationRequest(string[] UserIds, string Title, string Message, string? Category);
public record AdminBulkStatusRequest(string[] UserIds, string NewStatus, string Reason);

// B3 Enterprise/Sponsor Channel
public record SponsorCreateRequest(string Name, string Type, string ContactEmail, string? OrganizationName);
public record SponsorUpdateRequest(string? Name, string? ContactEmail, string? OrganizationName, string? Status);
public record CohortCreateRequest(string SponsorId, string Name, string ExamTypeCode, DateOnly? StartDate, DateOnly? EndDate, int MaxSeats);
public record CohortUpdateRequest(string? Name, DateOnly? StartDate, DateOnly? EndDate, int? MaxSeats, string? Status);
public record CohortMemberAddRequest(string LearnerId);
public record SponsorLearnerLinkRequest(string SponsorId, string LearnerId);

// ── Grammar Admin ──

public record AdminGrammarLessonCreateRequest(
    string Title,
    string? ProfessionId,
    string? Category,
    string? Description,
    string? Content,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    int? SortOrder);

public record AdminGrammarLessonUpdateRequest(
    string? Title,
    string? ProfessionId,
    string? Category,
    string? Description,
    string? Content,
    string? Difficulty,
    int? EstimatedDurationMinutes,
    int? SortOrder,
    string? Status);

public record AdminGrammarAiDraftRequest(
    string Prompt,
    string? ExamTypeCode,
    string? TopicSlug,
    string? Level,
    int? TargetExerciseCount,
    string? Profession);

public record AdminWritingAiDraftRequest(
    string Prompt,
    string? Profession,
    string? LetterType,
    string? RecipientSpecialty,
    string? Difficulty,
    int? TargetCaseNoteCount);

public record AdminWritingOptionsUpdateRequest(
    bool AiGradingEnabled,
    bool AiCoachEnabled,
    string? KillSwitchReason,
    bool FreeTierEnabled,
    int FreeTierLimit,
    int FreeTierWindowDays);

// ── Vocabulary Admin ──

public record AdminVocabularyItemCreateRequest(
    string Term,
    string Definition,
    string? ProfessionId,
    string? Category,
    string? Pronunciation,
    string? ExampleSentence);

public record AdminVocabularyItemUpdateRequest(
    string? Term,
    string? Definition,
    string? ProfessionId,
    string? Category,
    string? Pronunciation,
    string? ExampleSentence,
    string? Status);

// ── Conversation Template Admin ──

public record AdminConversationTemplateCreateRequest(
    string Title,
    string? TaskTypeCode,
    string? ProfessionId,
    string Scenario,
    string? RoleDescription,
    string? PatientContext,
    string? ExpectedOutcomes,
    string? Difficulty,
    int? EstimatedDurationSeconds,
    string[]? Objectives,
    string[]? ExpectedRedFlags,
    string[]? KeyVocabulary,
    Dictionary<string, object?>? PatientVoice);

public record AdminConversationTemplateUpdateRequest(
    string? Title,
    string? TaskTypeCode,
    string? ProfessionId,
    string? Scenario,
    string? RoleDescription,
    string? PatientContext,
    string? ExpectedOutcomes,
    string? Difficulty,
    int? EstimatedDurationSeconds,
    string[]? Objectives,
    string[]? ExpectedRedFlags,
    string[]? KeyVocabulary,
    Dictionary<string, object?>? PatientVoice,
    string? Status);

// ── Pronunciation Drill Admin ──

public record AdminPronunciationDrillCreateRequest(
    string Word,
    string? PhoneticTranscription,
    string? AudioUrl,
    string? ProfessionId,
    string? Difficulty,
    string? Category,
    string? Profession = null,
    string? Focus = null,
    string? PrimaryRuleId = null,
    string? AudioModelAssetId = null,
    string? ExampleWordsJson = null,
    string? MinimalPairsJson = null,
    string? SentencesJson = null,
    string? TipsHtml = null,
    string? Status = null,
    int? OrderIndex = null);

public record AdminPronunciationDrillUpdateRequest(
    string? Word,
    string? PhoneticTranscription,
    string? AudioUrl,
    string? ProfessionId,
    string? Difficulty,
    string? Category,
    string? Status,
    string? Profession = null,
    string? Focus = null,
    string? PrimaryRuleId = null,
    string? AudioModelAssetId = null,
    string? ExampleWordsJson = null,
    string? MinimalPairsJson = null,
    string? SentencesJson = null,
    string? TipsHtml = null,
    int? OrderIndex = null);

public record AdminPronunciationDrillAiDraftRequest(
    string? Phoneme,
    string? Focus,
    string? Profession,
    string? Difficulty,
    string? Prompt,
    string? PrimaryRuleId);

// ── Notification Template Admin ──

public record AdminNotificationTemplateCreateRequest(
    string EventKey,
    string Channel,
    string? Category,
    string SubjectTemplate,
    string BodyTemplate,
    bool IsActive);

public record AdminNotificationTemplateUpdateRequest(
    string? SubjectTemplate,
    string? BodyTemplate,
    bool? IsActive,
    string? Category);

/// <summary>Status toggle from the admin templates table. Status is "active" or "inactive".</summary>
public record AdminNotificationTemplateStatusRequest(string Status);

// ── Free Tier ──

public record AdminFreeTierConfigUpdateRequest(
    bool Enabled,
    int MaxWritingAttempts,
    int MaxSpeakingAttempts,
    int MaxReadingAttempts,
    int MaxListeningAttempts,
    int TrialDurationDays,
    bool ShowUpgradePrompts,
    int? MaxSpeakingMockSets = null);

// -- Conversation Runtime Settings Admin --

public record AdminConversationSettingsRequest(
    bool? Enabled,
    string? AsrProvider,
    string? TtsProvider,
    string? AzureSpeechKey,
    string? AzureSpeechRegion,
    string? AzureLocale,
    string? WhisperBaseUrl,
    string? WhisperApiKey,
    string? WhisperModel,
    string? DeepgramApiKey,
    string? DeepgramModel,
    string? DeepgramLanguage,
    bool? RealtimeSttEnabled,
    string? RealtimeAsrProvider,
    bool? RealtimeSttAllowRealProvider,
    bool? RealtimeSttRealProviderProductionAuthorized,
    bool? RealtimeSttFallbackToBatch,
    int? RealtimeSttProviderConnectTimeoutSeconds,
    int? RealtimeSttMaxChunkBytes,
    int? RealtimeSttPartialMinIntervalMs,
    int? RealtimeSttTurnIdleTimeoutSeconds,
    int? RealtimeSttMaxConcurrentStreamsPerUser,
    int? RealtimeSttMaxAudioSecondsPerSession,
    int? RealtimeSttDailyAudioSecondsPerUser,
    decimal? RealtimeSttMonthlyBudgetCapUsd,
    decimal? RealtimeSttEstimatedCostUsdPerMinute,
    string? RealtimeSttProviderSessionTopology,
    string? RealtimeSttRegionId,
    bool? RealtimeSttAssumeLearnersAdult,
    bool? RealtimeSttAllowManagedLearnerRealProvider,
    string? RealtimeSttConsentVersion,
    string? RealtimeSttRollbackMode,
    string[]? RealtimeSttAllowedMimeTypes,
    string? ElevenLabsSttApiKey,
    string? ElevenLabsSttBaseUrl,
    string? ElevenLabsSttModel,
    string? ElevenLabsSttLanguage,
    string? ElevenLabsSttAudioFormat,
    string? ElevenLabsSttCommitStrategy,
    string? ElevenLabsSttKeytermsCsv,
    bool? ElevenLabsSttEnableProviderLogging,
    int? ElevenLabsSttTokenTtlSeconds,
    string? ElevenLabsApiKey,
    string? ElevenLabsTtsBaseUrl,
    string? ElevenLabsDefaultVoiceId,
    string? ElevenLabsModel,
    string? ElevenLabsOutputFormat,
    string? ElevenLabsPronunciationDictionaryId,
    string? ElevenLabsPronunciationDictionaryVersionId,
    double? ElevenLabsStability,
    double? ElevenLabsSimilarityBoost,
    double? ElevenLabsStyle,
    bool? ElevenLabsUseSpeakerBoost,
    long? MaxAudioBytes,
    int? AudioRetentionDays,
    int? PrepDurationSeconds,
    int? MaxSessionDurationSeconds,
    int? MaxTurnDurationSeconds,
    string[]? EnabledTaskTypes,
    int? FreeTierSessionsLimit,
    int? FreeTierWindowDays,
    string? ReplyModel,
    string? EvaluationModel,
    double? ReplyTemperature,
    double? EvaluationTemperature);

public record AdminConversationTtsPreviewRequest(
    string? Text,
    string? Voice,
    string? Locale,
    // ModelVariant overrides the ElevenLabs model id for this single preview;
    // Instructions is reserved/unused for ElevenLabs.
    string? ModelVariant,
    string? Instructions);
