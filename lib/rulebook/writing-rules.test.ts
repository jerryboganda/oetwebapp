import { lintWritingLetter, writingCoverageSummary } from './writing-rules';
import type { WritingLintInput } from './types';

function base(overrides: Partial<WritingLintInput> = {}): WritingLintInput {
  return {
    letterText: '',
    letterType: 'routine_referral',
    patientAge: 40,
    patientIsMinor: false,
    recipientSpecialty: 'Cardiologist',
    profession: 'medicine',
    caseNotesMarkers: {},
    ...overrides,
  };
}

describe('writing linter — R03.4 smoking/drinking inclusion', () => {
  const letterWithBoth = `Dr A B
Cardiology Clinic
Main Street
City

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones, a 45-year-old teacher, for your assessment.

Mr Jones smokes 10 cigarettes per day and drinks alcohol occasionally. He presented with chest pain today. His blood pressure was 150/90 mmHg. Examination revealed mild discomfort on palpation. He was advised lifestyle changes.

Please do not hesitate to contact me.

Yours sincerely,

Doctor`;

  it('passes when both smoking and drinking are mentioned', () => {
    const findings = lintWritingLetter(base({ letterText: letterWithBoth, letterType: 'routine_referral' }));
    expect(findings.find((f) => f.ruleId === 'R03.4')).toBeUndefined();
  });

  it('flags when smoking is missing', () => {
    const findings = lintWritingLetter(base({ letterText: letterWithBoth.replace(/smokes[^.]+\./, '') }));
    const smokingFinding = findings.filter((f) => f.ruleId === 'R03.4');
    expect(smokingFinding.length).toBeGreaterThan(0);
  });

  it('does NOT fire R03.4 when writing to an occupational therapist', () => {
    const findings = lintWritingLetter(base({
      letterText: letterWithBoth.replace(/smokes[^.]+\./, '').replace(/drinks[^.]+\./, ''),
      recipientSpecialty: 'Occupational Therapist',
      letterType: 'non_medical_referral',
    }));
    expect(findings.find((f) => f.ruleId === 'R03.4')).toBeUndefined();
  });
});

describe('writing linter — R03.6 atopic allergy rule', () => {
  it('fires when atopic flag is set and allergy not mentioned', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A B\n\nIntro\n\nBody\n\nYours sincerely,\nDoctor',
      caseNotesMarkers: { atopicCondition: true },
    }));
    expect(findings.find((f) => f.ruleId === 'R03.6')).toBeDefined();
  });

  it('does not fire when allergy is mentioned', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A B\n\nShe has no known drug allergy.\n\nBody\n\nYours sincerely,\nDoctor',
      caseNotesMarkers: { atopicCondition: true },
    }));
    expect(findings.find((f) => f.ruleId === 'R03.6')).toBeUndefined();
  });
});

describe('writing linter — R04.2 salutation/Re adjacency', () => {
  const bad = `Dear Dr Smith,

Re: Ms Miller D.O.B: 01/01/1980

Intro.

Body paragraph two words.

Yours sincerely,

Doctor`;

  const good = `Dear Dr Smith,
Re: Ms Miller D.O.B: 01/01/1980

Intro.

Body paragraph two words.

Yours sincerely,

Doctor`;

  it('flags blank line between salutation and Re:', () => {
    const findings = lintWritingLetter(base({ letterText: bad }));
    expect(findings.find((f) => f.ruleId === 'R04.2' || f.ruleId === 'R06.3')).toBeDefined();
  });

  it('passes when salutation and Re: are on consecutive lines', () => {
    const findings = lintWritingLetter(base({ letterText: good }));
    const adj = findings.filter((f) => f.ruleId === 'R04.2');
    expect(adj).toHaveLength(0);
  });
});

describe('writing linter — R05.8 no "Date:" prefix', () => {
  it('fires when Date: is written before the date', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dr A B\n\nDate: 1 January 2026\n\nDear Dr Smith,\nRe: Ms A\n\nIntro\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R05.8')).toBeDefined();
  });
});

