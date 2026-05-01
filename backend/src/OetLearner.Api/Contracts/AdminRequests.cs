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
    string? PromptLabel);

public record AdminAIConfigUpdateRequest(
    string? Model,
    string? Provider,
    string? TaskType,
    string? Status,
    double? Accuracy,
    double? ConfidenceThreshold,
    string? RoutingRule,
    string? ExperimentFlag,
    string? PromptLabel);

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

public record AdminUserLifecycleRequest(string? Reason);

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
    string? EntitlementsJson = null);

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
    string? EntitlementsJson = null);

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
    string? GrantEntitlementsJson = null);

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
    string? GrantEntitlementsJson = null);

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

// ── Vocabulary Admin ──

public record AdminVocabularyItemCreateRequest(
    string Term,
    string Definition,
    string? ProfessionId,
    string? Category,
    string? Pronunciation,
    string? ExampleSentence,
    string? Difficulty);

public record AdminVocabularyItemUpdateRequest(
    string? Term,
    string? Definition,
    string? ProfessionId,
    string? Category,
    string? Pronunciation,
    string? ExampleSentence,
    string? Difficulty,
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
    string? AzureTtsDefaultVoice,
    string? WhisperBaseUrl,
    string? WhisperApiKey,
    string? WhisperModel,
    string? DeepgramApiKey,
    string? DeepgramModel,
    string? DeepgramLanguage,
    string? ElevenLabsApiKey,
    string? ElevenLabsDefaultVoiceId,
    string? ElevenLabsModel,
    string? CosyVoiceBaseUrl,
    string? CosyVoiceApiKey,
    string? CosyVoiceDefaultVoice,
    string? ChatTtsBaseUrl,
    string? ChatTtsApiKey,
    string? ChatTtsDefaultVoice,
    string? GptSoVitsBaseUrl,
    string? GptSoVitsApiKey,
    string? GptSoVitsDefaultVoice,
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
    string? Locale);
