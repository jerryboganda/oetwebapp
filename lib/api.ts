import { ensureFreshAccessToken } from './auth-client';
import {
  ApiError,
  API_BASE_URL,
  apiBlobRequest,
  apiRequest,
  asArray,
  asRecord,
  getHeaders,
  isApiError,
  isRetryable,
  maybe,
  normalizeBillingCode,
  resolveApiUploadUrl,
  resolveApiUrl,
  resolveBrowserApiResourceUrl,
  toNullableString,
  toStringArray,
  type ApiRecord,
} from './api/client';
export { ApiError, isApiError } from './api/client';
import { fetchLearnerReviewResult, fetchLearnerReviewVoiceNotes } from './api/expert';
import { mapMockBooking, normalizeMockDeliveryMode } from './api/mock-bookings';
import { uploadMedia } from './api/content-discovery';
import {
  titleCase as domainTitleCase,
  minutesToLabel as domainMinutesToLabel,
  scoreRangeDisplay as domainScoreRangeDisplay,
  formatCurrency as domainFormatCurrency,
  normalizeWaveformPeaks as domainNormalizeWaveformPeaks,
  parseCriterionScore as domainParseCriterionScore,
  scoreToGrade as domainScoreToGrade,
  toExamFamilyCode,
} from './domain/format';
import { fetchWithTimeout } from './network/fetch-with-timeout';
import type { CurrentUser } from './types/auth';
import type {
  ExamFamilyCode,
  UserProfile,
  StudyPlanTask,
  WritingTask,
  WritingResult,
  WritingSubmission,
  CriteriaDelta,
  ModelAnswer,
  SpeakingTask,
  RoleCard,
  SpeakingResult,
  PhrasingSegment,
  ReadingTask,
  ReadingResult,
  ListeningTask,
  ListeningResult,
  ListeningDrill,
  ListeningReview,
  MockConfig,
  MockOptions,
  MockReport,
  MockSession,
  MockBooking,
  MockSpeakingContent,
  MockDiagnosticEntitlement,
  DiagnosticRecommendedLevel,
  DiagnosticRecommendedPlan,
  DiagnosticStudyPathStep,
  ReadinessData,
  ReadinessBlocker,
  ReadinessHistoryPoint,
  ReadinessForecast,
  SubTestReadiness,
  ProgressEvidenceSummary,
  TrendPoint,
  Submission,
  SubmissionComparison,
  SubmissionDetail,
  TurnaroundOption,
  FocusArea,
  DiagnosticSession,
  DiagnosticResult,
  CriterionFeedback,
  AnchoredComment,
  Confidence,
  SubTest,
  SettingsSectionData,
  SettingsSectionId,
  SpeakingTranscriptReview,
  MockTypeToken,
  MockDeliveryMode,
  MockStrictness,
  EvalStatus,
} from './mock-data';
import {
  WRITING_CRITERION_MAX_SCORES,
  type WritingCriterionCode,
} from './scoring';
import type {
  BillingData,
  BillingChangePreview,
  BillingQuote,
  BillingProductType,
  BillingPaymentStatus,
  Invoice,
  AiPackage,
  AiPackageCreditSnapshot,
  AiPackagesResponse,
} from './billing-types';
export type {
  BillingData,
  BillingChangePreview,
  BillingQuote,
  BillingProductType,
  BillingPaymentStatus,
  Invoice,
  AiPackage,
  AiPackageCreditSnapshot,
  AiPackagesResponse,
};
import { mapAiPackageCreditSnapshot } from './map-ai-package-credit-snapshot';
import type {
  CalibrationCaseDetail,
  CalibrationCase,
  CalibrationNote,
  ExpertDashboardData,
  ExpertMe,
  ExpertMetrics,
  ExpertLearnerDirectoryResponse,
  ExpertLearnerReviewContext,
  ExpertQueueFilterMetadata,
  ExpertReviewHistory,
  ExpertSchedule,
  LearnerProfileExpanded,
  ReviewDraft,
  ReviewQueueResponse,
  ScheduleException,
  SpeakingReviewDetail,
  ReviewVoiceNote,
  WritingPaperAsset,
  WritingReviewDetail,
  ExpertOnboardingProfile,
  ExpertOnboardingQualifications,
  ExpertOnboardingRates,
  ExpertOnboardingStatus,
} from './types/expert';
import type {
  AiGroundingContext,
  LintFinding,
  Rulebook,
  SpeakingAuditInput,
  WritingLintInput,
} from './rulebook';
import type { WeaknessDataPoint } from './writing-analytics/types';

type ApiClientInit = Omit<RequestInit, 'body' | 'method'>;
type ApiClientBody = unknown;

function isRequestBody(value: unknown): value is BodyInit {
  return (
    typeof value === 'string'
    || (typeof FormData !== 'undefined' && value instanceof FormData)
    || (typeof URLSearchParams !== 'undefined' && value instanceof URLSearchParams)
    || (typeof Blob !== 'undefined' && value instanceof Blob)
    || (typeof ArrayBuffer !== 'undefined' && value instanceof ArrayBuffer)
    || (typeof ReadableStream !== 'undefined' && value instanceof ReadableStream)
  );
}

function toRequestBody(body: ApiClientBody): { body?: BodyInit; json: boolean } {
  if (body === undefined) {
    return { json: true };
  }

  if (isRequestBody(body)) {
    return {
      body,
      json: !(typeof FormData !== 'undefined' && body instanceof FormData),
    };
  }

  return {
    body: JSON.stringify(body),
    json: true,
  };
}

/**
 * Central API client for application code.
 *
 * All backend calls from app/components/hooks/lib code should use this client
 * (or the typed helpers in this file) so retries, auth headers, CSRF, timeout
 * handling and normalized `ApiError` behavior stay consistent.
 */
export const apiClient = {
  request: apiRequest,
  get<T = any>(path: string, init?: ApiClientInit): Promise<T> {
    return apiRequest<T>(path, { ...init, method: 'GET' });
  },
  post<T = any>(path: string, body?: ApiClientBody, init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(path, { ...init, method: 'POST', body: payload.body }, { json: payload.json });
  },
  postWithAcceptedStatuses<T = any>(path: string, body: ApiClientBody, acceptedStatuses: number[], init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(
      path,
      { ...init, method: 'POST', body: payload.body },
      { json: payload.json, acceptedStatuses },
    );
  },
  put<T = any>(path: string, body?: ApiClientBody, init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(path, { ...init, method: 'PUT', body: payload.body }, { json: payload.json });
  },
  patch<T = any>(path: string, body?: ApiClientBody, init?: ApiClientInit): Promise<T> {
    const payload = toRequestBody(body);
    return apiRequest<T>(path, { ...init, method: 'PATCH', body: payload.body }, { json: payload.json });
  },
  delete<T = any>(path: string, init?: ApiClientInit): Promise<T> {
    return apiRequest<T>(path, { ...init, method: 'DELETE' });
  },
  postForm<T = any>(path: string, body: FormData, init?: ApiClientInit): Promise<T> {
    return apiRequest<T>(path, { ...init, method: 'POST', body }, { json: false });
  },
};

