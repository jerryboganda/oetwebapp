import { describe, it, expect } from 'vitest';
import { loadRulebook, listRulebooks } from '../loader';
import type { ExamProfession } from '../types';

const ALL_WRITING_PROFESSIONS: ExamProfession[] = [
  'medicine',
  'nursing',
  'dentistry',
  'pharmacy',
  'physiotherapy',
  'veterinary',
  'optometry',
  'radiography',
  'occupational-therapy',
  'speech-pathology',
  'podiatry',
  'dietetics',
  'other-allied-health',
];

/**
 * Professions migrated to the canonical `OET_AI_Rules_Master.jsonl` registry
 * (docs/canonical-rules/README.md). Everything else stays on the legacy
 * 172-rule baseline until a canonical pack exists for it. Every Writing
 * rulebook also carries the 38 owner Rev8 rules OWN-W-001..038 (11 Sep 2026).
 */
const CANONICAL_PROFESSION_COUNTS: Partial<Record<ExamProfession, number>> = {
  // Medicine: 268 + OA-01..OA-15 + OA2-01..OA2-20 + OA3-01..OA3-05 (the addendum rows are carried
  // in the registry under profession "Medicine"). Every other canonical pack
  // gains the same 40 globally scoped rows, because Addendum Two §14 requires
  // them "active globally — not sample-only edits".
  medicine: 308,
  nursing: 315,
  dentistry: 315,
  pharmacy: 318,
  physiotherapy: 318,
  radiography: 315,
};
// 172 base + 38 owner Rev8 rules, PLUS the derived operational modules the
// ULTIMATE FINAL round appended to the legacy packs (PRD-*) — which is why they
// no longer share one number. Pinned per profession so drift stays visible.
const LEGACY_RULE_COUNT = 172 + 38;
const LEGACY_PROFESSION_COUNTS: Partial<Record<ExamProfession, number>> = {
  dietetics: 219,
  'occupational-therapy': 218,
  optometry: 218,
  podiatry: 218,
  'speech-pathology': 218,
  veterinary: 211,
};

describe('writing rulebooks — Phase D coverage', () => {
  it('registers a writing rulebook for every supported profession', () => {
    const registered = listRulebooks()
      .filter((b) => b.kind === 'writing')
      .map((b) => b.profession);
    for (const p of ALL_WRITING_PROFESSIONS) {
      expect(registered).toContain(p);
    }
  });

  for (const profession of ALL_WRITING_PROFESSIONS) {
    describe(`writing/${profession}`, () => {
      const book = loadRulebook('writing', profession);

      it('has the right kind and profession', () => {
        expect(book.kind).toBe('writing');
        expect(book.profession).toBe(profession);
      });

      it('declares a non-empty version, sections, and rules', () => {
        expect(book.version).toMatch(/^\d+\.\d+\.\d+/);
        expect(book.sections.length).toBeGreaterThan(0);
        expect(book.rules.length).toBeGreaterThan(0);
      });

      const canonicalCount = CANONICAL_PROFESSION_COUNTS[profession];
      if (canonicalCount) {
        it(`has the canonical registry rule count for this profession (${canonicalCount})`, () => {
          expect(book.rules.length).toBe(canonicalCount);
        });
      } else {
        it('has the legacy 172-rule baseline + 38 owner Rev8 rules (locks against silent deletions)', () => {
          expect(book.rules.length).toBe(LEGACY_PROFESSION_COUNTS[profession] ?? LEGACY_RULE_COUNT);
        });
      }

      it('every rule has id, severity, title, and body', () => {
        for (const rule of book.rules) {
          expect(rule.id).toBeTruthy();
          expect(['critical', 'major', 'minor', 'info']).toContain(rule.severity);
          expect(rule.title).toBeTruthy();
          expect(rule.body).toBeTruthy();
        }
      });

      it('rule ids are unique within the rulebook', () => {
        const ids = book.rules.map((r) => r.id);
        const unique = new Set(ids);
        expect(unique.size).toBe(ids.length);
      });

      if (profession !== 'medicine') {
        it('carries professionSpecific metadata (recipients, letterTypes, notes)', () => {
          const extra = (book as unknown as { professionSpecific?: Record<string, unknown> })
            .professionSpecific;
          expect(extra).toBeDefined();
          expect(Array.isArray(extra?.typicalRecipients)).toBe(true);
          expect((extra?.typicalRecipients as unknown[])?.length).toBeGreaterThan(0);
          expect(Array.isArray(extra?.primaryLetterTypes)).toBe(true);
          expect(typeof extra?.notes).toBe('string');
        });
      }
    });
  }
});
