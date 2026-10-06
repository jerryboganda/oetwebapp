/**
 * Candidate-facing text hygiene for the Writing result screens.
 *
 * The backend is the root fix (WritingCandidateText projects clean text before
 * it leaves the API). This is defence in depth for old stored rows, a cached
 * response or any string a later change forgets to clean: it removes internal
 * rule/check ids, provider and model tags, from free text the server wrote.
 *
 * NEVER run it on verbatim fields: the candidate's own letter, `candidateWording`,
 * quotes, snippets or the model answer.
 */

import type { WritingCandidateSeverity } from './types';

// Internal id families (rulebook ids, registry check ids, grader pseudo-ids).
// Case-sensitive on purpose: a lowercase word must never match. `\b` in front,
// a hyphen-aware lookahead behind (ids contain hyphens, so `\b` alone is wrong).
// Deliberately closed: a generic "letters-digits" pattern would eat clinical
// tokens such as B-12 or T-12.
const ID_FAMILIES = [
  String.raw`BUILTIN\.[A-Za-z0-9_]+`,
  String.raw`AI[.:][A-Za-z0-9_.-]*[A-Za-z0-9_]`,
  String.raw`(?:G|DH|OWN|[A-Z]{2,4})-W-\d{1,3}`,
  String.raw`[A-Z]{2,4}-[A-Z]{1,4}-\d{1,3}`,
  String.raw`OW-\d{1,3}`,
  String.raw`OA\d?-\d{1,3}`,
  String.raw`SC-\d{1,3}`,
  String.raw`LT-[A-Z]{2}`,
  String.raw`R\d{1,2}\.\d{1,3}`,
].join('|');
const ID = String.raw`\b(?:${ID_FAMILIES})(?![\w-])`;
const ID_LIST = String.raw`${ID}(?:\s*(?:,|;|/|&|and)\s*${ID})*`;
const DASH = String.raw`[–—-]`;

// "(OW-005, DH-W-016)", "[G-W-117]", "(see R3.4)"
const BRACKETED_IDS = new RegExp(
  String.raw`\s*[(\[]\s*(?:(?:[Ss]ee|[Pp]er|[Rr]ef\.?|[Rr]ules?)\s+)?${ID_LIST}\s*[)\]]`,
  'g',
);
// "OW-005: ...", "Rule R08.14 - ...", "AI.purpose: ..." at the start of the text
const LEADING_ID_LABEL = new RegExp(String.raw`^\s*(?:[Rr]ules?\s+)?${ID_LIST}\s*(?::|${DASH})\s*`);
// A bare R1 / R5.2 / "R1-R5" label, only in label position
const LEADING_R_LABEL = new RegExp(
  String.raw`^\s*R\d{1,2}(?:\.\d{1,3})?(?:\s*${DASH}\s*R\d{1,2}(?:\.\d{1,3})?)?\s*(?::|${DASH}|\.(?=\s))\s*`,
);
const R_RANGE = /\bR\d{1,2}\s*[–-]\s*R\d{1,2}\b/g;
// "violates G-W-117", "per OW-005", "see R3.4" -> a plain pointer
const POINTER_TO_ID = new RegExp(
  String.raw`\b(?:[Ss]ee|[Pp]er|[Uu]nder|[Vv]iolates?|[Bb]reaches?|[Rr]ules?)\s+${ID_LIST}`,
  'g',
);
const BARE_IDS = new RegExp(ID_LIST, 'g');
// Registry check ids are snake_case ("linker_avoid_words"); ordinary prose never is.
const SNAKE_CASE_ID = /\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b/g;
// Provider / model debug tags.
const PROVIDER_TAG =
  /\b(?:writing-claude-sub|claude(?:[- ](?:opus|sonnet|haiku))?(?:-[\w.]+)*|gpt-[\w.-]+|openai|anthropic|codex|typesafe|jev)\b/gi;

/**
 * The candidate-safe form of server-written free text: internal ids, provider
 * tags and their leftover punctuation removed. Returns '' when nothing readable
 * is left, so callers can drop the line or show their own fallback.
 */