describe('writing linter — R06.10 minor naming', () => {
  it('flags Mr/Ms title in Re: line for minors', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Miss Sara Miller D.O.B: 01/01/2015\n\nIntro\n\nBody\n\nYours sincerely,\nDoctor',
      patientIsMinor: true,
      patientAge: 8,
    }));
    expect(findings.find((f) => f.ruleId === 'R06.10')).toBeDefined();
  });

  it('passes when minor has full name only in Re: line', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Sara Miller D.O.B: 01/01/2015\n\nIntro\n\nBody\n\nYours sincerely,\nDoctor',
      patientIsMinor: true,
      patientAge: 8,
    }));
    expect(findings.find((f) => f.ruleId === 'R06.10')).toBeUndefined();
  });
});

describe('writing linter — R06.11 sincerely vs faithfully', () => {
  it('flags Yours sincerely when salutation is Dear Sir/Madam', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Sir/Madam,\nRe: Ms A B\n\nIntro\n\nBody\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R06.11')).toBeDefined();
  });

  it('flags Yours faithfully when recipient is named', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A B\n\nIntro\n\nBody\n\nYours faithfully,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R06.11')).toBeDefined();
  });
});

describe('writing linter — R07.6 urgent intro must contain "urgent"', () => {
  it('flags a routine-style intro in an urgent letter', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for assessment.\n\nShe presented with severe chest pain today.\n\nAt your earliest convenience.\n\nYours sincerely,\nDoctor',
      letterType: 'urgent_referral',
    }));
    expect(findings.find((f) => f.ruleId === 'R07.6' || f.ruleId === 'R13.2')).toBeDefined();
  });

  it('passes an urgent intro that contains "urgent"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nI am writing to urgently refer Ms A for emergency review.\n\nOn today\'s visit, she presented with severe chest pain.\n\nAt your earliest convenience.\n\nYours sincerely,\nDoctor',
      letterType: 'urgent_referral',
    }));
    expect(findings.find((f) => f.ruleId === 'R07.6')).toBeUndefined();
  });
});

describe('writing linter — R08.7 forbidden "next visit"', () => {
  it('flags the phrase "next visit"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nOn the next visit, she reported improvement.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.7' || f.ruleId === 'R10.14')).toBeDefined();
  });
});

describe('writing linter — R08.14 forbidden "the patient"', () => {
  it('flags "the patient" in the body', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms Miller\n\nIntro.\n\nThe patient presented with nausea.\n\nYours sincerely,\nDoctor',
    }));
    const hits = findings.filter((f) => f.ruleId === 'R08.14' || f.ruleId === 'R12.2');
    expect(hits.length).toBeGreaterThan(0);
  });
});

describe('writing linter — R08.9 "yesterday"', () => {
  it('flags use of "yesterday"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nYesterday she reported chest pain.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.9')).toBeDefined();
  });
});

describe('writing linter — R09.2 urgent closure phrase', () => {
  it('flags an urgent letter missing "at your earliest convenience"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nI am writing to urgently refer Ms A.\n\nOn today\'s visit she collapsed.\n\nPlease see her soon.\n\nYours sincerely,\nDoctor',
      letterType: 'urgent_referral',
    }));
    expect(findings.find((f) => f.ruleId === 'R09.2' || f.ruleId === 'R13.3')).toBeDefined();
  });
});

describe('writing linter — R11.1 Latin abbreviations', () => {
  it('flags "bd" (twice a day) in a letter body', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed amoxicillin 500 mg bd.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R11.1')).toBeDefined();
  });

  it('flags "prn"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe takes paracetamol 500 mg prn.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R11.1')).toBeDefined();
  });

  it('does not flag when abbreviation is translated', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed amoxicillin 500 mg twice a day.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R11.1')).toBeUndefined();
  });
});

describe('writing linter — R12.1 no contractions', () => {
  it('flags "don\'t"', () => {
    const findings = lintWritingLetter(base({
      letterText: "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe doesn't take any regular medication.\n\nYours sincerely,\nDoctor",
    }));
    expect(findings.find((f) => f.ruleId === 'R12.1')).toBeDefined();
  });
});

describe('writing linter — R12.5 conditions lowercase', () => {
  it('flags "Hypertension" in running text', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has Hypertension since 2015.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.5')).toBeDefined();
  });
});

describe('writing linter — R13.10 "ASAP"', () => {
  it('flags ASAP', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nPlease see her ASAP.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R13.10')).toBeDefined();
  });
});

