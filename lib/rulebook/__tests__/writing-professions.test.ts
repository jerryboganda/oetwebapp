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
  medicine: 268,
  nursing: 275,
  dentistry: 275,
  pharmacy: 278,
  physiotherapy: 278,
  radiography: 275,
};
const LEGACY_RULE_COUNT = 172 + 38;

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
          expect(book.rules.length).toBe(LEGACY_RULE_COUNT);
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
