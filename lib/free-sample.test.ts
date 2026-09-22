import { FREE_SAMPLE_FEEDBACK, FREE_SAMPLE_TAG, findFreeSamplePaper, hasFreeSampleTag } from './free-sample';

describe('free-sample helpers', () => {
  it('matches the exact free-sample tag only (case/whitespace tolerant, like the server)', () => {
    expect(hasFreeSampleTag('free-sample')).toBe(true);
    expect(hasFreeSampleTag('listening,atlas-practice-series,free-sample')).toBe(true);
    expect(hasFreeSampleTag(' FREE-SAMPLE ,x')).toBe(true);
    expect(hasFreeSampleTag('free-sample-2')).toBe(false);
    expect(hasFreeSampleTag('access:free-sample')).toBe(false);
    expect(hasFreeSampleTag('access:free')).toBe(false);
    expect(hasFreeSampleTag('')).toBe(false);
    expect(hasFreeSampleTag(null)).toBe(false);
    expect(hasFreeSampleTag(undefined)).toBe(false);
  });

  it('finds the tagged paper and ignores untagged ones', () => {
    const papers = [
      { id: 'a', tagsCsv: 'listening,atlas-practice-series' },
      { id: 'b', tagsCsv: `listening,atlas-practice-series,${FREE_SAMPLE_TAG}` },
      { id: 'c', tagsCsv: null },
    ];
    expect(findFreeSamplePaper(papers)?.id).toBe('b');
    expect(findFreeSamplePaper([papers[0], papers[2]])).toBeNull();
    expect(findFreeSamplePaper([])).toBeNull();
  });

  it('keeps the wire constants the server sends (ContentEntitlementService)', () => {
    expect(FREE_SAMPLE_TAG).toBe('free-sample');
    expect(FREE_SAMPLE_FEEDBACK).toBe('Free sample — no credits used.');
  });
});
