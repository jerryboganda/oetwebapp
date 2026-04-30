/**
 * Pure-grader tests. No I/O, no DOM — runs in Vitest jsdom env (default ok).
 */

import { describe, it, expect } from 'vitest';
import { gradeDrill } from '../grader';
import type {
  AbbreviationDrill,
  ExpansionDrill,
  OpeningDrill,
  OrderingDrill,
  RelevanceDrill,
  ToneDrill,
} from '../types';

const baseMeta = {
  profession: 'medicine' as const,
  letterType: 'referral' as const,
  difficulty: 'core' as const,
  estimatedMinutes: 5,
  rulebookRefs: [],
};

// ---------------------------------------------------------------------------
// Relevance
// ---------------------------------------------------------------------------

describe('gradeRelevance', () => {
  const drill: RelevanceDrill = {
    ...baseMeta,
    id: 'rel-test',
    type: 'relevance',
    title: 't',
    brief: 'b',
    scenario: { patient: 'P', writerRole: 'GP', recipientRole: 'Specialist', purpose: 'p' },
    notes: [
      { id: 'n1', category: 'PMH', text: 'Asthma', expected: 'relevant', rationale: 'atopic' },
      { id: 'n2', category: 'Social', text: 'Owns a cat', expected: 'irrelevant', rationale: 'irrelevant' },
      { id: 'n3', category: 'Social', text: 'Smoker', expected: 'relevant', rationale: 'always include' },
      { id: 'n4', category: 'Family', text: 'Cousin diabetes', expected: 'irrelevant', rationale: 'no link' },
      { id: 'n5', category: 'Hx', text: 'Hobby fishing', expected: 'optional', rationale: 'maybe' },
      { id: 'n6', category: 'Allergy', text: 'NKDA', expected: 'relevant', rationale: 'atopic' },
      { id: 'n7', category: 'Hx', text: 'Tonsillitis age 6', expected: 'irrelevant', rationale: 'old' },
      { id: 'n8', category: 'Tx', text: 'Salbutamol PRN', expected: 'relevant', rationale: 'med' },
    ],
  };

  it('passes when all required selections match', () => {
    const result = gradeDrill(drill, {
      type: 'relevance',
      selections: {
        n1: 'relevant',
        n2: 'irrelevant',
        n3: 'relevant',
        n4: 'irrelevant',
        n5: 'relevant', // optional → either is fine
        n6: 'relevant',
        n7: 'irrelevant',
        n8: 'relevant',
      },
    });
    expect(result.passed).toBe(true);
    expect(result.score).toBe(1);
    expect(result.errorTags).toEqual([]);
  });

  it('flags missing key content when relevant note marked irrelevant', () => {
    const result = gradeDrill(drill, {
      type: 'relevance',
      selections: {
        n1: 'irrelevant', // wrong
        n2: 'irrelevant',
        n3: 'relevant',
        n4: 'irrelevant',
        n5: 'relevant',
        n6: 'relevant',
        n7: 'irrelevant',
        n8: 'relevant',
      },
    });
    expect(result.errorTags).toContain('missing_key_content');
    expect(result.findings.find((f) => f.itemId === 'n1')?.correct).toBe(false);
  });

  it('flags irrelevant_content when junk note marked relevant', () => {
    const result = gradeDrill(drill, {
      type: 'relevance',
      selections: {
        n1: 'relevant',
        n2: 'relevant', // wrong
        n3: 'relevant',
        n4: 'irrelevant',
        n5: 'relevant',
        n6: 'relevant',
        n7: 'irrelevant',
        n8: 'relevant',
      },
    });
    expect(result.errorTags).toContain('irrelevant_content');
  });
});

// ---------------------------------------------------------------------------
// Opening
// ---------------------------------------------------------------------------

