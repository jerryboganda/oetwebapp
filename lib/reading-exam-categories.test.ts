import { describe, expect, it } from 'vitest';
import {
  READING_EXAM_CATEGORIES,
  groupReadingExamPapers,
  resolveReadingExamCategoryId,
} from './reading-exam-categories';

describe('reading exam categories', () => {
  it('keeps the five official book folders in screenshot order', () => {
    expect(READING_EXAM_CATEGORIES.map((category) => category.title)).toEqual([
      'Anna Hartford',
      'Atlas Practice Series',
      'Jayden Book',
      'Nova Practice Series',
      'VERY DIFFICULT READING EXAMS',
    ]);
  });

  it('classifies Jayden papers from slug or tags', () => {
    expect(resolveReadingExamCategoryId({ slug: 'jayden-book-01-bed-bugs' })).toBe('jayden-book');
    expect(resolveReadingExamCategoryId({ tagsCsv: 'reading,jayden-book,official-key' })).toBe(
      'jayden-book',
    );
    expect(resolveReadingExamCategoryId({ title: 'Jayden Book 03 — Skin Lightening' })).toBe(
      'jayden-book',
    );
  });

  it('classifies the other official series without inventing extra names', () => {
    expect(resolveReadingExamCategoryId({ slug: 'anna-hartford-sample-1' })).toBe('anna-hartford');
    expect(resolveReadingExamCategoryId({ title: 'Atlas Practice Series 02' })).toBe(
      'atlas-practice-series',
    );
    expect(resolveReadingExamCategoryId({ tagsCsv: 'nova-practice-series' })).toBe(
      'nova-practice-series',
    );
    expect(resolveReadingExamCategoryId({ slug: 'very-difficult-reading-exams-01' })).toBe(
      'very-difficult-reading-exams',
    );
  });

  it('always returns the five official sections and never adds Other papers', () => {
    const sections = groupReadingExamPapers([
      { slug: 'jayden-book-01-bed-bugs', title: 'JB1 Bed Bugs' },
      { slug: 'jayden-book-02-obstetric-ultrasound', title: 'JB2 Obstetric Ultrasound' },
      { slug: 'legacy-sample-reading', title: 'Older sample paper' },
    ]);

    expect(sections.map((section) => section.id)).toEqual([
      'anna-hartford',
      'atlas-practice-series',
      'jayden-book',
      'nova-practice-series',
      'very-difficult-reading-exams',
    ]);
    expect(sections.find((section) => section.id === 'jayden-book')?.papers).toHaveLength(2);
    expect(sections.find((section) => section.id === 'anna-hartford')?.papers).toHaveLength(0);
    expect(sections.some((section) => section.id === 'other')).toBe(false);
  });

  it('sorts each folder ascending by the exam number, not by API order', () => {
    const sections = groupReadingExamPapers([
      { slug: 'atlas-practice-series-10', title: 'Atlas Practice Series 10 — Sedation' },
      { slug: 'atlas-practice-series-02', title: 'Atlas Practice Series 2 — Burns' },
      { slug: 'atlas-practice-series-22', title: 'Atlas Practice Series 22 — Hernia' },
      { slug: 'atlas-practice-series-09', title: 'Atlas Practice Series 9 — Head injuries' },
      { slug: 'atlas-practice-series-renal-colic', title: 'Atlas Practice Series — Renal colic' },
    ]);

    expect(
      sections.find((section) => section.id === 'atlas-practice-series')?.papers.map((p) => p.title),
    ).toEqual([
      'Atlas Practice Series 2 — Burns',
      'Atlas Practice Series 9 — Head injuries',
      'Atlas Practice Series 10 — Sedation',
      'Atlas Practice Series 22 — Hernia',
      'Atlas Practice Series — Renal colic',
    ]);
  });

  it('sorts every folder independently and preserves genuine numbering gaps', () => {
    const sections = groupReadingExamPapers([
      { slug: 'very-difficult-reading-exams-22', title: 'Very Difficult Reading Exams 22 — Spigelian hernia' },
      { slug: 'nova-practice-series-20', title: 'Nova Practice Series 20 — Menopause' },
      { slug: 'very-difficult-reading-exams-05', title: 'Very Difficult Reading Exams 05 — Amino Acids' },
      { slug: 'nova-practice-series-04', title: 'Nova Practice Series 4 — Sepsis' },
      { slug: 'very-difficult-reading-exams-18', title: 'Very Difficult Reading Exams 18 — Tetanus' },
    ]);

    expect(
      sections
        .find((section) => section.id === 'very-difficult-reading-exams')
        ?.papers.map((p) => p.slug),
    ).toEqual([
      'very-difficult-reading-exams-05',
      'very-difficult-reading-exams-18',
      'very-difficult-reading-exams-22',
    ]);
    expect(
      sections.find((section) => section.id === 'nova-practice-series')?.papers.map((p) => p.slug),
    ).toEqual(['nova-practice-series-04', 'nova-practice-series-20']);
  });

  it('reorders without ever rewriting a title', () => {
    const titles = [
      'Atlas Practice Series 10 — Sedation Part A',
      'Atlas Practice Series 2 — Burns',
    ];
    const sections = groupReadingExamPapers(titles.map((title) => ({ title })));

    expect(
      sections
        .find((section) => section.id === 'atlas-practice-series')
        ?.papers.map((p) => p.title)
        .slice()
        .sort(),
    ).toEqual([...titles].sort());
  });
});
