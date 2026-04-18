import {
  deriveWritingCaseNotesMarkers,
  inferSpeakingCardType,
  inferWritingLetterType,
} from './context';

describe('rulebook context inference', () => {
  it('infers urgent referral from suspected cancer markers', () => {
    expect(inferWritingLetterType({ caseNotes: 'Suspicious breast mass — query malignancy' })).toBe('urgent_referral');
  });

  it('infers discharge from discharge wording', () => {
    expect(inferWritingLetterType({ scenarioType: 'Discharge Summary' })).toBe('discharge');
  });

  it('infers transfer from ICU/ward transfer language', () => {
    expect(inferWritingLetterType({ title: 'Transfer Letter — ICU to Ward' })).toBe('transfer');
  });

  it('infers non-medical referral from occupational therapy', () => {
    expect(inferWritingLetterType({ caseNotes: 'Request: Occupational therapist home assessment' })).toBe('non_medical_referral');
  });

  it('defaults to routine referral otherwise', () => {
    expect(inferWritingLetterType({ title: 'Referral Letter' })).toBe('routine_referral');
  });

  it('derives high-value case note markers', () => {
    const markers = deriveWritingCaseNotesMarkers(
      `Non-smoker. Occasional alcohol. NKDA. Asthma since childhood. Follow-up: 6 weeks. Please find enclosed pathology results. Patient requested referral. Consent discussed.`,
    );
    expect(markers.smokingMentioned).toBe(true);
    expect(markers.drinkingMentioned).toBe(true);
    expect(markers.allergyMentioned).toBe(true);
    expect(markers.atopicCondition).toBe(true);
    expect(markers.followUpDate).toContain('6 weeks');
    expect(markers.resultsEnclosed).toBe(true);
    expect(markers.patientInitiatedReferral).toBe(true);
    expect(markers.consentDocumented).toBe(true);
  });

  it('infers speaking breaking-bad-news cards', () => {
    expect(inferSpeakingCardType('Breaking Bad News — Cancer Diagnosis')).toBe('breaking_bad_news');
  });

  it('infers examination cards', () => {
    expect(inferSpeakingCardType('Examination Card — Abdominal Pain')).toBe('examination');
  });

  it('infers follow-up cards', () => {
    expect(inferSpeakingCardType('Follow-up Visit — Test Results')).toBe('follow_up');
  });

  it('falls back to first_visit_routine', () => {
    expect(inferSpeakingCardType('Routine consultation')).toBe('first_visit_routine');
  });
});
