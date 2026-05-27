import { describe, it, expect } from 'vitest';
import { findUnimplementedExamModeRules } from '../exam-mode-rules';

/**
 * CI gate — every CRITICAL or MAJOR rule in the listening-exam-mode and
 * reading-exam-mode rulebooks must either:
 *  (a) have a `checkId` AND an entry in `lib/rulebook/exam-mode-rules.ts`, OR
 *  (b) declare `enforcement: 'ai-grounded'` or `'human-review-only'`.
 *
 * This test fails when a rulebook edit adds a new rule but forgets to wire
 * an enforcer, which is exactly the "silent drop" the audit warned about.
 */

describe('exam-mode rulebook enforcer coverage', () => {
  it('every critical/major exam-mode rule has either a checkId-enforcer or an explicit enforcement marker', () => {
    const gaps = findUnimplementedExamModeRules();
    if (gaps.length > 0) {
      const summary = gaps
        .map((g) => `  ${g.kind} ${g.rule.id} (${g.rule.severity}): ${g.reason}`)
        .join('\n');
      throw new Error(`Exam-mode enforcement gaps:\n${summary}`);
    }
    expect(gaps).toEqual([]);
  });
});
