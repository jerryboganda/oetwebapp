/**
 * Reading AUTHORING detectors (non-blocking).
 *
 * Deterministic checks for the structural rules in the Reading authoring
 * rulebook (`rulebooks/reading/<profession>/rulebook.v1.json`). Each detector
 * is keyed by the rule's `checkId` and returns `LintFinding[]` — WARNINGS only.
 * Per decision 2, these MUST NEVER block publishing; they feed the admin
 * conformance dashboard, the golden fixtures, and the CI gate.
 *
 * Content-judgement rules (clinical-synonym equivalence, distractor
 * plausibility, source-grounding) are NOT here — they are `human-review-only`
 * in the rulebook JSON.
 */

import { loadRulebook } from './loader';
import type { ExamProfession, LintFinding, Rule } from './types';

export interface AuthoredReadingItem {
  part: 'A' | 'B' | 'C';
  /** matching | short_answer | sentence_completion | mcq */
  type?: string;
  /** number of MCQ options, when applicable */
  optionCount?: number;
}

export interface AuthoredReadingPaper {
  partATextCount?: number;
  items: AuthoredReadingItem[];
}

type ReadingDetector = (rule: Rule, paper: AuthoredReadingPaper) => LintFinding[];

function finding(rule: Rule, message: string): LintFinding {
  return { ruleId: rule.id, severity: rule.severity, message };
}

function countPart(paper: AuthoredReadingPaper, part: AuthoredReadingItem['part']): number {
  return paper.items.filter((i) => i.part === part).length;
}

const DETECTORS: Record<string, ReadingDetector> = {
  reading_shape_42(rule, paper) {
    const a = countPart(paper, 'A');
    const b = countPart(paper, 'B');
    const c = countPart(paper, 'C');
    const total = paper.items.length;
    if (total === 42 && a === 20 && b === 6 && c === 16) return [];
    return [finding(rule, `Reading paper must be 42 items (A=20, B=6, C=16); got total=${total} (A=${a}, B=${b}, C=${c}).`)];
  },
  reading_part_a_four_texts(rule, paper) {
    if (paper.partATextCount === undefined || paper.partATextCount === 4) return [];
    return [finding(rule, `Part A must have exactly 4 source texts; got ${paper.partATextCount}.`)];
  },
  reading_part_b_three_options(rule, paper) {
    const offenders = paper.items.filter(
      (i) => i.part === 'B' && i.optionCount !== undefined && i.optionCount !== 3,
    );
    if (offenders.length === 0) return [];
    return [finding(rule, `Part B items must have exactly 3 options; ${offenders.length} item(s) do not.`)];
  },
  reading_part_c_four_options(rule, paper) {
    const offenders = paper.items.filter(
      (i) => i.part === 'C' && i.optionCount !== undefined && i.optionCount !== 4,
    );
    if (offenders.length === 0) return [];
    return [finding(rule, `Part C items must have exactly 4 options; ${offenders.length} item(s) do not.`)];
  },
};

export const SUPPORTED_READING_CHECK_IDS = Object.freeze(Object.keys(DETECTORS).sort());

/** Run every reading authoring detector whose rule is present in the rulebook. */
export function lintReadingPaper(
  paper: AuthoredReadingPaper,
  profession: ExamProfession = 'medicine',
): LintFinding[] {
  const book = loadRulebook('reading', profession);
  const findings: LintFinding[] = [];
  for (const rule of book.rules) {
    if (rule.checkId && DETECTORS[rule.checkId]) {
      findings.push(...DETECTORS[rule.checkId](rule, paper));
    }
  }
  return findings;
}
