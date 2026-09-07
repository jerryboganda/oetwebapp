import {
  criticalRules,
  findRule,
  getAssessmentCriteria,
  listRulebooks,
  loadRulebook,
  RulebookNotFoundError,
  rulesApplicableTo,
} from './loader';

describe('rulebook loader — medicine rulebooks load cleanly', () => {
  it('lists both registered rulebooks', () => {
    const list = listRulebooks();
    expect(list).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ kind: 'writing', profession: 'medicine' }),
        expect.objectContaining({ kind: 'speaking', profession: 'medicine' }),
      ]),
    );
  });

  it('loads the Writing medicine rulebook', () => {
    const book = loadRulebook('writing', 'medicine');
    expect(book.kind).toBe('writing');
    expect(book.profession).toBe('medicine');
    expect(book.version).toBe('2.0.0-canonical');
    expect(book.sections.length).toBe(43);
    expect(book.rules.length).toBeGreaterThan(90);
  });

  it('loads the Speaking medicine rulebook', () => {
    const book = loadRulebook('speaking', 'medicine');
    expect(book.kind).toBe('speaking');
    expect(book.profession).toBe('medicine');
    expect(book.sections.length).toBeGreaterThanOrEqual(7);
    expect(book.rules.length).toBeGreaterThanOrEqual(55);
  });

  it('throws RulebookNotFoundError for unregistered (kind, profession)', () => {
    // All 12 writing professions are registered (Phase D). Use a (kind, profession)
    // pair that intentionally has no rulebook on disk yet to exercise the error path.
    expect(() => loadRulebook('speaking', 'veterinary')).toThrow(RulebookNotFoundError);
  });

  it('finds a specific rule by id', () => {
    const r = findRule('writing', 'medicine', 'OW-001');
    expect(r).toBeDefined();
    expect(r!.severity).toBe('critical');
    expect(r!.title).toMatch(/Six Writing criteria/i);
  });

  it('returns undefined when rule id does not exist', () => {
    expect(findRule('writing', 'medicine', 'R99.9')).toBeUndefined();
  });

  it('loads Writing + Speaking assessment criteria', () => {
    expect(getAssessmentCriteria('writing')).toBeTruthy();
    expect(getAssessmentCriteria('speaking')).toBeTruthy();
  });

  it('criticalRules filters severity === critical only', () => {
    const book = loadRulebook('writing', 'medicine');
    const crit = criticalRules(book);
    expect(crit.length).toBeGreaterThan(15);
    for (const r of crit) expect(r.severity).toBe('critical');
  });

  it('rulesApplicableTo filters on appliesTo context', () => {
    // Canonical writing book: every rule appliesTo "all", so any context
    // returns the full set.
    const book = loadRulebook('writing', 'medicine');
    expect(rulesApplicableTo(book, 'urgent_referral')).toHaveLength(book.rules.length);

    // Exercise the array branch with a synthetic book.
    const mixed = {
      kind: 'writing',
      profession: 'medicine',
      version: 'test',
      sections: [],
      rules: [
        { id: 'T1', section: 's', severity: 'critical', title: 't', body: 'b', appliesTo: 'all' },
        { id: 'T2', section: 's', severity: 'major', title: 't', body: 'b', appliesTo: ['urgent_referral'] },
        { id: 'T3', section: 's', severity: 'major', title: 't', body: 'b', appliesTo: ['discharge'] },
      ],
    } as unknown as Parameters<typeof rulesApplicableTo>[0];
    const urgent = rulesApplicableTo(mixed, 'urgent_referral').map((r) => r.id);
    expect(urgent).toContain('T1');
    expect(urgent).toContain('T2');
    expect(urgent).not.toContain('T3');
  });
});

describe('rulebook — critical rule coverage (canonical writing book)', () => {
  const book = loadRulebook('writing', 'medicine');

  it('has the canonical critical-rule count', () => {
    expect(criticalRules(book)).toHaveLength(59);
  });

  it.each(['OW-001', 'DH-W-001'])('%s exists and is severity=critical', (id) => {
    const r = book.rules.find((x) => x.id === id);
    expect(r, `Rule ${id} missing`).toBeDefined();
    expect(r!.severity).toBe('critical');
  });

  it('every critical rule has section, title, and body', () => {
    for (const r of criticalRules(book)) {
      expect(r.section).toBeTruthy();
      expect(r.title).toBeTruthy();
      expect(r.body).toBeTruthy();
    }
  });
});

describe('rulebook — speaking critical rule coverage', () => {
  const book = loadRulebook('speaking', 'medicine');
  const criticalIds = [
    'RULE_06', 'RULE_07',
    'RULE_15', 'RULE_18', 'RULE_20', 'RULE_21', 'RULE_22',
    'RULE_23', 'RULE_27', 'RULE_32',
    'RULE_40', 'RULE_41', 'RULE_42', 'RULE_43', 'RULE_44', 'RULE_45', 'RULE_46', 'RULE_47',
  ];

  it.each(criticalIds)('%s exists and is severity=critical', (id) => {
    const r = book.rules.find((x) => x.id === id);
    expect(r, `Rule ${id} missing`).toBeDefined();
    expect(r!.severity).toBe('critical');
  });

  it('includes the 13-stage consultation state machine', () => {
    const sm = (book.stateMachines ?? {}) as { consultationStages?: unknown[] };
    expect(sm.consultationStages).toBeDefined();
    expect(sm.consultationStages).toHaveLength(13);
  });

  it('includes the 7-step Breaking Bad News protocol', () => {
    const sm = (book.stateMachines ?? {}) as { breakingBadNewsProtocol?: unknown[] };
    expect(sm.breakingBadNewsProtocol).toBeDefined();
    expect(sm.breakingBadNewsProtocol).toHaveLength(7);
  });

  it('includes the 3-step smoking negotiation ladder', () => {
    const sm = (book.stateMachines ?? {}) as { smokingLadder?: unknown[] };
    expect(sm.smokingLadder).toBeDefined();
    expect(sm.smokingLadder).toHaveLength(3);
  });

  it('exposes the full jargon → layman glossary', () => {
    const tables = (book.tables ?? {}) as { laymanGlossary?: unknown[] };
    expect(tables.laymanGlossary).toBeDefined();
    expect((tables.laymanGlossary as unknown[]).length).toBeGreaterThanOrEqual(17);
  });
});
