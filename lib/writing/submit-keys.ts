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

/**
 * Candidate-safe error copy for Writing submit/grading failures. Internal
 * configuration codes, parser names, provider details, and stack traces
 * must never reach the learner UI: known business codes map to controlled
 * copy, and any message containing raw internal identifiers falls back to
 * the generic caller-provided text. Diagnostic codes stay in server logs
 * and the admin catalogue report.
 */
export function toCandidateSafeWritingErrorMessage(err: unknown, fallback: string): string {
  const code = (err as { code?: string } | null)?.code;
  switch (code) {
    case 'writing_assessment_missing_input':
    case 'writing_assessment_release_blocked':
    case 'writing_assessment_requires_review':
    case 'writing_rubric_failed':
    case 'writing_rubric_unavailable':
    case 'ai_platform_budget_exhausted':
      return 'Grading is not available for this writing task right now. Your draft has been saved — please try another task or contact support.';
    case 'writing_rubric_already_in_progress':
      return 'This submission is already being graded. Please wait a moment and check again.';
    case 'writing_submission_locked':
      return 'You have already submitted this task. Submitted attempts are locked; use revise to try again.';
    case 'ai_credits_insufficient':
      return 'You have no AI grading credits remaining. Purchase an AI Credits package to continue.';
    default:
      break;
  }
  const message = err instanceof Error ? err.message : '';
  if (
    /profession_pack|letter_type_pack|recipient_unknown|case_note_pages|task_classification|SqlException|Npgsql|NullReference|at OetLearner\.|Unhandled exception/i.test(
      message,
    )
  ) {
    return fallback;
  }
  return message || fallback;
}
