import { describe, it, expect } from 'vitest';
import { loadRulebook } from '../loader';
import type { ExamProfession } from '../types';

/**
 * Baseline structural lock for the Writing rulebook across all 13
 * professions. Prevents silent rule deletions or section drift.
 *
 * The canonical ID set is generated programmatically from
 * SECTION_RULE_COUNTS so the test failure message can name exactly
 * which IDs are missing per profession.
 */

const ALL_WRITING_PROFESSIONS: ExamProfession[] = [
  'medicine',
  'nursing',
  'dentistry',
  'physiotherapy',
  'pharmacy',
  'dietetics',
  'occupational-therapy',
  'optometry',
  'podiatry',
  'radiography',
  'speech-pathology',
  'veterinary',
  'other-allied-health',
];

/**
 * Per-section expected rule count.
 * Section ID → number of rules (R{section}.1 .. R{section}.N).
 * Sum = 172.
 */
const SECTION_RULE_COUNTS: Record<string, number> = {
  '01': 8,
  '02': 8,
  '03': 9,
  '04': 4,
  '05': 9,
  '06': 13,
  '07': 9,
  '08': 15,
  '09': 9,
  '10': 14,
  '11': 11,
  '12': 22,
  '13': 11,
  '14': 14,
  '15': 8,
  '16': 8,
};

const TOTAL_RULE_COUNT = Object.values(SECTION_RULE_COUNTS).reduce(
  (a, b) => a + b,
  0,
);

/**
 * Per-profession expected total rule count. Defaults to
 * TOTAL_RULE_COUNT (172) for every profession; override here if a
 * profession legitimately differs.
 */
const PROFESSION_BASELINES: Partial<Record<ExamProfession, number>> = {};

function expectedRuleIds(): string[] {
  const ids: string[] = [];
  for (const [section, count] of Object.entries(SECTION_RULE_COUNTS)) {
    for (let i = 1; i <= count; i++) {
      ids.push(`R${section}.${i}`);
    }
  }
  return ids;
}

const CANONICAL_IDS = expectedRuleIds();
const VALID_SEVERITIES = ['critical', 'major', 'minor', 'info'] as const;

describe('writing rulebooks — structural baseline lock', () => {
  it('canonical ID list sums to the documented total (172)', () => {
    expect(CANONICAL_IDS.length).toBe(TOTAL_RULE_COUNT);
    expect(TOTAL_RULE_COUNT).toBe(172);
  });

  for (const profession of ALL_WRITING_PROFESSIONS) {
    describe(`writing/${profession}`, () => {
      const book = loadRulebook('writing', profession);
      const expectedTotal = PROFESSION_BASELINES[profession] ?? TOTAL_RULE_COUNT;

      it(`has exactly ${expectedTotal} rules`, () => {
        expect(book.rules.length).toBe(expectedTotal);
      });

      it('contains every canonical rule ID (no silent deletions)', () => {
        const present = new Set(book.rules.map((r) => r.id));
        const missing = CANONICAL_IDS.filter((id) => !present.has(id));
        expect(
          missing,
          `Profession "${profession}" is missing ${missing.length} canonical rule ID(s): ${missing.join(', ')}`,
        ).toEqual([]);
      });

      it('does not introduce unexpected rule IDs beyond the canonical set', () => {
        const canonical = new Set(CANONICAL_IDS);
        const unexpected = book.rules
          .map((r) => r.id)
          .filter((id) => !canonical.has(id));
        expect(
          unexpected,
          `Profession "${profession}" has ${unexpected.length} unexpected rule ID(s): ${unexpected.join(', ')}`,
        ).toEqual([]);
      });

      it('every rule has a non-empty body (>10 chars) and valid severity', () => {
        const offenders: string[] = [];
        for (const rule of book.rules) {
          if (typeof rule.body !== 'string' || rule.body.trim().length <= 10) {
            offenders.push(`${rule.id} (body too short)`);
          }
          if (!VALID_SEVERITIES.includes(rule.severity)) {
            offenders.push(`${rule.id} (invalid severity: ${String(rule.severity)})`);
          }
        }
        expect(
          offenders,
          `Profession "${profession}" has ${offenders.length} rule(s) failing body/severity checks: ${offenders.join('; ')}`,
        ).toEqual([]);
      });

      it("every rule's section field matches its id prefix", () => {
        const mismatches: string[] = [];
        for (const rule of book.rules) {
          const match = /^R(\d{2})\./.exec(rule.id);
          if (!match) {
            mismatches.push(`${rule.id} (id does not match R##.N pattern)`);
            continue;
          }
          const expectedSection = match[1];
          if (rule.section !== expectedSection) {
            mismatches.push(
              `${rule.id} (section="${rule.section}", expected "${expectedSection}")`,
            );
          }
        }
        expect(
          mismatches,
          `Profession "${profession}" has ${mismatches.length} rule(s) with section/id mismatch: ${mismatches.join('; ')}`,
        ).toEqual([]);
      });
    });
  }
});
