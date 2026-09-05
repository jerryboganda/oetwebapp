/**
 * Unique idempotency keys for Writing submit actions.
 *
 * Key-building only: callers mint ONE key per submit action (click or
 * timer expiry) and reuse that same value across automatic retries of the
 * action. Same-attempt collapsing for identical AND near-identical content
 * lives server-side behind the SubmitGrading seam (explicit-key match, then
 * content-hash guard) — never in module-level client state.
 *
 * Dependency-free so it can be unit-tested in isolation and imported from
 * any submit surface (practice session, paper session) without pulling in
 * the API client chain.
 */

/**
 * A unique key for one logical submit action — call once per submit
 * click/timer-expiry and pass the SAME value into every retry of that one
 * attempt; a fresh user-initiated submit mints a new key.
 */
export function createSubmitIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `submit-${Date.now()}-${Math.random().toString(36).slice(2)}`;
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
