/**
 * ============================================================================
 * Writing Rule Engine — Cross-Engine Parity Corpus
 * ============================================================================
 *
 * Asserts that lib/rulebook/writing-rules.ts produces a specific, locked-in
 * set of non-info ruleIds for each fixture letter in
 * `__fixtures__/writing-engine-parity.json`.
 *
 * The same fixture file is consumed by the .NET sibling test
 * `backend/tests/OetLearner.Api.Tests/Rulebook/WritingEngineParityTests.cs`,
 * which runs `WritingRuleEngine.Lint(...)` and asserts the SAME expected set
 * — making the docblock claim ("Behaviour MUST match the TypeScript engine")
 * mechanically enforceable.
 *
 * Rationale:
 *   - `info`-severity findings are excluded from the comparison: they are
 *     advisory-only and prone to subtle wording drift between engines.
 *   - To regenerate after a deliberate engine change: run with the env var
 *     `WRITE_PARITY_FIXTURE=1` and the actual rule IDs will be written back
 *     to the JSON. NEVER do this without reviewing the diff.
 * ============================================================================
 */

import fs from 'node:fs';
import path from 'node:path';
import { describe, it, expect } from 'vitest';
import { lintWritingLetter } from '../writing-rules';
import type { ExamProfession, LetterType, WritingLintInput } from '../types';

interface ParityFixture {
  id: string;
  letterType: LetterType;
  profession: ExamProfession;
  patientIsMinor: boolean;
  recipientSpecialty?: string;
  caseNotesMarkers?: WritingLintInput['caseNotesMarkers'];
  letter: string;
  expectedRuleIds: string[];
  skipTs?: boolean;
}

interface ParityFixtureFile {
  fixtures: ParityFixture[];
}

const FIXTURE_PATH = path.resolve(
  __dirname,
  '__fixtures__',
  'writing-engine-parity.json',
);

function loadFixtures(): { raw: unknown; data: ParityFixtureFile } {
  const text = fs.readFileSync(FIXTURE_PATH, 'utf-8');
  const raw: unknown = JSON.parse(text);
  return { raw, data: raw as ParityFixtureFile };
}

function actualRuleIdsFor(fx: ParityFixture): string[] {
  const input: WritingLintInput = {
    letterText: fx.letter,
    letterType: fx.letterType,
    profession: fx.profession,
    patientIsMinor: fx.patientIsMinor,
    recipientSpecialty: fx.recipientSpecialty,
    caseNotesMarkers: fx.caseNotesMarkers,
  };
  const findings = lintWritingLetter(input);
  const ids = findings
    .filter((f) => f.severity !== 'info')
    .map((f) => f.ruleId);
  return Array.from(new Set(ids)).sort();
}

describe('writing rule engine — cross-engine parity corpus', () => {
  const { raw, data } = loadFixtures();

  it('fixture file has at least one fixture and well-formed shape', () => {
    expect(Array.isArray(data.fixtures)).toBe(true);
    expect(data.fixtures.length).toBeGreaterThan(0);
    for (const fx of data.fixtures) {
      expect(typeof fx.id).toBe('string');
      expect(typeof fx.letter).toBe('string');
      expect(fx.letter.length).toBeGreaterThan(50);
      expect(Array.isArray(fx.expectedRuleIds)).toBe(true);
    }
  });

  // Optional regeneration mode: write actual rule IDs back into the JSON.
  // Triggered ONLY when the operator explicitly sets WRITE_PARITY_FIXTURE=1.
  if (process.env.WRITE_PARITY_FIXTURE === '1') {
    it('[regen] writes actual TS rule IDs back to the fixture file', () => {
      const updated = data.fixtures.map((fx) => ({
        ...fx,
        expectedRuleIds: fx.skipTs ? fx.expectedRuleIds : actualRuleIdsFor(fx),
      }));
      const next = { ...(raw as Record<string, unknown>), fixtures: updated };
      fs.writeFileSync(FIXTURE_PATH, JSON.stringify(next, null, 2) + '\n', 'utf-8');
      expect(true).toBe(true);
    });
    return;
  }

  for (const fx of data.fixtures) {
    if (fx.skipTs) {
      it.skip(`[${fx.id}] (skipTs=true)`, () => {});
      continue;
    }
    it(`[${fx.id}] TS engine produces the locked rule-id set`, () => {
      const actual = actualRuleIdsFor(fx);
      const expected = [...fx.expectedRuleIds].sort();
      expect(actual).toEqual(expected);
    });
  }
});
