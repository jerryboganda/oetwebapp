import { describe, expect, it } from 'vitest';
import {
  CRASH_COURSE_ARABIC_WRITING_TAG,
  CRASH_COURSE_ONLY_TAG,
  CRASH_COURSE_WORKSHOPS_TAG,
  CRASH_COURSE_WRITING_OLD_TAG,
  FAST_TRACK_CRASH_COURSE_TAG,
  FULL_COURSE_ONLY_TAG,
  VIDEO_BATCH_TAGS,
  detectAccessMode,
} from './VideoBatchTagPicker';

describe('VideoBatchTagPicker.detectAccessMode', () => {
  it('returns "full" when only batch:full-course-only is set', () => {
    expect(detectAccessMode(FULL_COURSE_ONLY_TAG)).toBe('full');
  });

  it('returns "crash" when any crash-course tag is set', () => {
    expect(detectAccessMode(CRASH_COURSE_ARABIC_WRITING_TAG)).toBe('crash');
    expect(detectAccessMode(CRASH_COURSE_WRITING_OLD_TAG)).toBe('crash');
    expect(detectAccessMode(FAST_TRACK_CRASH_COURSE_TAG)).toBe('crash');
    expect(detectAccessMode(CRASH_COURSE_WORKSHOPS_TAG)).toBe('crash');
    expect(detectAccessMode(CRASH_COURSE_ONLY_TAG)).toBe('crash');
  });

  it('returns "shared" when neither is set', () => {
    expect(detectAccessMode('')).toBe('shared');
    expect(detectAccessMode('language:ar, skimming')).toBe('shared');
  });

  it('returns "custom" when both full and crash tags are mixed', () => {
    expect(
      detectAccessMode(`${FULL_COURSE_ONLY_TAG}, ${CRASH_COURSE_ARABIC_WRITING_TAG}`),
    ).toBe('custom');
  });
});

describe('VIDEO_BATCH_TAGS catalog', () => {
  it('exposes a single canonical Full Course tag', () => {
    const fullTags = VIDEO_BATCH_TAGS.filter((t) => t.group === 'full');
    expect(fullTags).toHaveLength(1);
    expect(fullTags[0]?.value).toBe(FULL_COURSE_ONLY_TAG);
  });

  it('exposes a Crash Course tag per the four owner-confirmed Writing folders', () => {
    const values = VIDEO_BATCH_TAGS.filter((t) => t.group === 'crash').map((t) => t.value);
    expect(values).toEqual(
      expect.arrayContaining([
        CRASH_COURSE_ARABIC_WRITING_TAG,
        CRASH_COURSE_WRITING_OLD_TAG,
        FAST_TRACK_CRASH_COURSE_TAG,
        CRASH_COURSE_WORKSHOPS_TAG,
        CRASH_COURSE_ONLY_TAG,
      ]),
    );
  });
});
