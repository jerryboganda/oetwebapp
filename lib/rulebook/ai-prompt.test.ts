import { buildAiGroundedPrompt } from './ai-prompt';

describe('AI rulebook-grounded prompt — writing', () => {
  it('includes the writing rulebook version + profession in metadata', () => {
    const p = buildAiGroundedPrompt({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
      candidateCountry: 'UK',
      letterType: 'routine_referral',
    });
    expect(p.metadata.rulebookVersion).toBe('1.0.0');
    expect(p.metadata.rulebookKind).toBe('writing');
    expect(p.metadata.profession).toBe('medicine');
    expect(p.metadata.appliedRulesCount).toBeGreaterThan(60);
  });

  it('applies Grade B (350) for UK candidates', () => {
    const p = buildAiGroundedPrompt({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
      candidateCountry: 'UK',
    });
    expect(p.metadata.scoringPassMark).toBe(350);
    expect(p.metadata.scoringGrade).toBe('B');
    expect(p.system).toMatch(/350\/500 \(Grade B\)/);
  });

  it('applies Grade C+ (300) for USA candidates', () => {
    const p = buildAiGroundedPrompt({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
      candidateCountry: 'USA',
    });
    expect(p.metadata.scoringPassMark).toBe(300);
    expect(p.metadata.scoringGrade).toBe('C+');
  });

  it('applies Grade C+ (300) for Qatar candidates', () => {
    const p = buildAiGroundedPrompt({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
      candidateCountry: 'Qatar',
    });
    expect(p.metadata.scoringPassMark).toBe(300);
    expect(p.metadata.scoringGrade).toBe('C+');
  });

  it('defaults to Grade B (350) when no country is provided', () => {
    const p = buildAiGroundedPrompt({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
    });
    expect(p.metadata.scoringPassMark).toBe(350);
  });

  it('system prompt cites the canonical OET scoring rules', () => {
    const p = buildAiGroundedPrompt({ kind: 'writing', profession: 'medicine', task: 'score' });
    expect(p.system).toMatch(/30\/42 ≡ 350\/500/);
    expect(p.system).toMatch(/Grade C\+ at 300\/500 for US\/QA/);
    expect(p.system).toMatch(/SPEAKING: Grade B at 350\/500, universal/);
  });

  it('system prompt lists critical and major rules with IDs', () => {
    const p = buildAiGroundedPrompt({ kind: 'writing', profession: 'medicine', task: 'score', letterType: 'urgent_referral' });
    expect(p.system).toMatch(/R13\.3/);
    expect(p.system).toMatch(/R07\.6/);
    expect(p.system).toMatch(/CRITICAL rules/);
    expect(p.system).toMatch(/MAJOR rules/);
  });

  it('system prompt filters by letter type when provided', () => {
    const dischargePrompt = buildAiGroundedPrompt({
      kind: 'writing',
      profession: 'medicine',
      task: 'score',
      letterType: 'discharge',
    });
    // Discharge-specific rules appear
    expect(dischargePrompt.system).toMatch(/R14\.2/);
    // Urgent-only rules are dropped (R13.3 is urgent_referral only)
    expect(dischargePrompt.system).not.toMatch(/R13\.3 \(critical\)/);
  });

  it('system prompt contains the strict guardrails', () => {
    const p = buildAiGroundedPrompt({ kind: 'writing', profession: 'medicine', task: 'score' });
    expect(p.system).toMatch(/Cite rule IDs explicitly/);
    expect(p.system).toMatch(/Do NOT invent/);
    expect(p.system).toMatch(/Do NOT produce a numeric grade/);
    expect(p.system).toMatch(/advisory/);
  });

  it('score task reply format contains a JSON shape with criteriaScores', () => {
    const p = buildAiGroundedPrompt({ kind: 'writing', profession: 'medicine', task: 'score' });
    expect(p.system).toMatch(/criteriaScores/);
    expect(p.system).toMatch(/estimatedScaledScore/);
    expect(p.system).toMatch(/estimatedGrade/);
  });
});

describe('AI rulebook-grounded prompt — speaking', () => {
  it('always uses universal 350/500 threshold regardless of country', () => {
    const p = buildAiGroundedPrompt({
      kind: 'speaking',
      profession: 'medicine',
      task: 'coach',
      candidateCountry: 'USA',
    });
    expect(p.metadata.scoringPassMark).toBe(350);
    expect(p.metadata.scoringGrade).toBe('B');
    expect(p.system).toMatch(/universal 350\/500 pass mark regardless of country/);
  });

  it('includes Breaking Bad News rules when cardType=breaking_bad_news', () => {
    const p = buildAiGroundedPrompt({
      kind: 'speaking',
      profession: 'medicine',
      task: 'coach',
      cardType: 'breaking_bad_news',
    });
    expect(p.system).toMatch(/RULE_41/);
    expect(p.system).toMatch(/RULE_44/);
    expect(p.system).toMatch(/RULE_47/);
  });

  it('filters BBN rules out when cardType is first_visit_routine', () => {
    const p = buildAiGroundedPrompt({
      kind: 'speaking',
      profession: 'medicine',
      task: 'coach',
      cardType: 'first_visit_routine',
    });
    // RULE_44 is breaking_bad_news only
    expect(p.system).not.toMatch(/RULE_44 \(critical\)/);
    // Universal rules remain
    expect(p.system).toMatch(/RULE_06/);
    expect(p.system).toMatch(/RULE_22/);
  });
});

describe('AI rulebook-grounded prompt — task-specific reply formats', () => {
  it.each(['score', 'coach', 'correct', 'generate_feedback', 'generate_content'] as const)(
    '%s task produces a JSON reply contract',
    (task) => {
      const p = buildAiGroundedPrompt({ kind: 'writing', profession: 'medicine', task });
      expect(p.system).toMatch(/Reply format/);
      expect(p.system).toMatch(/```json/);
    },
  );

  it('summarise task produces a plain-text reply contract', () => {
    const p = buildAiGroundedPrompt({ kind: 'writing', profession: 'medicine', task: 'summarise' });
    expect(p.system).toMatch(/Reply format/);
    expect(p.system).toMatch(/Plain text/);
  });
});
