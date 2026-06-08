/**
 * Listening AUTHORING detectors (non-blocking).
 *
 * Deterministic checks for the structural rules in the Listening authoring
 * rulebook (`rulebooks/listening/<profession>/rulebook.v1.json`). Each detector
 * is keyed by the rule's `checkId` and returns `LintFinding[]` — WARNINGS only.
 * Per decision 2 they MUST NEVER block publishing; they feed the admin
 * conformance dashboard, the golden fixtures, and the CI gate.
 *
 * Enum-driven checks (distractor categories, speaker attitudes) read the
 * allowed values from the rulebook's own `tables`, so the rulebook stays the
 * single source of truth.
 *
 * Content-judgement rules (clinical spelling variants, gist targeting,
 * inference, distractor plausibility, accent realism) are NOT here — they are
 * `human-review-only` in the rulebook JSON.
 */

import { loadRulebook } from './loader';
import type { ExamProfession, LintFinding, Rule, Rulebook } from './types';

export type ListeningPart = 'A1' | 'A2' | 'B' | 'C1' | 'C2';

export interface AuthoredListeningOption {
  category?: string | null;
}

export interface AuthoredListeningItem {
  part: ListeningPart;
  /** short_answer | fill_in_blank | mcq */
  type?: string;
  optionCount?: number;
  distractors?: AuthoredListeningOption[];
  transcriptExcerpt?: string | null;
  transcriptStartMs?: number | null;
  transcriptEndMs?: number | null;
  speakerAttitude?: string | null;
}

export interface AuthoredListeningPaper {
  items: AuthoredListeningItem[];
}

type ListeningDetector = (rule: Rule, paper: AuthoredListeningPaper, book: Rulebook) => LintFinding[];

function finding(rule: Rule, message: string): LintFinding {
  return { ruleId: rule.id, severity: rule.severity, message };
}

const isPartA = (p: ListeningPart) => p === 'A1' || p === 'A2';
const isPartC = (p: ListeningPart) => p === 'C1' || p === 'C2';
const isMcqType = (t?: string) => t === 'mcq';

function tableList(book: Rulebook, key: string): string[] {
  const tables = (book.tables ?? {}) as Record<string, unknown>;
  const value = tables[key];
  return Array.isArray(value) ? (value as string[]) : [];
}

const DETECTORS: Record<string, ListeningDetector> = {
  listening_shape_42(rule, paper) {
    if (paper.items.length === 42) return [];
    return [finding(rule, `Listening paper must have exactly 42 items; got ${paper.items.length}.`)];
  },
  listening_part_split_24_6_12(rule, paper) {
    const a = paper.items.filter((i) => isPartA(i.part)).length;
    const b = paper.items.filter((i) => i.part === 'B').length;
    const c = paper.items.filter((i) => isPartC(i.part)).length;
    if (a === 24 && b === 6 && c === 12) return [];
    return [finding(rule, `Listening split must be A=24, B=6, C=12; got A=${a}, B=${b}, C=${c}.`)];
  },
  listening_part_a_short_answer_only(rule, paper) {
    const offenders = paper.items.filter((i) => isPartA(i.part) && isMcqType(i.type));
    if (offenders.length === 0) return [];
    return [finding(rule, `Part A items must be short-answer, never MCQ; ${offenders.length} item(s) are MCQ.`)];
  },
  listening_part_b_three_options(rule, paper) {
    const offenders = paper.items.filter((i) => i.part === 'B' && i.optionCount !== undefined && i.optionCount !== 3);
    if (offenders.length === 0) return [];
    return [finding(rule, `Part B items must have exactly 3 options; ${offenders.length} item(s) do not.`)];
  },
  listening_part_c_three_options(rule, paper) {
    const offenders = paper.items.filter((i) => isPartC(i.part) && i.optionCount !== undefined && i.optionCount !== 3);
    if (offenders.length === 0) return [];
    return [finding(rule, `Part C items must have exactly 3 options; ${offenders.length} item(s) do not.`)];
  },
  listening_distractor_categories_valid(rule, paper, book) {
    const allowed = new Set(tableList(book, 'distractorCategories'));
    if (allowed.size === 0) return [];
    const bad = paper.items
      .flatMap((i) => i.distractors ?? [])
      .filter((d) => d.category != null && !allowed.has(d.category));
    if (bad.length === 0) return [];
    return [finding(rule, `${bad.length} distractor(s) use a category outside ${[...allowed].join(', ')}.`)];
  },
  listening_item_has_transcript_evidence(rule, paper) {
    const missing = paper.items.filter((i) => !i.transcriptExcerpt || i.transcriptExcerpt.trim() === '');
    if (missing.length === 0) return [];
    return [finding(rule, `${missing.length} item(s) are missing a supporting transcript excerpt.`)];
  },
  listening_transcript_timecodes_valid(rule, paper) {
    const bad = paper.items.filter((i) => {
      const s = i.transcriptStartMs;
      const e = i.transcriptEndMs;
      if (s == null && e == null) return false;
      return s == null || e == null || s < 0 || e <= s;
    });
    if (bad.length === 0) return [];
    return [finding(rule, `${bad.length} item(s) have invalid transcript time-codes (need 0 <= start < end).`)];
  },
  listening_speaker_attitude_part_c_only(rule, paper) {
    const offenders = paper.items.filter((i) => !isPartC(i.part) && i.speakerAttitude != null);
    if (offenders.length === 0) return [];
    return [finding(rule, `speakerAttitude must be null outside Part C; ${offenders.length} non-Part-C item(s) set it.`)];
  },
  listening_speaker_attitude_enum_valid(rule, paper, book) {
    const allowed = new Set(tableList(book, 'speakerAttitudes'));
    if (allowed.size === 0) return [];
    const bad = paper.items.filter((i) => i.speakerAttitude != null && !allowed.has(i.speakerAttitude));
    if (bad.length === 0) return [];
    return [finding(rule, `${bad.length} item(s) use a speakerAttitude outside ${[...allowed].join(', ')}.`)];
  },
};

export const SUPPORTED_LISTENING_CHECK_IDS = Object.freeze(Object.keys(DETECTORS).sort());

/** Run every listening authoring detector whose rule is present in the rulebook. */
export function lintListeningPaper(
  paper: AuthoredListeningPaper,
  profession: ExamProfession = 'medicine',
): LintFinding[] {
  const book = loadRulebook('listening', profession);
  const findings: LintFinding[] = [];
  for (const rule of book.rules) {
    if (rule.checkId && DETECTORS[rule.checkId]) {
      findings.push(...DETECTORS[rule.checkId](rule, paper, book));
    }
  }
  return findings;
}
