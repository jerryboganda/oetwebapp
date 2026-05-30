/**
 * Writing Module — OET exam-faithful closure API client.
 *
 * Endpoints for the unified authored task (admin builder + import/export),
 * attempt-event ingestion, tutor marking (annotations, content-checklist
 * verdict, double-marking, moderation), AI pre-assessment, gated learner
 * feedback, rewrite comparison, result-visibility config, and admin analytics.
 *
 * As with lib/writing/api.ts, the path strings + shapes here ARE the contract;
 * the backend route handlers MUST conform. Implemented across WS-B2..B5.
 */

import { apiClient } from '../api';
import type {
  WritingTaskDto,
  WritingTaskUpsertDto,
  WritingTaskValidationDto,
  WritingTaskImportJson,
  WritingAttemptEventDto,
  WritingFeedbackAnnotationDto,
  WritingPreAssessmentDto,
  WritingTutorMarkingContextDto,
  WritingTutorReviewSubmitDto,
  WritingModerationDto,
  WritingResultVisibilityDto,
  WritingSubmissionFeedbackDto,
  WritingRewriteComparisonDto,
  WritingAdminAnalyticsDto,
  WritingMarkingQualityDto,
  WritingCriterionCode,
  WritingProfession,
  WritingLetterType,
} from './types';

function qs(params: Record<string, string | number | boolean | null | undefined>): string {
  const u = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === null || v === undefined || v === '') continue;
    u.set(k, String(v));
  }
  const s = u.toString();
  return s.length > 0 ? `?${s}` : '';
}

function p(template: string, parts: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (_, key) => {
    const value = parts[key];
    if (value === undefined) throw new Error(`Missing path parameter: ${key}`);
    return encodeURIComponent(value);
  });
}

// ── Admin: unified writing-task builder (spec §3/§4/§5/§6/§18/§19.2) ──────────

export interface WritingTaskListQuery {
  profession?: WritingProfession;
  letterType?: WritingLetterType;
  status?: 'draft' | 'published' | 'archived';
  search?: string;
  page?: number;
  pageSize?: number;
}

export const listWritingTasks = (query: WritingTaskListQuery = {}) =>
  apiClient.get<{ items: WritingTaskDto[]; total: number }>(
    `/v1/admin/writing/tasks${qs(query as Record<string, string | number | undefined>)}`,
  );

export const getWritingTask = (taskId: string) =>
  apiClient.get<WritingTaskDto>(p('/v1/admin/writing/tasks/{id}', { id: taskId }));

export const createWritingTask = (payload: WritingTaskUpsertDto) =>
  apiClient.post<WritingTaskDto>('/v1/admin/writing/tasks', payload);

export const updateWritingTask = (taskId: string, payload: WritingTaskUpsertDto) =>
  apiClient.put<WritingTaskDto>(p('/v1/admin/writing/tasks/{id}', { id: taskId }), payload);

export const validateWritingTask = (taskId: string) =>
  apiClient.get<WritingTaskValidationDto>(p('/v1/admin/writing/tasks/{id}/validate', { id: taskId }));

export const publishWritingTask = (taskId: string) =>
  apiClient.post<WritingTaskDto>(p('/v1/admin/writing/tasks/{id}/publish', { id: taskId }), {});

export const archiveWritingTask = (taskId: string) =>
  apiClient.post<WritingTaskDto>(p('/v1/admin/writing/tasks/{id}/archive', { id: taskId }), {});

export const cloneWritingTask = (taskId: string) =>
  apiClient.post<WritingTaskDto>(p('/v1/admin/writing/tasks/{id}/clone', { id: taskId }), {});

export const importWritingTask = (payload: WritingTaskImportJson) =>
  apiClient.post<WritingTaskDto>('/v1/admin/writing/tasks/import', payload);

export const exportWritingTask = (taskId: string) =>
  apiClient.get<WritingTaskImportJson>(p('/v1/admin/writing/tasks/{id}/export', { id: taskId }));

// ── Attempt events (spec §17.7) ───────────────────────────────────────────────

export const postWritingAttemptEvents = (events: WritingAttemptEventDto[]) =>
  apiClient.post<{ accepted: number }>('/v1/writing/attempt-events', { events });

// best-effort single event helper (fire-and-forget at call sites)
export const recordWritingAttemptEvent = (event: WritingAttemptEventDto) =>
  postWritingAttemptEvents([event]);

// ── Tutor marking, annotations, moderation (spec §12/§13/§14) ─────────────────

