import { describe, expect, it } from 'vitest';
import {
  buildVideoStepHref,
  unseedVideoValue,
  VIDEO_DRAFT_SEED_TITLE,
  VIDEO_SUBTEST_OPTIONS,
  VIDEO_WIZARD_STEPS,
} from '../video-wizard-config';

describe('video wizard config', () => {
  it('defines the five steps in authoring order', () => {
    expect(VIDEO_WIZARD_STEPS.map((s) => s.id)).toEqual([
      'details',
      'video',
      'extras',
      'access',
      'review',
    ]);
  });

  it('marks only the extras step as optional', () => {
    const optional = VIDEO_WIZARD_STEPS.filter((s) => s.optional).map((s) => s.id);
    expect(optional).toEqual(['extras']);
  });

  it('builds step hrefs under /admin/content/videos with an encoded id', () => {
    expect(buildVideoStepHref('vid-1', 'details')).toBe('/admin/content/videos/vid-1/details');
    expect(buildVideoStepHref('a b/c', 'review')).toBe('/admin/content/videos/a%20b%2Fc/review');
  });

  it('blanks the draft seed title on hydration and keeps real values', () => {
    expect(unseedVideoValue(VIDEO_DRAFT_SEED_TITLE)).toBe('');
    expect(unseedVideoValue('')).toBe('');
    expect(unseedVideoValue(null)).toBe('');
    expect(unseedVideoValue(undefined)).toBe('');
    expect(unseedVideoValue('Real title')).toBe('Real title');
  });

  it('offers the four subtests plus general', () => {
    expect(VIDEO_SUBTEST_OPTIONS.map((o) => o.value)).toEqual([
      'listening',
      'reading',
      'writing',
      'speaking',
      'general',
    ]);
  });
});
