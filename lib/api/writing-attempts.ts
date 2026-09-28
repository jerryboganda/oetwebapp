import { ensureAttempt } from './attempt-cache';

/**
 * Writing attempt session + ensureWritingAttempt + evaluation-id helpers.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export interface WritingAttemptSession {
  attemptId: string;
  contentId: string;
  context: string;
  mode: string;
  state: string;
  startedAt: string;
  draftVersion: number;
  draftContent: string;
  feedbackMessage?: string | null;
}

export type WritingAttemptMode = 'exam' | 'learning' | 'diagnostic';

export async function ensureWritingAttempt(taskId: string, mode: WritingAttemptMode = 'exam'): Promise<WritingAttemptSession> {
  const attempt = await ensureAttempt('writing', taskId, mode);
  const startedAt = typeof attempt.startedAt === 'string' ? attempt.startedAt : '';
  if (!startedAt || Number.isNaN(Date.parse(startedAt))) {
    throw new Error('Writing attempt start time was missing from the API response.');
  }

  return {
    attemptId: String(attempt.attemptId ?? ''),
    contentId: String(attempt.contentId ?? taskId),
    context: String(attempt.context ?? (mode === 'diagnostic' ? 'diagnostic' : mode === 'exam' ? 'exam' : 'practice')),
    mode: String(attempt.mode ?? mode),
    state: String(attempt.state ?? 'in_progress'),
    startedAt,
    draftVersion: Number(attempt.draftVersion ?? 1),
    feedbackMessage: typeof attempt.feedbackMessage === 'string' ? attempt.feedbackMessage : null,
    draftContent: typeof attempt.draftContent === 'string' ? attempt.draftContent : '',
  };
}