export const getTutorMarkingContext = (submissionId: string) =>
  apiClient.get<WritingTutorMarkingContextDto>(
    p('/v1/writing/tutor/reviews/{id}/context', { id: submissionId }),
  );

export const getWritingPreAssessment = (submissionId: string) =>
  apiClient.get<WritingPreAssessmentDto>(
    p('/v1/writing/tutor/reviews/{id}/pre-assessment', { id: submissionId }),
  );

export const listWritingAnnotations = (submissionId: string) =>
  apiClient.get<{ items: WritingFeedbackAnnotationDto[] }>(
    p('/v1/writing/tutor/reviews/{id}/annotations', { id: submissionId }),
  );

export interface WritingAnnotationCreatePayload {
  criterion: WritingCriterionCode | null;
  highlightedText: string;
  startOffset: number;
  endOffset: number;
  severity: 'high' | 'medium' | 'low';
  suggestion?: string | null;
  feedbackText: string;
}

export const createWritingAnnotation = (submissionId: string, payload: WritingAnnotationCreatePayload) =>
  apiClient.post<WritingFeedbackAnnotationDto>(
    p('/v1/writing/tutor/reviews/{id}/annotations', { id: submissionId }),
    payload,
  );

export const deleteWritingAnnotation = (submissionId: string, annotationId: string) =>
  apiClient.delete<void>(
    p('/v1/writing/tutor/reviews/{id}/annotations/{annotationId}', { id: submissionId, annotationId }),
  );

export const submitWritingTutorReview = (submissionId: string, payload: WritingTutorReviewSubmitDto) =>
  apiClient.post<{ review: import('./types').WritingTutorReviewDto; moderation: WritingModerationDto | null }>(
    p('/v1/writing/tutor/reviews/{id}', { id: submissionId }),
    payload,
  );

export const getWritingModeration = (submissionId: string) =>
  apiClient.get<WritingModerationDto | null>(
    p('/v1/writing/tutor/reviews/{id}/moderation', { id: submissionId }),
  );

export interface WritingModerationFinalizePayload {
  finalScore: Partial<import('./types').WritingCriteriaScoresDto>;
  finalDecisionNote: string;
}

export const finalizeWritingModeration = (submissionId: string, payload: WritingModerationFinalizePayload) =>
  apiClient.post<WritingModerationDto>(
    p('/v1/writing/tutor/reviews/{id}/moderation/finalize', { id: submissionId }),
    payload,
  );

// ── Learner gated feedback + rewrite (spec §15) ───────────────────────────────

export const getWritingSubmissionFeedback = (submissionId: string) =>
  apiClient.get<WritingSubmissionFeedbackDto>(
    p('/v1/writing/submissions/{id}/feedback', { id: submissionId }),
  );

export const getWritingRewriteComparison = (rewriteSubmissionId: string) =>
  apiClient.get<WritingRewriteComparisonDto>(
    p('/v1/writing/submissions/{id}/rewrite-comparison', { id: rewriteSubmissionId }),
  );

// ── Result-visibility config (spec §15.1) ─────────────────────────────────────

export const getEffectiveResultVisibility = (scenarioId?: string) =>
  apiClient.get<WritingResultVisibilityDto>(
    `/v1/writing/result-visibility${qs({ scenarioId })}`,
  );

export const getResultVisibilityConfig = (scenarioId?: string) =>
  apiClient.get<WritingResultVisibilityDto>(
    `/v1/admin/writing/result-visibility${qs({ scenarioId })}`,
  );

export const updateResultVisibilityConfig = (
  payload: WritingResultVisibilityDto & { scenarioId?: string | null },
) =>
  apiClient.put<WritingResultVisibilityDto>('/v1/admin/writing/result-visibility', payload);

// ── Admin analytics + quality control (spec §16) ──────────────────────────────

export interface WritingAnalyticsQuery {
  profession?: WritingProfession;
  letterType?: WritingLetterType;
  fromDate?: string;
  toDate?: string;
}

export const getWritingAdminAnalytics = (query: WritingAnalyticsQuery = {}) =>
  apiClient.get<WritingAdminAnalyticsDto>(
    `/v1/admin/writing/analytics/overview${qs(query as Record<string, string | undefined>)}`,
  );

export const getWritingMarkingQuality = (query: WritingAnalyticsQuery = {}) =>
  apiClient.get<WritingMarkingQualityDto>(
    `/v1/admin/writing/analytics/quality${qs(query as Record<string, string | undefined>)}`,
  );
