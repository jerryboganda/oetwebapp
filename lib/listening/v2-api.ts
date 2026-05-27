import { apiClient } from '@/lib/api';
import type { ListeningFsmState } from '@/lib/listening/transitions';
import type { ListeningReviewDto } from '@/lib/listening-api';

/**
 * Listening V2 API client. Targets the backend `/v1/listening/v2/...`
 * endpoint group introduced in this rebuild. Kept as a focused module so
 * the legacy V1 client in `lib/listening-api.ts` continues to work unchanged.
 */

export interface ListeningV2SessionState {
  attemptId: string;
  mode: string;
  state: ListeningFsmState;
  locks: string[];
  windowDurationMs: number;
  windowRemainingMs: number;
  confirmRequired: boolean;
  freeNavigation: boolean;
  oneWayLocks: boolean;
  unansweredWarningRequired: boolean;
  /**
   * R08 annotations payload (highlights + strikethroughs) last persisted by
   * the learner on this attempt. Null until first save. Hydrated by the
   * frontend reducer on mount via `useListeningAnnotations`.
   */
  annotationsJson: string | null;
}

export interface ListeningAnnotationsResult {
  annotationsJson: string | null;
}

export type AdvanceOutcome = 'applied' | 'confirm-required' | 'rejected';

export interface AdvanceResult {
  outcome: AdvanceOutcome;
  state: ListeningV2SessionState | null;
  confirmToken: string | null;
  confirmTokenTtlMs: number | null;
  rejectionReason: string | null;
  rejectionDetail: string | null;
}

export interface AudioResumeResult {
  resume: boolean;
  serverState: ListeningFsmState;
  resumeAtMs: number;
  reason: string;
}

export interface TechReadinessResult {
  audioOk: boolean;
  durationMs: number;
  checkedAt: string;
  ttlMs: number;
  // 2026-05-27 audit fix — Listening rule L-R10.3 wired-audio gate.
  audioOutputDeviceLabel?: string | null;
  audioInputDeviceLabel?: string | null;
  bluetoothAudioDetected?: boolean;
  resolutionMeetsMinimum?: boolean;
  displayScaleAcceptable?: boolean;
}

export interface TechReadinessProbe {
  audioOk: boolean;
  durationMs: number;
  /** Output device label from navigator.mediaDevices.enumerateDevices(); used to detect Bluetooth. */
  audioOutputDeviceLabel?: string | null;
  /** Input device label (microphone). */
  audioInputDeviceLabel?: string | null;
  /** Screen.width / Screen.height — checked against the 1920×1080 minimum. */
  screenWidth?: number | null;
  screenHeight?: number | null;
  /** window.devicePixelRatio * 100 (a coarse proxy for Windows display scale). */
  displayScalePercent?: number | null;
}

export interface ListeningGradingResult {
  attemptId: string;
  rawScore: number;
  maxRawScore: number;
  scaledScore: number;
}

export interface ListeningPathwayStageView {
  stage: string;
  status: 'Locked' | 'Unlocked' | 'InProgress' | 'Completed';
  scaledScore: number | null;
  completedAt: string | null;
  actionHref: string | null;
}

export const listeningV2Api = {
  getState(attemptId: string) {
    return apiClient.get<ListeningV2SessionState>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/state`,
    );
  },
  advance(attemptId: string, toState: ListeningFsmState, confirmToken: string | null) {
    return apiClient.postWithAcceptedStatuses<AdvanceResult>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/advance`,
      { toState, confirmToken },
      [412, 422],
    );
  },
  audioResume(attemptId: string, cuePointMs: number) {
    return apiClient.post<AudioResumeResult>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/audio-resume`,
      { cuePointMs },
    );
  },
  saveAnswer(attemptId: string, questionId: string, userAnswer: string | null) {
    return apiClient.put<void>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/answers/${encodeURIComponent(questionId)}`,
      { userAnswer },
    );
  },
  submit(attemptId: string, answers?: Record<string, string | null>) {
    return apiClient.post<ListeningReviewDto>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/submit`,
      { answers: answers ?? {} },
    );
  },
  recordTechReadiness(attemptId: string, result: TechReadinessProbe) {
    return apiClient.post<TechReadinessResult>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/tech-readiness`,
      result,
    );
  },
  grade(attemptId: string) {
    return apiClient.post<ListeningGradingResult>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/grade`,
      {},
    );
  },
  myPathway() {
    return apiClient.get<ListeningPathwayStageView[]>('/v1/listening/v2/me/pathway');
  },
  /**
   * R08 — persist learner highlights + strikethroughs. Server caps the
   * payload at 64 KB and validates JSON shape; treat 400 responses as a
   * permanent failure (do not retry the same payload).
   */
  saveAnnotations(attemptId: string, annotationsJson: string | null) {
    return apiClient.put<void>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/annotations`,
      { annotationsJson },
    );
  },
  getAnnotations(attemptId: string) {
    return apiClient.get<ListeningAnnotationsResult>(
      `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/annotations`,
    );
  },
};

