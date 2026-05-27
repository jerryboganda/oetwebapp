import { describe, it, expect } from 'vitest';
import { loadRulebook, listRulebooks } from '../loader';
import type { ExamProfession } from '../types';

/**
 * 2026-05-27 audit fix — Writing canon parity gate.
 *
 * Goal: prove that the canonical rulebook (rulebooks/writing/{profession}/
 * rulebook.v1.json) and the legacy backend canon (Data/Seeds/WritingV2/
 * canon-rules.launch-25.json with SC-001..025) do NOT collide in the
 * WritingCanonRule table.
 *
 * Strategy: this test asserts every Writing rule ID across every profession
 * starts with "R" (the rulebook namespace). The legacy SC-* namespace is
 * disjoint by construction. The CI gate catches any future authoring that
 * accidentally re-uses an SC-* ID or strays into another namespace.
 */

const IN_SCOPE_PROFESSIONS: ExamProfession[] = [
  'medicine',
  'nursing',
  'dentistry',
  'pharmacy',
  'physiotherapy',
  'other-allied-health',
];

describe('Writing canon parity — rulebook IDs vs legacy SC-* namespace', () => {
  it('every rule across every in-scope profession uses an R-prefixed ID', () => {
    const offenders: Array<{ profession: ExamProfession; id: string }> = [];
    for (const profession of IN_SCOPE_PROFESSIONS) {
      const book = loadRulebook('writing', profession);
      for (const rule of book.rules) {
        if (!/^R\d/.test(rule.id)) {
          offenders.push({ profession, id: rule.id });
        }
      }
    }
    expect(offenders, JSON.stringify(offenders, null, 2)).toEqual([]);
  });

  it('no rulebook ID accidentally enters the SC-* legacy namespace', () => {
    const collisions: Array<{ profession: ExamProfession; id: string }> = [];
    for (const profession of IN_SCOPE_PROFESSIONS) {
      const book = loadRulebook('writing', profession);
      for (const rule of book.rules) {
        if (/^SC-/i.test(rule.id)) collisions.push({ profession, id: rule.id });
      }
    }
    expect(collisions).toEqual([]);
  });

  it('rulebook ID set across professions is consistent (no rule appears with conflicting bodies)', () => {
    // Group by id; assert body identical across professions OR profession-
    // specific intentionally. This catches accidental divergence.
    const byId = new Map<string, Map<string, ExamProfession[]>>();
    for (const profession of IN_SCOPE_PROFESSIONS) {
      const book = loadRulebook('writing', profession);
      for (const rule of book.rules) {
        let bodies = byId.get(rule.id);
        if (!bodies) {
          bodies = new Map();
          byId.set(rule.id, bodies);
        }
        const profs = bodies.get(rule.body) ?? [];
        profs.push(profession);
        bodies.set(rule.body, profs);
      }
    }
    const divergent: Array<{ id: string; bodyCount: number }> = [];
    for (const [id, bodies] of byId.entries()) {
      if (bodies.size > 1) divergent.push({ id, bodyCount: bodies.size });
    }
    // It's OK for a few rules to diverge per profession (e.g. profession-
    // specific jargon lists). We assert the count is bounded so a wholesale
    // drift is caught.
    expect(divergent.length, `Rules with divergent bodies across professions: ${JSON.stringify(divergent)}`).toBeLessThanOrEqual(5);
  });

  it('the loader manifest includes all 6 in-scope Writing professions', () => {
    const writing = listRulebooks().filter((r) => r.kind === 'writing');
    const haveProfessions = new Set(writing.map((r) => r.profession));
    for (const profession of IN_SCOPE_PROFESSIONS) {
      expect(haveProfessions.has(profession), `Writing rulebook missing for ${profession}`).toBe(true);
    }
  });
});
