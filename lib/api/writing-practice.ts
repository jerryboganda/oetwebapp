import { parseCriterionScore, scoreToGrade } from './result-mappers';
import { normalizeCriterionName } from './task-mappers';
import { ensureAttempt, cacheRemove, cacheSet, attemptCacheKey, evaluationCacheKey } from './attempt-cache';
import type { WritingPaperAsset } from '../types/expert';
import { WRITING_CRITERION_MAX_SCORES, type WritingCriterionCode } from '../scoring';
import type { CriterionFeedback, AnchoredComment } from '../mock-data';
import { apiRequest, type ApiRecord } from './client';
import { mapWritingTask, titleCase } from './task-mappers';
import { scoreRangeDisplay, toConfidence, toEvalStatus, toExamFamilyCode, toReviewStatus } from './result-mappers';
import { type CriteriaDelta, ModelAnswer, WritingResult, WritingSubmission, WritingTask } from '../mock-data';
import { type WritingAttemptMode } from './writing-attempts';

export function mapCriterionFeedback(criterionScores: ApiRecord[], feedbackItems: ApiRecord[]): CriterionFeedback[] {
  return criterionScores.map((criterion) => {
    const score = parseCriterionScore(criterion.scoreRange);
    const criterionCode = String(criterion.criterionCode ?? '').toLowerCase();
    const maxScore = Object.prototype.hasOwnProperty.call(WRITING_CRITERION_MAX_SCORES, criterionCode)
      ? WRITING_CRITERION_MAX_SCORES[criterionCode as WritingCriterionCode]
      : 7;
    const relatedFeedback = feedbackItems.filter((item) => item.criterionCode === criterionCode);

    return {
      name: normalizeCriterionName(criterionCode),
      score,
      maxScore,
      grade: scoreToGrade(score),
      explanation: criterion.explanation ?? '',
      anchoredComments: relatedFeedback.map((item, index) => {
        const rawSeverity = typeof item.severity === 'string' ? item.severity.toLowerCase() : '';
        const severity: AnchoredComment['severity'] | undefined =
          rawSeverity === 'critical' || rawSeverity === 'major' || rawSeverity === 'minor' || rawSeverity === 'info'
            ? (rawSeverity as AnchoredComment['severity'])
            : undefined;
        const rawSource = typeof item.source === 'string' ? item.source.toLowerCase() : '';
        const source: AnchoredComment['source'] | undefined =
          rawSource === 'rule_engine' || rawSource === 'ai' ? (rawSource as AnchoredComment['source']) : undefined;
        return {
          id: item.feedbackItemId ?? `${criterionCode}-${index}`,
          text: item.anchor?.snippet ?? item.anchor?.lineId ?? normalizeCriterionName(criterionCode),
          comment: item.message ?? '',
          ruleId: typeof item.ruleId === 'string' && item.ruleId ? item.ruleId : undefined,
          severity,
          source,
          suggestedFix:
            typeof item.suggestedFix === 'string' && item.suggestedFix ? item.suggestedFix : undefined,
        };
      }),
      omissions: [],
      unnecessaryDetails: [],
      revisionSuggestions: relatedFeedback.map((item) => item.suggestedFix).filter(Boolean),
      strengths: [],
      issues: [],
    };
  });
}

