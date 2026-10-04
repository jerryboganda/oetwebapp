import { MAX_WORDS_BEFORE_INVITATION, MIN_WORDS_AFTER_INVITATION, openingControlVerdict } from './live-voice-opening-control.mjs';

type Segment = { speaker: string; text: string; startMs: number; endMs: number };

const candidate = (text: string, startMs: number, endMs: number): Segment => ({ speaker: 'candidate', text, startMs, endMs });
const patient = (text: string, startMs: number, endMs: number): Segment => ({ speaker: 'patient', text, startMs, endMs });

const GOOD_RUN: Segment[] = [
  candidate('Good morning.', 6_000, 7_200),
  patient('Good morning, doctor.', 8_000, 9_000),
  candidate('My name is Doctor Smith, one of the doctors here. How may I address you?', 11_000, 16_000),
  patient('Anne Carter, but Anne is fine.', 17_000, 19_000),
  candidate('Please have a seat.', 27_000, 28_500),
  patient('Thank you.', 29_000, 29_800),
  candidate('How can I help you today?', 37_000, 39_000),
  patient('Well doctor, I have had this pain in my chest for about three days now and it keeps coming back.', 40_000, 47_000),
];

describe('openingControlVerdict (owner spec 4 Oct 2026: the patient waits to be invited)', () => {
  it('passes a patient who greets back, gives a name, says thank you and tells the story only when invited', () => {
    const verdict = openingControlVerdict({ segments: GOOD_RUN });

    expect(verdict.ok).toBe(true);
    expect(verdict.neverSpeaksFirst).toBe(true);
    expect(verdict.noStoryBeforeInvitation).toBe(true);
    expect(verdict.answersTheInvitation).toBe(true);
    expect(verdict.wordsBeforeInvitation).toBeLessThanOrEqual(MAX_WORDS_BEFORE_INVITATION);
    expect(verdict.wordsAfterInvitation).toBeGreaterThanOrEqual(MIN_WORDS_AFTER_INVITATION);
  });

  it('fails a patient who starts the story after the greeting and the name', () => {
    const early = [
      ...GOOD_RUN.slice(0, 2),
      patient('I have had a terrible pain in my chest for three days and I am really worried about my heart because my father died of it.', 9_100, 15_000),
      ...GOOD_RUN.slice(2),
    ];

    const verdict = openingControlVerdict({ segments: early });

    expect(verdict.ok).toBe(false);
    expect(verdict.noStoryBeforeInvitation).toBe(false);
    expect(verdict.wordsBeforeInvitation).toBeGreaterThan(MAX_WORDS_BEFORE_INVITATION);
  });

  it('fails a patient who speaks before the candidate has said anything', () => {
    const first = [patient('Hello doctor, thank you for seeing me.', 1_000, 3_000), ...GOOD_RUN];

    const verdict = openingControlVerdict({ segments: first });

    expect(verdict.neverSpeaksFirst).toBe(false);
    expect(verdict.ok).toBe(false);
  });

  it('fails a patient who stays silent when finally invited', () => {
    const silent = GOOD_RUN.filter((s) => !(s.speaker === 'patient' && s.startMs >= 40_000));

    const verdict = openingControlVerdict({ segments: silent });

    expect(verdict.answersTheInvitation).toBe(false);
    expect(verdict.ok).toBe(false);
  });

  it('still judges when the first lines were saved as one candidate segment', () => {
    const merged = [
      candidate('Good morning. My name is Doctor Smith, one of the doctors here. How may I address you?', 6_000, 16_000),
      patient('Anne Carter.', 17_000, 18_000),
      candidate('Please have a seat. How can I help you today?', 27_000, 31_000),
      patient('Well doctor, I have had this pain in my chest for about three days now and it keeps coming back.', 32_000, 38_000),
    ];

    expect(openingControlVerdict({ segments: merged }).ok).toBe(true);
  });

  it('says it cannot judge when the invitation is not in the saved transcript', () => {
    const verdict = openingControlVerdict({ segments: GOOD_RUN.slice(0, 5) });

    expect(verdict.ok).toBeNull();
    expect(verdict.reason).toMatch(/invitation/);
  });
});
