import { describe, expect, it } from 'vitest';
import {
  classifyRuleEnforcement,
  isRuleEnforced,
  summarizeRuleCoverage,
  buildConformanceReport,
  type RuleEnforcementStatus,
} from '../coverage';
import { findUnenforcedRules, discoverRulebooks } from '../registry';
import { WRITING_CHECK_IDS } from '../check-ids';
import type { Rule } from '../types';

function rule(partial: Partial<Rule>): Rule {
  return {
    id: partial.id ?? 'R00.0',
    section: partial.section ?? '00',
    title: partial.title ?? 'test',
    body: partial.body ?? 'test body',
    severity: partial.severity ?? 'critical',
    ...partial,
  };
}

describe('browser-safe rule enforcement classifier', () => {
  it('classifies a backed checkId as deterministic', () => {
    const known = [...WRITING_CHECK_IDS][0];
    expect(classifyRuleEnforcement(rule({ checkId: known }), 'writing')).toBe('deterministic');
  });

  it('classifies an unbacked (dead) checkId as not-enforced', () => {
    expect(classifyRuleEnforcement(rule({ checkId: '__dead_check__' }), 'writing')).toBe(
      'not-enforced',
    );
  });

  it('classifies forbiddenPatterns, ai-grounded, human-review, and untagged rules', () => {
    expect(classifyRuleEnforcement(rule({ forbiddenPatterns: ['x'] }), 'writing')).toBe(
      'forbidden-pattern',
    );
    expect(classifyRuleEnforcement(rule({ enforcement: 'ai-grounded' }), 'writing')).toBe(
      'ai-grounded',
    );
    expect(classifyRuleEnforcement(rule({ enforcement: 'human-review-only' }), 'writing')).toBe(
      'human-review',
    );
    expect(classifyRuleEnforcement(rule({}), 'writing')).toBe('not-enforced');
  });

  it('treats minor/info rules as always enforced (no enforcer required)', () => {
    expect(isRuleEnforced(rule({ severity: 'minor' }), 'writing')).toBe(true);
    expect(isRuleEnforced(rule({ severity: 'info' }), 'writing')).toBe(true);
    expect(isRuleEnforced(rule({ severity: 'critical' }), 'writing')).toBe(false);
  });

  it('summarizes coverage counts by status', () => {
    const rules = [
      rule({ id: 'A', checkId: [...WRITING_CHECK_IDS][0] }),
      rule({ id: 'B', forbiddenPatterns: ['x'] }),
      rule({ id: 'C' }),
      rule({ id: 'D', severity: 'minor' }),
    ];
    const summary = summarizeRuleCoverage(rules, 'writing');
    expect(summary.total).toBe(4);
    expect(summary.byStatus.deterministic).toBe(1);
    expect(summary.byStatus['forbidden-pattern']).toBe(1);
    expect(summary.unenforcedCriticalMajor).toBe(1); // only C (critical, untagged)
  });

  it('builds a conformance report for all six OET rulebooks with zero unenforced critical/major rules', () => {
    const report = buildConformanceReport();
    expect(report.map((r) => r.kind).sort()).toEqual(
      ['listening', 'listening-exam-mode', 'reading', 'reading-exam-mode', 'speaking', 'writing'],
    );
    for (const r of report) {
      expect(r.rows.length).toBe(r.summary.total);
      expect(r.summary.unenforcedCriticalMajor).toBe(0);
    }
  });

  it('is locked to findUnenforcedRules: the gate flags exactly the not-enforced critical/major rules', () => {
    const discovered = discoverRulebooks();
    const expected = new Set<string>();
    for (const d of discovered) {
      for (const r of d.rulebook.rules) {
        if (!isRuleEnforced(r, d.kind)) expected.add(`${d.kind}:${d.profession}:${r.id}`);
      }
    }
    const actual = new Set(
      findUnenforcedRules(discovered).map((u) => `${u.kind}:${u.profession}:${u.rule.id}`),
    );
    expect(actual).toEqual(expected);
  });
});
