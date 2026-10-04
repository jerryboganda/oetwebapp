// Opening control of the live AI patient (owner spec 4 Oct 2026): the patient never speaks first, a greeting, an introduction
// or a name exchange is not an invitation to tell the story, and the opening response comes only after an explicit invitation.
// Judged from the SAVED transcript of a run of the `opening-control` candidate script
// (scripts/qa/speaking-candidate-scripts/opening-control.txt): "Good morning." then a 2 s pause, "My name is Doctor Smith. How
// may I address you?", "Please have a seat.", and last "How can I help you today?".
// Pure: unit-tested without a browser. Used by speaking-live-voice-browser-e2e.mjs (checks.openingControl).
import { tokens } from './live-voice-transcript-quality.mjs';

/** Most words the patient may say before the invitation: a greeting back, a name, a "thank you" (never the story). */
export const MAX_WORDS_BEFORE_INVITATION = 20;
/** Fewest words that show the patient has begun answering the invitation. */
export const MIN_WORDS_AFTER_INVITATION = 10;

const wordCount = (text) => tokens(text).length;

/**
 * @param {{ segments: Array<{ speaker: string, text: string, startMs: number, endMs: number }> }} input
 * @returns {{ ok: boolean | null, reason?: string, neverSpeaksFirst?: boolean, noStoryBeforeInvitation?: boolean,
 *   answersTheInvitation?: boolean, wordsBeforeInvitation?: number, wordsAfterInvitation?: number }}
 */
export function openingControlVerdict({ segments }) {
  const candidate = segments
    .filter((s) => s.speaker === 'candidate')
    .sort((a, b) => a.startMs - b.startMs);
  const invitation = candidate.find((s) => /\bhelp you today\b/i.test(String(s.text)));
  if (!candidate.length || !invitation) return { ok: null, reason: 'the invitation was not found in the saved transcript' };

  const firstCandidateAt = candidate[0].startMs;
  const patient = segments.filter((s) => s.speaker === 'patient');
  const before = patient.filter((s) => s.startMs < invitation.endMs);
  const after = patient.filter((s) => s.startMs >= invitation.endMs);
  const wordsBeforeInvitation = before.reduce((n, s) => n + wordCount(s.text), 0);
  const wordsAfterInvitation = after.reduce((n, s) => n + wordCount(s.text), 0);

  const neverSpeaksFirst = !patient.some((s) => s.startMs < firstCandidateAt);
  const noStoryBeforeInvitation = wordsBeforeInvitation <= MAX_WORDS_BEFORE_INVITATION;
  const answersTheInvitation = wordsAfterInvitation >= MIN_WORDS_AFTER_INVITATION;
  return {
    ok: neverSpeaksFirst && noStoryBeforeInvitation && answersTheInvitation,
    neverSpeaksFirst,
    noStoryBeforeInvitation,
    answersTheInvitation,
    wordsBeforeInvitation,
    wordsAfterInvitation,
  };
}
