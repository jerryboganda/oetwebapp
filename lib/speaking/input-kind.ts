/**
 * What a Speaking learner handed in for marking, and the plain-English copy that
 * depends on it.
 *
 * Pure on purpose: the results-page tests mock '@/lib/api/speaking-sessions' with a
 * fixed export list, so nothing here may live in (or import from) lib/api.
 *
 * - `live_voice`: a transcript of the live conversation. No audio exists.
 * - `recording`: an audio recording (recorder fallback, or a human tutor room).
 * - `null`: nothing received yet, or a server that does not say. Copy stays neutral.
 *
 * Do not put the words "processing", "being graded", "analysing" or "Check again" in
 * any persistent copy here: the production QA script reads them as "still grading".
 */
export type SpeakingInputKind = 'live_voice' | 'recording';

type Kind = SpeakingInputKind | null;

/** A tutor room is always recorded; otherwise trust the server and fall back to neutral wording. */
export const speakingInputKind = (isTutorRoom: boolean, serverKind?: SpeakingInputKind | null): Kind =>
  isTutorRoom ? 'recording' : serverKind ?? null;

/** The kind every entry shares, or null when they differ (or none is known yet). */
export function commonInputKind(kinds: ReadonlyArray<Kind>): Kind {
  const first = kinds[0] ?? null;
  return first !== null && kinds.every((kind) => kind === first) ? first : null;
}

/** Info note above a live conversation's transcript: there is no audio to play. */
export const LIVE_TRANSCRIPT_NOTE =
  'No audio recording is stored for live conversations, so there is nothing to play back. This transcript is what was marked.';

/** Body of the "Submission received" banner. `onLabel` is the already-formatted date, if known. */
export function submissionReceivedCopy(kind: Kind, onLabel?: string | null): string {
  const on = onLabel ? ` on ${onLabel}` : '';
  if (kind === 'live_voice') return `We saved the transcript of your live conversation${on} and queued it for marking.`;
  if (kind === 'recording') return `We received your recording${on} and queued it for marking.`;
  return `We received your role-play${on} and queued it for marking.`;
}

/** Shown while a card is being marked. */
export function gradingInProgressCopy(kind: Kind): string {
  if (kind === 'live_voice') return 'Your live conversation transcript is being marked. This page updates automatically.';
  if (kind === 'recording') return 'Your recording is being transcribed and marked. This page updates automatically.';
  return 'Your role-play is being marked. This page updates automatically.';
}

/** Fallback body of a failed-grade notice (the server's own reason is shown when it sends one). */
export function gradeFailedSavedCopy(kind: Kind): string {
  const what = kind === 'live_voice' ? 'transcript' : kind === 'recording' ? 'recording' : 'role-play';
  return `Your ${what} is saved. No credits were used for this failed grade.`;
}

/** Exam-level notice once polling gives up. Cards can differ, so it names what they all share. */
export function gradingSlowSavedCopy(kinds: ReadonlyArray<Kind>): string {
  const common = commonInputKind(kinds);
  const what = common === 'live_voice' ? 'transcripts' : common === 'recording' ? 'recordings' : 'role-plays';
  return `Grading is taking longer than usual. Your ${what} are saved and the result will appear here.`;
}
