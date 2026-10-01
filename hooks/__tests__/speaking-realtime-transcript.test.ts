import { describe, expect, it } from 'vitest';
import {
  MAX_SAME_SPEAKER_GAP_MS,
  MAX_SEGMENT_CHARS,
  ProviderConnectError,
  appendTranscriptFragment,
  isClientRejection,
  isProviderFailure,
  planProviders,
} from '../useSpeakingRealtimeVoice';
import { ApiError } from '@/lib/api/client';
import type { LiveVoicePreflight, LiveVoiceTranscriptSegmentInput } from '@/lib/api/speaking-live-voice';

const at = (startMs: number, endMs = startMs + 100) => ({ startMs, endMs });

describe('appendTranscriptFragment', () => {
  it('joins GPT-Live fragments exactly and keeps one segment per speaker run', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'How can I', true, at(0), true);
    appendTranscriptFragment(segments, 'candidate', ' help', true, at(200), true);
    appendTranscriptFragment(segments, 'patient', 'Thanks,', true, at(900), true);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', 'How can I help'],
      ['patient', 'Thanks,'],
    ]);
  });

  it('merges a late candidate fragment back instead of splitting the sentence', () => {
    // Production 25 Sep 2026: "...help you" / patient "Thanks," / candidate "today".
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'How can I help you', true, at(0, 700), true);
    appendTranscriptFragment(segments, 'patient', 'Thanks,', true, at(900), true);
    const late = appendTranscriptFragment(segments, 'candidate', ' today', true, at(750, 850), true);
    appendTranscriptFragment(segments, 'patient', ' Doctor.', true, at(1_000), true);
    expect(late).toBe(true);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', 'How can I help you today'],
      ['patient', 'Thanks, Doctor.'],
    ]);
  });

  it('starts a new segment when the candidate genuinely speaks again', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Hello', true, at(0), true);
    appendTranscriptFragment(segments, 'patient', 'Hi', true, at(500), true);
    expect(appendTranscriptFragment(segments, 'candidate', 'Tell me more', true, at(2_000), true)).toBe(false);
    expect(segments).toHaveLength(3);
  });

  it('rejoins the last word when GPT-Live times it at or after the patient backchannel', () => {
    // Production 26 Sep 2026 (two-card mock, Card A, saved transcript): the provider timed
    // "today" at/after the patient's "Uh.", so start_ms alone could not order it.
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Mr. Smith, one of the doctors here. Please have a seat. How can I help', true, at(0, 4_800), true);
    appendTranscriptFragment(segments, 'candidate', ' you', true, at(4_800, 4_950), true);
    appendTranscriptFragment(segments, 'patient', ' Uh.', true, at(5_160, 5_300), true);
    const late = appendTranscriptFragment(segments, 'candidate', ' today', true, at(5_330, 5_450), true);
    appendTranscriptFragment(segments, 'patient', ' Well, I was lifting weights this morning', true, at(5_340, 6_500), true);
    expect(late).toBe(true);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', 'Mr. Smith, one of the doctors here. Please have a seat. How can I help you today'],
      ['patient', ' Uh. Well, I was lifting weights this morning'],
    ]);
  });

  it('keeps a patient backchannel whole when a candidate tail lands inside it', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Can you tell', true, at(0, 900), true);
    appendTranscriptFragment(segments, 'patient', ' Mm-h', true, at(1_000, 1_200), true);
    const late = appendTranscriptFragment(segments, 'candidate', ' me', true, at(1_050, 1_250), true);
    appendTranscriptFragment(segments, 'patient', 'mm.', true, at(1_100, 1_300), true);
    expect(late).toBe(true);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', 'Can you tell me'],
      ['patient', ' Mm-hmm.'],
    ]);
  });

  it('rejoins a tail that starts with punctuation', () => {
    // Replay of the 26 Sep 2026 mock: GPT-Live sent ", I would like..." and ". Could you..." as
    // separate deltas after a patient backchannel.
    for (const [first, backchannel, tail] of [
      ['Based on what you have told me', ' Mm-hmm.', ', I would like us to agree on a plan'],
      ["I'm sorry to hear", ' [sigh]', '. Could you tell me everything'],
    ]) {
      const segments: LiveVoiceTranscriptSegmentInput[] = [];
      appendTranscriptFragment(segments, 'candidate', first, true, at(0, 1_000), true);
      appendTranscriptFragment(segments, 'patient', backchannel, true, at(1_100, 1_400), true);
      expect(appendTranscriptFragment(segments, 'candidate', tail, true, at(1_450, 1_800), true)).toBe(true);
      expect(segments.map((s) => s.speaker)).toEqual(['candidate', 'patient']);
      expect(segments[0].text).toBe(first + tail);
    }
  });

  it('rejoins the whole rest of a sentence that the backchannel interrupted', () => {
    // Replay of the 26 Sep 2026 mock: "I'm sorry to hear" / patient "[sigh]" / then "that. Could you tell me
    // everything that has happened..." arrived word by word after the backchannel.
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', " I'm sorry to hear", true, at(0, 1_000), true);
    appendTranscriptFragment(segments, 'patient', ' [sigh]', true, at(1_100, 1_400), true);
    const tail = [' that', '. Could', ' you', ' tell', ' me', ' everything', ' that has', ' happened', ' from', ' the', ' very', ' beginning'];
    tail.forEach((word, i) => {
      expect(appendTranscriptFragment(segments, 'candidate', word, true, at(1_450 + i * 250, 1_650 + i * 250), true)).toBe(true);
    });
    appendTranscriptFragment(segments, 'patient', ' Well, it started this morning', true, at(4_600, 5_500), true);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', " I'm sorry to hear that. Could you tell me everything that has happened from the very beginning"],
      ['patient', ' [sigh] Well, it started this morning'],
    ]);
  });

  it('keeps a tail that starts long after the previous candidate fragment as its own segment', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Could you tell me', true, at(0, 900), true);
    appendTranscriptFragment(segments, 'patient', ' Uh.', true, at(1_000, 1_200), true);
    expect(appendTranscriptFragment(segments, 'candidate', ' more', true, at(3_100, 3_200), true)).toBe(false);
    expect(segments).toHaveLength(3);
  });

  it('does not merge a genuine reply after a backchannel', () => {
    const cases: Array<[string, string, string]> = [
      // capitalised new sentence: whole sentences keep the provider's order (only mid-sentence tails are rejoined)
      ['Could you tell me more about the pain', ' Sharp.', ' Okay, and where exactly'],
      // the candidate's sentence was already finished
      ['How long has that been going on?', ' A week.', ' thanks'],
      // the other speaker's segment is a real answer, not a backchannel
      ['Tell me more', ' No, not really sure', ' and then'],
    ];
    for (const [first, reply, next] of cases) {
      const segments: LiveVoiceTranscriptSegmentInput[] = [];
      appendTranscriptFragment(segments, 'candidate', first, true, at(0, 2_000), true);
      appendTranscriptFragment(segments, 'patient', reply, true, at(2_200, 2_500), true);
      expect(appendTranscriptFragment(segments, 'candidate', next, true, at(2_600, 3_000), true)).toBe(false);
      expect(segments).toHaveLength(3);
    }
  });

  it('keeps a barge-in and a long-stale sentence separate', () => {
    const barge: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(barge, 'candidate', 'Tell me more', true, at(0, 1_000), true);
    appendTranscriptFragment(barge, 'patient', ' I felt a sharp pain in my chest yesterday and it spread', true, at(1_200, 4_000), true);
    expect(appendTranscriptFragment(barge, 'candidate', ' and then', true, at(3_000, 3_400), true)).toBe(false);
    expect(barge).toHaveLength(3);

    const stale: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(stale, 'candidate', 'Take a seat', true, at(0, 500), true);
    appendTranscriptFragment(stale, 'patient', ' Thanks.', true, at(9_000, 9_400), true);
    expect(appendTranscriptFragment(stale, 'candidate', ' please', true, at(9_500, 9_700), true)).toBe(false);
    expect(stale).toHaveLength(3);
  });

  it('keeps Gemini chunks space-joined without provider timing', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'patient', 'I have', false, at(10));
    appendTranscriptFragment(segments, 'patient', 'pain', false, at(20));
    expect(segments).toEqual([{ speaker: 'patient', startMs: 10, endMs: 120, text: 'I have pain' }]);
  });

  it('starts a new segment before one exceeds the server limit', () => {
    // Production 26 Sep 2026: a silent patient left 5 minutes of candidate
    // speech in one segment and the transcript could not be saved.
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    const sentence = 'I will now explain the next part of your care plan in some detail. ';
    for (let i = 0; i < 100; i += 1) appendTranscriptFragment(segments, 'candidate', sentence, true, at(i * 1_000), true);
    expect(segments.length).toBeGreaterThan(1);
    expect(Math.max(...segments.map((s) => s.text.length))).toBeLessThanOrEqual(MAX_SEGMENT_CHARS);
    expect(segments.map((s) => s.text).join('')).toBe(sentence.repeat(100));
  });

  it('keeps the space GPT-Live sends as a delta of its own, so two words are not fused', () => {
    // Production 1 Oct 2026: " Doctor." / " " / "Well," was saved as "Doctor.Well,".
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'patient', 'Thanks,', true, at(0, 400), true);
    appendTranscriptFragment(segments, 'patient', ' Doctor.', true, at(400, 800), true);
    expect(appendTranscriptFragment(segments, 'patient', ' ', true, at(800, 850), true)).toBe(false);
    appendTranscriptFragment(segments, 'patient', 'Well,', true, at(850, 1_200), true);
    expect(segments).toEqual([{ speaker: 'patient', startMs: 0, endMs: 1_200, text: 'Thanks, Doctor. Well,' }]);
  });

  it('never turns whitespace into a segment of its own, or into a late tail of the other speaker', () => {
    // The server rejects a blank segment and fails the whole save.
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    expect(appendTranscriptFragment(segments, 'candidate', ' ', true, at(0, 50), true)).toBe(false);
    expect(segments).toEqual([]);

    appendTranscriptFragment(segments, 'candidate', 'How can I help you', true, at(100, 900), true);
    appendTranscriptFragment(segments, 'patient', 'Thanks,', true, at(1_000, 1_300), true);
    // Timed before the patient began (the shape of a late tail), but it is a space and the last segment is the patient's.
    expect(appendTranscriptFragment(segments, 'candidate', ' ', true, at(950, 990), true)).toBe(false);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', 'How can I help you'],
      ['patient', 'Thanks,'],
    ]);
  });

  it('does not let a lone space hide a long silence from the gap rule', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Um, let me think', true, at(0, 3_000), true);
    appendTranscriptFragment(segments, 'candidate', ' ', true, at(40_000, 40_050), true);
    appendTranscriptFragment(segments, 'candidate', 'Sorry for the long pause', true, at(40_050, 42_000), true);
    expect(segments.map((s) => [s.startMs, s.endMs])).toEqual([[0, 3_000], [40_050, 42_000]]);
  });

  it('starts a new segment when the same speaker resumes after more than the gap limit', () => {
    // Production 1 Oct 2026: "Um, let me think about the best way to explain this" + 45 s of silence + "Sorry for the
    // long pause..." was one 54 s candidate segment, which also inflated the candidate's talk time about 2x.
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Um, let me think about the best way to explain this', true, at(0, 6_000), true);
    // Exactly at the limit still extends the segment ...
    appendTranscriptFragment(segments, 'candidate', ' and then', true, at(6_000 + MAX_SAME_SPEAKER_GAP_MS, 17_000), true);
    expect(segments).toHaveLength(1);
    // ... one millisecond more starts a new one, so the pause shows between the two.
    appendTranscriptFragment(segments, 'candidate', 'Sorry for the long pause', true, at(17_000 + MAX_SAME_SPEAKER_GAP_MS + 1, 29_000), true);
    expect(segments.map((s) => [s.startMs, s.endMs, s.text])).toEqual([
      [0, 17_000, 'Um, let me think about the best way to explain this and then'],
      [27_001, 29_000, 'Sorry for the long pause'],
    ]);
  });

  it('applies the gap rule to Gemini chunks too', () => {
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'patient', 'I have a pain', false, at(1_000, 1_100));
    appendTranscriptFragment(segments, 'patient', 'in my chest', false, at(1_500, 1_600));
    appendTranscriptFragment(segments, 'patient', 'It started yesterday', false, at(20_000, 20_100));
    expect(segments.map((s) => s.text)).toEqual(['I have a pain in my chest', 'It started yesterday']);
  });

  it('still rejoins a late tail whatever the silence before it: the gap rule is only for a speaker who resumes', () => {
    // The candidate's last word is timed before the patient's reply began, 16 s after the candidate's previous fragment.
    const segments: LiveVoiceTranscriptSegmentInput[] = [];
    appendTranscriptFragment(segments, 'candidate', 'Tell me about it', true, at(0, 3_000), true);
    appendTranscriptFragment(segments, 'patient', ' Well, it started a while ago', true, at(20_000, 22_000), true);
    expect(appendTranscriptFragment(segments, 'candidate', ' please', true, at(19_000, 19_500), true)).toBe(true);
    expect(segments.map((s) => [s.speaker, s.text])).toEqual([
      ['candidate', 'Tell me about it please'],
      ['patient', ' Well, it started a while ago'],
    ]);
  });
});

