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
    expect(book.version).toBe('1.0.0');
    expect(book.sections.length).toBe(16);
    expect(book.rules.length).toBeGreaterThan(90);
  });

  it('loads the Speaking medicine rulebook', () => {
    const book = loadRulebook('speaking', 'medicine');
    expect(book.kind).toBe('speaking');
    expect(book.profession).toBe('medicine');
    expect(book.sections.length).toBe(7);
    expect(book.rules.length).toBe(55);
  });

  it('throws RulebookNotFoundError for unregistered profession', () => {
    expect(() => loadRulebook('writing', 'nursing')).toThrow(RulebookNotFoundError);
  });

  it('finds a specific rule by id', () => {
    const r = findRule('writing', 'medicine', 'R03.4');
    expect(r).toBeDefined();
    expect(r!.severity).toBe('critical');
    expect(r!.title).toMatch(/smoking/i);
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
    const book = loadRulebook('writing', 'medicine');
    const urgentRules = rulesApplicableTo(book, 'urgent_referral');
    // Urgent-only rules exist
    expect(urgentRules.find((r) => r.id === 'R13.3')).toBeDefined();
    // Applies-to-all rules are included
    expect(urgentRules.find((r) => r.id === 'R03.4')).toBeDefined();
    // Discharge-only rules are excluded
    expect(urgentRules.find((r) => r.id === 'R14.2')).toBeUndefined();
  });
});

describe('rulebook — critical rule coverage (all present & correctly flagged)', () => {
  const book = loadRulebook('writing', 'medicine');
  const criticalIds = [
    'R01.5', 'R02.2', 'R03.2', 'R03.4', 'R03.6',
    'R04.2', 'R05.2', 'R05.8',
    'R06.1', 'R06.3', 'R06.7', 'R06.10', 'R06.11',
    'R07.3', 'R07.6', 'R07.7',
    'R08.1', 'R08.3', 'R08.5', 'R08.7', 'R08.8', 'R08.14',
    'R09.2', 'R09.4', 'R09.5',
    'R10.2', 'R10.5', 'R10.6', 'R10.8', 'R10.10', 'R10.14',
    'R11.1', 'R11.8',
    'R12.1', 'R12.2', 'R12.5', 'R12.9',
    'R13.2', 'R13.3', 'R13.4', 'R13.6', 'R13.10',
    'R14.2', 'R14.3', 'R14.4', 'R14.6', 'R14.7', 'R14.9', 'R14.10', 'R14.12',
    'R15.2', 'R15.7',
    'R16.2', 'R16.3', 'R16.5', 'R16.7',
  ];

  it.each(criticalIds)('%s exists and is severity=critical', (id) => {
    const r = book.rules.find((x) => x.id === id);
    expect(r, `Rule ${id} missing`).toBeDefined();
    expect(r!.severity).toBe('critical');
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
