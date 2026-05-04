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
        expect(book.version).toMatch(/^\d+\.\d+\.\d+$/);
        expect(book.sections.length).toBeGreaterThan(0);
        expect(book.rules.length).toBeGreaterThan(0);
      });

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
