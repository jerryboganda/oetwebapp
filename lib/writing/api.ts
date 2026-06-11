/**
 * Writing Module V2 — typed API client.
 *
 * All 60+ writing V2 endpoints. Grouped by domain. Uses the shared
 * `apiClient` from `lib/api.ts` (which already handles auth headers,
 * CSRF, retries, timeouts via `ensureFreshAccessToken`).
 *
 * Contract with Wave B (WS6 endpoints): the path strings, methods, and
 * request/response shapes in this file ARE the API contract. Backend
 * route handlers MUST conform; if a divergence appears at integration,
 * fix the backend, not this file.
 *
 * Route table (Wave B reference):
 *
 *   ─ Onboarding & profile ─────────────────────────────────────────────
 *   GET    /v1/writing/v2/profile
 *   POST   /v1/writing/profile
 *   GET    /v1/writing/profile/budget
 *   POST   /v1/writing/onboarding/complete
 *
 *   ─ Diagnostic ───────────────────────────────────────────────────────
 *   POST   /v1/writing/diagnostic/start
 *   GET    /v1/writing/diagnostic/sessions/{id}
 *   POST   /v1/writing/diagnostic/sessions/{id}/begin-writing
 *   POST   /v1/writing/diagnostic/sessions/{id}/submit
 *   GET    /v1/writing/diagnostic/sessions/{id}/results
 *
 *   ─ Pathway / today ──────────────────────────────────────────────────
 *   GET    /v1/writing/v2/pathway
 *   POST   /v1/writing/pathway/recalculate
 *   GET    /v1/writing/v2/today
 *   POST   /v1/writing/today/items/{id}/complete
 *   POST   /v1/writing/today/regenerate
 *
 *   ─ Submissions / grade / appeal / dispute ──────────────────────────
 *   POST   /v1/writing/submissions
 *   GET    /v1/writing/submissions/{id}
 *   GET    /v1/writing/submissions/{id}/grade
 *   POST   /v1/writing/submissions/{id}/revise
 *   POST   /v1/writing/submissions/{id}/appeal
 *   POST   /v1/writing/submissions/{id}/dispute-violation
 *
 *   ─ Drafts V2 ────────────────────────────────────────────────────────
 *   PUT    /v1/writing/drafts/{scenarioId}/{mode}
 *   GET    /v1/writing/drafts/{scenarioId}/{mode}
 *   DELETE /v1/writing/drafts/{scenarioId}/{mode}
 *
 *   ─ Scenarios ───────────────────────────────────────────────────────
 *   GET    /v1/writing/scenarios
 *   GET    /v1/writing/scenarios/{id}
 *   GET    /v1/writing/scenarios/random
 *
 *   ─ Drills ──────────────────────────────────────────────────────────
 *   GET    /v1/writing/v2/drills
 *   GET    /v1/writing/v2/drills/{id}
 *   POST   /v1/writing/v2/drills/{id}/attempt
 *   GET    /v1/writing/v2/drills/case-notes
 *   POST   /v1/writing/v2/drills/case-notes/{id}/attempt
 *
 *   ─ Lessons ─────────────────────────────────────────────────────────
 *   GET    /v1/writing/v2/lessons
 *   GET    /v1/writing/v2/lessons/{id}
 *   POST   /v1/writing/v2/lessons/{id}/complete
 *
 *   ─ Mocks ───────────────────────────────────────────────────────────
 *   GET    /v1/writing/mocks
 *   POST   /v1/writing/mocks/start
 *   GET    /v1/writing/mocks/sessions/{id}
 *   POST   /v1/writing/mocks/sessions/{id}/submit
 *   GET    /v1/writing/mocks/sessions/{id}/results
 *
 *   ─ Coach ───────────────────────────────────────────────────────────
 *   POST   /v1/writing/coach/hints
 *   (WebSocket: ws://.../ws/writing/coach/{sessionId} — see realtime.ts)
 *
 *   ─ Stats ───────────────────────────────────────────────────────────
 *   GET    /v1/writing/stats/dashboard
 *   GET    /v1/writing/stats/bands
 *   GET    /v1/writing/stats/criteria
 *   GET    /v1/writing/stats/letter-types
 *   GET    /v1/writing/stats/canon
 *   GET    /v1/writing/stats/time
 *   GET    /v1/writing/stats/skills
 *   GET    /v1/writing/stats/readiness
 *   GET    /v1/writing/stats/calendar
 *   GET    /v1/writing/stats/export
 *
 *   ─ Canon library ───────────────────────────────────────────────────
 *   GET    /v1/writing/v2/canon
 *   GET    /v1/writing/v2/canon/{ruleId}
 *   GET    /v1/writing/v2/canon/{ruleId}/violations/mine
 *
 *   ─ Mistakes ────────────────────────────────────────────────────────
 *   GET    /v1/writing/mistakes
 *   GET    /v1/writing/mistakes/mine
 *   GET    /v1/writing/mistakes/{id}
 *
 *   ─ Tutor review (learner-facing) ───────────────────────────────────
 *   POST   /v1/writing/submissions/{id}/request-tutor-review
 *   GET    /v1/writing/submissions/{id}/tutor-review
 *
 *   ─ OCR ─────────────────────────────────────────────────────────────
 *   POST   /v1/writing/ocr/upload                 (multipart)
 *   GET    /v1/writing/ocr/jobs/{id}
 *
 *   ─ Showcase ────────────────────────────────────────────────────────
 *   GET    /v1/writing/showcase
 *   POST   /v1/writing/submissions/{id}/showcase
 *
 *   ─ Tools (AI utilities) ────────────────────────────────────────────
 *   POST   /v1/writing/tools/rewrite
 *   POST   /v1/writing/tools/paraphrase
 *   POST   /v1/writing/tools/ask
 *   POST   /v1/writing/tools/outline
 */

