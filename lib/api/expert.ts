/**
 * Expert console: onboarding, review queue, metrics, schedule, calibration
 * cases, learner directory, writing/speaking review detail, voice notes —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api` imports
 * keep working.
 */
import { ApiError, apiRequest } from './client';
import { uploadMedia } from './content-discovery';
import type {
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
} from '../types/expert';

export async function fetchExpertOnboardingStatus(): Promise<ExpertOnboardingStatus> {
  return apiRequest<ExpertOnboardingStatus>('/v1/expert/onboarding/status');
}

export async function saveExpertOnboardingProfile(data: ExpertOnboardingProfile): Promise<ExpertOnboardingProfile> {
  return apiRequest<ExpertOnboardingProfile>('/v1/expert/onboarding/profile', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export async function saveExpertOnboardingQualifications(data: ExpertOnboardingQualifications): Promise<ExpertOnboardingQualifications> {
  return apiRequest<ExpertOnboardingQualifications>('/v1/expert/onboarding/qualifications', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export async function saveExpertOnboardingRates(data: ExpertOnboardingRates): Promise<ExpertOnboardingRates> {
  return apiRequest<ExpertOnboardingRates>('/v1/expert/onboarding/rates', {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export async function completeExpertOnboarding(): Promise<{ completed: boolean }> {
  return apiRequest<{ completed: boolean }>('/v1/expert/onboarding/complete', {
    method: 'PATCH',
  });
}

export async function fetchReviewQueue(params?: {
  search?: string;
  type?: string[];
  profession?: string[];
  priority?: string[];
  status?: string[];
  confidence?: string[];
  assignment?: string[];
  overdue?: boolean;
  page?: number;
  pageSize?: number;
}): Promise<ReviewQueueResponse> {
  const queryParams = new URLSearchParams();
  if (params?.search?.trim()) queryParams.set('search', params.search.trim());
  if (params?.type?.length) queryParams.set('type', params.type.join(','));
  if (params?.profession?.length) queryParams.set('profession', params.profession.join(','));
  if (params?.priority?.length) queryParams.set('priority', params.priority.join(','));
  if (params?.status?.length) queryParams.set('status', params.status.join(','));
  if (params?.confidence?.length) queryParams.set('confidence', params.confidence.join(','));
  if (params?.assignment?.length) queryParams.set('assignment', params.assignment.join(','));
  if (params?.overdue) queryParams.set('overdue', 'true');
  if (params?.page) queryParams.set('page', String(params.page));
  if (params?.pageSize) queryParams.set('pageSize', String(params.pageSize));
  const query = queryParams.toString();
  return apiRequest<ReviewQueueResponse>(`/v1/expert/queue${query ? `?${query}` : ''}`);
}

export async function fetchExpertQueueFilterMetadata(): Promise<ExpertQueueFilterMetadata> {
  return apiRequest<ExpertQueueFilterMetadata>('/v1/expert/queue/filters/metadata');
}

export async function fetchExpertMetrics(days?: number): Promise<{ metrics: ExpertMetrics; completionData: { day: string; count: number }[]; days: number; generatedAt: string }> {
  const query = days ? `?days=${days}` : '';
  return apiRequest(`/v1/expert/metrics${query}`);
}

export async function fetchExpertSchedule(): Promise<ExpertSchedule> {
  return apiRequest<ExpertSchedule>('/v1/expert/schedule');
}

export async function saveExpertSchedule(schedule: ExpertSchedule): Promise<ExpertSchedule> {
  return apiRequest<ExpertSchedule>('/v1/expert/schedule', {
    method: 'PUT',
    body: JSON.stringify({ timezone: schedule.timezone, days: schedule.days }),
  });
}

export async function fetchScheduleExceptions(from?: string, to?: string): Promise<{ exceptions: ScheduleException[] }> {
  const params = new URLSearchParams();
  if (from) params.set('from', from);
  if (to) params.set('to', to);
  const query = params.toString();
  return apiRequest<{ exceptions: ScheduleException[] }>(`/v1/expert/schedule/exceptions${query ? `?${query}` : ''}`);
}

export async function createScheduleException(data: {
  date: string;
  isBlocked: boolean;
  startTime?: string;
  endTime?: string;
  reason?: string;
}): Promise<ScheduleException> {
  return apiRequest<ScheduleException>('/v1/expert/schedule/exceptions', {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export async function deleteScheduleException(exceptionId: string): Promise<{ deleted: boolean }> {
  return apiRequest<{ deleted: boolean }>(`/v1/expert/schedule/exceptions/${encodeURIComponent(exceptionId)}`, {
    method: 'DELETE',
  });
}

export async function fetchCalibrationCases(): Promise<CalibrationCase[]> {
  return apiRequest<CalibrationCase[]>('/v1/expert/calibration/cases');
}

export async function fetchCalibrationCaseDetail(caseId: string): Promise<CalibrationCaseDetail> {
  return apiRequest<CalibrationCaseDetail>(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}`);
}

export async function fetchCalibrationNotes(): Promise<CalibrationNote[]> {
  return apiRequest<CalibrationNote[]>('/v1/expert/calibration/notes');
}

export async function fetchExpertLearners(params?: {
  search?: string;
  profession?: string;
  subTest?: string;
  relevance?: string;
  page?: number;
  pageSize?: number;
}): Promise<ExpertLearnerDirectoryResponse> {
  const queryParams = new URLSearchParams();
  if (params?.search?.trim()) queryParams.set('search', params.search.trim());
  if (params?.profession) queryParams.set('profession', params.profession);
  if (params?.subTest) queryParams.set('subTest', params.subTest);
  if (params?.relevance) queryParams.set('relevance', params.relevance);
  if (params?.page) queryParams.set('page', String(params.page));
  if (params?.pageSize) queryParams.set('pageSize', String(params.pageSize));
  const query = queryParams.toString();
  return apiRequest<ExpertLearnerDirectoryResponse>(`/v1/expert/learners${query ? `?${query}` : ''}`);
}

export async function fetchLearnerProfile(learnerId: string): Promise<LearnerProfileExpanded> {
  return apiRequest<LearnerProfileExpanded>(`/v1/expert/learners/${encodeURIComponent(learnerId)}`);
}

export async function fetchExpertLearnerReviewContext(learnerId: string): Promise<ExpertLearnerReviewContext> {
  return apiRequest<ExpertLearnerReviewContext>(`/v1/expert/learners/${encodeURIComponent(learnerId)}/review-context`);
}

export async function fetchWritingReviewDetail(reviewRequestId: string): Promise<WritingReviewDetail> {
  return apiRequest<WritingReviewDetail>(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/writing`);
}

export interface TutorWritingQueueItem {
  submissionId: string;
  userId: string;
  profession: string;
  letterType: string;
  [key: string]: unknown;
}

export interface TutorWritingQueueResponse {
  items: TutorWritingQueueItem[];
}

/** Tutor portal's own Writing queue (GET /v1/tutors/writing/queue) — distinct from the expert review queue above. */
export async function fetchTutorWritingQueue(status?: string): Promise<TutorWritingQueueResponse> {
  const query = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiRequest<TutorWritingQueueResponse>(`/v1/tutors/writing/queue${query}`);
}

export async function addWritingReviewVoiceNote(reviewRequestId: string, payload: { mediaAssetId: string; durationSeconds?: number | null; transcriptText?: string; writtenNotes?: string; rubricScores?: Record<string, number>; }): Promise<{ reviewRequestId: string; item: ReviewVoiceNote }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/writing/voice-notes`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchLearnerReviewVoiceNotes(reviewRequestId: string): Promise<{ reviewRequestId: string; items: ReviewVoiceNote[] }> {
  return apiRequest(`/v1/reviews/requests/${encodeURIComponent(reviewRequestId)}/voice-notes`);
}

// ── Per-criterion voice notes (Phase 7b — VoiceNoteRecorder) ──
//
// The backend voice-note endpoints (writing on ExpertEndpoints, speaking on
// SpeakingReviewVoiceNoteEndpoints) accept JSON referencing a MediaAsset. To
// keep the existing schema intact while still recording the *which-criterion*
// signal, the recorder uploads the audio to /v1/media/upload, then attaches
// the resulting MediaAsset with the criterion code carried through
// `writtenNotes` (human-readable tag) and `rubricJson` (machine-readable, for
// speaking). The shape returned is normalised to a small
// `{ voiceNoteId, url }` envelope the recorder component can consume.
export interface ReviewCriterionVoiceNoteResult {
  voiceNoteId: string;
  url: string;
  mediaAssetId: string;
}

async function uploadReviewCriterionVoiceNote(
  subtest: 'speaking' | 'writing',
  reviewRequestId: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  if (!reviewRequestId) {
    throw new ApiError(400, 'review_request_required', 'Review request id is required.', false);
  }
  if (!body.criterionCode) {
    throw new ApiError(400, 'criterion_required', 'Criterion code is required.', false);
  }
  if (!body.audio || body.audio.size === 0) {
    throw new ApiError(400, 'audio_required', 'A non-empty audio blob is required.', false);
  }

  const inferredType = body.audio.type || 'audio/webm';
  const fileExt = inferredType.includes('webm')
    ? 'webm'
    : inferredType.includes('mp4') || inferredType.includes('m4a')
      ? 'm4a'
      : inferredType.includes('wav')
        ? 'wav'
        : inferredType.includes('ogg')
          ? 'ogg'
          : 'webm';
  const fileName = `voice-note-${subtest}-${body.criterionCode}-${Date.now()}.${fileExt}`;
  const file = body.audio instanceof File ? body.audio : new File([body.audio], fileName, { type: inferredType });

  const uploaded = await uploadMedia(file);
  const durationSeconds = Math.max(0, Math.round(body.durationMs / 1000));
  const writtenNotes = `[criterion:${body.criterionCode}] Voice note (${durationSeconds}s)`;

  if (subtest === 'writing') {
    const response = await addWritingReviewVoiceNote(reviewRequestId, {
      mediaAssetId: uploaded.id,
      durationSeconds,
      writtenNotes,
      // The criterion scores cannot be intuited here; carry only the criterion
      // code via writtenNotes. rubricScores is left undefined so we don't
      // accidentally overwrite the saved draft scores.
    });
    return {
      voiceNoteId: response.item?.id ?? uploaded.id,
      url: response.item?.url ?? uploaded.url,
      mediaAssetId: uploaded.id,
    };
  }

  // Speaking — POST to SpeakingReviewVoiceNoteEndpoints (JSON body).
  const speakingResponse = await apiRequest<{ id: string; mediaAssetId?: string; url?: string }>(
    `/v1/expert/speaking/reviews/${encodeURIComponent(reviewRequestId)}/voice-notes`,
    {
      method: 'POST',
      body: JSON.stringify({
        mediaAssetId: uploaded.id,
        durationSeconds,
        writtenNotes,
        rubricJson: JSON.stringify({ criterionCode: body.criterionCode }),
      }),
    },
  );

  return {
    voiceNoteId: speakingResponse.id ?? uploaded.id,
    url: speakingResponse.url ?? uploaded.url,
    mediaAssetId: uploaded.id,
  };
}

export function uploadSpeakingReviewCriterionVoiceNote(
  reviewRequestId: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  return uploadReviewCriterionVoiceNote('speaking', reviewRequestId, body);
}

export function uploadWritingReviewCriterionVoiceNote(
  reviewRequestId: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  return uploadReviewCriterionVoiceNote('writing', reviewRequestId, body);
}

// ── Writing V2 marking voice note (System A, submission-keyed) ─────────────────
// One overall tutor voice note per writing submission (mock + normal). Distinct
// from uploadWritingReviewCriterionVoiceNote, which posts per-criterion notes to
// the older ReviewRequest-keyed expert flow. The signature matches the
// VoiceNoteRecorder `uploader` prop (criterionCode is accepted but ignored — the
// note is always the overall one).
export interface WritingMarkingVoiceNote {
  id: string;
  submissionId: string;
  mediaAssetId: string;
  url: string;
  durationSeconds: number | null;
  status: string;
  createdAt: string;
}

export async function uploadWritingMarkingVoiceNote(
  submissionId: string,
  body: { audio: Blob; criterionCode?: string; durationMs: number },
): Promise<ReviewCriterionVoiceNoteResult> {
  if (!submissionId) {
    throw new ApiError(400, 'submission_required', 'Submission id is required.', false);
  }
  if (!body.audio || body.audio.size === 0) {
    throw new ApiError(400, 'audio_required', 'A non-empty audio blob is required.', false);
  }

  const inferredType = body.audio.type || 'audio/webm';
  const fileExt = inferredType.includes('webm')
    ? 'webm'
    : inferredType.includes('mp4') || inferredType.includes('m4a')
      ? 'm4a'
      : inferredType.includes('wav')
        ? 'wav'
        : inferredType.includes('ogg')
          ? 'ogg'
          : 'webm';
  const fileName = `voice-note-writing-overall-${Date.now()}.${fileExt}`;
  const file = body.audio instanceof File ? body.audio : new File([body.audio], fileName, { type: inferredType });

  const uploaded = await uploadMedia(file);
  const durationSeconds = Math.max(0, Math.round(body.durationMs / 1000));
  const response = await apiRequest<WritingMarkingVoiceNote>(
    `/v1/writing/tutor/reviews/${encodeURIComponent(submissionId)}/voice-note`,
    {
      method: 'POST',
      body: JSON.stringify({ mediaAssetId: uploaded.id, durationSeconds }),
    },
  );
  return {
    voiceNoteId: response?.id ?? uploaded.id,
    url: response?.url ?? uploaded.url,
    mediaAssetId: uploaded.id,
  };
}

export async function getWritingSubmissionVoiceNote(
  submissionId: string,
): Promise<WritingMarkingVoiceNote | null> {
  return apiRequest<WritingMarkingVoiceNote | null>(
    `/v1/writing/submissions/${encodeURIComponent(submissionId)}/voice-note`,
    { method: 'GET' },
  );
}

interface LearnerReviewResultCriterion {
  code: string;
  name: string;
  score: number;
  maxScore: number;
  explanation: string;
}

interface LearnerReviewResultResponse {
  reviewRequestId: string;
  attemptId: string;
  subtest: string;
  state: string;
  completedAt?: string | null;
  submittedAt?: string | null;
  finalComment: string;
  scoreLabel?: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  criteria: LearnerReviewResultCriterion[];
}

export async function fetchLearnerReviewResult(reviewRequestId: string): Promise<LearnerReviewResultResponse> {
  return apiRequest(`/v1/reviews/requests/${encodeURIComponent(reviewRequestId)}/result`);
}

export async function fetchSpeakingReviewDetail(reviewRequestId: string): Promise<SpeakingReviewDetail> {
  return apiRequest<SpeakingReviewDetail>(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/speaking`);
}

export async function fetchExpertReviewHistory(reviewRequestId: string): Promise<ExpertReviewHistory> {
  return apiRequest<ExpertReviewHistory>(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/history`);
}

export async function saveDraftReview(draft: ReviewDraft): Promise<ReviewDraft> {
  const comments = Array.isArray(draft.comments) ? draft.comments : [];
  const hasTimestampComments = comments.some((comment) => 'timestampStart' in comment);
  const response = await apiRequest<{
    version: number;
    state: string;
    scores: Record<string, number>;
    criterionComments: Record<string, string>;
    finalComment: string;
    anchoredComments: ReviewDraft['comments'];
    timestampComments: ReviewDraft['comments'];
    scratchpad: string;
    checklistItems: { id: string; label: string; checked: boolean }[];
    savedAt: string;
  }>(`/v1/expert/reviews/${encodeURIComponent(draft.reviewRequestId)}/draft`, {
    method: 'PUT',
    body: JSON.stringify({
      scores: draft.scores,
      criterionComments: draft.criterionComments,
      finalComment: draft.finalComment,
      anchoredComments: hasTimestampComments ? undefined : comments,
      timestampComments: hasTimestampComments ? comments : undefined,
      scratchpad: draft.scratchpad,
      checklistItems: draft.checklistItems,
      version: draft.version,
    }),
  });

  return {
    reviewRequestId: draft.reviewRequestId,
    scores: response.scores,
    criterionComments: response.criterionComments,
    finalComment: response.finalComment,
    comments: hasTimestampComments ? response.timestampComments : response.anchoredComments,
    scratchpad: response.scratchpad,
    checklistItems: response.checklistItems,
    savedAt: response.savedAt,
    version: response.version,
  };
}

export async function submitExpertWritingReview(reviewRequestId: string, payload: { scores: Record<string, number>; criterionComments: Record<string, string>; finalComment: string; version?: number; }): Promise<{ success: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/writing/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function submitExpertSpeakingReview(reviewRequestId: string, payload: { scores: Record<string, number>; criterionComments: Record<string, string>; finalComment: string; version?: number; }): Promise<{ success: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/speaking/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function claimReview(reviewRequestId: string): Promise<{ claimed: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/queue/${encodeURIComponent(reviewRequestId)}/claim`, { method: 'POST' });
}

export async function releaseReview(reviewRequestId: string): Promise<{ released: boolean; reviewRequestId: string }> {
  return apiRequest(`/v1/expert/queue/${encodeURIComponent(reviewRequestId)}/release`, { method: 'POST' });
}

export async function requestRework(reviewRequestId: string, reason: string): Promise<{ success: boolean }> {
  return apiRequest(`/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/rework`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function submitCalibrationCase(
  caseId: string,
  payload: { scores: Record<string, number>; notes?: string },
): Promise<{ success: boolean; caseId: string; alignment: number }> {
  return apiRequest(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}/submit`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function saveCalibrationDraft(
  caseId: string,
  payload: { scores: Record<string, number>; notes?: string },
): Promise<{ success: boolean; caseId: string; isDraft: boolean; scores: Record<string, number>; notes: string; updatedAt: string | null }> {
  return apiRequest(`/v1/expert/calibration/cases/${encodeURIComponent(caseId)}/draft`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

// ─── Expert calibration history + alignment (supplement §4.8) ───

export interface ExpertCalibrationHistoryEntry {
  id: string;
  caseId: string;
  caseTitle: string;
  profession: string;
  subTest: string;
  benchmarkScore: number;
  reviewerScore: number;
  alignmentScore: number;
  disagreementSummary: string;
  submittedAt: string;
}

export interface ExpertCalibrationHistory {
  entries: ExpertCalibrationHistoryEntry[];
  totalCount: number;
  generatedAt: string;
}

export interface ExpertCalibrationAlignmentBreakdown {
  subTest: string;
  submissionCount: number;
  averageAlignment: number;
  latestAlignment: number | null;
}

export interface ExpertCalibrationAlignmentTrendPoint {
  submittedAt: string;
  alignmentScore: number;
}

export interface ExpertCalibrationAlignment {
  totalSubmissions: number;
  overallAverageAlignment: number;
  latestAlignment: number | null;
  previousAlignment: number | null;
  deltaFromPrevious: number | null;
  perSubTest: ExpertCalibrationAlignmentBreakdown[];
  trend: ExpertCalibrationAlignmentTrendPoint[];
  generatedAt: string;
}

export async function fetchExpertCalibrationHistory(limit?: number): Promise<ExpertCalibrationHistory> {
  const query = typeof limit === 'number' ? `?limit=${limit}` : '';
  return apiRequest<ExpertCalibrationHistory>(`/v1/expert/calibration/history${query}`);
}

export async function fetchExpertCalibrationAlignment(): Promise<ExpertCalibrationAlignment> {
  return apiRequest<ExpertCalibrationAlignment>('/v1/expert/calibration/alignment');
}

// ─── Expert availability constraints (supplement: GET /v1/expert/availability/constraints) ───

export interface ExpertAvailabilityConstraints {
  minNoticeHours: number;
  maxHoursPerWeek: number;
  maxExceptionsPerMonth: number;
  minSlotDuration: string;
  maxSlotDuration: string;
  supportedTimezones: string[];
  dayKeys: string[];
}

export async function fetchExpertAvailabilityConstraints(): Promise<ExpertAvailabilityConstraints> {
  return apiRequest<ExpertAvailabilityConstraints>('/v1/expert/availability/constraints');
}
