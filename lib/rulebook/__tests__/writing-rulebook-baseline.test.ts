import { describe, it, expect } from 'vitest';
import { loadRulebook } from '../loader';
import type { ExamProfession } from '../types';

/**
 * Baseline structural lock for the Writing rulebook.
 *
 * As of the canonical-registry migration (docs/canonical-rules/README.md),
 * scoring truth for the six professions with live Writing content
 * (medicine, nursing, dentistry, pharmacy, physiotherapy, radiography) comes
 * from `OET_AI_Rules_Master.jsonl` — NOT the legacy 172-rule `R##.#` set,
 * which the registry's own deployment contract marks
 * `never_load_as_scoring_truth`. The remaining seven professions have no
 * live Writing tasks yet and stay on the legacy 172-rule baseline until a
 * canonical pack is built for them.
 *
 * This file locks both halves: the legacy 172-rule baseline for the
 * not-yet-migrated professions (unchanged from before), and the canonical
 * baseline for the six migrated professions (rule counts from the registry,
 * and — the core regression this migration existed to fix — a hard
 * assertion that no legacy `R##.#` rule id is ever active for them again).
 */

const LEGACY_PROFESSIONS: ExamProfession[] = [
  'dietetics',
  'occupational-therapy',
  'optometry',
  'podiatry',
  'speech-pathology',
  'veterinary',
  'other-allied-health',
];

/** Per-profession active rule count, from the vendored canonical registry. */
const CANONICAL_PROFESSION_COUNTS: Partial<Record<ExamProfession, number>> = {
  medicine: 230,
  nursing: 237,
  dentistry: 237,
  pharmacy: 240,
  physiotherapy: 240,
  radiography: 237,
};

const CANONICAL_PROFESSIONS = Object.keys(CANONICAL_PROFESSION_COUNTS) as ExamProfession[];

/**
 * Per-section expected rule count for the LEGACY set only.
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

function expectedLegacyRuleIds(): string[] {
  const ids: string[] = [];
  for (const [section, count] of Object.entries(SECTION_RULE_COUNTS)) {
    for (let i = 1; i <= count; i++) {
      ids.push(`R${section}.${i}`);
    }
  }
  return ids;
}

const CANONICAL_LEGACY_IDS = expectedLegacyRuleIds();
const VALID_SEVERITIES = ['critical', 'major', 'minor', 'info'] as const;
const LEGACY_ID_PATTERN = /^R\d{2}\.\d+$/;

describe('writing rulebooks — structural baseline lock', () => {
  it('legacy canonical ID list sums to the documented total (172)', () => {
    expect(CANONICAL_LEGACY_IDS.length).toBe(TOTAL_RULE_COUNT);
    expect(TOTAL_RULE_COUNT).toBe(172);
  });

  for (const profession of LEGACY_PROFESSIONS) {
    describe(`writing/${profession} (legacy, not yet migrated)`, () => {
      const book = loadRulebook('writing', profession);

      it(`has exactly ${TOTAL_RULE_COUNT} rules`, () => {
        expect(book.rules.length).toBe(TOTAL_RULE_COUNT);
      });

      it('contains every canonical rule ID (no silent deletions)', () => {
        const present = new Set(book.rules.map((r) => r.id));
        const missing = CANONICAL_LEGACY_IDS.filter((id) => !present.has(id));
        expect(
          missing,
          `Profession "${profession}" is missing ${missing.length} canonical rule ID(s): ${missing.join(', ')}`,
        ).toEqual([]);
      });

      it('does not introduce unexpected rule IDs beyond the canonical set', () => {
        const canonical = new Set(CANONICAL_LEGACY_IDS);
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

  for (const profession of CANONICAL_PROFESSIONS) {
    describe(`writing/${profession} (canonical registry)`, () => {
      const book = loadRulebook('writing', profession);
      const expectedTotal = CANONICAL_PROFESSION_COUNTS[profession]!;

      it(`has exactly ${expectedTotal} active canonical rules`, () => {
        expect(book.rules.length).toBe(expectedTotal);
      });

      it('never loads a legacy R##.# rule id as active scoring truth', () => {
        const legacyIds = book.rules.map((r) => r.id).filter((id) => LEGACY_ID_PATTERN.test(id));
        expect(
          legacyIds,
          `Profession "${profession}" has ${legacyIds.length} legacy-format rule id(s) active: ${legacyIds.join(', ')}. ` +
            'The canonical registry deployment contract marks these never_load_as_scoring_truth.',
        ).toEqual([]);
      });

      it('rule ids are unique', () => {
        const ids = book.rules.map((r) => r.id);
        expect(new Set(ids).size).toBe(ids.length);
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

      it('only uses critical/major severity (canonical mapping has no minor/info tier)', () => {
        const offTier = book.rules.filter((r) => r.severity !== 'critical' && r.severity !== 'major');
        expect(
          offTier.map((r) => `${r.id} (${r.severity})`),
          'Every active canonical rule must be surfaced to the grader — see docs/canonical-rules/README.md severity mapping.',
        ).toEqual([]);
      });
    });
  }
});
