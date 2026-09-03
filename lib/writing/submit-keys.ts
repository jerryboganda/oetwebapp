/**
 * Stable idempotency keys for Writing submit actions.
 *
 * Dependency-free so it can be unit-tested in isolation and imported from
 * any submit surface (practice session, paper session) without pulling in
 * the API client chain.
 */

/**
 * A stable, unique key for one logical submit action — call once per submit
 * click/timer-expiry and pass the SAME value into every retry of that one
 * attempt; a fresh user-initiated submit should call this again for a new key.
 */
export function createSubmitIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `submit-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

interface SubmitAttemptTracker {
  scenarioId: string;
  content: string;
  key: string;
}

let lastSubmitAttempt: SubmitAttemptTracker | null = null;

/**
 * Idempotency key for a submit action that is STABLE across the duplicate
 * sends of one logical attempt (double-tap, React re-render resend, network
 * retry, 429/409 single-retry) yet FRESH when the candidate actually changed
 * the letter: the same (scenario, content) reuses the stored key, different
 * content mints a new one. Combined with the server's content-hash guard,
 * one Submit click can never open two paid grading workflows.
 */
export function keyForSubmitAction(scenarioId: string, content: string): string {
  if (
    lastSubmitAttempt !== null &&
    lastSubmitAttempt.scenarioId === scenarioId &&
    lastSubmitAttempt.content === content
  ) {
    return lastSubmitAttempt.key;
  }
  const key = createSubmitIdempotencyKey();
  lastSubmitAttempt = { scenarioId, content, key };
  return key;
}

/** Test seam: clear the module-level submit tracker between cases. */
export function __resetSubmitKeyTrackerForTests(): void {
  lastSubmitAttempt = null;
}
