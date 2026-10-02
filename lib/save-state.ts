import { ApiError } from '@/lib/api/client';

/** Autosave state shared by the exam players (Reading, Listening, Writing). */
export type SaveState = 'idle' | 'saving' | 'saved' | 'offline-saved' | 'conflict' | 'error';

/** True when a save failed because the device is offline or the request never reached the server. */
export function isNetworkInterruption(error: unknown): boolean {
  return (error instanceof ApiError && error.status === 0)
    || (typeof navigator !== 'undefined' && !navigator.onLine);
}
