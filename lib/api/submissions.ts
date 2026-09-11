import { apiRequest, type ApiRecord } from './client';
import { titleCase, toSubTest } from './task-mappers';
import { scoreRangeDisplay, toReviewStatus } from './result-mappers';
import { fetchLearnerReviewResult, fetchLearnerReviewVoiceNotes } from './expert';
import { fetchWritingResult } from './writing-practice';
import { fetchSpeakingResult, fetchTranscript } from './speaking-results';
import { fetchListeningResult, fetchReadingResult } from './subtest-tasks';
import { type CriterionFeedback, Submission, SubmissionComparison, SubmissionDetail } from '../mock-data';

/**
 * Learner submissions list, detail, comparison.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchSubmissions(options?: { subtest?: string }): Promise<Submission[]> {
  // Follow the cursor until exhausted so callers that expect the full history
  // still get it, while the wire protocol uses the real cursor pagination
  // contract documented in the learner blueprint.
  // Passing `subtest` pushes the filter to the server (see brief item 5) so a
  // Writing-only caller pages through the 100 most recent WRITING items
  // instead of the 100 most recent items of any subtest.
  const subtestParam = options?.subtest ? `&subtest=${encodeURIComponent(options.subtest)}` : '';
  const collected: ApiRecord[] = [];
  let cursor: string | null = null;
  // Hard safety cap to prevent runaway loops if the server mis-behaves.
  for (let page = 0; page < 50; page += 1) {
    const query: string = cursor
      ? `?cursor=${encodeURIComponent(cursor)}&limit=100${subtestParam}`
      : `?limit=100${subtestParam}`;
    const response: { items: ApiRecord[]; nextCursor?: string | null } = await apiRequest<{
      items: ApiRecord[];
      nextCursor?: string | null;
    }>(`/v1/submissions${query}`);
    collected.push(...(response.items ?? []));
    const next: string | null = response.nextCursor ?? null;
    if (!next) break;
    cursor = next;
  }
  return collected.map((item) => ({
    id: item.submissionId,
    contentId: item.contentId,
    taskName: item.taskName,
    subTest: toSubTest(item.subtest),
    attemptDate: item.attemptDate,
    scoreEstimate: scoreRangeDisplay(item.scoreEstimate ?? ''),
    reviewStatus: toReviewStatus(item.reviewStatus),
    reviewRequestId: item.reviewRequestId ?? null,
    evaluationId: item.evaluationId ?? undefined,
    state: item.state ?? undefined,
    submissionMode: item.submissionMode ?? undefined,
    assessorType: item.assessorType ?? undefined,
    voiceNoteCount: Number(item.voiceNoteCount ?? 0),
    comparisonGroupId: item.comparisonGroupId ?? null,
    canRequestReview: Boolean(item.canRequestReview),
    actions: {
      reopenFeedbackRoute: item.actions?.reopenFeedbackRoute ?? null,
      compareRoute: item.actions?.compareRoute ?? null,
      requestReviewRoute: item.actions?.requestReviewRoute ?? null,
    },
  }));
}

export async function fetchSubmissionDetail(submissionId: string): Promise<SubmissionDetail> {
  const submissions = await fetchSubmissions();
  const submission = submissions.find((item) => item.id === submissionId || item.evaluationId === submissionId);
  if (!submission) {
    throw new Error('Submission not found.');
  }

  const baseDetail: SubmissionDetail = {
    submission,
    evidenceSummary: {
      title: submission.taskName,
      scoreLabel: submission.scoreEstimate || 'Pending',
      stateLabel: titleCase(submission.state ?? 'completed'),
      reviewLabel: titleCase(submission.reviewStatus.replace(/_/g, ' ')),
      nextActionLabel: submission.canRequestReview ? 'Request review' : 'Review current evidence',
    },
    strengths: [],
    issues: [],
  };

  if (submission.reviewRequestId) {
    try {
      const voice = await fetchLearnerReviewVoiceNotes(submission.reviewRequestId);
      baseDetail.voiceNotes = (voice.items ?? []).map((note) => ({
        id: note.id,
        reviewRequestId: note.reviewRequestId,
        url: note.url,
        fileName: note.fileName,
        mimeType: note.mimeType,
        durationSeconds: note.durationSeconds,
        transcriptText: note.transcriptText,
        writtenNotes: note.writtenNotes,
        createdAt: note.createdAt,
      }));
    } catch (error) {
      console.warn('[API] Failed to load review voice notes:', error);
    }

    if (submission.reviewStatus === 'reviewed') {
      try {
        const result = await fetchLearnerReviewResult(submission.reviewRequestId);
        const criteria = result.criteria.map((criterion) => ({
          name: criterion.name,
          score: criterion.score,
          maxScore: criterion.maxScore,
          grade: '',
          explanation: criterion.explanation || 'Reviewed by Dr. Ahmed.',
          anchoredComments: [],
          omissions: [],
          unnecessaryDetails: [],
          revisionSuggestions: [],
          strengths: [],
          issues: [],
        } satisfies CriterionFeedback));
        baseDetail.expertReview = {
          reviewRequestId: result.reviewRequestId,
          finalComment: result.finalComment,
          scoreLabel: result.scoreLabel,
          completedAt: result.completedAt,
          criteria,
        };
        baseDetail.criteria = criteria;
        baseDetail.evidenceSummary.scoreLabel = result.scoreLabel || baseDetail.evidenceSummary.scoreLabel;
      } catch (error) {
        console.warn('[API] Failed to load review result:', error);
      }
    }
  }

  if (!submission.evaluationId) {
    return baseDetail;
  }

  if (submission.subTest === 'Writing') {
    const result = await fetchWritingResult(submission.evaluationId);
    return {
      ...baseDetail,
      strengths: result.topStrengths,
      issues: result.topIssues,
      criteria: result.criteria,
    };
  }

  if (submission.subTest === 'Speaking') {
    const [result, transcript] = await Promise.all([
      fetchSpeakingResult(submission.evaluationId),
      fetchTranscript(submission.evaluationId),
    ]);
    return {
      ...baseDetail,
      strengths: result.strengths,
      issues: result.improvements,
      transcript: transcript.transcript,
    };
  }

  if (submission.subTest === 'Reading') {
    const result = await fetchReadingResult(submission.contentId);
    return {
      ...baseDetail,
      strengths: [`${result.score}/${result.totalQuestions} questions answered correctly.`],
      issues: result.errorClusters.filter((cluster) => cluster.count > 0).map((cluster) => `${cluster.type}: ${cluster.count} items to review.`),
      questionReview: result.items.map((item) => ({
        id: item.id,
        number: item.number,
        text: item.text,
        learnerAnswer: item.userAnswer,
        correctAnswer: item.correctAnswer,
        isCorrect: item.isCorrect,
        explanation: item.explanation,
      })),
    };
  }

  const result = await fetchListeningResult(submission.contentId);
  return {
    ...baseDetail,
    strengths: [`${result.score}/${result.total} listening items captured correctly.`],
    issues: result.questions.filter((question) => !question.isCorrect && !question.isInvalid).map((question) => question.distractorExplanation ?? question.explanation),
    questionReview: result.questions.map((question) => ({
      id: question.id,
      number: question.number,
      text: question.text,
      learnerAnswer: question.userAnswer,
      correctAnswer: question.correctAnswer,
      isCorrect: question.isCorrect,
      explanation: question.explanation,
      transcriptExcerpt: question.transcriptExcerpt,
      distractorExplanation: question.distractorExplanation,
    })),
  };
}

export async function fetchSubmissionComparison(leftId?: string, rightId?: string): Promise<SubmissionComparison> {
  const params = new URLSearchParams();
  if (leftId) params.set('leftId', leftId);
  if (rightId) params.set('rightId', rightId);
  const response = await apiRequest<ApiRecord>(`/v1/submissions/compare?${params.toString()}`);
  return {
    canCompare: Boolean(response.canCompare),
    reason: response.reason ?? undefined,
    summary: response.summary ?? undefined,
    comparisonGroupId: response.comparisonGroupId ?? null,
    left: response.left
      ? {
          attemptId: response.left.attemptId,
          evaluationId: response.left.evaluationId ?? undefined,
          scoreRange: scoreRangeDisplay(response.left.scoreRange ?? ''),
          subtest: toSubTest(response.left.subtest),
        }
      : undefined,
    right: response.right
      ? {
          attemptId: response.right.attemptId,
          evaluationId: response.right.evaluationId ?? undefined,
          scoreRange: scoreRangeDisplay(response.right.scoreRange ?? ''),
          subtest: toSubTest(response.right.subtest),
        }
      : undefined,
  };
}
