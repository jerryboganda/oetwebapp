/**
 * ============================================================================
 * Browser-safe Rule Enforcement Classifier
 * ============================================================================
 *
 * The single, honest answer to "is this rule actually enforced, and how?" —
 * usable from the admin conformance dashboard (client-side) because it imports
 * ONLY `check-ids.ts` + `types.ts` and never `node:fs` / `registry.ts`.
 *
 * `findUnenforcedRules()` in `registry.ts` (the Node-only CI gate) delegates to
 * `isRuleEnforced()` here, so the dashboard and the gate can never disagree —
 * the `coverage.test.ts` lock test proves it.
 *
 * Honest contract — a rule is genuinely enforced iff:
 *   - it has a `checkId` BACKED by a real detector (`isSupportedCheckId`), OR
 *   - it has `forbiddenPatterns` (a regex detector runs), OR
 *   - `enforcement === 'ai-grounded'` (its body is fed to the grounded AI prompt), OR
 *   - `enforcement === 'human-review-only'` (a tutor/expert scores it).
 * A dead/unsupported `checkId`, or no marker at all, is `not-enforced` — a
 * SILENT gap, which is exactly what the gate must catch.
 * ============================================================================
 */

import { isSupportedCheckId } from './check-ids';
import { loadRulebook } from './loader';
import type { ExamProfession, Rule, RuleKind, RuleSeverity } from './types';

export type RuleEnforcementStatus =
  | 'deterministic'
  | 'forbidden-pattern'
  | 'ai-grounded'
  | 'human-review'
  | 'not-enforced';

function hasForbiddenPatterns(rule: Rule): boolean {
  return Array.isArray(rule.forbiddenPatterns) && rule.forbiddenPatterns.length > 0;
}

/** How a single rule is enforced (or that it is not). */
export function classifyRuleEnforcement(rule: Rule, kind: RuleKind): RuleEnforcementStatus {
  if (rule.checkId && isSupportedCheckId(kind, rule.checkId)) return 'deterministic';
  if (hasForbiddenPatterns(rule)) return 'forbidden-pattern';
  if (rule.enforcement === 'ai-grounded') return 'ai-grounded';
  if (rule.enforcement === 'human-review-only') return 'human-review';
  return 'not-enforced';
}

/** Only critical/major rules are REQUIRED to carry a concrete enforcer. */
export function ruleNeedsEnforcement(rule: Rule): boolean {
  return rule.severity === 'critical' || rule.severity === 'major';
}

/** True when a rule is genuinely enforced, or is not required to be (minor/info). */
export function isRuleEnforced(rule: Rule, kind: RuleKind): boolean {
  if (!ruleNeedsEnforcement(rule)) return true;
  return classifyRuleEnforcement(rule, kind) !== 'not-enforced';
}

export interface CoverageSummary {
  total: number;
  byStatus: Record<RuleEnforcementStatus, number>;
  /** Count of critical/major rules with no concrete enforcer — the gate's target. */
  unenforcedCriticalMajor: number;
}

const ZERO_BY_STATUS: () => Record<RuleEnforcementStatus, number> = () => ({
  deterministic: 0,
  'forbidden-pattern': 0,
  'ai-grounded': 0,
  'human-review': 0,
  'not-enforced': 0,
});

/** Aggregate enforcement counts for a set of rules (e.g. one rulebook). */
export function summarizeRuleCoverage(rules: readonly Rule[], kind: RuleKind): CoverageSummary {
  const byStatus = ZERO_BY_STATUS();
  let unenforcedCriticalMajor = 0;
  for (const rule of rules) {
    const status = classifyRuleEnforcement(rule, kind);
    byStatus[status] += 1;
    if (ruleNeedsEnforcement(rule) && status === 'not-enforced') unenforcedCriticalMajor += 1;
  }
  return { total: rules.length, byStatus, unenforcedCriticalMajor };
}

// ---------------------------------------------------------------------------
// Conformance report — data source for the admin /admin/conformance dashboard
// ---------------------------------------------------------------------------

export interface ConformanceRuleRow {
  ruleId: string;
  section: string;
  title: string;
  severity: RuleSeverity;
  status: RuleEnforcementStatus;
}

export interface ConformanceKindReport {
  kind: RuleKind;
  profession: ExamProfession;
  label: string;
  summary: CoverageSummary;
  rows: ConformanceRuleRow[];
}

/**
 * The four OET exam rulebooks the conformance program targets, plus the two
 * candidate-facing exam-mode books. Profession `medicine` is canonical — the
 * rule set and enforcement classification are identical across professions.
 */
export const OET_CONFORMANCE_RULEBOOKS: ReadonlyArray<{
  kind: RuleKind;
  profession: ExamProfession;
  label: string;
}> = [
  { kind: 'listening', profession: 'medicine', label: 'Listening (authoring)' },
  { kind: 'listening-exam-mode', profession: 'medicine', label: 'Listening (exam mode)' },
  { kind: 'reading', profession: 'medicine', label: 'Reading (authoring)' },
  { kind: 'reading-exam-mode', profession: 'medicine', label: 'Reading (exam mode)' },
  { kind: 'writing', profession: 'medicine', label: 'Writing' },
  { kind: 'speaking', profession: 'medicine', label: 'Speaking' },
];

/**
 * Build the per-rulebook conformance report for the admin dashboard. Pure +
 * browser-safe (static rulebook imports only); the dashboard renders this with
 * NO publish/validate controls — it is read-only reporting.
 */
export function buildConformanceReport(): ConformanceKindReport[] {
  return OET_CONFORMANCE_RULEBOOKS.map(({ kind, profession, label }) => {
    const book = loadRulebook(kind, profession);
    const rows: ConformanceRuleRow[] = book.rules.map((rule) => ({
      ruleId: rule.id,
      section: rule.section,
      title: rule.title,
      severity: rule.severity,
      status: classifyRuleEnforcement(rule, kind),
    }));
    return { kind, profession, label, summary: summarizeRuleCoverage(book.rules, kind), rows };
  });
}