describe('writing linter — R14.6 "was presented"', () => {
  it('flags "was presented" in discharge letters', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nI am writing to update you regarding Ms A.\n\nShe was presented with chest pain on admission.\n\nYours sincerely,\nDoctor',
      letterType: 'discharge',
    }));
    expect(findings.find((f) => f.ruleId === 'R14.6')).toBeDefined();
  });
});

describe('writing linter — R14.12 treatment FOR not FROM', () => {
  it('flags "treated from"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was treated from pneumonia.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R14.12')).toBeDefined();
  });
});

describe('writing linter — R15.2 non-medical no jargon', () => {
  it('flags "hypertension" in a non-medical referral', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Sir/Madam,\nRe: Ms A\n\nMs A has hypertension and diabetes.\n\nYours faithfully,\nDoctor',
      letterType: 'non_medical_referral',
      recipientSpecialty: 'Occupational Therapist',
    }));
    expect(findings.find((f) => f.ruleId === 'R15.2')).toBeDefined();
  });
});

describe('writing linter — R10.10 "X ago" past simple only', () => {
  it('flags "has presented 3 weeks ago"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has presented 3 weeks ago with fatigue.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R10.10')).toBeDefined();
  });
});

describe('writing linter — R08.1 paragraph count', () => {
  it('flags body with only one paragraph', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nOne paragraph only.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.1')).toBeDefined();
  });
});

describe('writing linter — R03.8 word count band', () => {
  // Helper: build a letter with a body of exactly N words.
  const buildLetter = (bodyWords: number): string => {
    const word = 'word';
    const body = Array.from({ length: bodyWords }, () => word).join(' ');
    return `Dear Dr Smith,\nRe: Ms A\n\n${body}\n\nYours sincerely,\nDoctor`;
  };

  it('emits no R03.8 finding when body is 195 words (in band)', () => {
    const findings = lintWritingLetter(base({ letterText: buildLetter(195) }));
    expect(findings.find((f) => f.ruleId === 'R03.8')).toBeUndefined();
  });

  it('emits one advisory R03.8 finding when body is 100 words (too short)', () => {
    const findings = lintWritingLetter(base({ letterText: buildLetter(100) }));
    const r03_8 = findings.find((f) => f.ruleId === 'R03.8');
    expect(r03_8).toBeDefined();
    expect(r03_8!.severity).toBe('minor');
    expect(r03_8!.message).toMatch(/100 word/);
    expect(r03_8!.message).toMatch(/short/);
  });

  it('emits one advisory R03.8 finding when body is 260 words (too long)', () => {
    const findings = lintWritingLetter(base({ letterText: buildLetter(260) }));
    const r03_8 = findings.find((f) => f.ruleId === 'R03.8');
    expect(r03_8).toBeDefined();
    expect(r03_8!.severity).toBe('minor');
    expect(r03_8!.message).toMatch(/260 word/);
    expect(r03_8!.message).toMatch(/long/);
  });

  it('suppresses R03.8 finding while draft is below the noise threshold (50 words)', () => {
    const findings = lintWritingLetter(base({ letterText: buildLetter(50) }));
    expect(findings.find((f) => f.ruleId === 'R03.8')).toBeUndefined();
  });
});

describe('writing coverage summary', () => {
  it('tallies critical/major/minor and returns findings', () => {
    const summary = writingCoverageSummary(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nThe patient has Hypertension.\n\nShe takes amoxicillin bd.\n\nYours sincerely,\nDoctor',
    }));
    expect(summary.totalRules).toBeGreaterThan(50);
    expect(summary.critical.total).toBeGreaterThan(15);
    expect(summary.critical.violated).toBeGreaterThan(0);
    expect(summary.findings.length).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// New detectors — extended deterministic coverage (audit P1-3 follow-up).
// Each block proves the detector FIRES on a violating sample and DOES NOT
// FIRE on a compliant counter-example.
// ---------------------------------------------------------------------------

describe('writing linter — R10.7 smoking/drinking + duration tense', () => {
  it('flags "She smokes 10 cigarettes for 20 years"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe smokes 10 cigarettes for 20 years.\n\nYours sincerely,\nDoctor',
    }));
    const r10_7 = findings.find((f) => f.ruleId === 'R10.7');
    expect(r10_7).toBeDefined();
    expect(r10_7!.message).toMatch(/has been smoking/);
  });

  it('does NOT fire on "She has been smoking for 20 years"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has been smoking for 20 years.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R10.7')).toBeUndefined();
  });

  it('does NOT fire on "She smokes" without a duration', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe smokes occasionally and drinks socially.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R10.7')).toBeUndefined();
  });
});