const preflight = (overrides: Partial<LiveVoicePreflight> = {}): LiveVoicePreflight => ({
  provider: 'openai',
  providerDisplayName: 'Provider',
  model: 'model',
  disclosure: 'disclosure',
  retentionDays: 30,
  sessionId: 's1',
  rolePlayCardId: 'c1',
  ...overrides,
});

describe('planProviders', () => {
  it('keeps the order the server chose', () => {
    expect(planProviders(preflight({ provider: 'gemini', candidates: ['gemini', 'openai'] }))).toEqual(['gemini', 'openai']);
    expect(planProviders(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }))).toEqual(['openai', 'gemini']);
  });

  it('drops duplicates and values that are not a live voice provider', () => {
    const candidates = ['openai', 'openai', 'claude', 'gemini'] as unknown as LiveVoicePreflight['candidates'];
    expect(planProviders(preflight({ candidates }))).toEqual(['openai', 'gemini']);
  });

  it('is a single attempt with the named provider when the server sent no candidates (older server)', () => {
    expect(planProviders(preflight({ provider: 'gemini' }))).toEqual(['gemini']);
    expect(planProviders(preflight({ provider: 'gemini', candidates: [] }))).toEqual(['gemini']);
  });

  it('never fails over a run the server pinned', () => {
    expect(planProviders(preflight({ candidates: ['openai', 'gemini'], pinned: true }))).toEqual(['openai']);
  });

  it('is never shrunk by a provider the page asked for: only the server pins', () => {
    // ?voiceProvider=gemini from an account without the QA pin flag: the server ignores it and answers the automatic order.
    expect(planProviders(preflight({ provider: 'openai', candidates: ['openai', 'gemini'], pinned: false }))).toEqual(['openai', 'gemini']);
    expect(planProviders(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }))).toEqual(['openai', 'gemini']);
  });

  it('plans nothing when the preflight names no usable provider', () => {
    expect(planProviders(preflight({ provider: 'claude' as unknown as LiveVoicePreflight['provider'] }))).toEqual([]);
  });
});

