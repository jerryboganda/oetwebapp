/**
 * Listening policy admin — extracted from `lib/api.ts`. Re-exported
 * there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export interface ListeningPolicyDto {
  id: string;
  // §1 Retry
  attemptsPerPaperPerUser: number;
  attemptCooldownMinutes: number;
  bestScoreDisplay: string;
  showPastAttempts: boolean;
  // §2 Timer
  fullPaperTimerMinutes: number;
  gracePeriodSeconds: number;
  onExpirySubmitPolicy: string;
  countdownWarningsJson: string;
  // §3 Audio replay
  examReplayAllowed: boolean;
  learningReplayAllowed: boolean;
  learningEvidenceLoopEnabled: boolean;
  // §4 Grading
  shortAnswerNormalisation: string;
  shortAnswerAcceptSynonyms: boolean;
  // §5 AI extraction
  aiExtractionEnabled: boolean;
  aiExtractionRequireHumanApproval: boolean;
  aiExtractionMaxRetriesPerPaper: number;
  // §6 Review
  showExplanationsAfterSubmit: boolean;
  showExplanationsOnlyIfWrong: boolean;
  showCorrectAnswerOnReview: boolean;
  // §7 Accessibility
  defaultExtraTimePct: number;
  screenReaderOptimised: boolean;
  // §8 Lifecycle
  autoExpireWorkerEnabled: boolean;
  autoExpireAfterMinutes: number;
  allowResumeAfterExpiry: boolean;
  // §9 Retention
  retainAnswerRowsDays: number;
  retainAttemptHeadersDays: number;
  anonymiseOnAccountDelete: boolean;
  // Listening V2 fields (nullable)
  previewWindowMsA1?: number | null;
  previewWindowMsA2?: number | null;
  previewWindowMsC1?: number | null;
  previewWindowMsC2?: number | null;
  reviewWindowMsA1?: number | null;
  reviewWindowMsA2?: number | null;
  reviewWindowMsC1?: number | null;
  reviewWindowMsC2FinalCbt?: number | null;
  reviewWindowMsC2FinalPaper?: number | null;
  betweenSectionTransitionMs?: number | null;
  partBQuestionWindowMs?: number | null;
  oneWayLocksEnabled?: boolean | null;
  confirmDialogRequired?: boolean | null;
  unansweredWarningRequired?: boolean | null;
  confirmTokenTtlMs?: number | null;
  highlightingEnabledPartA?: boolean | null;
  highlightingEnabledPartBC?: boolean | null;
  optionStrikethroughEnabled?: boolean | null;
  inAppZoomEnabled?: boolean | null;
  ctrlZoomBlocked?: boolean | null;
  annotationsPersistOnAdvance?: boolean | null;
  techReadinessRequired?: boolean | null;
  techReadinessTtlMs?: number | null;
  finalReviewAllPartsMsPaper?: number | null;
  rowVersion: number;
  updatedAt: string;
  updatedByAdminId?: string | null;
}

export interface ListeningUserPolicyOverrideDto {
  userId: string;
  extraTimeEntitlementPct: number;
  blockAttempts: boolean;
  accessibilityModeEnabled: boolean;
  reason?: string | null;
  grantedByAdminId?: string | null;
  createdAt: string;
  updatedAt: string;
  expiresAt?: string | null;
}

export async function adminGetListeningPolicy(): Promise<ListeningPolicyDto> {
  return apiRequest<ListeningPolicyDto>('/v1/admin/listening-policy');
}

export async function adminUpsertListeningPolicy(payload: ListeningPolicyDto): Promise<ListeningPolicyDto> {
  return apiRequest<ListeningPolicyDto>('/v1/admin/listening-policy', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function adminGetListeningUserPolicyOverride(userId: string): Promise<ListeningUserPolicyOverrideDto | null> {
  return apiRequest<ListeningUserPolicyOverrideDto | null>(
    `/v1/admin/listening-policy/users/${encodeURIComponent(userId)}`
  );
}

export async function adminUpsertListeningUserPolicyOverride(
  userId: string,
  payload: Omit<ListeningUserPolicyOverrideDto, 'userId' | 'createdAt' | 'updatedAt' | 'grantedByAdminId'>
): Promise<ListeningUserPolicyOverrideDto> {
  return apiRequest<ListeningUserPolicyOverrideDto>(
    `/v1/admin/listening-policy/users/${encodeURIComponent(userId)}`,
    { method: 'PUT', body: JSON.stringify({ ...payload, userId }) }
  );
}
