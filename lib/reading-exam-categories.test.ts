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
});
