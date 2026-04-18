import { auditSpeakingTranscript } from './speaking-rules';
import type { SpeakingAuditInput, SpeakingTurn } from './types';

function turn(speaker: SpeakingTurn['speaker'], text: string, ms: [number, number] = [0, 0]): SpeakingTurn {
  return { speaker, text, startMs: ms[0], endMs: ms[1] };
}

function baseInput(overrides: Partial<SpeakingAuditInput> = {}): SpeakingAuditInput {
  return {
    cardType: 'first_visit_routine',
    profession: 'medicine',
    transcript: [],
    ...overrides,
  };
}

describe('speaking audit — RULE_06/07 jargon detector', () => {
  it('flags "CT scan" without plain explanation', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', "Hello, we'll need to do a CT scan of your chest next week."),
        ],
      }),
    );
    const jargon = findings.filter((f) => f.ruleId === 'RULE_06' || f.ruleId === 'RULE_07');
    expect(jargon.length).toBeGreaterThan(0);
  });

  it('does not flag when candidate explains the term', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', "I'd like to arrange an imaging scan of your chest."),
        ],
      }),
    );
    const jargon = findings.filter((f) => f.ruleId === 'RULE_07');
    expect(jargon.length).toBe(0);
  });

  it('flags "hypertension" when used to a patient', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [turn('candidate', 'Based on your reading, I am concerned about hypertension.')],
      }),
    );
    expect(findings.find((f) => /RULE_(06|07|09|10)/.test(f.ruleId))).toBeDefined();
  });
});

describe('speaking audit — RULE_22 monologue detector', () => {
  it('flags a candidate turn longer than 120 words without a patient response', () => {
    const longText = Array.from({ length: 160 }, (_, i) => `word${i}`).join(' ');
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [turn('candidate', longText)],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_22')).toBeDefined();
  });

  it('does not flag short turns separated by patient replies', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', 'Hello, I\'m Dr Smith. How can I help?'),
          turn('patient', 'I have chest pain.'),
          turn('candidate', "I'm sorry to hear that. Can you tell me more?"),
        ],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_22')).toBeUndefined();
  });
});

describe('speaking audit — RULE_23 weight sensitivity', () => {
  it('flags "What is your weight?"', () => {
    const findings = auditSpeakingTranscript(
      baseInput({ transcript: [turn('candidate', 'What is your weight, please?')] }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_23')).toBeDefined();
  });

  it('does not flag sensitive framing', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [turn('candidate', 'Would you mind if I noted your height and weight to check your BMI?')],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_23')).toBeUndefined();
  });
});

describe('speaking audit — RULE_27 smoking ladder order', () => {
  it('flags starting with "reduce" before "quit"', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', 'I would suggest you try to reduce how many cigarettes you smoke.'),
          turn('patient', "I'm not sure."),
          turn('candidate', 'Ideally we want you to quit smoking completely.'),
        ],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_27')).toBeDefined();
  });

  it('passes when cessation is recommended first', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', 'I strongly recommend you quit smoking completely.'),
          turn('patient', "I don't think I can."),
          turn('candidate', 'If complete cessation is hard, we could discuss reducing your intake.'),
        ],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_27')).toBeUndefined();
  });
});

describe('speaking audit — RULE_32 over-diagnosis', () => {
  it('flags "you have hypertension"', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [turn('candidate', 'Based on this reading, you have hypertension.')],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_32')).toBeDefined();
  });
});

describe('speaking audit — RULE_15/20/21 stage coverage', () => {
  it('flags missing empathy / recap / closure in a transcript', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', 'Hello, I am Dr X. How can I help?'),
          turn('patient', 'I have a headache.'),
          turn('candidate', 'Can you tell me more about the pain?'),
          turn('patient', 'It started last week.'),
        ],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_15')).toBeDefined();
    expect(findings.find((f) => f.ruleId === 'RULE_20')).toBeDefined();
    expect(findings.find((f) => f.ruleId === 'RULE_21')).toBeDefined();
  });

  it('passes a transcript with empathy, recap and closure', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        transcript: [
          turn('candidate', 'Hello, I am Dr X. How can I help?'),
          turn('patient', 'I have a headache.'),
          turn('candidate', "I'm really sorry to hear that — that must have been very difficult."),
          turn('patient', 'It has been hard.'),
          turn('candidate', 'So to recap — you have had headaches for two weeks, and we have agreed on paracetamol.'),
          turn('candidate', "Please don't hesitate to come back if things get worse."),
        ],
      }),
    );
    expect(findings.find((f) => f.ruleId === 'RULE_15')).toBeUndefined();
    expect(findings.find((f) => f.ruleId === 'RULE_20')).toBeUndefined();
    expect(findings.find((f) => f.ruleId === 'RULE_21')).toBeUndefined();
  });
});

describe('speaking audit — BBN protocol (RULE_41–47) for breaking_bad_news cards', () => {
  it('flags missing warning shots / support-system ask when delivering cancer diagnosis', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        cardType: 'breaking_bad_news',
        transcript: [
          turn('candidate', 'Your results show cancer.'),
          turn('patient', '…'),
        ],
        silenceAfterDiagnosisMs: 500,
      }),
    );
    // Expect multiple BBN findings
    expect(findings.some((f) => f.ruleId === 'RULE_41')).toBe(true);
    expect(findings.some((f) => f.ruleId === 'RULE_42')).toBe(true);
    expect(findings.some((f) => f.ruleId === 'RULE_44')).toBe(true); // silence too short
  });

  it('passes a correctly-sequenced BBN transcript with 4s silence', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        cardType: 'breaking_bad_news',
        silenceAfterDiagnosisMs: 4000,
        transcript: [
          turn('candidate', "Before we discuss your results, is there anyone you'd like to have here with you?"),
          turn('patient', 'My partner is outside.'),
          turn('candidate', "I'm afraid the results are not quite what we had hoped for."),
          turn('candidate', 'I am very sorry to tell you — the results are showing signs of cancer.'),
          turn('patient', '…'),
          turn('candidate', 'I know this is a lot to take in. Please take all the time you need.'),
          turn('candidate', 'I want you to know that we caught this at an early stage, and there are effective treatment options available.'),
          turn('candidate', 'I am here for you, and my number is available whenever you need anything.'),
        ],
      }),
    );
    // No BBN protocol findings for a correctly sequenced conversation
    const bbnFindings = findings.filter((f) => /RULE_4[1-7]/.test(f.ruleId));
    expect(bbnFindings.length).toBe(0);
  });

  it('BBN rules do NOT fire on non-BBN card types', () => {
    const findings = auditSpeakingTranscript(
      baseInput({
        cardType: 'first_visit_routine',
        transcript: [turn('candidate', 'Hello there.')],
      }),
    );
    const bbnFindings = findings.filter((f) => /RULE_4[1-7]/.test(f.ruleId));
    expect(bbnFindings.length).toBe(0);
  });
});
