import { describe, expect, it } from 'vitest';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { findUnenforcedRules } from '../registry';
import baseline from './__fixtures__/unenforced-rules-baseline.json';

/**
 * Rulebook enforcement coverage — RATCHET (Phase 1, informational).
 *
 * `findUnenforcedRules()` lists every critical/major rule with no concrete
 * enforcer (a BACKED checkId / forbiddenPatterns / ai-grounded / human-review).
 * The committed baseline is the live worklist of those gaps for the four OET
 * exam rulebooks.
 *
 * The ratchet forbids only NEW gaps (current ⊆ baseline), so as Phase 2
 * enforces rules the set shrinks and this stays green with no edit. Re-generate
 * the (smaller) baseline any time with:
 *
 *   UPDATE_RULEBOOK_BASELINE=1 pnpm exec vitest run \
 *     lib/rulebook/__tests__/rulebook-enforcement-coverage.test.ts
 *
 * Phase 3 empties the baseline and promotes this to the required `=== []` gate.
 *
 * Scope: the four OET exam sub-test rulebooks + their candidate-facing
 * exam-mode books. grammar/vocabulary/pronunciation/conversation/remediation
 * are separate prep-content features, out of scope for the 4 OET rulebooks.
 */
const OET_EXAM_KINDS = new Set<string>([
  'writing',
  'speaking',
  'listening',
  'reading',
  'listening-exam-mode',
  'reading-exam-mode',
]);

function key(kind: string, profession: string, ruleId: string): string {
  return `${kind}:${profession}:${ruleId}`;
}

const current = findUnenforcedRules()
  .filter((u) => OET_EXAM_KINDS.has(u.kind))
  .map((u) => key(u.kind, String(u.profession), u.rule.id))
  .sort();

const BASELINE_PATH = join(__dirname, '__fixtures__', 'unenforced-rules-baseline.json');
if (process.env.UPDATE_RULEBOOK_BASELINE === '1') {
  writeFileSync(BASELINE_PATH, `${JSON.stringify(current, null, 2)}\n`);
}

describe('rulebook enforcement coverage ratchet (OET exam kinds)', () => {
  const baselineSet = new Set(baseline as string[]);

  it('introduces no NEW unenforced critical/major rule beyond the baseline', () => {
    const novel = current.filter((k) => !baselineSet.has(k));
    expect(novel).toEqual([]);
  });

  it('reports resolved baseline entries (informational — shrink the baseline)', () => {
    const currentSet = new Set(current);
    const resolved = [...baselineSet].filter((k) => !currentSet.has(k));
    if (resolved.length > 0) {
      console.info(
        `[rulebook-conformance] ${resolved.length} baseline rule(s) now enforced — ` +
          'rerun with UPDATE_RULEBOOK_BASELINE=1 to shrink the baseline.',
      );
    }
    expect(current.length).toBeLessThanOrEqual(baselineSet.size);
  });
});
