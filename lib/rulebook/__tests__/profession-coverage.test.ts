import { describe, it, expect } from 'vitest';
import { professions } from '../../auth/enrollment';
import { discoverRulebooks, findMissingCoverage } from '../registry';
import type { ExamProfession, RuleKind } from '../types';

/**
 * Phase 0 CI gate — for every (kind, profession) pair the UI exposes, the
 * matching rulebook must exist on disk AND be loaded by the runtime loader
 * (proven separately by `loader.test.ts`).
 *
 * The matrix below encodes the audit's "in-scope" decision:
 *   - 6 UI professions (from `lib/auth/enrollment.ts`)
 *   - 9 first-class rulebook kinds (the 4 OET skills + 5 supporting)
 *
 * Today this test FAILS for the gaps the plan addresses (Speaking dentistry +
 * pharmacy, Vocabulary physiotherapy + other-allied-health, Remediation for
 * everyone except medicine, ...). As each Phase ships, gaps get filled and
 * the matrix turns green. The test must be kept GREEN for any PR to merge.
 */

// UI professions, derived from the single source of truth.
const UI_PROFESSIONS: ExamProfession[] = professions.map((p) => p.id as ExamProfession);

// Skill / supporting kinds that every UI profession must have a rulebook for.
// `listening` and `reading` use the same authoring rulebook for all
// professions today (content is profession-agnostic), so coverage on those
// kinds is checked at the per-rulebook level rather than per-profession.
// The exam-mode (-exam-mode) rulebooks are also profession-agnostic.
const REQUIRED_KINDS_PER_PROFESSION: RuleKind[] = [
  'writing',
  'speaking',
  'grammar',
  'vocabulary',
  'pronunciation',
  'conversation',
  'remediation',
];

describe('profession coverage matrix', () => {
  const discovered = discoverRulebooks();

  // ---- Listening + Reading: at least one profession-keyed rulebook present ----
  it('listening has at least one profession rulebook on disk', () => {
    expect(discovered.some((d) => d.kind === 'listening')).toBe(true);
  });

  it('reading has at least one profession rulebook on disk', () => {
    expect(discovered.some((d) => d.kind === 'reading')).toBe(true);
  });

  // ---- Per-UI-profession × per-required-kind matrix ----
  describe.each(UI_PROFESSIONS)('profession=%s', (profession) => {
    it.each(REQUIRED_KINDS_PER_PROFESSION)(`has a %s rulebook`, (kind) => {
      const missing = findMissingCoverage([{ kind, profession }], discovered);
      expect(
        missing,
        `Missing rulebook for kind="${kind}" profession="${profession}". ` +
          `Author rulebooks/${kind}/${profession}/rulebook.v1.json and register it in lib/rulebook/loader.ts.`,
      ).toEqual([]);
    });
  });
});