describe('writing linter — R11.4 trade vs generic medication consistency', () => {
  it('flags a sentence mixing Panadol (trade) and paracetamol (generic)', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed Panadol and paracetamol.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R11.4')).toBeDefined();
  });

  it('does NOT fire when only generic names are used', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed paracetamol and amoxicillin.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R11.4')).toBeUndefined();
  });
});

describe('writing linter — R12.6 eponymous diseases capitalised', () => {
  it('flags lowercase "parkinson\'s"', () => {
    const findings = lintWritingLetter(base({
      letterText: "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has parkinson's disease.\n\nYours sincerely,\nDoctor",
    }));
    expect(findings.find((f) => f.ruleId === 'R12.6')).toBeDefined();
  });

  it('does NOT fire on properly capitalised "Crohn\'s"', () => {
    const findings = lintWritingLetter(base({
      letterText: "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has Crohn's disease.\n\nYours sincerely,\nDoctor",
    }));
    expect(findings.find((f) => f.ruleId === 'R12.6')).toBeUndefined();
  });
});

describe('writing linter — R12.7 bacteria binomial capitalisation', () => {
  it('flags "staphylococcus aureus" (genus lowercase)', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nThe culture grew staphylococcus aureus.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.7')).toBeDefined();
  });

  it('flags "Staphylococcus Aureus" (species capitalised)', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nThe culture grew Staphylococcus Aureus.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.7')).toBeDefined();
  });

  it('does NOT fire on canonical "Staphylococcus aureus"', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nThe culture grew Staphylococcus aureus.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.7')).toBeUndefined();
  });
});

describe('writing linter — R12.12 "in addition to" no preceding punctuation', () => {
  it('flags "She has hypertension, in addition to diabetes."', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has hypertension, in addition to diabetes.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.12')).toBeDefined();
  });

  it('flags "; together with" usage', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe takes metformin; together with insulin.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.12')).toBeDefined();
  });

  it('does NOT fire on "in addition to" without preceding comma/semicolon', () => {
    const findings = lintWritingLetter(base({
      letterText: 'Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has hypertension in addition to diabetes.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R12.12')).toBeUndefined();
  });
});

describe('writing linter — R08.15 name vs pronoun within paragraph', () => {
  it('flags repeated "Mr Jones" inside the same paragraph', () => {
    const findings = lintWritingLetter(base({
      letterText:
        'Dear Dr Smith,\nRe: Mr Jones\n\nMr Jones presented with chest pain. Mr Jones was tachycardic and Mr Jones declined investigation.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.15')).toBeDefined();
  });

  it('does NOT fire when the second mention is a pronoun', () => {
    const findings = lintWritingLetter(base({
      letterText:
        'Dear Dr Smith,\nRe: Mr Jones\n\nMr Jones presented with chest pain. He was tachycardic and he declined investigation.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.15')).toBeUndefined();
  });

  it('does NOT fire when the same name reappears in a NEW paragraph', () => {
    const findings = lintWritingLetter(base({
      letterText:
        'Dear Dr Smith,\nRe: Mr Jones\n\nMr Jones presented with chest pain. He was tachycardic.\n\nMr Jones was discharged in stable condition.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.15')).toBeUndefined();
  });
});

describe('writing linter — R08.1 minimum 2 body paragraphs (regression)', () => {
  it('does NOT fire when body has two clear paragraphs', () => {
    const findings = lintWritingLetter(base({
      letterText:
        'Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph one.\n\nBody paragraph two with detail.\n\nYours sincerely,\nDoctor',
    }));
    expect(findings.find((f) => f.ruleId === 'R08.1')).toBeUndefined();
  });
});

describe('writing linter — R14.6 "was admitted" not "was presented" (regression)', () => {
  it('does NOT fire when discharge letter uses "was admitted"', () => {
    const findings = lintWritingLetter(base({
      letterText:
        'Dear Dr Smith,\nRe: Ms A\n\nI am writing to update you regarding Ms A.\n\nShe was admitted to St James with chest pain on 1 January.\n\nYours sincerely,\nDoctor',
      letterType: 'discharge',
    }));
    expect(findings.find((f) => f.ruleId === 'R14.6')).toBeUndefined();
  });
});
