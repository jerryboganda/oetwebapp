import { describe, it, expect } from 'vitest';
import { loadRulebook } from '../loader';
import { WRITING_CHECK_IDS } from '../check-ids';
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
 *
 * Owner Rev8 (11 Sep 2026): every Writing rulebook — canonical (built from
 * registry rows OWN-W-001..038) and legacy (hand-maintained copies) — also
 * carries the 38 owner rules, locked by the last describe block below.
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

/**
 * Per-profession active rule count, from the vendored canonical registry
 * (v1.0 count + the 38 owner Rev8 rows OWN-W-001..038).
 */
const CANONICAL_PROFESSION_COUNTS: Partial<Record<ExamProfession, number>> = {
  medicine: 268,
  nursing: 275,
  dentistry: 275,
  pharmacy: 278,
  physiotherapy: 278,
  radiography: 275,
};

const CANONICAL_VERSION = '2.1.0-canonical-rev8';
const LEGACY_VERSION = '1.1.0-rev8';

/** Owner Rev8 (11 Sep 2026) rule ids present in every Writing rulebook. */
const OWNER_REV8_IDS = Array.from({ length: 38 }, (_, i) => `OWN-W-${String(i + 1).padStart(3, '0')}`);

/** Legacy letter-type tokens (WritingLetterTypeTaxonomy.ToLegacyLetterType). */
const LETTER_TYPE_TOKENS = new Set([
  'routine_referral',
  'urgent_referral',
  'discharge',
  'transfer_letter',
  'non_medical_referral',
  'other_letters',
]);

const CANONICAL_PROFESSIONS = Object.keys(CANONICAL_PROFESSION_COUNTS) as ExamProfession[];
const ALL_WRITING_PROFESSIONS: ExamProfession[] = [...LEGACY_PROFESSIONS, ...CANONICAL_PROFESSIONS];

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
      const expectedTotal = TOTAL_RULE_COUNT + OWNER_REV8_IDS.length;

      it(`has exactly ${expectedTotal} rules (${TOTAL_RULE_COUNT} legacy + ${OWNER_REV8_IDS.length} owner Rev8)`, () => {
        expect(book.rules.length).toBe(expectedTotal);
      });

      it(`is versioned ${LEGACY_VERSION}`, () => {
        expect(book.version).toBe(LEGACY_VERSION);
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
        const canonical = new Set([...CANONICAL_LEGACY_IDS, ...OWNER_REV8_IDS]);
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
        const sectionIds = new Set(book.sections.map((s) => s.id));
        const ownerIds = new Set(OWNER_REV8_IDS);
        for (const rule of book.rules) {
          if (ownerIds.has(rule.id)) {
            // Owner Rev8 rules carry no R## prefix; they must still sit in one
            // of this book's existing sections.
            if (!sectionIds.has(rule.section)) {
              mismatches.push(`${rule.id} (section="${rule.section}" is not a section of this rulebook)`);
            }
            continue;
          }
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

      it(`is versioned ${CANONICAL_VERSION} (regenerated from the Rev8 registry)`, () => {
        expect(book.version).toBe(CANONICAL_VERSION);
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

  describe('owner Rev8 rules (OWN-W-001..038) in every Writing rulebook', () => {
    for (const profession of ALL_WRITING_PROFESSIONS) {
      it(`writing/${profession} carries OWN-W-001..038 exactly once each`, () => {
        const ids = loadRulebook('writing', profession)
          .rules.map((r) => r.id)
          .filter((id) => id.startsWith('OWN-W-'));
        expect(ids.sort()).toEqual(OWNER_REV8_IDS);
      });

      it(`writing/${profession} owner rules are ai-grounded, critical/major, scoped by letter-type tokens and cite only real detectors`, () => {
        const offenders: string[] = [];
        for (const rule of loadRulebook('writing', profession).rules) {
          if (!rule.id.startsWith('OWN-W-')) continue;
          if (rule.severity !== 'critical' && rule.severity !== 'major') offenders.push(`${rule.id} severity=${rule.severity}`);
          if (rule.enforcement !== 'ai-grounded') offenders.push(`${rule.id} enforcement=${String(rule.enforcement)}`);
          // A top-level checkId would re-key that detector's findings from
          // BUILTIN.<checkId> to the rule id; detectors stay on the battery.
          if (rule.checkId !== undefined) offenders.push(`${rule.id} has a top-level checkId (${rule.checkId})`);
          const checkIds = rule.params?.checkIds;
          if (checkIds !== undefined && !Array.isArray(checkIds)) offenders.push(`${rule.id} params.checkIds is not an array`);
          for (const id of Array.isArray(checkIds) ? checkIds : []) {
            if (typeof id !== 'string' || !WRITING_CHECK_IDS.has(id)) offenders.push(`${rule.id} params.checkIds has unknown ${String(id)}`);
          }
          const appliesTo = rule.appliesTo ?? 'all';
          if (appliesTo !== 'all' && !(appliesTo.length > 0 && appliesTo.every((t) => LETTER_TYPE_TOKENS.has(t)))) {
            offenders.push(`${rule.id} appliesTo=${JSON.stringify(appliesTo)}`);
          }
        }
        expect(offenders).toEqual([]);
      });
    }

    it('owner rule severity/title/body/scope is identical in all 13 Writing rulebooks', () => {
      const fingerprint = (r: { severity: string; title: string; body: string; appliesTo?: unknown }) =>
        `${r.severity}|${r.title}|${r.body}|${JSON.stringify(r.appliesTo ?? 'all')}`;
      const reference = new Map(
        loadRulebook('writing', 'medicine')
          .rules.filter((r) => r.id.startsWith('OWN-W-'))
          .map((r) => [r.id, fingerprint(r)] as const),
      );
      const drift: string[] = [];
      for (const profession of ALL_WRITING_PROFESSIONS) {
        for (const rule of loadRulebook('writing', profession).rules) {
          if (rule.id.startsWith('OWN-W-') && reference.get(rule.id) !== fingerprint(rule)) drift.push(`${profession}:${rule.id}`);
        }
      }
      expect(
        drift,
        'Legacy copies of the OWN-W rules must match the registry rows (docs/canonical-rules/OET_AI_Rules_Master.jsonl).',
      ).toEqual([]);
    });
  });
});