import { apiClient } from '../api';
import type {
  WritingProfileDto,
  WritingProfileBudgetDto,
  WritingPathwayV2Dto,
  WritingTodayPlanDto,
  WritingScenarioDto,
  WritingSubmissionDto,
  WritingGradeDto,
  WritingScoreAppealDto,
  WritingDisputeViolationDto,
  WritingDraftV2Dto,
  WritingDrillDto,
  WritingDrillResponseDto,
  WritingDrillAttemptResultDto,
  WritingCaseNoteDrillDto,
  WritingCaseNoteDrillAttemptResultDto,
  WritingLessonDto,
  WritingLessonCompletionDto,
  WritingMockDto,
  WritingMockSessionDto,
  WritingCoachHintDto,
  WritingStatsDashboardDto,
  WritingStatsBandsDto,
  WritingStatsCriteriaDto,
  WritingStatsLetterTypesDto,
  WritingStatsCanonDto,
  WritingStatsTimeDto,
  WritingStatsSkillsDto,
  WritingStatsCalendarDto,
  WritingCanonRuleV2Dto,
  WritingCanonViolationDto,
  WritingCommonMistakeDto,
  WritingLearnerMistakeStatDto,
  WritingTutorReviewDto,
  WritingOcrJobDto,
  WritingShowcasePostDto,
  WritingRewriteResultDto,
  WritingParaphraseResultDto,
  WritingAskTurnResponseDto,
  WritingOutlineResultDto,
  WritingReadinessScoreDto,
  WritingProfession,
  WritingLetterType,
  WritingEditorMode,
  WritingSubSkill,
  WritingDrillType,
} from './types';

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const u = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === null || v === undefined) continue;
    u.set(k, String(v));
  }
  const s = u.toString();
  return s.length > 0 ? `?${s}` : '';
}

