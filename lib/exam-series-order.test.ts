import { describe, expect, it } from 'vitest';
import { compareExamSeriesPapers, extractSeriesNumber } from './exam-series-order';

const ATLAS = ['atlas-practice-series', 'atlas practice series', 'atlas-practice'];
const NOVA = ['nova-practice-series', 'nova practice series', 'nova-practice'];
const VERY_DIFFICULT = [
  'very-difficult-reading-exams',
  'very difficult reading exams',
  'very-difficult',
];
const JAYDEN = ['jayden-book', 'jayden book'];

describe('extractSeriesNumber', () => {
  it.each([
    ['Atlas Practice Series 09 \u2014 Head injuries', undefined, ATLAS, 9],
    ['Atlas Practice Series 10 \u2014 Sedation Part A', undefined, ATLAS, 10],
    ['Very Difficult Reading Exams 22 \u2014 Spigelian hernia', undefined, VERY_DIFFICULT, 22],
    ['Very Difficult Reading Exams 05 \u2014 Branched-Chain Amino Acid Supplements', undefined, VERY_DIFFICULT, 5],
    ['Nova Practice Series 20 \u2014 Menopause', undefined, NOVA, 20],
    ['Nova Practice Series 12 \u2014 COVID-19 vaccines', undefined, NOVA, 12],
    ['Atlas Practice Series \u2014 Listening Sample Test 9', undefined, ATLAS, 9],
    ['Atlas Practice Series \u2014 Listening Sample Test 10', undefined, ATLAS, 10],
    ['JB1 Bed Bugs', 'jayden-book-01-bed-bugs', JAYDEN, 1],
    ['JB2 Obstetric Ultrasound', 'jayden-book-02-obstetric-ultrasound', JAYDEN, 2],
  ])('reads %s as exam number %s', (title, slug, matchers, expected) => {
    expect(extractSeriesNumber({ title, slug }, matchers)).toBe(expected);
  });

  it('falls back to the slug when the title carries no number', () => {
    expect(
      extractSeriesNumber(
        { title: 'Atlas Practice Series', slug: 'atlas-practice-series-07-renal-colic' },
        ATLAS,
      ),
    ).toBe(7);
  });

  it('skips an implausible year in the slug and takes the real number', () => {
    expect(
      extractSeriesNumber({ title: '', slug: 'atlas-practice-series-2026-09-launch' }, ATLAS),
    ).toBe(9);
  });

  it('returns null when neither the title nor the slug carries a number', () => {
    expect(
      extractSeriesNumber(
        { title: 'Atlas Practice Series \u2014 Renal colic', slug: 'atlas-practice-series-renal-colic' },
        ATLAS,
      ),
    ).toBeNull();
  });

  it('strips the longest matcher first so a shorter prefix cannot leak', () => {
    expect(extractSeriesNumber({ title: 'Atlas Practice Series 3' }, ATLAS)).toBe(3);
  });

  it('tolerates missing title and slug', () => {
    expect(extractSeriesNumber({}, ATLAS)).toBeNull();
    expect(extractSeriesNumber({ title: null, slug: null }, ATLAS)).toBeNull();
  });
});

describe('compareExamSeriesPapers', () => {
  it('sorts ascending by exam number, not by string order', () => {
    const papers = [
      { title: 'Atlas Practice Series 10 \u2014 Sedation' },
      { title: 'Atlas Practice Series 2 \u2014 Burns' },
      { title: 'Atlas Practice Series 22 \u2014 Hernia' },
      { title: 'Atlas Practice Series 9 \u2014 Head injuries' },
    ];

    expect([...papers].sort(compareExamSeriesPapers(ATLAS)).map((p) => p.title)).toEqual([
      'Atlas Practice Series 2 \u2014 Burns',
      'Atlas Practice Series 9 \u2014 Head injuries',
      'Atlas Practice Series 10 \u2014 Sedation',
      'Atlas Practice Series 22 \u2014 Hernia',
    ]);
  });

  it('keeps unnumbered papers last', () => {
    const papers = [
      { title: 'Atlas Practice Series \u2014 Renal colic', slug: 'atlas-practice-series-renal-colic' },
      { title: 'Atlas Practice Series 3 \u2014 Burns' },
      { title: 'Atlas Practice Series 1 \u2014 Asthma' },
    ];

    expect([...papers].sort(compareExamSeriesPapers(ATLAS)).map((p) => p.title)).toEqual([
      'Atlas Practice Series 1 \u2014 Asthma',
      'Atlas Practice Series 3 \u2014 Burns',
      'Atlas Practice Series \u2014 Renal colic',
    ]);
  });

  it('leaves genuine gaps in the numbering alone', () => {
    const papers = [
      { title: 'Nova Practice Series 20 \u2014 Menopause' },
      { title: 'Nova Practice Series 4 \u2014 Sepsis' },
      { title: 'Nova Practice Series 18 \u2014 Appendicitis' },
    ];

    expect([...papers].sort(compareExamSeriesPapers(NOVA)).map((p) => p.title)).toEqual([
      'Nova Practice Series 4 \u2014 Sepsis',
      'Nova Practice Series 18 \u2014 Appendicitis',
      'Nova Practice Series 20 \u2014 Menopause',
    ]);
  });

  it('is stable for exact ties so the API order survives', () => {
    const first = { title: 'Atlas Practice Series 5', slug: 'a' };
    const second = { title: 'Atlas Practice Series 5', slug: 'a' };

    expect(compareExamSeriesPapers(ATLAS)(first, second)).toBe(0);
    expect([first, second].sort(compareExamSeriesPapers(ATLAS))).toEqual([first, second]);
  });

  it('never rewrites a title', () => {
    const titles = [
      'Atlas Practice Series 10 \u2014 Sedation Part A',
      'Atlas Practice Series 2 \u2014 Burns',
    ];
    const sorted = titles.map((title) => ({ title })).sort(compareExamSeriesPapers(ATLAS));

    expect(sorted.map((p) => p.title).sort()).toEqual([...titles].sort());
  });
});