describe('gradeOpening', () => {
  const drill: OpeningDrill = {
    ...baseMeta,
    id: 'open-test',
    type: 'opening',
    title: 't',
    brief: 'b',
    scenario: { patient: 'P', writerRole: 'GP', recipientRole: 'Specialist', purpose: 'p' },
    choices: [
      { id: 'a', text: 'I am writing to refer Ms X for assessment...', quality: 'best', rationale: 'purpose first', flags: [] },
      { id: 'b', text: 'Ms X is a 50-year-old woman with diabetes.', quality: 'acceptable', rationale: 'patient first, purpose missing initially', flags: ['unclear_purpose'] },
      { id: 'c', text: 'Hi mate, can you have a look at this lady?', quality: 'weak', rationale: 'informal', flags: ['informal_tone', 'unclear_purpose'] },
    ],
  };

  it('best choice → 100% pass', () => {
    const r = gradeDrill(drill, { type: 'opening', choiceId: 'a' });
    expect(r.passed).toBe(true);
    expect(r.scorePercent).toBe(100);
  });

  it('acceptable choice → 70% pass', () => {
    const r = gradeDrill(drill, { type: 'opening', choiceId: 'b' });
    expect(r.passed).toBe(true);
    expect(r.scorePercent).toBe(70);
    expect(r.errorTags).toContain('unclear_purpose');
  });

  it('weak choice fails with informal_tone tag', () => {
    const r = gradeDrill(drill, { type: 'opening', choiceId: 'c' });
    expect(r.passed).toBe(false);
    expect(r.errorTags).toEqual(expect.arrayContaining(['informal_tone', 'unclear_purpose']));
  });
});

// ---------------------------------------------------------------------------
// Ordering
// ---------------------------------------------------------------------------

describe('gradeOrdering', () => {
  const drill: OrderingDrill = {
    ...baseMeta,
    id: 'ord-test',
    type: 'ordering',
    title: 't',
    brief: 'b',
    items: [
      { id: 'op', text: 'Opening', role: 'opening' },
      { id: 'cur', text: 'Current', role: 'current' },
      { id: 'his', text: 'History', role: 'history' },
      { id: 'req', text: 'Request', role: 'request' },
    ],
    expectedOrder: ['op', 'cur', 'his', 'req'],
  };

  it('correct order passes', () => {
    const r = gradeDrill(drill, { type: 'ordering', order: ['op', 'cur', 'his', 'req'] });
    expect(r.passed).toBe(true);
    expect(r.score).toBe(1);
  });

  it('one swap reduces score and flags poor_paragraphing', () => {
    const r = gradeDrill(drill, { type: 'ordering', order: ['cur', 'op', 'his', 'req'] });
    expect(r.score).toBeLessThan(1);
    expect(r.errorTags).toContain('poor_paragraphing');
  });
});

// ---------------------------------------------------------------------------
// Expansion
// ---------------------------------------------------------------------------

describe('gradeExpansion', () => {
  const drill: ExpansionDrill = {
    ...baseMeta,
    id: 'exp-test',
    type: 'expansion',
    title: 't',
    brief: 'b',
    targets: [
      {
        id: 't1',
        noteForm: 'pt c/o cough x 3/52',
        mustInclude: ['cough', 'three weeks'],
        mustNotInclude: ['c/o', '3/52'],
        minWords: 8,
        maxWords: 30,
        exemplar: 'The patient reports a cough that has persisted for three weeks.',
        rationale: 'expand abbreviations + duration in words',
      },
    ],
  };

  it('exemplar-style answer passes', () => {
    const r = gradeDrill(drill, {
      type: 'expansion',
      answers: { t1: 'The patient has reported a persistent cough for three weeks.' },
    });
    expect(r.passed).toBe(true);
    expect(r.score).toBe(1);
  });

  it('keeping note-form abbreviations fails', () => {
    const r = gradeDrill(drill, {
      type: 'expansion',
      answers: { t1: 'Patient c/o cough for three weeks now there now there now.' },
    });
    expect(r.passed).toBe(false);
    expect(r.errorTags).toEqual(expect.arrayContaining(['grammar_articles']));
  });

  it('empty answer flagged missing_key_content', () => {
    const r = gradeDrill(drill, { type: 'expansion', answers: { t1: '' } });
    expect(r.passed).toBe(false);
    expect(r.errorTags).toContain('missing_key_content');
  });
});