describe('isProviderFailure', () => {
  const api = (status: number, code = 'code') => new ApiError(status, code, 'message', false);

  it('is true for a leg that failed before it was live and for outage-shaped create failures', () => {
    expect(isProviderFailure(new ProviderConnectError('closed before setup'))).toBe(true);
    expect(isProviderFailure(api(503, 'live_voice_provider_unavailable'))).toBe(true);
    expect(isProviderFailure(api(503, 'live_voice_provider_timeout'))).toBe(true);
    expect(isProviderFailure(api(502))).toBe(true);
    expect(isProviderFailure(api(500))).toBe(true);
    expect(isProviderFailure(api(408, 'request_timeout'))).toBe(true);
    expect(isProviderFailure(api(0, 'network_error'))).toBe(true);
  });

  it('is false for a definite answer, our own rate limit and anything that is not an API error', () => {
    for (const status of [400, 401, 403, 404, 409, 422]) expect(isProviderFailure(api(status)), String(status)).toBe(false);
    expect(isProviderFailure(api(429, 'rate_limited'))).toBe(false);
    expect(isProviderFailure(new Error('plain'))).toBe(false);
    expect(isProviderFailure(new DOMException('denied', 'NotAllowedError'))).toBe(false);
    expect(isProviderFailure(null)).toBe(false);
  });

  it('reads the status of any error object, so stubbed API errors behave like the real one', () => {
    expect(isProviderFailure(Object.assign(new Error('stub'), { status: 503 }))).toBe(true);
    expect(isProviderFailure(Object.assign(new Error('stub'), { status: 409 }))).toBe(false);
  });
});

describe('isClientRejection', () => {
  it('is true only for a 4xx the server will give again for the same request', () => {
    for (const status of [400, 403, 404, 409, 422]) {
      expect(isClientRejection(new ApiError(status, 'code', 'message', false)), String(status)).toBe(true);
    }
    // 401 is an expired sign-in: the same request succeeds after re-auth, so it is never a permanent rejection.
    for (const status of [0, 401, 408, 429, 500, 503]) {
      expect(isClientRejection(new ApiError(status, 'code', 'message', true)), String(status)).toBe(false);
    }
    expect(isClientRejection(new Error('plain'))).toBe(false);
  });
});