function path(template: string, parts: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (_, key) => {
    const value = parts[key];
    if (value === undefined) {
      throw new Error(`Missing path parameter: ${key}`);
    }
    return encodeURIComponent(value);
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Onboarding & profile
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingProfileSavePayload {
  profession: WritingProfession;
  subDiscipline?: string | null;
  yearsExperience?: number | null;
  targetBand: string;
  examDate: string | null;
  daysPerWeek: number;
  minutesPerDay: number;
  targetCountry: string;
  letterTypeFocus: WritingLetterType[];
  optInCommunity?: boolean;
  optInLeaderboard?: boolean;
  optInDataForTraining?: boolean;
}

export const getWritingV2Profile = () =>
  apiClient.get<WritingProfileDto>('/v1/writing/v2/profile');

export const saveWritingV2Profile = (payload: WritingProfileSavePayload) =>
  apiClient.post<WritingProfileDto>('/v1/writing/profile', payload);

export const getWritingProfileBudget = () =>
  apiClient.get<WritingProfileBudgetDto>('/v1/writing/profile/budget');

export const completeWritingOnboarding = () =>
  apiClient.post<WritingProfileDto>('/v1/writing/onboarding/complete', {});

// ─────────────────────────────────────────────────────────────────────────────
// Diagnostic
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingDiagnosticSessionDto {
  id: string;
  scenarioId: string;
  phase: 'reading' | 'writing' | 'submitted';
  readingSecondsRemaining: number;
  writingSecondsRemaining: number;
  startedAt: string;
  readingPhaseEndedAt: string | null;
  submittedAt: string | null;
  submissionId: string | null;
}

export interface WritingDiagnosticResultsDto {
  sessionId: string;
  submissionId: string;
  grade: WritingGradeDto;
  pathwayPreview: WritingPathwayV2Dto;
}

export const startWritingDiagnostic = () =>
  apiClient.post<WritingDiagnosticSessionDto>('/v1/writing/diagnostic/start', {});

export const getWritingDiagnosticSession = (sessionId: string) =>
  apiClient.get<WritingDiagnosticSessionDto>(
    path('/v1/writing/diagnostic/sessions/{id}', { id: sessionId }),
  );

export const beginWritingDiagnosticWriting = (sessionId: string) =>
  apiClient.post<WritingDiagnosticSessionDto>(
    path('/v1/writing/diagnostic/sessions/{id}/begin-writing', { id: sessionId }),
    {},
  );

export interface WritingDiagnosticSubmitPayload {
  letterContent: string;
  wordCount: number;
  timeSpentSeconds: number;
}

export const submitWritingDiagnostic = (sessionId: string, payload: WritingDiagnosticSubmitPayload) =>
  apiClient.post<WritingSubmissionDto>(
    path('/v1/writing/diagnostic/sessions/{id}/submit', { id: sessionId }),
    payload,
  );

export const getWritingDiagnosticResults = (sessionId: string) =>
  apiClient.get<WritingDiagnosticResultsDto>(
    path('/v1/writing/diagnostic/sessions/{id}/results', { id: sessionId }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// Pathway / today
// ─────────────────────────────────────────────────────────────────────────────

export const getWritingPathwayV2 = () =>
  apiClient.get<WritingPathwayV2Dto>('/v1/writing/v2/pathway');

export const recalculateWritingPathway = () =>
  apiClient.post<WritingPathwayV2Dto>('/v1/writing/pathway/recalculate', {});

export const getWritingTodayPlanV2 = () =>
  apiClient.get<WritingTodayPlanDto>('/v1/writing/v2/today');

export const completeWritingTodayItem = (itemId: string) =>
  apiClient.post<WritingTodayPlanDto>(
    path('/v1/writing/today/items/{id}/complete', { id: itemId }),
    {},
  );

export const regenerateWritingTodayPlan = () =>
  apiClient.post<WritingTodayPlanDto>('/v1/writing/today/regenerate', {});

// ─────────────────────────────────────────────────────────────────────────────
// Submissions / grade / appeal / dispute
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingSubmissionCreatePayload {
  scenarioId: string;
  mode: WritingEditorMode;
  letterContent: string;
  wordCount: number;
  timeSpentSeconds: number;
  inputSource?: 'editor' | 'paper-ocr' | 'voice-draft';
}

export const createWritingSubmission = (payload: WritingSubmissionCreatePayload) =>
  apiClient.post<WritingSubmissionDto>('/v1/writing/submissions', payload);

export const getWritingSubmission = (submissionId: string) =>
  apiClient.get<WritingSubmissionDto>(
    path('/v1/writing/submissions/{id}', { id: submissionId }),
  );

export const getWritingSubmissionGrade = (submissionId: string) =>
  apiClient.get<WritingGradeDto>(
    path('/v1/writing/submissions/{id}/grade', { id: submissionId }),
  );

export const reviseWritingSubmission = (submissionId: string, payload: { letterContent: string; wordCount: number; timeSpentSeconds: number }) =>
  apiClient.post<WritingSubmissionDto>(
    path('/v1/writing/submissions/{id}/revise', { id: submissionId }),
    payload,
  );

export interface WritingAppealRequestPayload {
  reason: string;
}

export const appealWritingSubmission = (submissionId: string, payload: WritingAppealRequestPayload) =>
  apiClient.post<WritingScoreAppealDto>(
    path('/v1/writing/submissions/{id}/appeal', { id: submissionId }),
    payload,
  );

/**
 * Writing V2 Score Appeal alias — preferred name per appeal page spec.
 * Routes to the same backend endpoint as `appealWritingSubmission`.
 */
export const requestWritingAppeal = (submissionId: string, reason: string) =>
  appealWritingSubmission(submissionId, { reason });

/**
 * Read the latest appeal record for a submission so the UI can poll
 * status (pending → in_progress → resolved). Returns null when no appeal
 * exists yet for this submission.
 */
export const getWritingAppealResult = async (submissionId: string): Promise<WritingScoreAppealDto | null> => {
  try {
    return await apiClient.get<WritingScoreAppealDto>(
      path('/v1/writing/submissions/{id}/appeal', { id: submissionId }),
    );
  } catch (err) {
    if (err && typeof err === 'object' && 'status' in err && (err as { status: number }).status === 404) {
      return null;
    }
    throw err;
  }
};

export const disputeWritingCanonViolation = (submissionId: string, payload: WritingDisputeViolationDto) =>
  apiClient.post<WritingCanonViolationDto>(
    path('/v1/writing/submissions/{id}/dispute-violation', { id: submissionId }),
    payload,
  );

// ─────────────────────────────────────────────────────────────────────────────
// Drafts V2
// ─────────────────────────────────────────────────────────────────────────────

export const putWritingDraftV2 = (scenarioId: string, mode: WritingEditorMode, payload: { content: string; wordCount: number; timeSpentSeconds: number }) =>
  apiClient.put<WritingDraftV2Dto>(
    path('/v1/writing/drafts/{scenarioId}/{mode}', { scenarioId, mode }),
    payload,
  );

export const getWritingDraftV2 = (scenarioId: string, mode: WritingEditorMode) =>
  apiClient.get<WritingDraftV2Dto | null>(
    path('/v1/writing/drafts/{scenarioId}/{mode}', { scenarioId, mode }),
  );

export const deleteWritingDraftV2 = (scenarioId: string, mode: WritingEditorMode) =>
  apiClient.delete<void>(
    path('/v1/writing/drafts/{scenarioId}/{mode}', { scenarioId, mode }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// Scenarios
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingScenarioListQuery {
  profession?: WritingProfession;
  letterType?: WritingLetterType;
  difficulty?: number;
  isDiagnostic?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

export const listWritingScenarios = (query: WritingScenarioListQuery = {}) =>
  apiClient.get<{ items: WritingScenarioDto[]; total: number }>(
    `/v1/writing/scenarios${qs(query as Record<string, string | number | boolean | undefined>)}`,
  );

export const getWritingScenario = (scenarioId: string) =>
  apiClient.get<WritingScenarioDto>(
    path('/v1/writing/scenarios/{id}', { id: scenarioId }),
  );

export const getRandomWritingScenario = (query: { profession?: WritingProfession; letterType?: WritingLetterType } = {}) =>
  apiClient.get<WritingScenarioDto>(
    `/v1/writing/scenarios/random${qs(query as Record<string, string | undefined>)}`,
  );

// ─────────────────────────────────────────────────────────────────────────────
// Drills
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingDrillListQuery {
  drillType?: WritingDrillType;
  targetSubSkill?: WritingSubSkill;
  profession?: WritingProfession;
  letterType?: WritingLetterType;
  difficulty?: number;
  dueOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export const listWritingDrills = (query: WritingDrillListQuery = {}) =>
  apiClient.get<{ items: WritingDrillDto[]; total: number }>(
    `/v1/writing/v2/drills${qs(query as Record<string, string | number | boolean | undefined>)}`,
  );

export const getWritingDrill = (drillId: string) =>
  apiClient.get<WritingDrillDto>(
    path('/v1/writing/v2/drills/{id}', { id: drillId }),
  );

export const submitWritingDrillAttempt = (drillId: string, payload: WritingDrillResponseDto) =>
  apiClient.post<WritingDrillAttemptResultDto>(
    path('/v1/writing/v2/drills/{id}/attempt', { id: drillId }),
    payload,
  );

export const listCaseNoteDrills = (query: { profession?: WritingProfession; format?: string } = {}) =>
  apiClient.get<{ items: WritingCaseNoteDrillDto[]; total: number }>(
    `/v1/writing/v2/drills/case-notes${qs(query as Record<string, string | undefined>)}`,
  );

export const submitCaseNoteDrillAttempt = (drillId: string, payload: { selectedIndices: number[] }) =>
  apiClient.post<WritingCaseNoteDrillAttemptResultDto>(
    path('/v1/writing/v2/drills/case-notes/{id}/attempt', { id: drillId }),
    payload,
  );

// ─────────────────────────────────────────────────────────────────────────────
// Lessons
// ─────────────────────────────────────────────────────────────────────────────

export const listWritingLessons = (query: { subSkill?: WritingSubSkill } = {}) =>
  apiClient.get<{ items: WritingLessonDto[]; completions: WritingLessonCompletionDto[] }>(
    `/v1/writing/v2/lessons${qs(query as Record<string, string | undefined>)}`,
  );

export const getWritingLesson = (lessonId: string) =>
  apiClient.get<WritingLessonDto>(
    path('/v1/writing/v2/lessons/{id}', { id: lessonId }),
  );

export const completeWritingLesson = (lessonId: string, payload: { quizScore: number; quizAnswers: number[] }) =>
  apiClient.post<WritingLessonCompletionDto>(
    path('/v1/writing/v2/lessons/{id}/complete', { id: lessonId }),
    payload,
  );

// ─────────────────────────────────────────────────────────────────────────────
// Mocks
// ─────────────────────────────────────────────────────────────────────────────

export const listWritingMocks = () =>
  apiClient.get<{ items: WritingMockDto[] }>('/v1/writing/mocks');

export const startWritingMock = (payload: { mockId: string; isPractice?: boolean }) =>
  apiClient.post<WritingMockSessionDto>('/v1/writing/mocks/start', payload);

export const getWritingMockSession = (sessionId: string) =>
  apiClient.get<WritingMockSessionDto>(
    path('/v1/writing/mocks/sessions/{id}', { id: sessionId }),
  );

export const beginWritingMockWriting = (sessionId: string) =>
  apiClient.post<WritingMockSessionDto>(
    path('/v1/writing/mocks/sessions/{id}/begin-writing', { id: sessionId }),
    {},
  );

export const submitWritingMock = (sessionId: string, payload: { letterContent: string; wordCount: number; timeSpentSeconds: number }) =>
  apiClient.post<WritingSubmissionDto>(
    path('/v1/writing/mocks/sessions/{id}/submit', { id: sessionId }),
    payload,
  );

export const getWritingMockResults = (sessionId: string) =>
  // `grade` is null while a mock submission is awaiting human examiner marking
  // (mock Writing is never AI-graded). `status` is "awaiting_review" until a
  // tutor submits the mark, then "graded".
  apiClient.get<{ session: WritingMockSessionDto; grade: WritingGradeDto | null; status: string }>(
    path('/v1/writing/mocks/sessions/{id}/results', { id: sessionId }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// Coach (HTTP fallback; live channel in realtime.ts)
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingCoachHintRequestPayload {
  sessionId: string;
  scenarioId: string;
  letterContent: string;
  wordCount: number;
  letterType: WritingLetterType;
  profession: WritingProfession;
}

export const requestWritingCoachHints = (payload: WritingCoachHintRequestPayload) =>
  apiClient.post<{ hints: WritingCoachHintDto[] }>('/v1/writing/coach/hints', payload);

// ─────────────────────────────────────────────────────────────────────────────
// Stats
// ─────────────────────────────────────────────────────────────────────────────

export const getWritingStatsDashboard = () =>
  apiClient.get<WritingStatsDashboardDto>('/v1/writing/stats/dashboard');

export const getWritingStatsBands = () =>
  apiClient.get<WritingStatsBandsDto>('/v1/writing/stats/bands');

export const getWritingStatsCriteria = () =>
  apiClient.get<WritingStatsCriteriaDto>('/v1/writing/stats/criteria');

export const getWritingStatsLetterTypes = () =>
  apiClient.get<WritingStatsLetterTypesDto>('/v1/writing/stats/letter-types');

export const getWritingStatsCanon = () =>
  apiClient.get<WritingStatsCanonDto>('/v1/writing/stats/canon');

export const getWritingStatsTime = () =>
  apiClient.get<WritingStatsTimeDto>('/v1/writing/stats/time');

export const getWritingStatsSkills = () =>
  apiClient.get<WritingStatsSkillsDto>('/v1/writing/stats/skills');

export const getWritingReadiness = () =>
  apiClient.get<WritingReadinessScoreDto>('/v1/writing/stats/readiness');

export const getWritingStatsCalendar = () =>
  apiClient.get<WritingStatsCalendarDto>('/v1/writing/stats/calendar');

export const exportWritingStats = () =>
  apiClient.get<{ url: string }>('/v1/writing/stats/export');

// ─────────────────────────────────────────────────────────────────────────────
// Canon library
// ─────────────────────────────────────────────────────────────────────────────

export const listWritingCanonRules = (query: { search?: string; severity?: string; category?: string } = {}) =>
  apiClient.get<{ items: WritingCanonRuleV2Dto[] }>(
    `/v1/writing/v2/canon${qs(query as Record<string, string | undefined>)}`,
  );

export const getWritingCanonRule = (ruleId: string) =>
  apiClient.get<WritingCanonRuleV2Dto>(
    path('/v1/writing/v2/canon/{ruleId}', { ruleId }),
  );

export const getMyCanonViolationsForRule = (ruleId: string) =>
  apiClient.get<{ items: WritingCanonViolationDto[] }>(
    path('/v1/writing/v2/canon/{ruleId}/violations/mine', { ruleId }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// Mistakes
// ─────────────────────────────────────────────────────────────────────────────

export const listCommonMistakes = (query: { category?: string; subSkill?: WritingSubSkill } = {}) =>
  apiClient.get<{ items: WritingCommonMistakeDto[] }>(
    `/v1/writing/mistakes${qs(query as Record<string, string | undefined>)}`,
  );

export const listMyCommonMistakes = () =>
  apiClient.get<{ items: Array<WritingCommonMistakeDto & { stat: WritingLearnerMistakeStatDto }> }>(
    '/v1/writing/mistakes/mine',
  );

export const getCommonMistake = (mistakeId: string) =>
  apiClient.get<WritingCommonMistakeDto>(
    path('/v1/writing/mistakes/{id}', { id: mistakeId }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// Tutor review (learner-facing)
// ─────────────────────────────────────────────────────────────────────────────

export const requestTutorReview = (submissionId: string, payload: { priority?: 'standard' | 'priority' } = {}) =>
  apiClient.post<WritingTutorReviewDto>(
    path('/v1/writing/submissions/{id}/request-tutor-review', { id: submissionId }),
    payload,
  );

export const getTutorReview = (submissionId: string) =>
  apiClient.get<WritingTutorReviewDto | null>(
    path('/v1/writing/submissions/{id}/tutor-review', { id: submissionId }),
  );

export const getTutorReviewDetail = (submissionId: string) =>
  apiClient.get<{ submission: WritingSubmissionDto; grade: WritingGradeDto | null }>(
    path('/v1/tutors/writing/reviews/{id}', { id: submissionId }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// OCR
// ─────────────────────────────────────────────────────────────────────────────

export const uploadWritingOcrImages = (files: File[], options: { submissionId?: string } = {}) => {
  const form = new FormData();
  for (const file of files) {
    form.append('images', file, file.name);
  }
  if (options.submissionId) {
    form.append('submissionId', options.submissionId);
  }
  return apiClient.postForm<WritingOcrJobDto>('/v1/writing/ocr/upload', form);
};

export const getWritingOcrJob = (jobId: string) =>
  apiClient.get<WritingOcrJobDto>(
    path('/v1/writing/ocr/jobs/{id}', { id: jobId }),
  );

// ─────────────────────────────────────────────────────────────────────────────
// Showcase
// ─────────────────────────────────────────────────────────────────────────────

export const listShowcasePosts = (query: { profession?: WritingProfession; letterType?: WritingLetterType; page?: number; pageSize?: number } = {}) =>
  apiClient.get<{ items: WritingShowcasePostDto[]; total: number }>(
    `/v1/writing/showcase${qs(query as Record<string, string | number | undefined>)}`,
  );

export const publishToShowcase = (submissionId: string) =>
  apiClient.post<WritingShowcasePostDto>(
    path('/v1/writing/submissions/{id}/showcase', { id: submissionId }),
    {},
  );

// ─────────────────────────────────────────────────────────────────────────────
// Tools (rewrite / paraphrase / ask / outline)
// ─────────────────────────────────────────────────────────────────────────────

export interface WritingRewriteRequestPayload {
  text: string;
  letterType: WritingLetterType;
  profession: WritingProfession;
  preserveFacts?: boolean;
}

export const requestWritingRewrite = (payload: WritingRewriteRequestPayload) =>
  apiClient.post<WritingRewriteResultDto>('/v1/writing/tools/rewrite', payload);

export interface WritingParaphraseRequestPayload {
  text: string;
  formality?: 'formal' | 'neutral' | 'concise';
}

export const requestWritingParaphrase = (payload: WritingParaphraseRequestPayload) =>
  apiClient.post<WritingParaphraseResultDto>('/v1/writing/tools/paraphrase', payload);

export interface WritingAskRequestPayload {
  threadId?: string;
  letterContent: string;
  scenarioId: string;
  question: string;
}

export const requestWritingAsk = (payload: WritingAskRequestPayload) =>
  apiClient.post<WritingAskTurnResponseDto>('/v1/writing/tools/ask', payload);

export interface WritingOutlineRequestPayload {
  scenarioId: string;
  letterType: WritingLetterType;
  profession: WritingProfession;
}

export const requestWritingOutline = (payload: WritingOutlineRequestPayload) =>
  apiClient.post<WritingOutlineResultDto>('/v1/writing/tools/outline', payload);