// ---------------------------------------------------------------------------
// Tone
// ---------------------------------------------------------------------------

describe('gradeTone', () => {
  const drill: ToneDrill = {
    ...baseMeta,
    id: 'tone-test',
    type: 'tone',
    title: 't',
    brief: 'b',
    items: [
      {
        id: 'i1',
        informal: 'The patient is doing okay-ish lately.',
        acceptableFormal: ['stable', 'remained stable', 'condition has stabilised'],
        forbidden: ['okay', 'okay-ish', 'a bit'],
        exemplar: 'The patient has remained clinically stable.',
        rationale: 'avoid colloquial hedges',
      },
    ],
  };

  it('formal answer passes', () => {
    const r = gradeDrill(drill, {
      type: 'tone',
      answers: { i1: 'The patient has remained stable since the last review.' },
    });
    expect(r.passed).toBe(true);
  });

  it('residual informality fails with informal_tone tag', () => {
    const r = gradeDrill(drill, {
      type: 'tone',
      answers: { i1: 'The patient is doing okay since last review.' },
    });
    expect(r.passed).toBe(false);
    expect(r.errorTags).toContain('informal_tone');
  });
});

// ---------------------------------------------------------------------------
// Abbreviation
// ---------------------------------------------------------------------------

describe('gradeAbbreviation', () => {
  const drill: AbbreviationDrill = {
    ...baseMeta,
    id: 'abb-test',
    type: 'abbreviation',
    title: 't',
    brief: 'b',
    items: [
      {
        id: 'a1',
        abbreviation: 'PRN',
        context: 'writing to a community pharmacist',
        expected: 'keep',
        expansion: 'as needed',
        rationale: 'pharmacist will recognise PRN',
      },
      {
        id: 'a2',
        abbreviation: 'COPD',
        context: 'writing to a patient',
        expected: 'expand',
        expansion: 'chronic obstructive pulmonary disease',
        rationale: 'patient may not know COPD',
      },
      {
        id: 'a3',
        abbreviation: 'NKDA',
        context: 'writing to a school nurse',
        expected: 'expand',
        expansion: 'no known drug allergies',
        rationale: 'lay reader',
      },
    ],
  };

  it('all correct → pass', () => {
    const r = gradeDrill(drill, {
      type: 'abbreviation',
      answers: { a1: 'keep', a2: 'expand', a3: 'expand' },
    });
    expect(r.passed).toBe(true);
  });

  it('mistakes flag abbreviation_issue', () => {
    const r = gradeDrill(drill, {
      type: 'abbreviation',
      answers: { a1: 'expand', a2: 'keep', a3: 'expand' },
    });
    expect(r.passed).toBe(false);
    expect(r.errorTags).toContain('abbreviation_issue');
  });
});

// ---------------------------------------------------------------------------
// Dispatcher mismatch
// ---------------------------------------------------------------------------

describe('gradeDrill dispatcher', () => {
  it('throws when submission type does not match drill type', () => {
    const drill: AbbreviationDrill = {
      ...baseMeta,
      id: 'mismatch',
      type: 'abbreviation',
      title: 't',
      brief: 'b',
      items: [
        {
          id: 'a1',
          abbreviation: 'PRN',
          context: 'ctx',
          expected: 'keep',
          expansion: 'as needed',
          rationale: 'r',
        },
        {
          id: 'a2',
          abbreviation: 'PRN',
          context: 'ctx',
          expected: 'keep',
          expansion: 'as needed',
          rationale: 'r',
        },
        {
          id: 'a3',
          abbreviation: 'PRN',
          context: 'ctx',
          expected: 'keep',
          expansion: 'as needed',
          rationale: 'r',
        },
      ],
    };
    expect(() =>
      gradeDrill(drill, { type: 'opening', choiceId: 'a' }),
    ).toThrow(/type mismatch/i);
  });
});
