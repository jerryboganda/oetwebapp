import { describe, expect, it } from 'vitest';
import {
  classifySpeakingCard,
  type SpeakingCardClassifiable,
} from './category-taxonomy';

// FINAL 2026-09-06 §8C — edge cases and expected outputs. These lock the
// deterministic priority rules: encounter type wins over behavioural state,
// "after examination you find…" alone never forces Examination Card, and
// unknown scenarios fall through to Other Cards with a review flag.

function card(overrides: SpeakingCardClassifiable) {
  return classifySpeakingCard({
    scenarioTitle: '',
    setting: '',
    background: '',
    tasks: [],
    ...overrides,
  });
}

describe('classifySpeakingCard (§8C QA examples)', () => {
  it('first visit + angry patient → First Visit + Angry tag', () => {
    const result = card({
      scenarioTitle: 'First visit with an angry patient',
      background:
        'Mrs Allen is attending the clinic for the first time with headaches. She is angry about the waiting time.',
      tasks: ['Take a history', 'Acknowledge her concern about the wait'],
    });
    expect(result.primary).toBe('First Visit');
    expect(result.secondaryTags).toContain('Angry');
    expect(result.needsReview).toBe(false);
  });

  it('follow-up + reluctant patient → Second Visit + Reluctant tag', () => {
    const result = card({
      scenarioTitle: 'Follow-up hypertension review',
      background:
        'Mr Baker is returning for a follow-up of his hypertension. He refuses to take the prescribed tablets.',
      tasks: ['Check progress since the last visit', 'Address his refusal'],
    });
    expect(result.primary).toBe('Second Visit / Follow-up');
    expect(result.secondaryTags).toContain('Reluctant');
    expect(result.needsReview).toBe(false);
  });

  it('first visit + breaking bad news → First Visit + Breaking Bad News tag', () => {
    const result = card({
      scenarioTitle: 'First visit with bad news',
      background:
        'Ms Clark is attending for the first time. The biopsy confirms cancer and you must break the bad news today.',
      tasks: ['Give the diagnosis sensitively'],
    });
    expect(result.primary).toBe('First Visit');
    expect(result.secondaryTags).toContain('Breaking Bad News');
    expect(result.needsReview).toBe(false);
  });

  it('follow-up test result confirms cancer → Second Visit + Breaking Bad News tag', () => {
    const result = card({
      scenarioTitle: 'Follow-up test results',
      background:
        'Mr Davis is coming back for his test results. The results confirm cancer and you must explain the next steps.',
      tasks: ['Communicate the results', 'Discuss referral'],
    });
    expect(result.primary).toBe('Second Visit / Follow-up');
    expect(result.secondaryTags).toContain('Breaking Bad News');
    expect(result.needsReview).toBe(false);
  });

  it('"after examination you find…" alone does NOT force Examination Card', () => {
    const result = card({
      scenarioTitle: 'First visit headache',
      background:
        'Mrs Evans is attending for the first time with headaches. After examination you find nothing abnormal.',
      tasks: ['Take a history', 'Explain the findings'],
    });
    expect(result.primary).toBe('First Visit');
    expect(result.secondaryTags).toEqual([]);
    expect(result.needsReview).toBe(false);
  });

  it('"You have just examined the patient…" → Examination Card', () => {
    const result = card({
      scenarioTitle: 'Post-examination discussion',
      background:
        'You have just examined the patient, who presented with abdominal pain. Discuss your findings and plan.',
      tasks: ["Begin with 'Thank you for letting me examine you'"],
    });
    expect(result.primary).toBe('Examination Card');
    expect(result.needsReview).toBe(false);
  });

  it('patient has just arrived in ED → Emergency', () => {
    const result = card({
      scenarioTitle: 'Emergency chest pain',
      setting: 'Emergency Department',
      background:
        'Mr Farah has just arrived in the Emergency Department with chest pain. Take a focused history.',
      tasks: ['Assess the emergency'],
    });
    expect(result.primary).toBe('Emergency / Emergency Department');
    expect(result.needsReview).toBe(false);
  });

  it('patient in ED/ward under care for hours → Already Known Patient', () => {
    const result = card({
      scenarioTitle: 'Ward review',
      setting: 'Emergency Department observation ward',
      background:
        'Mrs Green has been in the ED under care for hours and is now admitted to the ward. Review her progress.',
      tasks: ['Check how she is responding to treatment'],
    });
    expect(result.primary).toBe('Already Known Patient');
    expect(result.needsReview).toBe(false);
  });

  it('discharge discussion after admission → Already Known Patient', () => {
    const result = card({
      scenarioTitle: 'Discharge advice after appendectomy',
      setting: 'Surgical ward',
      background:
        'Mr Hill was admitted three days ago and had an appendectomy. You are discharging him today. Give discharge advice.',
      tasks: ['Explain wound care', 'Explain red flags'],
    });
    expect(result.primary).toBe('Already Known Patient');
    expect(result.needsReview).toBe(false);
  });

  it('nursing home visit with no match → Other Cards + review flag', () => {
    const result = card({
      scenarioTitle: 'Nursing home medication review visit',
      setting: 'Nursing home',
      background:
        'You are visiting a nursing home resident to review the medication chart with the senior carer.',
      tasks: ['Go through the chart'],
    });
    expect(result.primary).toBe('Other Cards');
    expect(result.needsReview).toBe(true);
  });

  it('pure breaking bad news with no visit context → Breaking Bad News', () => {
    const result = card({
      scenarioTitle: 'Breaking bad news consultation',
      background: 'You must tell the patient the diagnosis is terminal cancer.',
      tasks: ['Break the bad news sensitively'],
    });
    expect(result.primary).toBe('Breaking Bad News');
    expect(result.needsReview).toBe(false);
  });

  it('pure angry complaint with no visit context → Angry Patient', () => {
    const result = card({
      scenarioTitle: 'Complaint about a bill',
      background: 'The patient is furious about an incorrect bill and demands an explanation.',
      tasks: ['Acknowledge the complaint'],
    });
    expect(result.primary).toBe('Angry Patient');
    expect(result.needsReview).toBe(false);
  });

  it('pure refusal with no visit context → Reluctant Patient', () => {
    const result = card({
      scenarioTitle: 'Vaccination refusal',
      background: 'The mother refuses vaccination for her child despite your advice.',
      tasks: ['Explore her concerns'],
    });
    expect(result.primary).toBe('Reluctant Patient');
    expect(result.needsReview).toBe(false);
  });

  it('patient surname "Ward" does not read as a hospital ward', () => {
    const result = card({
      scenarioTitle: 'Sharing a new diagnosis of colorectal cancer',
      patientName: 'Mrs Hannah Ward',
      background:
        'Mrs Ward underwent a colonoscopy two weeks ago. Biopsy results have confirmed adenocarcinoma.',
    });
    expect(result.primary).toBe('Second Visit / Follow-up');
    expect(result.secondaryTags).toContain('Breaking Bad News');
  });

  it('empty / ambiguous card → Other Cards + review flag, never forced', () => {
    const result = card({
      scenarioTitle: 'General consultation',
      background: 'Talk to the patient about their health.',
    });
    expect(result.primary).toBe('Other Cards');
    expect(result.needsReview).toBe(true);
  });
});
