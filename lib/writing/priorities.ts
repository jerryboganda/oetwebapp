/**
 * Candidate-facing Top Priorities for a Writing result.
 *
 * The API now sends each priority as plain, label-free text, already distinct
 * and capped; this module is the legacy safety net for old stored rows and
 * cached responses, so the result and mock-result screens render the same way.
 */

import { cleanCandidateText } from './candidate-text';

// Rows written before the digest stored each priority as "<rule id>: <message>",
// and AI findings carry "AI.<criterion>" / "AI:<rule id>" ids. A rule id has no
// spaces and contains a digit, "." , "_" or "-", so a plain lead-in such as
// "Purpose: …" is left alone. Owner-mandated safety net: keep it verbatim.
export const PRIORITY_RULE_LABEL = /^(?:AI(?:[.:][\w.-]*)?|[\w-]*[\d._-][\w.-]*):\s+/;

/** The candidate-facing priority text: the message without its internal rule label. */
export function priorityText(priority: string): string {
  return priority.replace(PRIORITY_RULE_LABEL, '').trim();
}

// The digest never emits it any more; an old row could still carry the placeholder.
const PLACEHOLDER_KEY = 'ai grader finding';

const normalisedKey = (text: string) => text.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();

/**
 * Up to `max` distinct priorities, label-free, in the order received. Distinct
 * means the same words after case/punctuation are ignored. Fewer than `max`
 * (down to none) is correct: nothing is padded.
 */
export function candidatePriorities(
  list: readonly (string | null | undefined)[] | null | undefined,
  max = 3,
): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of list ?? []) {
    if (typeof raw !== 'string') continue;
    const text = cleanCandidateText(priorityText(raw));
    if (!text) continue;
    const key = normalisedKey(text);
    if (!key || key === PLACEHOLDER_KEY || seen.has(key)) continue;
    seen.add(key);
    out.push(text);
    if (out.length >= max) break;
  }
  return out;
}