export const teacherClassApi = {
  list() {
    return apiClient.get<TeacherClassDto[]>('/v1/listening/v2/teacher/classes');
  },
  create(name: string, description?: string | null) {
    return apiClient.post<TeacherClassDto>('/v1/listening/v2/teacher/classes', { name, description });
  },
  delete(classId: string) {
    return apiClient.delete<void>(`/v1/listening/v2/teacher/classes/${encodeURIComponent(classId)}`);
  },
  addMember(classId: string, memberUserId: string) {
    return apiClient.post<void>(
      `/v1/listening/v2/teacher/classes/${encodeURIComponent(classId)}/members`,
      { memberUserId },
    );
  },
  removeMember(classId: string, memberUserId: string) {
    return apiClient.delete<void>(
      `/v1/listening/v2/teacher/classes/${encodeURIComponent(classId)}/members/${encodeURIComponent(memberUserId)}`,
    );
  },
  async analytics(classId: string, days = 30) {
    const result = await apiClient.get<ListeningClassAnalyticsApiDto>(
      `/v1/listening/v2/teacher/classes/${encodeURIComponent(classId)}/analytics?days=${encodeURIComponent(days)}`,
    );
    return toTeacherClassAnalytics(result);
  },
};

export interface TeacherClassDto {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ListeningPartBreakdownDto {
  partCode: string;
  earned: number;
  max: number;
  accuracyPercent: number;
}

export interface ListeningHardestQuestionDto {
  paperId: string;
  paperTitle: string;
  questionNumber: number;
  partCode: string;
  attemptCount: number;
  accuracyPercent: number;
}

interface ListeningClassAnalyticsApiDto {
  classId: string;
  className: string;
  description: string | null;
  memberCount: number;
  analytics: ListeningTeacherAnalyticsDto;
}

export interface ListeningTeacherDistractorHeatDto {
  paperId: string;
  questionNumber: number;
  correctAnswer: string;
  wrongAnswerCount: number;
}

export interface ListeningTeacherAnalyticsDto {
  days: number;
  completedAttempts: number;
  averageScaledScore: number | null;
  percentLikelyPassing: number;
  classPartAverages: ListeningPartBreakdownDto[];
  hardestQuestions: ListeningHardestQuestionDto[];
  distractorHeat: ListeningTeacherDistractorHeatDto[];
}

export interface ListeningClassAnalyticsDto {
  classId: string;
  className: string;
  description: string | null;
  memberCount: number;
  analytics: ListeningTeacherAnalyticsDto;
}

function toTeacherClassAnalytics(result: ListeningClassAnalyticsApiDto): ListeningClassAnalyticsDto {
  return {
    classId: result.classId,
    className: result.className,
    description: result.description,
    memberCount: result.memberCount,
    analytics: {
      days: result.analytics.days,
      completedAttempts: result.analytics.completedAttempts,
      averageScaledScore: result.analytics.averageScaledScore,
      percentLikelyPassing: result.analytics.percentLikelyPassing,
      classPartAverages: result.analytics.classPartAverages,
      hardestQuestions: result.analytics.hardestQuestions,
      distractorHeat: result.analytics.distractorHeat.map((item) => ({
        paperId: item.paperId,
        questionNumber: item.questionNumber,
        correctAnswer: item.correctAnswer,
        wrongAnswerCount: item.wrongAnswerCount,
      })),
    },
  };
}