async function uploadBinary(pathOrUrl: string, blob: Blob): Promise<void> {
  const response = await fetchWithTimeout(resolveApiUploadUrl(pathOrUrl), {
    method: 'PUT',
    headers: await getHeaders(pathOrUrl, { 'Content-Type': blob.type || 'audio/webm' }, { json: false }),
    body: blob,
  }, 90_000);

  if (!response.ok) {
    let message = `Upload failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error('[API] uploadBinary: failed to parse error response:', err);
    }
    throw new Error(message);
  }
}

// ═══════════════════ RULEBOOK / GROUNDED AI API ═══════════════════



// ═════════ EXTRACTED MODULES — re-export facade ═════════
export { fetchAuthorizedObjectUrl, fetchAuthorizedBlob } from './api/binary';
// Rulebook / grounded AI
export {
  fetchWritingRulebook, fetchSpeakingRulebook, fetchRulebookRule, fetchRulebookAssessment,
  fetchWritingWeaknesses, fetchExpertAssignedReviews, fetchWritingDualAssessment,
  lintWritingViaApi, auditSpeakingViaApi, completeGroundedAi,
  type WritingWeaknessTagRow, type WritingWeaknessCriterionRow, type WritingWeaknessTrendBucket,
  type WritingGradeTrendPoint, type WritingPurposeTrendPoint, type WritingWeaknessSummary,
  type ExpertAssignedItem, type WritingDualCriterionCode, type WritingCriterionScore,
  type WritingAiTrack, type WritingTutorTrack, type WritingDualDivergence, type WritingDualAssessment,
} from './api/rulebook-api';
// Attempt cache
export {
  ensureWritingAttempt,
  type WritingAttemptSession, type WritingAttemptMode,
} from './api/writing-attempts';
// Learner profile / onboarding
export {
  fetchUserProfile, fetchMockSpeakingAccess, fetchOnboardingState, startOnboarding,
  completeOnboarding, fetchTourState, markTour, fetchDiagnosticOverview,
  type OnboardingTourState, type DiagnosticOverviewResponse,
} from './api/learner-profile';
// Dashboard home + wallet + PayPal
export {
  fetchDashboardHome, fetchEngagement, fetchWalletTransactions, createWalletTopUp, fetchWalletTopUpTiers,
  fetchAvailablePaymentGateways, fetchPayPalClientConfig, captureBillingCheckout,
  safePaymentRedirect,
  type DashboardHomeResponse, type EngagementResponse, type WalletTransactionsResponse, type WalletTopUpResponse,
  type WalletTopUpTier, type WalletTopUpTiersResponse, type PaymentMethodMode,
  type PaymentMethodOption, type AvailablePaymentGatewaysResponse, type PayPalClientConfig,
  type PaymentCaptureResult,
} from './api/billing-checkout';
// Settings + sessions
export {
  fetchExamFamilies, fetchSettingsData, fetchSettingsSection, updateSettingsSection,
  updateMyAvatar, fetchActiveSessions, fetchTrustedDevice, revokeSession,
  revokeAllOtherSessions,
  type ExamFamiliesResponse, type SettingsDataResponse, type UpdateSettingsSectionResponse,
  type ActiveSession, type TrustedDeviceSelf,
} from './api/settings-sessions';
// Learner homes + profile updates
export {
  fetchReadingHome, fetchListeningHome, fetchWritingHome, fetchWritingWeaknessData,
  fetchSpeakingHome, fetchMocksHome, postSpeakingDeviceCheck, setActiveProfession,
  updateUserProfile,
  type ReadingHomeResponse, type ListeningHomeResponse, type WritingHomeResponse,
  type WritingWeaknessAnalyticsResponse, type MocksHomeResponse, type SpeakingDeviceCheckResponse,
  type SpeakingHome, type SpeakingHomeAction, type SpeakingHomeDrillGroup,
  type SpeakingHomeAttempt, type SpeakingHomeReviewCredits,
} from './api/learner-home';
// Study plan
export {
  fetchStudyPlan, updateStudyPlanTask, fetchStudyPlanSwapCandidates, applyStudyPlanSwap,
  type StudyPlanTaskUpdate, type StudyPlanSwapCandidate,
} from './api/study-plan';
// Writing practice
export {
  fetchWritingTask, submitWritingTask, attachWritingPaperAssets, fetchWritingPaperAssets,
  fetchWritingEntitlement, fetchWritingResult, fetchWritingSubmissions, fetchCriteriaDeltas,
  fetchModelAnswer, mapCriterionFeedback,
  type WritingExamMode, type WritingAssessorType, type WritingSubmitOptions, type WritingEntitlement,
} from './api/writing-practice';
// Speaking results
export {
  fetchSpeakingTasks, fetchSpeakingMockSets, startSpeakingMockSet, fetchSpeakingMockSession,
  startSpeakingMockBridge, finishSpeakingMockBridge, fetchSpeakingCompliance, fetchRoleCard,
  fetchSpeakingResult, fetchTranscript, fetchPhrasingData, submitSpeakingRecording,
  type SpeakingMockSetSummary, type SpeakingMockSetEntitlement, type SpeakingMockSessionRolePlay,
  type SpeakingMockSession, type SpeakingComplianceCopy,
} from './api/speaking-results';
// Reading/listening tasks
export {
  fetchReadingTask, submitReadingAnswers, fetchReadingResult, fetchListeningTask,
  submitListeningAnswers, fetchListeningResult, fetchListeningDrill, fetchListeningReview,
} from './api/subtest-tasks';
// Mock attempts
export {
  fetchMockReports, fetchMockReport, downloadMockWritingPdf, downloadSpeakingEvaluationPdf,
  fetchMockOptions, createMockSession, fetchMockSession, submitMockSession,
  startMockSection, completeMockSection, cancelMockSession, recordMockProctoringEvents,
  fetchMockBookings, fetchMockBookingDetail, createMockBooking, fetchMockAvailability,
  createMockBookingV2, rescheduleMockBookingV2, cancelMockBookingV2, updateMockBooking,
  reportMockLeak, fetchMockDiagnosticStudyPath, parseDiagnosticRecommendedPlan,
  fetchMockDiagnosticEntitlement, fetchMockEntitlementsSummary, fetchMockBookingList,
  rescheduleMockBooking, cancelMockBooking, appendMockBookingRecordingChunk,
  finalizeMockBookingRecording, fetchRemediationPlan, generateRemediationPlan,
  completeRemediationTask, fetchMockReadinessTrend, MOCK_PROCTORING_KINDS,
  type MockProctoringKind, type MockProctoringSeverity, type MockProctoringEventInput,
  type MockProctoringBatchResult, type MockAvailabilitySlot, type MockEntitlementSummaryItem,
  type MockEntitlementSummary, type MockBookingListResponse, type MockBookingChunkAck,
  type MockBookingRecordingFinalizeResult, type RemediationTask, type MockReadinessTrend,
} from './api/mock-attempts';
// Readiness
export {
  fetchReadiness, fetchReadinessHistory, fetchReadinessBlockers, fetchReadinessForecast,
  refreshReadiness, fetchAdminReadinessLearners, fetchAdminReadinessLearner,
  recomputeAdminReadiness, fetchAdminReadinessMetrics, fetchTrendData,
  fetchCompletionData, fetchSubmissionVolume, fetchProgressEvidenceSummary,
  type AdminReadinessLearnerRow, type AdminReadinessLearnerList, type AdminReadinessMetrics,
} from './api/readiness';
// Submissions
export { fetchSubmissions, fetchSubmissionDetail, fetchSubmissionComparison } from './api/submissions';
// Billing core
export {
  fetchPublicPlans, fetchBilling, fetchBillingQuote, purchaseReviewCredits,
  fetchBillingChangePreview, createBillingCheckoutSession, fetchBillingPaymentStatus,
  fetchAiPackages, fetchMyAiPackageCredits, fetchMyAttemptHistory, fetchAdminUserAiCredits,
  adjustAdminUserAiCredits, adjustAdminAiPackageCredits, downloadInvoice,
  pauseSubscription, resumeSubscription, fetchMyBankAccounts, fetchTurnaroundOptions,
  fetchFocusAreas, submitReviewRequest,
  type PublicBillingPlan, type LearnerAttemptHistoryItem,
  type AiPackageCreditAdjustmentPayload, type BankAccountConfigDto,
} from './api/billing-core';
// Diagnostic
export { fetchDiagnosticSession, startDiagnostic, fetchDiagnosticTaskId, fetchDiagnosticResults } from './api/diagnostic';
// Expert console
export { fetchExpertMe, fetchExpertDashboard } from './api/expert-console';
// Admin billing coupons
export {
  fetchAdminBillingCoupons, fetchAdminBillingCouponVersions, createAdminBillingCoupon,
  updateAdminBillingCoupon, fetchAdminBillingSubscriptions,
} from './api/admin-billing-coupons';
// Scoring policy + rulebook PDFs
export {
  adminGetScoringPolicy, adminUpdateScoringPolicy, adminListScoringPolicyHistory,
  adminActivateScoringPolicy, learnerGetScoringPolicy, adminUploadRulebookReferencePdf,
  adminDeleteRulebookReferencePdf, learnerGetRulebookReferencePdf,
  type ScoringPolicyDto, type ScoringPolicyLearnerDto, type RulebookReferencePdfDto,
} from './api/scoring-policy';
// Recall set tags
export {
  adminListRecallSetTags, adminGetRecallSetTag, adminCreateRecallSetTag,
  adminUpdateRecallSetTag, adminArchiveRecallSetTag, adminUnarchiveRecallSetTag,
  adminDeleteRecallSetTag, type RecallSetTagDto,
} from './api/recall-tags';
// Result templates
export {
  adminListResultTemplates, adminUploadResultTemplate, adminUpdateResultTemplate,
  adminActivateResultTemplate, adminDeactivateResultTemplate, adminDeleteResultTemplate,
  adminForceDeleteResultTemplate, learnerGetActiveResultTemplate,
  type ResultTemplateDto, type LearnerResultTemplateDto,
} from './api/result-templates';
// Speaking shared resources
export {
  adminListSpeakingSharedResources, adminUploadSpeakingSharedResource,
  adminPublishSpeakingSharedResource, adminArchiveSpeakingSharedResource,
  adminDeleteSpeakingSharedResource, adminForceDeleteSpeakingSharedResource,
  learnerListSpeakingSharedResources, downloadSpeakingSharedResourceMedia,
  type SpeakingSharedResourceDto, type SpeakingSharedResourceLearnerDto,
  type SpeakingSharedResourceKind,
} from './api/speaking-shared-resources';
// Content staging + media downloads
export {
  adminStageRealContentFolder, adminCommitRealContentFolder, downloadRulebookReferencePdfMedia,
  downloadMediaAssetContent,
  type RealContentProposalDto, type RealContentStageResultDto, type RealContentCommitResultDto,
} from './api/content-staging';
// Pronunciation admin
export { uploadElevenLabsPronunciationDictionary } from './api/pronunciation-admin';
// Checkout status
export { fetchCheckoutSessionStatus, type CheckoutSessionStatus, type CheckoutSessionStatusItem } from './api/checkout-status';


// ── Subscription lifecycle (admin manual actions) ──
export {
  activateAdminAIConfig,
  activateAdminFlag,
  adminApproveSubscriptionFreeze,
  adminCancelSubscription,
  adminChangeSubscriptionPlan,
  adminCreateSubscription,
  adminExtendSubscription,
  adminFreezeSubscription,
  adminReactivateSubscription,
  adminRejectSubscriptionFreeze,
  adminResumeSubscription,
  adminSetSubscriptionStatus,
  approveAdminFreeze,
  assignAdminReview,
  bulkAdminContentAction,
  cancelAdminReview,
  cancelFreeze,
  confirmFreeze,
  createAdminManualFreeze,
  deactivateAdminFlag,
  deleteAdminAIConfig,
  endAdminFreeze,
  fetchAdminAuditLogDetail,
  fetchAdminBillingCouponRedemptions,
  fetchAdminBillingEntitlementDiagnostics,
  fetchAdminBillingInvoiceEvidence,
  fetchAdminBillingInvoices,
  fetchAdminBillingPaymentTransactions,
  fetchAdminBillingProviderLifecycleSignals,
  fetchAdminCohortAnalysis,
  fetchAdminContentEffectiveness,
  fetchAdminContentImpact,
  fetchAdminExpertEfficiency,
  fetchAdminFreezeOverview,
  fetchAdminQualityAnalytics,
  fetchAdminReviewFailures,
  fetchAdminReviewOpsQueue,
  fetchAdminReviewOpsSummary,
  fetchAdminSubscriptionHealth,
  fetchAdminTaxonomyImpact,
  fetchFreezeStatus,
  forceEndAdminFreeze,
  reopenAdminReview,
  rejectAdminFreeze,
  requestFreeze,
  updateAdminFreezePolicy,
} from './api/admin-operations';

// ── Gamification ─────────────────────────────────────────────────────────────
export type { LearnerFeatureFlag } from './api/gamification';
export {
  fetchAchievements,
  fetchLeaderboard,
  fetchLearnerFeatureFlag,
  fetchMyLeaderboardPosition,
  fetchStreak,
  fetchXP,
  recordActivity,
  setLeaderboardOptIn,
} from './api/gamification';

// ── Spaced Repetition ─────────────────────────────────────────────────────────
export {
  createReviewItem,
  deleteReviewItem,
  fetchDueReviewItems,
  fetchReviewSummary,
  submitReview,
} from './api/spaced-repetition';

// ── Recalls (unified vocabulary + spaced-repetition) ─────────────────────────
// See docs/RECALLS-MODULE-PLAN.md.
export type {
  RecallsBulkUploadResult,
  RecallsBulkUploadRow,
  RecallsLibraryItem,
  RecallsQueueItem,
  RecallsRevisionPlanResponse,
  RecallsSpellingCheckResponse,
  RecallsSpellingMistakeItem,
  RecallsSpellingMistakesResponse,
  RecallsSpellingSetItem,
  RecallsSpellingSetResponse,
  RecallsSpellingTestSize,
  RecallsSpellingTestSource,
  RecallsStarReason,
  RecallsTodayResponse,
  RecallsWeeklyReport,
} from './api/recalls';
export {
  adminBulkUploadRecalls,
  checkRecallSpelling,
  clearRecallsAudioCache,
  fetchRecallsAudio,
  fetchRecallsLibrary,
  fetchRecallsQueue,
  fetchRecallsRevisionPlan,
  fetchRecallSpellingMistakes,
  fetchRecallSpellingSet,
  fetchRecallsToday,
  fetchRecallsWeeklyReport,
  starRecall,
} from './api/recalls';

// ── Vocabulary ────────────────────────────────────────────────────────────────
export type {
  MyVocabularyPageRequest,
  RecallSetSummary,
  RecallSetsResponse,
} from './api/vocabulary';
export {
  addToMyVocabulary,
  fetchDueFlashcards,
  fetchMyVocabulary,
  fetchVocabQuiz,
  fetchVocabularyCategories,
  fetchVocabularyDailySet,
  fetchVocabularyQuizHistory,
  fetchVocabularyRecallSets,
  fetchVocabularyStats,
  fetchVocabularyTerm,
  fetchVocabularyTerms,
  lookupVocabularyTerm,
  removeFromMyVocabulary,
  submitFlashcardReview,
  submitVocabQuiz,
} from './api/vocabulary';

// ── Content Hierarchy: Program Browser (Phase 8) ──
// ── Content Browser (access-aware) ──
export {
  fetchContentAccess,
  fetchContentBrowser,
  fetchContentPackage,
  fetchContentPackages,
  fetchContentProgram,
  fetchContentPrograms,
  fetchContentTracks,
  fetchFoundationResources,
  fetchFreePreviewAssets,
  fetchProgramsBrowser,
} from './api/content-browser';

// ── Phase 6: Readiness & Skill-based Content ──
// ── Phase 9: Search & Recommendations ──
// ── Phase 11: Media Access ──
// ── Media Management ──
export type { UploadedMediaAsset } from './api/content-discovery';
export {
  deleteMedia,
  fetchContentBySkill,
  fetchMediaItem,
  fetchMyMedia,
  fetchReadinessScore,
  fetchRecommendations,
  fetchSearchFacets,
  fetchSignedMediaUrl,
  searchContent,
  uploadMedia,
} from './api/content-discovery';
// uploadMedia is also used by voice-note submitters below in this file.

// ── Admin: Content Hierarchy Management ──
export {
  adminAssembleMockExam,
  adminBulkImportContent,
  adminDedupScan,
  adminDesignateCanonical,
  adminGenerateDiagnostic,
  adminProcessMediaAsset,
  adminUpdateContentEligibility,
  createAdminLesson,
  createAdminModule,
  createAdminPackage,
  createAdminProgram,
  createAdminTrack,
  fetchAdminContentInventory,
  fetchAdminContentPackages,
  fetchAdminContentPrograms,
  fetchAdminDedupGroups,
  fetchAdminLessons,
  fetchAdminMediaAssets,
  fetchAdminMediaAudit,
  fetchAdminModules,
  fetchAdminPackage,
  fetchAdminProgram,
  fetchAdminTracks,
  updateAdminLesson,
  updateAdminModule,
  updateAdminPackage,
  updateAdminProgram,
  updateAdminTrack,
} from './api/admin-content';

// ── Admin: Vocabulary Management ──────────────────────────────────────

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - admin CRUD for speaking
// mock sets. Permissions reuse AdminContent* on the backend.
export type { AdminSpeakingMockSetRow } from './api/speaking-mock-sets';
export {
  archiveAdminSpeakingMockSet,
  createAdminSpeakingMockSet,
  fetchAdminSpeakingContentOptions,
  fetchAdminSpeakingMockSet,
  fetchAdminSpeakingMockSets,
  forceDeleteAdminSpeakingMockSet,
  publishAdminSpeakingMockSet,
  updateAdminSpeakingMockSet,
} from './api/speaking-mock-sets';

// ── Wave 4 of docs/SPEAKING-MODULE-PLAN.md - tutor calibration drift +
// inline transcript comments. Three audiences:
//   • Admin: CRUD over calibration samples, drift report.
//   • Expert/tutor: list samples, submit rubric, post inline comments.
//   • Learner: read inline comments on their attempt.
export type {
  AdminSpeakingCalibrationDriftReport,
  AdminSpeakingCalibrationDriftRow,
  AdminSpeakingCalibrationSampleRow,
  SpeakingCriterionRubric,
  TutorCalibrationSubmissionResult,
  TutorSpeakingCalibrationSampleRow,
} from './api/speaking-calibration';
export {
  archiveAdminSpeakingCalibrationSample,
  createAdminSpeakingCalibrationSample,
  fetchAdminSpeakingCalibrationDrift,
  fetchAdminSpeakingCalibrationSamples,
  fetchTutorSpeakingCalibrationSamples,
  publishAdminSpeakingCalibrationSample,
  submitTutorSpeakingCalibrationScores,
} from './api/speaking-calibration';

// ── Inline transcript comments ──
export type {
  SpeakingDrillRow,
  SpeakingDrillsListResponse,
  SpeakingSelfPracticeStartResult,
  SpeakingTranscriptComment,
} from './api/speaking-practice';
export {
  fetchSpeakingDrills,
  fetchSpeakingTranscriptComments,
  postExpertSpeakingTranscriptComment,
  startSpeakingSelfPracticeSession,
} from './api/speaking-practice';

// ── Admin: Vocabulary Management ──
export type {
  AdminRecallSetSummary,
  AdminVocabularyAudioGenerateResponse,
  AdminVocabularyAudioProgress,
  AdminVocabularyBulkActivateResponse,
  AdminVocabularyBulkArchiveResponse,
  AdminVocabularyBulkDeleteResponse,
  AdminVocabularyBulkDraftResponse,
  AdminVocabularyBulkPreviewResponse,
} from './api/admin-vocabulary';
export {
  acceptAdminVocabularyAiDrafts,
  backfillAdminVocabularyAudio,
  bulkActivateAdminVocabularyItems,
  bulkArchiveAdminVocabularyItems,
  bulkDraftAdminVocabularyItems,
  bulkImportAdminVocabulary,
  cancelAdminVocabularyImportAudio,
  createAdminVocabularyItem,
  deleteAdminVocabularyItem,
  deleteAdminVocabularyItems,
  exportAdminVocabularyImportBatchCsv,
  fetchAdminVocabularyAudioProgress,
  fetchAdminVocabularyCategories,
  fetchAdminVocabularyImportBatch,
  fetchAdminVocabularyItem,
  fetchAdminVocabularyItems,
  fetchAdminVocabularyRecallSets,
  generateAdminVocabularyAudio,
  previewAdminVocabularyImport,
  reconcileAdminVocabularyImportBatch,
  requestAdminVocabularyAiDraft,
  resumeAdminVocabularyAudio,
  rollbackAdminVocabularyImportBatch,
  setAdminVocabularyFreePreview,
  updateAdminVocabularyItem,
} from './api/admin-vocabulary';

// ── Adaptive Difficulty ───────────────────────────────────────────────────────
// ── Predictions ────────────────────────────────────────────────────────────────
// ── Community ─────────────────────────────────────────────────────────────────
// ── Community Moderation (Admin) ──────────────────────────────────────────────
export {
  adminDeleteCommunityReply,
  adminDeleteCommunityThread,
  createForumThread,
  createReply,
  createStudyGroup,
  fetchAdminCommunityThreads,
  fetchAdaptiveContent,
  fetchForumCategories,
  fetchForumThread,
  fetchForumThreads,
  fetchPrediction,
  fetchPredictions,
  fetchSkillProfile,
  fetchStudyGroups,
  fetchThreadReplies,
  joinStudyGroup,
  lockCommunityThread,
  pinCommunityThread,
  requestPredictionComputation,
} from './api/community';

// ── Grammar ───────────────────────────────────────────────────────────────────
export type {
  AdminGrammarLessonFull,
  AdminGrammarLessonRow,
  AdminGrammarTopic,
  GrammarAttemptResult,
  GrammarContentBlockLearner,
  GrammarExerciseAuthoring,
  GrammarExerciseLearner,
  GrammarLessonDocument,
  GrammarLessonProgress,
  GrammarLessonSummary,
  GrammarLessonUpsertPayload,
  GrammarRecommendation,
  GrammarTopicLearner,
  GrammarTopicUpsertPayload,
} from './grammar/types';
export type { GrammarEntitlement } from './api/grammar';
export {
  adminArchiveGrammarLessonV2,
  adminArchiveGrammarTopic,
  adminCreateGrammarLessonV2,
  adminCreateGrammarTopic,
  adminEvaluateGrammarPublishGate,
  adminFetchGrammarPublishGate,
  adminFetchGrammarStats,
  adminForceDeleteGrammarLessonV2,
  adminGenerateGrammarAiDraft,
  adminGenerateWritingAiDraft,
  adminGetGrammarLessonV2,
  adminListGrammarLessonsV2,
  adminListGrammarTopics,
  adminPublishGrammarLesson,
  adminPublishGrammarLessonV2,
  adminUnpublishGrammarLesson,
  adminUnpublishGrammarLessonV2,
  adminUpdateGrammarLessonV2,
  adminUpdateGrammarTopic,
  completeGrammarLesson,
  dismissGrammarRecommendation,
  fetchGrammarEntitlement,
  fetchGrammarLesson,
  fetchGrammarLessons,
  fetchGrammarOverview,
  fetchGrammarTopicDetail,
  startGrammarLesson,
  submitGrammarAttempt,
} from './api/grammar';

// ── Writing Options (admin: AI kill-switch + entitlement) ──
// ── Writing Rule-Violation Analytics (admin: P22 dashboard) ──
// ── Video Lessons ─────────────────────────────────────────────────────────────
// Retired 2026-07: the legacy /v1/lessons feature was superseded by the Video
// Library (`lib/api/videos.ts`, /v1/video-library). Old endpoints return 410.

// ── Strategy Guides ───────────────────────────────────────────────────────────
export type {
  AdminWritingAttemptViolations,
  AdminWritingLetterTypeCount,
  AdminWritingOptions,
  AdminWritingProfessionCount,
  AdminWritingRuleViolationDashboard,
  AdminWritingRuleViolationGroup,
  AdminWritingRuleViolationRow,
  AdminWritingRuleViolationSummary,
} from './api/strategies';
export {
  adminArchiveStrategyGuide,
  adminCreateStrategyGuide,
  adminForceDeleteStrategyGuide,
  adminGetStrategyGuide,
  adminGetWritingAttemptViolations,
  adminGetWritingOptions,
  adminGetWritingRuleViolationDashboard,
  adminListStrategyGuides,
  adminPublishStrategyGuide,
  adminUpdateStrategyGuide,
  adminUpdateWritingOptions,
  adminValidateStrategyGuidePublish,
  fetchStrategyGuide,
  fetchStrategyGuides,
  setStrategyGuideBookmark,
  updateStrategyGuideProgress,
} from './api/strategies';

// ── Pronunciation ─────────────────────────────────────────────────────────────
// All pronunciation endpoints are protected by the backend's LearnerOnly policy
// and the `pronunciation_analysis` feature flag. The recording+scoring flow:
//   1. pronunciationInitAttempt(drillId) → { attemptId, uploadUrl, ... }
//   2. pronunciationUploadAudio(drillId, attemptId, blob, durationMs)
//   3. fetchPronunciationAssessment(assessmentId) for the result detail page.
// ── Admin: Pronunciation CMS ────────────────────────────────────────────────
export type {
  AdminPronunciationGenerateAudioResponse,
  PronunciationAssessmentDetail,
  PronunciationDrillSummary,
  PronunciationEntitlement,
  PronunciationProgressItem,
} from './api/pronunciation';
export {
  adminPronunciationAiDraft,
  archiveAdminPronunciationDrill,
  createAdminPronunciationDrill,
  fetchAdminPronunciationDrill,
  fetchAdminPronunciationDrills,
  fetchMyPronunciationProgress,
  fetchPronunciationAssessment,
  fetchPronunciationDrill,
  fetchPronunciationDrills,
  fetchPronunciationDueDrills,
  fetchPronunciationEntitlement,
  fetchPronunciationProfile,
  fetchPronunciationSpeakingLinked,
  forceDeleteAdminPronunciationDrill,
  generateAdminPronunciationModelAudio,
  pronunciationInitAttempt,
  pronunciationUploadAudio,
  submitPronunciationDiscrimination,
  updateAdminPronunciationDrill,
} from './api/pronunciation';

// ── Certificates ──────────────────────────────────────────────────────────────
// ── Referrals ─────────────────────────────────────────────────────────────────
// ── Exam Booking ──────────────────────────────────────────────────────────────
// ── Tutoring ──────────────────────────────────────────────────────────────────
export {
  applyReferralCode,
  bookTutoringSession,
  createExamBooking,
  deleteExamBooking,
  fetchExamBookings,
  fetchMyCertificates,
  fetchMyReferralCode,
  fetchMyReferrals,
  fetchTutoringSessions,
  rateTutoringSession,
  verifyCertificate,
} from './api/learner-perks';

// ── AI Conversation ─────────────────────────────────────────────────────
// ── Admin: Conversation Templates ──────────────────────────────────────
export type {
  AdminElevenLabsVoice,
  AdminLaunchReadinessSettings,
  AppReleasePolicy,
} from './api/conversation';
export {
  adminConversationTtsPreview,
  archiveAdminConversationTemplate,
  completeConversation,
  conversationTranscriptExportUrl,
  createAdminConversationTemplate,
  createConversation,
  downloadConversationTranscript,
  fetchAdminConversationSessionDetail,
  fetchAdminConversationSessions,
  fetchAdminConversationSettings,
  fetchAdminConversationTemplate,
  fetchAdminConversationTemplates,
  fetchAdminLaunchReadinessSettings,
  fetchAppReleasePolicy,
  forceDeleteAdminConversationTemplate,
  getConversation,
  getConversationEntitlement,
  getConversationEvaluation,
  getConversationHistory,
  getConversationTaskTypes,
  getElevenLabsVoices,
  publishAdminConversationTemplate,
  resumeConversation,
  updateAdminConversationSettings,
  updateAdminConversationTemplate,
  updateAdminLaunchReadinessSettings,
} from './api/conversation';

// ── Mocks Module Phase 6 — admin QC pipeline + item retire ──
export type {
  AdminAnswerKeyReport,
  AdminAnswerKeyReportAssessment,
  AdminAnswerKeyReportStatus,
  AdminMockItemAnalysisResponse,
  AdminMockItemAnalysisRow,
  AdminMockLeakReport,
  AdminMockLeakReportStatus,
  MockBundleBulkAction,
  MockBundleReviewStageEntry,
  MockBundleReviewStageSummary,
  MockItemRetireResponse,
  MockReviewStage,
} from './api/admin-mocks';
export {
  addAdminMockBundleSection,
  advanceMockBundleReviewStage,
  archiveAdminMockBundle,
  assignAdminMockBooking,
  bulkAdminMockBundles,
  createAdminMockBundle,
  fetchAdminMockAnalytics,
  fetchAdminMockBundle,
  fetchAdminMockBundleItemAnalysis,
  fetchAdminMockBundleListeningItemAnalysis,
  fetchAdminMockBundles,
  fetchAdminMockItemAnalysis,
  fetchAdminMockRiskList,
  fetchMockBundleReviewStage,
  listAdminAnswerKeyReports,
  listAdminMockLeakReports,
  publishAdminMockBundle,
  recomputeAdminMockBundleItemAnalysis,
  retireMockItem,
  reorderAdminMockBundleSections,
  updateAdminMockBundle,
  updateAdminMockLeakReport,
  updateAdminAnswerKeyReport,
  MOCK_REVIEW_STAGES,
} from './api/admin-mocks';
// Booking projections live in ./api/mock-bookings (shared mapMockBooking).
export type {
  AdminMockBookingRow,
  ExpertMockBookingDetail,
  ExpertSpeakingContent,
  ExpertSpeakingInterlocutorCard,
  MockLiveRoomTargetState,
  MockLiveRoomTransitionOptions,
} from './api/mock-bookings';
export {
  fetchAdminMockBookings,
  fetchExpertMockBookings,
  fetchExpertMockBookingDetail,
  mapMockBooking,
  normalizeMockDeliveryMode,
  transitionAdminMockBookingLiveRoom,
  transitionAdminMockBookingLiveRoomState,
  transitionExpertMockBookingLiveRoom,
  transitionMockBookingLiveRoom,
} from './api/mock-bookings';

// ── Writing Coach ───────────────────────────────────────────────────────
// ── Content Generation (Admin) ──────────────────────────────────────────
// ── Content Marketplace ─────────────────────────────────────────────────
export {
  browseMarketplace,
  coachCheckText,
  createMarketplaceSubmission,
  fetchCoachStats,
  fetchContentGenerationJob,
  fetchContentGenerationJobs,
  fetchMarketplaceProfile,
  fetchMarketplaceSubmission,
  fetchMyMarketplaceSubmissions,
  fetchPendingMarketplaceSubmissions,
  queueContentGeneration,
  resolveCoachSuggestion,
  reviewMarketplaceSubmission,
  updateMarketplaceProfile,
} from './api/content-studio';

// ── Admin Permissions (RBAC) ──────────────────────────
// ── Permission Templates ──────────────────────────────
// ── Content Publishing Workflow ────────────────────────
// ── Webhook Monitoring ────────────────────────────────
export {
  applyPermissionTemplate,
  approvePublishRequest,
  createPermissionTemplate,
  deletePermissionTemplate,
  editorApproveContent,
  editorRejectContent,
  fetchAdminPermissions,
  fetchAllPermissions,
  fetchPendingReviewContent,
  fetchPermissionTemplates,
  fetchPublishRequests,
  fetchWebhookEvents,
  fetchWebhookSummary,
  publisherApproveContent,
  publisherRejectContent,
  rejectPublishRequest,
  requestContentPublish,
  retryWebhook,
  submitContentForReview,
  updateAdminPermissions,
} from './api/admin-governance';

// ── Review Escalations ────────────────────────────────
// ── Learner Escalations (Disputes) ────────────────────
// ── Score Guarantee (Learner) ─────────────────────────
// ── Score Equivalences ────────────────────────────────
// ── Study Commitment ──────────────────────────────────
export {
  activateScoreGuarantee,
  assignEscalationReviewer,
  fetchEscalationDetails,
  fetchMyEscalations,
  fetchReviewEscalations,
  fetchScoreEquivalences,
  fetchScoreGuarantee,
  fetchStudyCommitment,
  resolveEscalation,
  setStudyCommitment,
  submitEscalation,
  submitScoreGuaranteeClaim,
} from './api/escalations';

// ── Certificates ──────────────────────────────────────
// ── Referral ──────────────────────────────────────────
export {
  fetchCertificates,
  fetchReferralInfo,
  generateReferralCode,
} from './api/learner-perks';

// ── Expert Annotation Templates ───────────────────────
// ── Expert Amend Review ──────────────────────────────
// ── Expert Rework Chain ──────────────────────────────
// ── Expert Bulk Operations ────────────────────────────
// ── Expert Messaging ──────────────────────────────────
// ── Expert Compensation ───────────────────────────────
// ── Admin: Score Guarantee Claims ─────────────────────
export {
  amendReview,
  bulkClaimReviews,
  bulkReleaseReviews,
  createAnnotationTemplate,
  createExpertMessageThread,
  deleteAnnotationTemplate,
  fetchAdminScoreGuaranteeClaims,
  fetchAmendEligibility,
  fetchAnnotationTemplates,
  fetchExpertCompensationSummary,
  fetchExpertEarningsHistory,
  fetchExpertMessageThread,
  fetchExpertMessageThreads,
  fetchExpertPayouts,
  fetchReworkChain,
  postExpertMessageReply,
  reviewScoreGuaranteeClaim,
  updateAnnotationTemplate,
} from './api/expert-ops';

// ── Private Speaking Sessions ─────────────────────────────
// ── Private Speaking: Expert ──────────────────────────────
// ── Private Speaking: Admin ───────────────────────────────
export type {
  LiveClassJoinToken,
  PrivateSpeakingBookingResult,
  PrivateSpeakingCalendarConnectResult,
  PrivateSpeakingCalendarStatus,
} from './api/private-speaking';
export {
  adminEditPrivateSpeakingBooking,
  adminManualReschedulePrivateSpeaking,
  adminMarkPrivateSpeakingNoShow,
  adminOverridePrivateSpeakingRefund,
  adminUpdatePrivateSpeakingAvailabilityRule,
  cancelAdminPrivateSpeakingBooking,
  cancelExpertPrivateSpeakingSession,
  cancelPrivateSpeakingBooking,
  completeAdminPrivateSpeakingBooking,
  connectExpertPrivateSpeakingGoogleCalendar,
  createAdminPrivateSpeakingAvailabilityRule,
  createAdminPrivateSpeakingTutor,
  createPrivateSpeakingBooking,
  deleteAdminPrivateSpeakingAvailabilityRule,
  deleteExpertPrivateSpeakingAvailability,
  disconnectExpertPrivateSpeakingCalendar,
  downloadAdminPrivateSpeakingBookingsCsv,
  downloadExpertPrivateSpeakingCalendarInvite,
  downloadPrivateSpeakingCalendarInvite,
  fetchAdminPrivateSpeakingAuditLogs,
  fetchAdminPrivateSpeakingAvailability,
  fetchAdminPrivateSpeakingBookings,
  fetchAdminPrivateSpeakingConfig,
  fetchAdminPrivateSpeakingStats,
  fetchAdminPrivateSpeakingTutor,
  fetchAdminPrivateSpeakingTutors,
  fetchAllPrivateSpeakingSlots,
  fetchExpertPrivateSpeakingAvailability,
  fetchExpertPrivateSpeakingCalendarStatus,
  fetchExpertPrivateSpeakingJoinToken,
  fetchExpertPrivateSpeakingProfile,
  fetchExpertPrivateSpeakingSessionDetail,
  fetchExpertPrivateSpeakingSessions,
  fetchLearnerPrivateSpeakingBookings,
  fetchPrivateSpeakingBookingDetail,
  fetchPrivateSpeakingConfig,
  fetchPrivateSpeakingJoinToken,
  fetchPrivateSpeakingSlots,
  fetchPrivateSpeakingTutors,
  markExpertPrivateSpeakingNoShow,
  ratePrivateSpeakingSession,
  reschedulePrivateSpeakingBooking,
  retryAdminPrivateSpeakingZoom,
  updateAdminPrivateSpeakingConfig,
  updateAdminPrivateSpeakingTutor,
  updateExpertPrivateSpeakingAvailability,
  updateExpertPrivateSpeakingAvailabilityRule,
} from './api/private-speaking';
import type { LiveClassJoinToken } from './api/private-speaking';

// ── Zoom Live Classes ───────────────────────────────────
// ── Tutor (Zoom-backed live classes — wave B1) ──────────
export type { AdminLiveClassUpsertPayload } from './api/live-classes';
export type {
  ClassFeedbackEntry,
  ClassFeedbackSubmitPayload,
  ClassWaitlistEntry,
  DayOfWeekString,
  LiveClassDetail,
  LiveClassEnrollment,
  LiveClassListItem,
  LiveClassQueryParams,
  LiveClassRecording,
  LiveClassSessionSummary,
  LiveClassTranscript,
  TutorAttendanceLine,
  TutorAvailabilitySlot,
  TutorAvailabilityUpsertPayload,
  TutorClassCreatePayload,
  TutorClassSessionCreatePayload,
  TutorClassSessionUpdatePayload,
  TutorClassUpdatePayload,
  TutorEarnings,
  TutorEarningsLine,
  TutorProfile,
  TutorUpsertPayload,
} from './api/live-classes';
export {
  addAdminLiveClassSession,
  addTutorClassSession,
  cancelAdminLiveClassSession,
  cancelLiveClassEnrollment,
  cancelTutorClassSession,
  createAdminLiveClass,
  createTutorClass,
  createTutorProfile,
  enrollLiveClassSession,
  fetchAdminLiveClassAnalytics,
  fetchAdminLiveClassDetail,
  fetchAdminLiveClasses,
  fetchClassTranscript,
  fetchExpertLiveClasses,
  fetchExpertLiveClassJoinToken,
  fetchLiveClassDetail,
  fetchLiveClassJoinToken,
  fetchLiveClassRecording,
  fetchLiveClasses,
  fetchMyPastLiveClasses,
  fetchMyUpcomingLiveClasses,
  fetchTutorAvailability,
  fetchTutorClasses,
  fetchTutorEarnings,
  fetchTutorProfile,
  fetchTutorSessionAttendance,
  joinClassWaitlist,
  leaveClassWaitlist,
  provisionTutorZoomUser,
  publishAdminLiveClass,
  replaceTutorAvailability,
  retryAdminLiveClassSessionZoom,
  submitClassFeedback,
  updateAdminLiveClassSession,
  updateTutorClass,
  updateTutorClassSession,
  updateTutorProfile,
} from './api/live-classes';

// ── Orphan Endpoint Wiring ────────────────────────────
// ── Sponsor Dashboard ──
export type {
  SponsorBillingData,
  SponsorDashboardData,
  SponsoredLearner,
  SponsorInvoice,
} from './api/misc';
export {
  applyStreakFreeze,
  fetchDiagnosticPersonalization,
  fetchFluencyTimeline,
  fetchReadinessRisk,
  fetchSponsorBilling,
  fetchSponsorDashboard,
  fetchSponsoredLearners,
  fetchStudyPlanDrift,
  inviteSponsoredLearner,
  regenerateStudyPlan,
  removeSponsoredLearner,
} from './api/misc';

// -- Admin: Rulebook Management ------------------------------------------
// === SUBAGENT_BACKEND: admin-content-management START ===
// === SUBAGENT_BACKEND: admin-content-management END ===
export type {
  AdminBulkPaperPublishResult,
  AdminBulkPaperStatusResult,
  AdminConversationAiDraftPayload,
  AdminConversationAiDraftResult,
  AdminPublishWithWarningsResponse,
  AdminRulebookDetail,
  AdminRulebookMetadata,
  AdminRulebookRule,
  AdminRulebookSection,
  AdminRulebookSummary,
} from './api/admin-rulebooks';
export {
  adminBulkPublishPapers,
  adminBulkSetPaperStatus,
  adminCloneRulebook,
  adminConversationAiDraft,
  adminCreateRulebook,
  adminCreateRulebookRule,
  adminCreateRulebookSection,
  adminDeleteRulebook,
  adminDeleteRulebookRule,
  adminDeleteRulebookSection,
  adminExportRulebook,
  adminGetRulebook,
  adminGetRulebookMetadata,
  adminImportRulebook,
  adminListRulebooks,
  adminPublishPaperWithWarnings,
  adminPublishRulebook,
  adminUnarchiveConversationTemplate,
  adminUnarchiveGrammarLesson,
  adminUnarchiveMockBundle,
  adminUnarchivePaper,
  adminUnarchivePronunciationDrill,
  adminUnpublishRulebook,
  adminUpdateRulebookMeta,
  adminUpdateRulebookRule,
  adminUpdateRulebookSection,
} from './api/admin-rulebooks';

// === SUBAGENT_B: listening-authoring START ===
// === SUBAGENT_B: listening-authoring END ===
export {
  adminListeningBackfillAll,
  adminListeningExportAttempt,
  adminListeningGetAnalytics,
  adminListeningGetExtracts,
  adminListeningGetStructure,
  adminListeningPatchExtract,
  adminListeningPatchQuestion,
  adminListeningReplaceExtracts,
  adminListeningReplaceStructure,
  adminListeningValidate,
} from './api/listening-authoring';

// === SUBAGENT_C: bulk-import-and-generation START ===
// === SUBAGENT_C: bulk-import-and-generation END ===
export {
  adminCommitZipImport,
  adminDiscardUpload,
  adminGetGenerationJob,
  adminListGenerationJobs,
  adminQueueContentGeneration,
  adminStartZipImport,
} from './api/bulk-import';

// === SUBAGENT_E: bulk-ops START ===
// === SUBAGENT_E: bulk-ops END ===
// (Wave-2 bulk pages were never built — helpers removed 2026-09-06 as dead
// code. Backend routes are untouched; re-scaffold from OpenAPI if needed.)

// === SUBAGENT_D: speaking-conv-pron START ===
// === SUBAGENT_D: speaking-conv-pron END ===
// (Wave-2 workspace/create/backfill/analytics pages were never built —
// helpers removed 2026-09-06 as dead code. Backend routes are untouched;
// re-scaffold from OpenAPI if needed.)

// === SUBAGENT_A: reading-authoring START ===
// === SUBAGENT_A: reading-authoring END ===
export {
  adminReadingApproveExtraction,
  adminReadingCreateExtraction,
  adminReadingDeleteQuestion,
  adminReadingDeleteText,
  adminReadingEnsureCanonical,
  adminReadingGetAnalytics,
  adminReadingGetExtraction,
  adminReadingGetManifest,
  adminReadingGetReviewHistory,
  adminReadingGetStructure,
  adminReadingImportManifest,
  adminReadingListExtractions,
  adminReadingRejectExtraction,
  adminReadingReorderQuestions,
  adminReadingReorderTexts,
  adminReadingSetDistractors,
  adminReadingTransitionReview,
  adminReadingUpsertPart,
  adminReadingUpsertQuestion,
  adminReadingUpsertText,
  adminReadingValidate,
} from './api/reading-authoring';

// -----------------------------------------------------------------------------
// Scoring Policy (admin singleton document + learner read)
// -----------------------------------------------------------------------------

// Expert + admin content facades below

export type {
  CalibrationCase,
  CalibrationCaseDetail,
  CalibrationNote,
  ExpertLearnerDirectoryResponse,
  ExpertLearnerReviewContext,
  ExpertMetrics,
  ExpertOnboardingProfile,
  ExpertOnboardingQualifications,
  ExpertOnboardingRates,
  ExpertOnboardingStatus,
  ExpertQueueFilterMetadata,
  ExpertReviewHistory,
  ExpertSchedule,
  LearnerProfileExpanded,
  ReviewDraft,
  ReviewQueueResponse,
  ReviewVoiceNote,
  ScheduleException,
  SpeakingReviewDetail,
  WritingReviewDetail,
} from './types/expert';
export type {
  ExpertAvailabilityConstraints,
  ExpertCalibrationAlignment,
  ExpertCalibrationAlignmentBreakdown,
  ExpertCalibrationAlignmentTrendPoint,
  ExpertCalibrationHistory,
  ExpertCalibrationHistoryEntry,
  ReviewCriterionVoiceNoteResult,
  WritingMarkingVoiceNote,
} from './api/expert';
export {
  addWritingReviewVoiceNote,
  claimReview,
  completeExpertOnboarding,
  createScheduleException,
  deleteScheduleException,
  fetchCalibrationCaseDetail,
  fetchCalibrationCases,
  fetchCalibrationNotes,
  fetchExpertAvailabilityConstraints,
  fetchExpertCalibrationAlignment,
  fetchExpertCalibrationHistory,
  fetchExpertLearnerReviewContext,
  fetchExpertMetrics,
  fetchExpertOnboardingStatus,
  fetchExpertQueueFilterMetadata,
  fetchExpertSchedule,
  fetchLearnerProfile,
  fetchExpertLearners,
  fetchLearnerReviewResult,
  fetchLearnerReviewVoiceNotes,
  fetchReviewQueue,
  fetchScheduleExceptions,
  fetchSpeakingReviewDetail,
  fetchExpertReviewHistory,
  fetchTutorWritingQueue,
  fetchWritingReviewDetail,
  getWritingSubmissionVoiceNote,
  releaseReview,
  requestRework,
  saveCalibrationDraft,
  saveDraftReview,
  saveExpertOnboardingProfile,
  saveExpertOnboardingQualifications,
  saveExpertOnboardingRates,
  saveExpertSchedule,
  submitCalibrationCase,
  submitExpertSpeakingReview,
  submitExpertWritingReview,
  uploadSpeakingReviewCriterionVoiceNote,
  uploadWritingMarkingVoiceNote,
  uploadWritingReviewCriterionVoiceNote,
} from './api/expert';

// ─── Admin / CMS API ───

// ── Admin Alerts ─────────────────────────────────────
// ── Admin Content ─────────────────────────────────────
export type {
  AdminSignupExamTypePayload,
  AdminSignupProfessionPayload,
} from './api/admin-platform';
export {
  archiveAdminContent,
  archiveAdminSignupExamType,
  archiveAdminSignupProfession,
  archiveAdminTaxonomy,
  activateAdminSignupExamType,
  activateAdminSignupProfession,
  createAdminAIConfig,
  createAdminContent,
  createAdminCriterion,
  createAdminFlag,
  createAdminSignupExamType,
  createAdminSignupProfession,
  createAdminTaxonomy,
  exportAdminAuditLogs,
  fetchAdminAIConfig,
  fetchAdminAlerts,
  fetchAdminAuditLogs,
  fetchAdminContent,
  fetchAdminContentDetail,
  fetchAdminContentRevisions,
  fetchAdminCriteria,
  fetchAdminDashboard,
  fetchAdminFlags,
  fetchAdminSignupCatalog,
  fetchAdminTaxonomy,
  forceDeleteAdminSignupExamType,
  forceDeleteAdminSignupProfession,
  forceDeleteAdminTaxonomy,
  publishAdminContent,
  restoreAdminContentRevision,
  updateAdminAIConfig,
  updateAdminContent,
  updateAdminCriterion,
  updateAdminFlag,
  updateAdminSignupExamType,
  updateAdminSignupProfession,
  updateAdminTaxonomy,
} from './api/admin-platform';

export type {
  AdminBillingAddOnOet2026Fields,
  AdminBillingPlanOet2026Fields,
  AdminSponsorDto,
  AdminUserProfileUpdatePayload,
  AdminWalletTierInput,
  AdminWalletTierRow,
  AdminWalletTiersResponse,
} from './api/admin-users';
export {
  adjustAdminUserCredits,
  bulkImportUsers,
  createAdminBillingAddOn,
  createAdminBillingPlan,
  deleteAdminBillingAddOn,
  deleteAdminBillingPlan,
  deleteAdminUser,
  fetchAdminBillingAddOnVersions,
  fetchAdminBillingAddOns,
  fetchAdminBillingPlanVersions,
  fetchAdminBillingPlans,
  fetchAdminSponsors,
  fetchAdminUserDetail,
  fetchAdminUsers,
  fetchAdminWalletTiers,
  hardDeleteAdminUser,
  inviteAdminUser,
  replaceAdminWalletTiers,
  resendAdminUserInvite,
  restoreAdminUser,
  revokeAdminUserSessions,
  setAdminUserPassword,
  triggerAdminUserPasswordReset,
  unlockAdminUser,
  updateAdminBillingAddOn,
  updateAdminBillingPlan,
  updateAdminUserProfile,
  updateAdminUserStatus,
  verifyAdminUserEmail,
} from './api/admin-users';

// ── Hard-delete (404 + 409 handled by caller). Server returns 409 when
// the plan/add-on still has historical references — caller should fall
// back to archive (PUT status=archived) in that case.

// ── Billing page copy (admin-editable learner-page strings) ──────────────
export type { AdminBillingContentEntry } from './api/billing-content';
export {
  deleteAdminBillingContentEntry,
  fetchAdminBillingContent,
  fetchBillingContent,
  replaceAdminBillingContent,
} from './api/billing-content';

// ── OET 2026 catalog API ─────────────────────────────────────────────────
export type {
  AdminCatalogPresentationResponse,
  MyEntitlementSnapshot,
  Oet2026ReseedResponse,
} from './api/catalog';
export {
  fetchAdminCatalogPresentation,
  fetchEligibilityMatrix,
  fetchMyEntitlementSnapshot,
  fetchPublicCatalog,
  quoteAddonEligibility,
  reseedOet2026Catalog,
  saveAdminCatalogPresentation,
} from './api/catalog';

// ── Tutor Book API ───────────────────────────────────────────────────────
export type {
  AdminTutorBookAudioScript,
  AdminTutorBookUpdate,
  TutorBookAudioScript,
  TutorBookUpdate,
  TutorBookWhatsAppResponse,
} from './api/tutor-book';
export {
  adminDeleteTutorBookAudioScript,
  adminDeleteTutorBookUpdate,
  adminListTutorBookAudioScripts,
  adminListTutorBookUpdates,
  adminUpsertTutorBookAudioScript,
  adminUpsertTutorBookUpdate,
  fetchTutorBookAudioScripts,
  fetchTutorBookUpdates,
  fetchTutorBookWhatsApp,
  tutorBookDownloadUrl,
} from './api/tutor-book';

// ─────────────────────────────────────────────────────────────────────────
// OET Speaking module re-exports
//
// Surface the typed API clients living under `lib/api/speaking-*.ts` so
// existing call sites can keep importing from `lib/api`. Each module
// owns its own request/response types — these `export *` lines are the
// single integration point.
// ─────────────────────────────────────────────────────────────────────────
export * from './api/speaking-role-play-cards';
export * from './api/speaking-sessions';
export * from './api/speaking-live-rooms';
export * from './api/speaking-assessments';
export * from './api/speaking-compliance';
export * from './api/billing-region';
export * from './api/billing-expansion';
export * from './api/ai-analytics';

// ─────────────────────────────────────────────────────────────────────────────
// Listening Policy Admin
// ─────────────────────────────────────────────────────────────────────────────
export type {
  ListeningPolicyDto,
  ListeningUserPolicyOverrideDto,
} from './api/listening-policy';
export {
  adminGetListeningPolicy,
  adminGetListeningUserPolicyOverride,
  adminUpsertListeningPolicy,
  adminUpsertListeningUserPolicyOverride,
} from './api/listening-policy';

// ── Wave B2: Cart / Checkout / Subscription self-service / Admin products & coupons ──
//
// (Server-cart wrappers removed 2026-09-06 as dead code: the live cart is
// client state (`lib/cart/cart-store`) + Stripe, and zero callers used these
// helpers. Backend routes are untouched; re-scaffold from OpenAPI if needed.)


// ── Subscription self-service ───────────────────────────────────────
export type {
  SubscriptionInvoice,
  SubscriptionMe,
  SubscriptionMeListItem,
} from './api/subscriptions';
export {
  cancelSubscription,
  changeSubscriptionPlanSelf,
  createSubscriptionPortalSession,
  fetchSubscriptionInvoices,
  fetchSubscriptionMe,
  fetchSubscriptionsMe,
  pauseSubscriptionSelf,
  requestSubscriptionFreeze,
  resumeSubscriptionById,
  resumeSubscriptionSelf,
} from './api/subscriptions';

// ── Admin: Billing products (catalog) — Wave B2 thin CRUD ───────────
export type {
  AdminBillingAnalyticsResponse,
  AdminBillingAnalyticsSeriesPoint,
  AdminBillingProduct,
  AdminBillingProductPrice,
  AdminRefundRequest,
} from './api/billing-products';
export {
  fetchAdminBillingAnalytics,
  fetchAdminBillingProduct,
  fetchAdminBillingProducts,
  fetchAdminRefunds,
  postAdminRefundAction,
  updateAdminBillingProduct,
} from './api/billing-products';


// ── Admin mocks analytics (Phase 3) ─────────────────────────────────────
export type {
  AdminMocksAnalyticsAttemptsCompletion,
  AdminMocksAnalyticsAverageReadiness,
  AdminMocksAnalyticsMarkingDelay,
  AdminMocksAnalyticsMarkingDelayRow,
  AdminMocksAnalyticsPassPrediction,
  AdminMocksAnalyticsPassPredictionProfessionRow,
  AdminMocksAnalyticsReadinessDistribution,
  AdminMocksAnalyticsResponse,
  AdminMocksAnalyticsRevenueRow,
  AdminMocksAnalyticsTutorWorkloadRow,
  AdminMocksAnalyticsLowQualityRow,
  AdminMocksAnalyticsReadingSection,
  AdminMocksAnalyticsWindow,
} from './api/admin-quality';
export {
  fetchAdminMocksAnalytics,
} from './api/admin-quality';

// ── Admin speaking calibration (Phase 7a) ───────────────────────────────
// ── Admin interlocutor onboarding (Phase 7a) ────────────────────────────
export type {
  InterlocutorPracticeQueueResponse,
  InterlocutorPracticeQueueRow,
  InterlocutorPracticeSessionStart,
  InterlocutorTraineeRow,
  InterlocutorTraineesResponse,
  InterlocutorTrainingStatusLabel,
  MarkInterlocutorTrainedResult,
  SpeakingCalibrationDriftSummary,
  SpeakingCalibrationDriftTutorRow,
  SpeakingCalibrationSampleSummaryRow,
  SpeakingCalibrationSamplesResponse,
} from './api/admin-quality';
export {
  fetchInterlocutorPracticeQueue,
  fetchInterlocutorTraineeList,
  fetchInterlocutorTrainees,
  fetchSpeakingCalibrationOverview,
  fetchSpeakingCalibrationSets,
  fetchSpeakingCalibrationSummary,
  markInterlocutorTrained,
  startInterlocutorPracticeSession,
} from './api/admin-quality';

// ── Voice Design Studio API ─────────────────────────────────────────
export type {
  AdminAudioBatch,
  AdminAudioRegenerateBatchResult,
  AdminAudioRegenerateRequest,
  AdminVoiceDesignConfig,
} from './api/voice-design';
export {
  cancelAudioRegenerationBatch,
  getAdminRecallsAudioBatchProgress,
  getAdminVoiceDesignConfig,
  getAudioRegenerationBatchProgress,
  getAudioRegenerationBatches,
  previewAdminVoiceDesign,
  regenerateAllAudio,
  retryAudioRegenerationBatch,
  saveAdminVoiceDesignConfig,
  startAdminRecallsAudioBackfill,
} from './api/voice-design';