/**
 * Writing task/submit/paper-assets/entitlement/result/submissions/deltas/model answer.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchWritingTask(taskId: string): Promise<WritingTask> {
  const item = await apiRequest<ApiRecord>(`/v1/writing/tasks/${taskId}`);
  return mapWritingTask(item);
}

export type WritingExamMode = 'computer' | 'paper';
export type WritingAssessorType = 'ai' | 'instructor';

export interface WritingSubmitOptions {
  examMode?: WritingExamMode;
  assessorType?: WritingAssessorType;
  paperAssetIds?: string[];
  turnaroundOption?: 'standard' | 'express';
  focusAreas?: string[];
  learnerNotes?: string;
}

export async function submitWritingTask(taskId: string, content: string, mode: WritingAttemptMode = 'exam', options: WritingSubmitOptions = {}): Promise<WritingSubmission & { attemptId?: string; reviewRequestId?: string; assessorType?: WritingAssessorType; examMode?: WritingExamMode }> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  const examMode = options.examMode ?? 'computer';
  const assessorType = options.assessorType ?? 'ai';

  if (examMode === 'computer') {
    await apiRequest(`/v1/writing/attempts/${attempt.attemptId}/draft`, {
      method: 'PATCH',
      body: JSON.stringify({ content, scratchpad: null, checklist: null, draftVersion: attempt.draftVersion ?? 1 }),
    });
  }

  const submitted = await apiRequest<ApiRecord>(`/v1/writing/attempts/${attempt.attemptId}/submit`, {
    method: 'POST',
    body: JSON.stringify({
      content: examMode === 'computer' ? content : null,
      idempotencyKey: crypto.randomUUID?.() ?? String(Date.now()),
      examMode,
      assessorType,
      paperAssetIds: options.paperAssetIds ?? [],
      turnaroundOption: options.turnaroundOption ?? 'standard',
      focusAreas: options.focusAreas ?? ['OET writing criteria', 'voice-note feedback'],
      learnerNotes: options.learnerNotes ?? null,
    }),
  });

  cacheRemove(attemptCacheKey('writing', taskId, mode));
  if (submitted.evaluationId) {
    cacheSet(evaluationCacheKey('writing', taskId), submitted.evaluationId);
  }
  const task = await fetchWritingTask(taskId);
  const id = String(submitted.evaluationId ?? submitted.reviewRequestId ?? attempt.attemptId);

  return {
    id,
    attemptId: String(submitted.attemptId ?? attempt.attemptId),
    reviewRequestId: submitted.reviewRequestId ? String(submitted.reviewRequestId) : undefined,
    assessorType,
    examMode,
    taskId,
    taskTitle: task.title,
    content,
    submittedAt: new Date().toISOString(),
    evalStatus: submitted.evaluationId ? toEvalStatus(submitted.state) : 'queued',
    reviewStatus: submitted.reviewRequestId ? 'pending' : 'not_requested',
  };
}

export async function attachWritingPaperAssets(taskId: string, mediaAssetIds: string[], mode: WritingAttemptMode = 'exam', replaceExisting = true): Promise<{ attemptId: string; assets: WritingPaperAsset[]; extractionState: string; extractedText: string; extractedCharCount: number; wordCount: number }> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attempt.attemptId)}/paper-assets`, {
    method: 'POST',
    body: JSON.stringify({ mediaAssetIds, replaceExisting }),
  });
}

export async function fetchWritingPaperAssets(taskId: string, mode: WritingAttemptMode = 'exam'): Promise<{ attemptId: string; assets: WritingPaperAsset[]; extractionState: string; extractedText: string }> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  return apiRequest(`/v1/writing/attempts/${encodeURIComponent(attempt.attemptId)}/paper-assets`);
}

export interface WritingEntitlement {
  allowed: boolean;
  tier: string;
  remaining: number | null;
  limitPerWindow: number | null;
  windowDays: number;
  resetAt: string | null;
  reason: string;
}

export async function fetchWritingEntitlement(): Promise<WritingEntitlement> {
  return apiRequest<WritingEntitlement>('/v1/writing/entitlement');
}

export async function fetchWritingResult(resultId: string): Promise<WritingResult> {
  const [summary, feedback] = await Promise.all([
    apiRequest<ApiRecord>(`/v1/writing/evaluations/${resultId}/summary`),
    apiRequest<ApiRecord>(`/v1/writing/evaluations/${resultId}/feedback`),
  ]);

  const criteria = mapCriterionFeedback(feedback.criterionScores ?? [], feedback.feedbackItems ?? []);

  return {
    id: resultId,
    taskId: summary.taskId,
    taskTitle: summary.taskTitle,
    profession: typeof summary.profession === 'string' ? summary.profession : 'medicine',
    examFamilyCode: toExamFamilyCode(summary.examFamilyCode),
    examFamilyLabel: summary.examFamilyLabel ?? titleCase(summary.examFamilyCode ?? 'oet'),
    estimatedScoreRange: scoreRangeDisplay(summary.scoreRange),
    estimatedGradeRange: scoreRangeDisplay(summary.gradeRange ?? 'Pending'),
    confidenceBand: toConfidence(summary.confidenceBand),
    confidenceLabel: summary.confidenceLabel ?? `${toConfidence(summary.confidenceBand)} confidence practice estimate`,
    learnerDisclaimer: summary.learnerDisclaimer ?? `Practice estimate only. This is not an official ${summary.examFamilyLabel ?? 'exam'} score.`,
    methodLabel: summary.methodLabel ?? 'AI-assisted practice evaluation',
    provenanceLabel: summary.provenanceLabel ?? `${summary.examFamilyLabel ?? 'Exam'} practice estimate`,
    humanReviewRecommended: Boolean(summary.humanReviewRecommended),
    escalationRecommended: Boolean(summary.escalationRecommended),
    isOfficialScore: Boolean(summary.isOfficialScore),
    topStrengths: summary.strengths ?? [],
    topIssues: summary.issues ?? [],
    criteria,
    submittedAt: summary.generatedAt ?? new Date().toISOString(),
    evalStatus: toEvalStatus(summary.state),
  };
}

export async function fetchWritingSubmissions(): Promise<WritingSubmission[]> {
  const response = await apiRequest<{ items: ApiRecord[] }>('/v1/submissions');
  return response.items
    .filter((item) => String(item.subtest).toLowerCase() === 'writing')
    .map((item) => ({
      id: item.evaluationId ?? item.submissionId,
      taskId: item.contentId,
      taskTitle: item.taskName,
      content: '',
      submittedAt: item.attemptDate,
      evalStatus: item.evaluationId ? 'completed' : 'processing',
      scoreEstimate: scoreRangeDisplay(item.scoreEstimate),
      reviewStatus: toReviewStatus(item.reviewStatus),
    }));
}

export async function fetchCriteriaDeltas(): Promise<CriteriaDelta[]> {
  const writingSubmissions = await fetchWritingSubmissions();
  const latest = writingSubmissions[0]?.id;
  if (!latest) {
    return [];
  }
  const result = await fetchWritingResult(latest);
  return result.criteria.map((criterion) => ({
    name: criterion.name,
    original: Math.max(criterion.score - 1, 0),
    revised: criterion.score,
    max: criterion.maxScore,
  }));
}

export async function fetchModelAnswer(taskId: string): Promise<ModelAnswer> {
  const response = await apiRequest<ApiRecord>(`/v1/writing/content/${taskId}/model-answer`);
  const payload = response.payload ?? {};
  return {
    taskId,
    taskTitle: response.title,
    profession: titleCase(response.professionId),
    paragraphs: (payload.paragraphs ?? []).map((paragraph: ApiRecord, index: number) => ({
      id: paragraph.id ?? `p-${index + 1}`,
      text: paragraph.text ?? '',
      rationale: paragraph.rationale ?? '',
      criteria: paragraph.criteria ?? [],
      included: paragraph.included ?? [],
      excluded: paragraph.excluded ?? [],
      languageNotes: paragraph.languageNotes ?? '',
    })),
  };
}