export function cleanCandidateText(text: string | null | undefined): string {
  if (typeof text !== 'string') return '';
  const original = text.trim();
  if (!original) return '';

  let out = original
    .replace(BRACKETED_IDS, '')
    .replace(LEADING_ID_LABEL, '')
    .replace(LEADING_R_LABEL, '')
    .replace(R_RANGE, '')
    .replace(POINTER_TO_ID, 'the relevant guideline')
    .replace(BARE_IDS, '')
    .replace(SNAKE_CASE_ID, '')
    .replace(PROVIDER_TAG, '');

  // Nothing internal in it: hand back the server's text exactly as written.
  if (out === original) return original;

  out = out
    .replace(/[(\[]\s*[)\]]/g, '')
    .replace(/,\s*,/g, ',')
    .replace(/\s+([,.;:!?])/g, '$1')
    .replace(/[ \t]{2,}/g, ' ')
    .replace(/^[\s,;:.–—-]+/, '')
    .trim();

  // The label was the start of the sentence: restore sentence case.
  if (/^[a-z]/.test(out)) out = out.charAt(0).toUpperCase() + out.slice(1);
  return out;
}

/** Alias for call sites that read better as a noun: `candidateText(item.whyItMatters)`. */
export const candidateText = cleanCandidateText;

/** Multi-line human text (tutor notes): each line is cleaned, the line breaks are kept. */
export function cleanCandidateParagraphs(text: string | null | undefined): string {
  if (typeof text !== 'string') return '';
  return text
    .split(/\r?\n/)
    .map((line) => cleanCandidateText(line))
    .join('\n')
    .trim();
}

/** Cleans every line, drops empties and exact repeats (order kept). */
export function cleanCandidateList(items: readonly (string | null | undefined)[] | null | undefined): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const item of items ?? []) {
    const text = cleanCandidateText(item);
    if (!text || seen.has(text)) continue;
    seen.add(text);
    out.push(text);
  }
  return out;
}

/**
 * A stored category slug ("layout_format", "register-jargon") as a plain label
 * ("Layout format"). Never show a raw slug to a candidate.
 */
export function plainCategoryLabel(category: string | null | undefined): string {
  const words = (category ?? '').replace(/[_-]+/g, ' ').replace(/\s{2,}/g, ' ').trim();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : '';
}

const SEVERITY_ALIASES: Record<string, WritingCandidateSeverity> = {
  critical: 'critical',
  high: 'critical',
  major: 'major',
  medium: 'major',
  moderate: 'major',
  minor: 'minor',
  low: 'minor',
  advisory: 'advisory',
  info: 'advisory',
};

/**
 * Normalises any severity the API may send (lowercase wire values, the stored
 * 'info', legacy high/medium/low, or a capitalised string from an old build)
 * to the four candidate-facing levels. An unknown value reads as Minor.
 */
export function candidateSeverity(severity: string | null | undefined): WritingCandidateSeverity {
  return SEVERITY_ALIASES[(severity ?? '').trim().toLowerCase()] ?? 'minor';
}

const SEVERITY_LABEL: Record<WritingCandidateSeverity, string> = {
  critical: 'Critical',
  major: 'Major',
  minor: 'Minor',
  advisory: 'Advisory',
};

const SEVERITY_MESSAGE_KEY: Record<WritingCandidateSeverity, string> = {
  critical: 'writing.submissions.results.severity.critical',
  major: 'writing.submissions.results.severity.major',
  minor: 'writing.submissions.results.severity.minor',
  advisory: 'writing.submissions.results.severity.advisory',
};

/**
 * "Critical" / "Major" / "Minor" / "Advisory". Pass the page's `t` for the
 * translated label; without it the English label is returned.
 */
export function severityLabel(
  severity: string | null | undefined,
  t?: (key: string) => string,
): string {
  const level = candidateSeverity(severity);
  return t ? t(SEVERITY_MESSAGE_KEY[level]) : SEVERITY_LABEL[level];
}
