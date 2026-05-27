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

// ===========================================================================
// 2026-05-27 audit fixes — direct unit tests for the 6 new detectors
// ===========================================================================

describe('writing linter — R01.5 suspected cancer must trigger urgent referral', () => {
  const letterRoutineWithCancer = `Dr A B
Oncology Clinic
Main Street

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones for your assessment of a suspected cancer in his left lung.

Mr Jones smokes 10 cigarettes per day and drinks alcohol occasionally. He presented today with persistent cough. His chest examination was unremarkable.

Yours sincerely,

Doctor`;

  it('fires R01.5 when suspected cancer is in a routine referral', () => {
    const findings = lintWritingLetter(base({ letterText: letterRoutineWithCancer, letterType: 'routine_referral' }));
    expect(findings.some((f) => f.ruleId === 'R01.5')).toBe(true);
  });

  it('does NOT fire R01.5 when the same cancer wording is in an urgent referral', () => {
    const findings = lintWritingLetter(base({ letterText: letterRoutineWithCancer, letterType: 'urgent_referral' }));
    expect(findings.some((f) => f.ruleId === 'R01.5')).toBe(false);
  });

  it('also fires when caseNotesMarkers.cancerSuspected is set without explicit wording', () => {
    const clean = letterRoutineWithCancer.replace('suspected cancer in his left lung', 'a chest abnormality');
    const findings = lintWritingLetter(
      base({
        letterText: clean,
        letterType: 'routine_referral',
        caseNotesMarkers: { cancerSuspected: true },
      }),
    );
    expect(findings.some((f) => f.ruleId === 'R01.5')).toBe(true);
  });
});

describe('writing linter — R08.3 visit paragraphization', () => {
  const letterWithSeparatePerVisitParagraphs = `Dr A B
Cardiology Clinic
Main Street

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones, a 45-year-old teacher, for your assessment.

On the first visit, Mr Jones presented with mild chest pain.

On the second visit, Mr Jones returned with worsening symptoms.

On the third visit, Mr Jones reported palpitations.

On the fourth visit, Mr Jones presented with shortness of breath.

On the fifth visit, Mr Jones described chest tightness.

Yours sincerely,

Doctor`;

  it('fires R08.3 when visit-per-paragraph count exceeds visitCount marker', () => {
    const findings = lintWritingLetter(
      base({
        letterText: letterWithSeparatePerVisitParagraphs,
        caseNotesMarkers: { visitCount: 3 },
      }),
    );
    expect(findings.some((f) => f.ruleId === 'R08.3')).toBe(true);
  });

  it('does NOT fire R08.3 when visit count is unknown', () => {
    const findings = lintWritingLetter(
      base({
        letterText: letterWithSeparatePerVisitParagraphs,
        caseNotesMarkers: {},
      }),
    );
    expect(findings.some((f) => f.ruleId === 'R08.3')).toBe(false);
  });
});

describe('writing linter — R10.2 visit content uses past simple', () => {
  it('fires when present perfect ("has presented") appears in body', () => {
    const letter = `Dr A B
Cardiology Clinic
Main Street

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones.

Mr Jones has presented with chest pain. He has been examined and was advised lifestyle changes.

Yours sincerely,

Doctor`;
    const findings = lintWritingLetter(base({ letterText: letter }));
    expect(findings.some((f) => f.ruleId === 'R10.2')).toBe(true);
  });

  it('does NOT fire when visits use past simple', () => {
    const letter = `Dr A B
Cardiology Clinic
Main Street

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones.

Mr Jones presented with chest pain. He was examined and advised lifestyle changes.

Yours sincerely,

Doctor`;
    const findings = lintWritingLetter(base({ letterText: letter }));
    expect(findings.some((f) => f.ruleId === 'R10.2')).toBe(false);
  });
});

describe('writing linter — R11.8 numerical values must have units', () => {
  it('fires when blood pressure value is missing mmHg unit', () => {
    const letter = `Dear Dr Smith,
Re: Mr John Jones

His blood pressure was 140 over 90 and his pulse was regular. His weight was 80.

Yours sincerely,
Doctor`;
    const findings = lintWritingLetter(base({ letterText: letter }));
    expect(findings.some((f) => f.ruleId === 'R11.8')).toBe(true);
  });

  it('does NOT fire when values are written with units', () => {
    const letter = `Dear Dr Smith,
Re: Mr John Jones

His blood pressure was 140/90 mmHg, glucose was 7.8 mmol/L, and his weight was 80 kg.

Yours sincerely,
Doctor`;
    const findings = lintWritingLetter(base({ letterText: letter }));
    expect(findings.some((f) => f.ruleId === 'R11.8')).toBe(false);
  });
});

describe('writing linter — R14.9 discharge must list all admission investigations', () => {
  const dischargeLetter = `Dr A B
Royal Hospital

1 January 2026

Dear Dr GP,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to update you regarding Mr Jones, who underwent an appendicectomy at Royal Hospital and is now ready for discharge.

Mr Jones was admitted with right iliac fossa pain. Bloods showed a raised white-cell count. He was treated with intravenous antibiotics and surgery.

Please find enclosed a copy of Mr Jones' pathology results.

Yours sincerely,

Doctor`;

  it('fires R14.9 when an investigation in case notes is not mentioned in the letter', () => {
    const findings = lintWritingLetter(
      base({
        letterText: dischargeLetter,
        letterType: 'discharge',
        caseNotesMarkers: {
          investigationsPerformed: [
            { name: 'White cell count', value: '14.5' },
            { name: 'CRP', value: '120' },
            { name: 'Ultrasound abdomen', value: 'inflamed appendix' },
          ],
        },
      }),
    );
    const r14_9 = findings.filter((f) => f.ruleId === 'R14.9');
    expect(r14_9.length).toBeGreaterThan(0);
    // CRP and Ultrasound are missing from the letter body.
    expect(r14_9[0].message).toMatch(/CRP|Ultrasound abdomen/i);
  });

  it('does NOT fire R14.9 on a routine referral letter regardless of caseNotesMarkers', () => {
    const findings = lintWritingLetter(
      base({
        letterText: dischargeLetter,
        letterType: 'routine_referral',
        caseNotesMarkers: {
          investigationsPerformed: [{ name: 'CRP' }],
        },
      }),
    );
    expect(findings.some((f) => f.ruleId === 'R14.9')).toBe(false);
  });
});

describe('writing linter — R05.2 address first-letter capitalisation', () => {
  it('fires when an address component starts with a lowercase letter', () => {
    const letter = `dr A B
cardiology clinic
Main Street

1 January 2026

Dear Dr Smith,
Re: Mr John Jones

Hello.

Yours sincerely,

Doctor`;
    const findings = lintWritingLetter(base({ letterText: letter }));
    const r05_2 = findings.filter((f) => f.ruleId === 'R05.2');
    expect(r05_2.length).toBeGreaterThan(0);
    expect(r05_2.some((f) => /capital/i.test(f.message))).toBe(true);
  });

  it('passes when every address line starts with a capital', () => {
    const letter = `Dr A B
Cardiology Clinic
Main Street

1 January 2026

Dear Dr Smith,
Re: Mr John Jones

Hello.

Yours sincerely,

Doctor`;
    const findings = lintWritingLetter(base({ letterText: letter }));
    expect(findings.some((f) => f.ruleId === 'R05.2' && /capital/i.test(f.message))).toBe(false);
  });
});
