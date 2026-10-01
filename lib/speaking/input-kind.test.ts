import {
  LIVE_TRANSCRIPT_NOTE,
  commonInputKind,
  gradeFailedSavedCopy,
  gradingInProgressCopy,
  gradingSlowSavedCopy,
  speakingInputKind,
  submissionReceivedCopy,
  type SpeakingInputKind,
} from './input-kind';

const KINDS: Array<SpeakingInputKind | null> = ['live_voice', 'recording', null];
const KINDS_WITHOUT_AUDIO: Array<SpeakingInputKind | null> = ['live_voice', null];

const copyFor = (kind: SpeakingInputKind | null) => ({
  banner: submissionReceivedCopy(kind, '1 Oct 2026, 10:06'),
  bannerNoDate: submissionReceivedCopy(kind),
  pending: gradingInProgressCopy(kind),
  failed: gradeFailedSavedCopy(kind),
  slow: gradingSlowSavedCopy([kind, kind]),
});

describe('speakingInputKind', () => {
  it('lets the tutor room win: it is always recorded', () => {
    expect(speakingInputKind(true, 'live_voice')).toBe('recording');
    expect(speakingInputKind(true, null)).toBe('recording');
    expect(speakingInputKind(true, undefined)).toBe('recording');
  });

  it('otherwise trusts the server, and an unknown kind stays null', () => {
    expect(speakingInputKind(false, 'live_voice')).toBe('live_voice');
    expect(speakingInputKind(false, 'recording')).toBe('recording');
    expect(speakingInputKind(false, null)).toBeNull();
    expect(speakingInputKind(false, undefined)).toBeNull();
    expect(speakingInputKind(false)).toBeNull();
  });
});

describe('commonInputKind', () => {
  it('returns the kind every entry shares', () => {
    expect(commonInputKind(['live_voice', 'live_voice'])).toBe('live_voice');
    expect(commonInputKind(['recording'])).toBe('recording');
  });

  it('is null when entries differ, when one is unknown, or when there are none', () => {
    expect(commonInputKind(['live_voice', 'recording'])).toBeNull();
    expect(commonInputKind(['live_voice', null])).toBeNull();
    expect(commonInputKind([null, null])).toBeNull();
    expect(commonInputKind([])).toBeNull();
  });
});

describe('results copy by input kind', () => {
  it('names the saved transcript for a live conversation', () => {
    const copy = copyFor('live_voice');
    expect(copy.banner).toBe('We saved the transcript of your live conversation on 1 Oct 2026, 10:06 and queued it for marking.');
    expect(copy.bannerNoDate).toBe('We saved the transcript of your live conversation and queued it for marking.');
    expect(copy.pending).toBe('Your live conversation transcript is being marked. This page updates automatically.');
    expect(copy.failed).toBe('Your transcript is saved. No credits were used for this failed grade.');
    expect(copy.slow).toBe('Grading is taking longer than usual. Your transcripts are saved and the result will appear here.');
  });

  it('keeps the recorder wording character for character', () => {
    const copy = copyFor('recording');
    expect(copy.banner).toBe('We received your recording on 1 Oct 2026, 10:06 and queued it for marking.');
    expect(copy.bannerNoDate).toBe('We received your recording and queued it for marking.');
    expect(copy.pending).toBe('Your recording is being transcribed and marked. This page updates automatically.');
    expect(copy.failed).toBe('Your recording is saved. No credits were used for this failed grade.');
    expect(copy.slow).toBe('Grading is taking longer than usual. Your recordings are saved and the result will appear here.');
  });

  it('is neutral when the kind is unknown', () => {
    const copy = copyFor(null);
    expect(copy.banner).toBe('We received your role-play on 1 Oct 2026, 10:06 and queued it for marking.');
    expect(copy.bannerNoDate).toBe('We received your role-play and queued it for marking.');
    expect(copy.pending).toBe('Your role-play is being marked. This page updates automatically.');
    expect(copy.failed).toBe('Your role-play is saved. No credits were used for this failed grade.');
    expect(copy.slow).toBe('Grading is taking longer than usual. Your role-plays are saved and the result will appear here.');
  });

  it('says role-plays when the cards of one exam differ', () => {
    expect(gradingSlowSavedCopy(['live_voice', 'recording'])).toContain('Your role-plays are saved');
    expect(gradingSlowSavedCopy(['live_voice', null])).toContain('Your role-plays are saved');
    expect(gradingSlowSavedCopy([])).toContain('Your role-plays are saved');
  });

  it.each(KINDS_WITHOUT_AUDIO)('never mentions a recording or audio when the kind is %s', (kind) => {
    for (const text of Object.values(copyFor(kind))) {
      expect(text).not.toMatch(/recording|audio/i);
    }
  });

  it.each(KINDS)('never uses the words the QA script reads as "still grading" (kind %s)', (kind) => {
    for (const text of Object.values(copyFor(kind))) {
      expect(text).not.toMatch(/processing|being graded|analysing|analyzing|check again/i);
    }
  });

  it('explains that a live conversation has no audio to play back', () => {
    expect(LIVE_TRANSCRIPT_NOTE).toBe(
      'No audio recording is stored for live conversations, so there is nothing to play back. This transcript is what was marked.',
    );
    expect(LIVE_TRANSCRIPT_NOTE).not.toMatch(/processing|being graded|analysing|check again/i);
  });
});
