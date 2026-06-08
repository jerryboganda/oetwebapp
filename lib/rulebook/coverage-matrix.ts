/**
 * ============================================================================
 * Generic Rule Coverage Matrix
 * ============================================================================
 *
 * Kind-agnostic generalization of the Writing coverage matrix
 * (`writing-coverage.ts`, which is now a thin wrapper over this module). For
 * any `(kind, profession)` it produces one `RuleCoverageRow` per rule
 * describing HOW that rule is enforced — deterministic detector, forbidden
 * pattern, structured AI adjudication, or human review — and a validator that
 * proves the matrix is internally consistent and that no critical rule is left
 * uncovered.
 *
 * Enforcement honesty: a rule is `deterministic-detector` ONLY when its
 * `checkId` is backed by a real detector for that kind (see `check-ids.ts`).
 * A `checkId` with no backing detector does NOT count as deterministic — it
 * falls through to the AI-grounded path, and the validator additionally flags
 * it as an unsupported checkId.
 *
 * Browser-safe: pulls rulebooks from the static `loader.ts`, never `node:fs`.
 * ============================================================================
 */

import { loadRulebook } from './loader';
import { isSupportedCheckId } from './check-ids';
import type { ExamProfession, Rule, RuleKind, RuleSeverity } from './types';

export type CoverageMode =
  | 'deterministic-detector'
  | 'forbidden-pattern-detector'
  | 'structured-ai-adjudication'
  | 'human-review-only'
  | 'display-only'
  | 'not-implemented';

export interface RuleCoverageRow {
  kind: RuleKind;
  profession: ExamProfession;
  ruleId: string;
  severity: RuleSeverity;
  coverageMode: CoverageMode;
  enforcementPath: string[];
  checkId: string | null;
  structuredAiTask: string | null;
  waiverReason: string | null;
  owner: string;
  lastReviewedAt: string;
}

const OWNER = 'rulebook-governance';
const LAST_REVIEWED_AT = '2026-05-10';

/** Per-kind label for the grounded-AI adjudication task (display + audit only). */
const STRUCTURED_AI_TASK_BY_KIND: Partial<Record<RuleKind, string>> = {
  writing: 'WritingGrade/WritingCoachSuggest grounded prompt with ruleId allowlist',
  speaking: 'SpeakingAssess grounded prompt with ruleId allowlist',
};
const DEFAULT_STRUCTURED_AI_TASK = 'AI-grounded assessment with ruleId allowlist';

function hasForbiddenPatterns(rule: Rule): boolean {
  return Array.isArray(rule.forbiddenPatterns) && rule.forbiddenPatterns.length > 0;
}

/**
 * The single source of truth for how a rule is enforced. Mirrors the legacy
 * writing semantics (untagged → structured AI) so the writing 172-row baseline
 * stays byte-identical, while honestly requiring a *backing detector* for the
 * `deterministic-detector` classification.
 */
export function coverageModeFor(rule: Rule, kind: RuleKind): CoverageMode {
  if (rule.checkId && isSupportedCheckId(kind, rule.checkId)) return 'deterministic-detector';
  if (hasForbiddenPatterns(rule)) return 'forbidden-pattern-detector';
  if (rule.enforcement === 'human-review-only') return 'human-review-only';
  // `ai-grounded` and legacy-untagged rules are adjudicated by the grounded AI
  // prompt, which is seeded with every rule's body. The stricter "does it have
  // a deterministic detector?" question is answered separately by
  // `findUnenforcedRules()` (the conformance gate) and the dashboard.
  return 'structured-ai-adjudication';
}

function structuredAiTaskFor(kind: RuleKind): string {
  return STRUCTURED_AI_TASK_BY_KIND[kind] ?? DEFAULT_STRUCTURED_AI_TASK;
}

function enforcementPathFor(rule: Rule, mode: CoverageMode, kind: RuleKind): string[] {
  const paths: string[] = [];
  if (rule.checkId && isSupportedCheckId(kind, rule.checkId)) {
    paths.push(`RuleEngine checkId:${rule.checkId}`);
  }
  if (hasForbiddenPatterns(rule)) paths.push('RuleEngine forbiddenPatterns');
  if (mode === 'structured-ai-adjudication' || rule.severity === 'critical' || rule.severity === 'major') {
    paths.push(structuredAiTaskFor(kind));
  }
  if (mode === 'human-review-only') paths.push('Human reviewer (tutor/expert)');
  return paths;
}

export function buildRuleCoverageMatrix(
  kind: RuleKind,
  profession: ExamProfession,
): RuleCoverageRow[] {
  const book = loadRulebook(kind, profession);
  return book.rules.map((rule) => {
    const coverageMode = coverageModeFor(rule, kind);
    return {
      kind,
      profession,
      ruleId: rule.id,
      severity: rule.severity,
      coverageMode,
      enforcementPath: enforcementPathFor(rule, coverageMode, kind),
      checkId: rule.checkId ?? null,
      structuredAiTask: coverageMode === 'structured-ai-adjudication' ? structuredAiTaskFor(kind) : null,
      waiverReason: null,
      owner: OWNER,
      lastReviewedAt: LAST_REVIEWED_AT,
    };
  });
}

const COVERED_CRITICAL_MODES: ReadonlySet<CoverageMode> = new Set<CoverageMode>([
  'deterministic-detector',
  'forbidden-pattern-detector',
  'structured-ai-adjudication',
  'human-review-only',
]);

export function validateRuleCoverageMatrix(
  kind: RuleKind,
  profession: ExamProfession,
): string[] {
  const book = loadRulebook(kind, profession);
  const canonical = new Map(book.rules.map((rule) => [rule.id, rule]));
  const matrix = buildRuleCoverageMatrix(kind, profession);
  const issues: string[] = [];

  if (matrix.length !== book.rules.length) {
    issues.push(`matrix has ${matrix.length} rows, expected ${book.rules.length}`);
  }

  const seen = new Set<string>();
  for (const row of matrix) {
    if (seen.has(row.ruleId)) issues.push(`${row.ruleId}: duplicate coverage row`);
    seen.add(row.ruleId);

    const rule = canonical.get(row.ruleId);
    if (!rule) {
      issues.push(`${row.ruleId}: coverage row has no canonical rule`);
      continue;
    }

    if (row.severity !== rule.severity) {
      issues.push(`${row.ruleId}: severity ${row.severity} does not match canonical ${rule.severity}`);
    }

    if (row.checkId && !isSupportedCheckId(kind, row.checkId)) {
      issues.push(`${row.ruleId}: unsupported checkId ${row.checkId}`);
    }

    if (row.coverageMode === 'deterministic-detector' && !row.checkId) {
      issues.push(`${row.ruleId}: deterministic-detector row has no checkId`);
    }

    if (row.coverageMode === 'forbidden-pattern-detector' && !hasForbiddenPatterns(rule)) {
      issues.push(`${row.ruleId}: forbidden-pattern-detector row has no forbiddenPatterns`);
    }

    if (row.severity === 'critical') {
      const covered = COVERED_CRITICAL_MODES.has(row.coverageMode) || Boolean(row.waiverReason);
      if (!covered) issues.push(`${row.ruleId}: critical rule lacks enforcement/adjudication/waiver`);
    }
  }

  for (const rule of book.rules) {
    if (!seen.has(rule.id)) issues.push(`${rule.id}: missing coverage row`);
  }

  return issues;
}
