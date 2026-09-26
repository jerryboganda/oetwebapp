import { describe, expect, it } from 'vitest';
import { MAX_SEGMENT_CHARS, appendTranscriptFragment } from '../useSpeakingRealtimeVoice';
import type { LiveVoiceTranscriptSegmentInput } from '@/lib/api/speaking-live-voice';

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
});
