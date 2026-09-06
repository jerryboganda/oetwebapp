import { ensureAttempt, cacheRemove, cacheSet, attemptCacheKey, evaluationCacheKey, latestEvaluationIdForContent } from './attempt-cache';
import { apiRequest, type ApiRecord } from './client';
import { normalizeRouteValues } from './route-normalizer';
import { type ListeningDrill, ListeningResult, ListeningReview, ListeningTask, ReadingResult, ReadingTask } from '../mock-data';

/**
 * Legacy reading stubs + listening tasks/results/drills/review.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchReadingTask(taskId: string): Promise<ReadingTask> {
  throw new Error(`Legacy Reading task ${taskId} is closed. Use structured Reading papers from /reading.`);
}

export async function submitReadingAnswers(taskId: string, answers: Record<string, string>): Promise<ReadingResult> {
  void answers;
  throw new Error(`Legacy Reading task ${taskId} is closed. Submit structured Reading attempts from /reading.`);
}

export async function fetchReadingResult(taskId: string): Promise<ReadingResult> {
  throw new Error(`Legacy Reading result ${taskId} is closed. Review structured Reading attempts from /reading.`);
}

export async function fetchListeningTask(taskId: string): Promise<ListeningTask> {
  const task = await apiRequest<ApiRecord>(`/v1/listening/tasks/${taskId}`);
  return {
    id: task.contentId,
    title: task.title,
    audioSrc: task.audioUrl ?? '',
    duration: task.durationSeconds ?? task.estimatedDurationMinutes * 60,
    audioAvailable: Boolean(task.audioUrl),
    audioUnavailableReason: task.audioUrl ? undefined : 'Audio for this listening task is not available yet. Please use transcript-backed review instead.',
    transcriptPolicy: task.transcriptPolicy ?? 'per_item_post_attempt',
    questions: (task.questions ?? []).map((question: ApiRecord) => ({
      id: question.id,
      number: question.number,
      text: question.text,
      type: question.type,
      options: question.options ?? undefined,
      correctAnswer: question.correctAnswer ?? '',
    })),
  };
}

export async function submitListeningAnswers(taskId: string, answers: Record<string, string>): Promise<ListeningResult> {
  const attempt = await ensureAttempt('listening', taskId, 'exam');
  await apiRequest(`/v1/listening/attempts/${attempt.attemptId}/answers`, { method: 'PATCH', body: JSON.stringify({ answers }) });
  const submitted = await apiRequest<ApiRecord>(`/v1/listening/attempts/${attempt.attemptId}/submit`, { method: 'POST' });
  cacheRemove(attemptCacheKey('listening', taskId, 'exam'));
  cacheSet(evaluationCacheKey('listening', taskId), submitted.evaluationId);
  return fetchListeningResult(taskId);
}

export async function fetchListeningResult(taskId: string): Promise<ListeningResult> {
  const evaluationId = await latestEvaluationIdForContent(taskId, 'listening');
  if (!evaluationId) {
    throw new Error('Listening result not found');
  }

  const [evaluation, task] = await Promise.all([
    apiRequest<ApiRecord>(`/v1/listening/evaluations/${evaluationId}`),
    apiRequest<ApiRecord>(`/v1/listening/tasks/${taskId}`),
  ]);

  const questions = (evaluation.itemReview ?? []).map((itemReview: ApiRecord, index: number) => {
    const transcript = itemReview.transcript ?? null;
    const isCorrect = Boolean(itemReview.isCorrect);
    return {
      id: `lrq-${index + 1}`,
      number: itemReview.number ?? index + 1,
      text: itemReview.prompt ?? itemReview.text ?? `Question ${index + 1}`,
      userAnswer: itemReview.learnerAnswer ?? '',
      correctAnswer: itemReview.correctAnswer ?? '',
      isCorrect,
      isInvalid: itemReview.isInvalid === true,
      explanation: itemReview.explanation ?? (isCorrect ? 'Correct.' : 'Review the transcript clue and distractor pattern.'),
      allowTranscriptReveal: Boolean(transcript?.allowed),
      transcriptExcerpt: transcript?.excerpt ?? undefined,
      distractorExplanation: transcript?.distractorExplanation ?? itemReview.distractorExplanation ?? undefined,
    };
  });

  const rawScore = Number(evaluation.rawScore ?? questions.filter((question: ListeningResult['questions'][number]) => question.isCorrect).length);
  const maxRawScore = Number(evaluation.maxRawScore ?? 42);
  const hasRecommendedDrill = evaluation.recommendedNextDrill && typeof evaluation.recommendedNextDrill === 'object';

  return {
    id: taskId,
    title: task.title,
    score: rawScore,
    total: maxRawScore,
    questions,
    invalidCount: Number(evaluation.invalidCount ?? questions.filter((question: { isInvalid?: boolean }) => question.isInvalid === true).length),
    recommendedDrill: hasRecommendedDrill
      ? {
          id: (evaluation.recommendedNextDrill as ApiRecord).drillId ?? (evaluation.recommendedNextDrill as ApiRecord).id ?? 'listening-drill-detail_capture',
          title: (evaluation.recommendedNextDrill as ApiRecord).title ?? 'Exact Detail Capture Drill',
          description: (evaluation.recommendedNextDrill as ApiRecord).description ?? (evaluation.recommendedNextDrill as ApiRecord).rationale ?? 'Practise the listening error type that appeared most often in this result.',
        }
      : null,
  };
}

export async function fetchListeningDrill(drillId: string): Promise<ListeningDrill> {
  const drill = normalizeRouteValues(await apiRequest<ApiRecord>(`/v1/listening-papers/drills/${drillId}`));
  return {
    id: drill.drillId,
    title: drill.title,
    focusLabel: drill.focusLabel,
    description: drill.description,
    errorType: drill.errorType,
    estimatedMinutes: Number(drill.estimatedMinutes ?? 10),
    highlights: drill.highlights ?? [],
    launchRoute: drill.launchRoute,
    reviewRoute: drill.reviewRoute,
  };
}

export async function fetchListeningReview(taskId: string): Promise<ListeningReview> {
  const evaluationId = await latestEvaluationIdForContent(taskId, 'listening');
  if (!evaluationId) {
    throw new Error('Complete a listening task before opening transcript-backed review.');
  }

  const evaluation = await apiRequest<ApiRecord>(`/v1/listening/evaluations/${evaluationId}`);
  return {
    id: taskId,
    title: evaluation.title ?? 'Listening transcript-backed review',
    transcriptPolicy: evaluation.transcriptAccess?.policy ?? 'per_item_post_attempt',
    recommendedDrill: undefined,
    questions: (evaluation.itemReview ?? []).map((item: ApiRecord, index: number) => ({
      id: item.questionId ?? `listening-review-${index + 1}`,
      number: Number(item.number ?? index + 1),
      text: item.prompt ?? '',
      learnerAnswer: item.learnerAnswer ?? '',
      correctAnswer: item.correctAnswer ?? '',
      explanation: item.explanation ?? '',
      transcriptExcerpt: item.transcript?.excerpt ?? undefined,
      distractorExplanation: item.transcript?.distractorExplanation ?? undefined,
    })),
  };
}
