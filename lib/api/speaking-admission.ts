/**
 * Live AI Speaking admission control (owner decision 5 Oct 2026).
 *
 * Mirrors `SpeakingLiveAdmissionView` in `SpeakingLiveAdmissionContracts.cs`. While the server's cap of live AI
 * patient sessions is full, `POST /v1/speaking/exams/{id}/finish-intro` and
 * `POST /v1/speaking/sessions/{id}/finish-warmup` do NOT start anything: the exam stays `intro` (the practice
 * card `warmup`), no credit is held, no clock runs, and the response carries `admission`. The page repeats the
 * same call; the call that finds a free place is the one that holds the credit and starts the clock. An admitted
 * learner gets the normal next state and no `admission` at all.
 */
export interface SpeakingLiveAdmission {
  status: 'waiting';
  /** 1-based place in the line. */
  position: number;
  queueLength: number;
  /** A rough estimate from the average session length; never a promise. */
  estimatedWaitSeconds: number;
  /** Seconds the page should wait between retries while it is visible. */
  pollAfterSeconds: number;
}

export function isWaitingForAdmission(
  admission: SpeakingLiveAdmission | null | undefined,
): admission is SpeakingLiveAdmission {
  return admission?.status === 'waiting';
}

/** "less than a minute", "about 3 minutes", "about 1 hour 20 minutes". */
export function formatAdmissionWait(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  if (total < 60) return 'less than a minute';
  const minutes = Math.ceil(total / 60);
  if (minutes < 60) return `about ${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  const hourText = `${hours} ${hours === 1 ? 'hour' : 'hours'}`;
  return rest === 0 ? `about ${hourText}` : `about ${hourText} ${rest} ${rest === 1 ? 'minute' : 'minutes'}`;
}

/**
 * Whether a failed admission retry is worth repeating. A server or network failure, a 408 or a 429 may pass;
 * any other refusal (no credits, wrong state, not found, forbidden) cannot be fixed by waiting. The shared client
 * throws `ApiError(0, 'network_error')` once its own retries are spent, so status 0 is a connectivity blip too.
 */
export function isTransientAdmissionFailure(status: number | undefined): boolean {
  if (status === undefined || status === 0) return true;
  return status >= 500 || status === 408 || status === 429;
}

/**
 * The 409 `code` for "one live place per learner": this learner already has another live AI Speaking place (waiting
 * or running). Refused up front, so it is a message to show, never a sign that the subject was admitted.
 */
export const LIVE_SESSION_ACTIVE_CODE = 'speaking_live_session_active';

/**
 * A 409 from finish-intro / finish-warmup that means "this exam or card is already past the gate" (admitted by another
 * tab, or the exam ended): the page should re-read the truth instead of showing an error. The one-place-per-learner
 * refusal is ALSO a 409 but means the opposite (nothing started), so it is excluded.
 */
export function isAlreadyPastGateConflict(status: number | undefined, code: string | undefined): boolean {
  return status === 409 && code !== LIVE_SESSION_ACTIVE_CODE;
}
