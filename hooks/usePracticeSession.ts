import { useState } from 'react';
import {
  startPracticeSession,
  submitAnswer as submitAnswerApi,
  endPracticeSession,
  type PracticeSessionStartRequest,
  type AnswerSubmitRequest,
  type AnswerResultDto,
} from '@/lib/reading-pathway-api';

type SessionState = 'idle' | 'starting' | 'in_progress' | 'submitting' | 'complete';

const SESSION_STORAGE_KEY = 'reading_practice_session_id';

export function usePracticeSession() {
  const [state, setState] = useState<SessionState>('idle');
  const [sessionId, setSessionId] = useState<string | null>(() => {
    if (typeof sessionStorage !== 'undefined') {
      return sessionStorage.getItem(SESSION_STORAGE_KEY);
    }
    return null;
  });
  const [error, setError] = useState<Error | null>(null);

  async function startSession(req: PracticeSessionStartRequest): Promise<void> {
    setState('starting');
    setError(null);
    try {
      const session = await startPracticeSession(req);
      setSessionId(session.sessionId);
      if (typeof sessionStorage !== 'undefined') {
        sessionStorage.setItem(SESSION_STORAGE_KEY, session.sessionId);
      }
      setState('in_progress');
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
      setState('idle');
    }
  }

  async function submitAnswer(
    questionId: string,
    selectedOption: string,
    timeSpentSeconds: number,
  ): Promise<AnswerResultDto | null> {
    if (!sessionId) return null;
    setState('submitting');
    try {
      const req: AnswerSubmitRequest = { questionId, selectedOption, timeSpentSeconds };
      const result = await submitAnswerApi(sessionId, req);
      setState('in_progress');
      return result;
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
      setState('in_progress');
      return null;
    }
  }

  async function endSession(): Promise<void> {
    if (!sessionId) return;
    setState('submitting');
    try {
      await endPracticeSession(sessionId);
      if (typeof sessionStorage !== 'undefined') {
        sessionStorage.removeItem(SESSION_STORAGE_KEY);
      }
      setState('complete');
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
      setState('in_progress');
    }
  }

  return { state, sessionId, error, startSession, submitAnswer, endSession };
}
