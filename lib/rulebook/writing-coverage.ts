import { loadRulebook } from './loader';
import { SUPPORTED_WRITING_CHECK_IDS } from './writing-rules';
import type { ExamProfession, Rule, RuleSeverity } from './types';

export type WritingCoverageMode =
  | 'deterministic-detector'
  | 'forbidden-pattern-detector'
  | 'structured-ai-adjudication'
  | 'human-review-only'
  | 'display-only'
  | 'not-implemented';

export interface WritingRuleCoverageRow {
  ruleId: string;
  severity: RuleSeverity;
  coverageMode: WritingCoverageMode;
  enforcementPath: string[];
  checkId: string | null;
  structuredAiTask: string | null;
  requiresCaseNoteMarkers: boolean;
  waiverReason: string | null;
  owner: string;
  lastReviewedAt: string;
}

const OWNER = 'rulebook-governance';
const LAST_REVIEWED_AT = '2026-05-10';

const MARKER_DEPENDENT_CHECK_IDS = new Set([
  'content_requires_allergy_for_atopic',
  'closure_mentions_review_if_required',
  'enclosure_results_phrase',
  'closure_mentions_patient_request_if_flagged',
  'closure_mentions_consent_if_flagged',
]);

const STRUCTURED_AI_TASK = 'WritingGrade/WritingCoachSuggest grounded prompt with ruleId allowlist';

function hasForbiddenPatterns(rule: Rule): boolean {
  return Array.isArray(rule.forbiddenPatterns) && rule.forbiddenPatterns.length > 0;
}

function coverageModeFor(rule: Rule): WritingCoverageMode {
  if (rule.checkId) return 'deterministic-detector';
  if (hasForbiddenPatterns(rule)) return 'forbidden-pattern-detector';
  return 'structured-ai-adjudication';
}

function enforcementPathFor(rule: Rule, mode: WritingCoverageMode): string[] {
  const paths: string[] = [];
  if (rule.checkId) paths.push(`WritingRuleEngine checkId:${rule.checkId}`);
  if (hasForbiddenPatterns(rule)) paths.push('WritingRuleEngine forbiddenPatterns');
  if (mode === 'structured-ai-adjudication' || rule.severity === 'critical' || rule.severity === 'major') {
    paths.push(STRUCTURED_AI_TASK);
  }
  return paths;
}

export function buildWritingRuleCoverageMatrix(profession: ExamProfession = 'medicine'): WritingRuleCoverageRow[] {
  const book = loadRulebook('writing', profession);
  return book.rules.map((rule) => {
    const coverageMode = coverageModeFor(rule);
    return {
      ruleId: rule.id,
      severity: rule.severity,
      coverageMode,
      enforcementPath: enforcementPathFor(rule, coverageMode),
      checkId: rule.checkId ?? null,
      structuredAiTask: coverageMode === 'structured-ai-adjudication' ? STRUCTURED_AI_TASK : null,
      requiresCaseNoteMarkers: rule.checkId ? MARKER_DEPENDENT_CHECK_IDS.has(rule.checkId) : false,
      waiverReason: null,
      owner: OWNER,
      lastReviewedAt: LAST_REVIEWED_AT,
    };
  });
}

export function validateWritingRuleCoverageMatrix(profession: ExamProfession = 'medicine'): string[] {
  const book = loadRulebook('writing', profession);
  const supported = new Set(SUPPORTED_WRITING_CHECK_IDS);
  const canonical = new Map(book.rules.map((rule) => [rule.id, rule]));
  const matrix = buildWritingRuleCoverageMatrix(profession);
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

    if (row.checkId && !supported.has(row.checkId)) {
      issues.push(`${row.ruleId}: unsupported checkId ${row.checkId}`);
    }

    if (row.coverageMode === 'deterministic-detector' && !row.checkId) {
      issues.push(`${row.ruleId}: deterministic-detector row has no checkId`);
    }

    if (row.coverageMode === 'forbidden-pattern-detector' && !hasForbiddenPatterns(rule)) {
      issues.push(`${row.ruleId}: forbidden-pattern-detector row has no forbiddenPatterns`);
    }

    if (row.severity === 'critical') {
      const covered = row.coverageMode === 'deterministic-detector'
        || row.coverageMode === 'forbidden-pattern-detector'
        || row.coverageMode === 'structured-ai-adjudication'
        || Boolean(row.waiverReason);
      if (!covered) issues.push(`${row.ruleId}: critical rule lacks enforcement/adjudication/waiver`);
    }
  }

  for (const rule of book.rules) {
    if (!seen.has(rule.id)) issues.push(`${rule.id}: missing coverage row`);
  }

  return issues;
}