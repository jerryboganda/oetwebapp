import { describe, expect, it } from 'vitest';
import {
  LISTENING_EXAM_CATEGORIES,
  groupListeningExamPapers,
  resolveListeningExamCategoryId,
} from './listening-exam-categories';

describe('listening exam categories', () => {
  it('keeps only Atlas and Nova Listening series', () => {
    expect(LISTENING_EXAM_CATEGORIES.map((category) => category.title)).toEqual([
      'Atlas Practice Series',
      'Nova Practice Series',
    ]);
  });

  it('classifies Atlas and Nova papers from slug, title, or tags', () => {
    expect(resolveListeningExamCategoryId({ slug: 'atlas-practice-series-listening-sample-test-09' })).toBe(
      'atlas-practice-series',
    );
    expect(resolveListeningExamCategoryId({ title: 'Atlas Practice Series 02' })).toBe('atlas-practice-series');
    expect(resolveListeningExamCategoryId({ tagsCsv: 'listening,atlas-practice,official-key' })).toBe(
      'atlas-practice-series',
    );
    expect(resolveListeningExamCategoryId({ slug: 'nova-practice-series-listening-20' })).toBe(
      'nova-practice-series',
    );
    expect(resolveListeningExamCategoryId({ tagsCsv: 'nova practice series' })).toBe('nova-practice-series');
  });

  it('does not classify Reading-only series as Listening folders', () => {
    expect(resolveListeningExamCategoryId({ slug: 'anna-hartford-sample-1' })).toBe('other');
    expect(resolveListeningExamCategoryId({ slug: 'jayden-book-01-bed-bugs' })).toBe('other');
    expect(resolveListeningExamCategoryId({ slug: 'very-difficult-reading-exams-01' })).toBe('other');
    expect(resolveListeningExamCategoryId({ title: 'Listening Sample 1' })).toBe('other');
  });

  it('always returns the two Listening sections and drops unmatched papers', () => {
    const sections = groupListeningExamPapers([
      { slug: 'atlas-practice-series-listening-sample-test-09', title: 'Atlas ST9' },
      { slug: 'nova-practice-series-listening-20', title: 'Nova 20' },
      { slug: 'jayden-book-01-bed-bugs', title: 'Jayden should not appear' },
      { slug: 'listening-sample-1', title: 'Older sample paper' },
    ]);

    expect(sections.map((section) => section.id)).toEqual([
      'atlas-practice-series',
      'nova-practice-series',
    ]);
    expect(sections.find((section) => section.id === 'atlas-practice-series')?.papers).toHaveLength(1);
    expect(sections.find((section) => section.id === 'nova-practice-series')?.papers).toHaveLength(1);
    expect(sections.some((section) => (section as { id: string }).id === 'other')).toBe(false);
    expect(sections.flatMap((section) => section.papers).map((paper) => paper.title)).toEqual([
      'Atlas ST9',
      'Nova 20',
    ]);
  });

  it('sorts each folder ascending by the exam number that follows the dash', () => {
    const sections = groupListeningExamPapers([
      {
        slug: 'atlas-practice-series-listening-sample-test-09',
        title: 'Atlas Practice Series — Listening Sample Test 9',
      },
      {
        slug: 'atlas-practice-series-listening-sample-test-10',
        title: 'Atlas Practice Series — Listening Sample Test 10',
      },
      {
        slug: 'atlas-practice-series-listening-sample-test-02',
        title: 'Atlas Practice Series — Listening Sample Test 2',
      },
    ]);

    expect(
      sections.find((section) => section.id === 'atlas-practice-series')?.papers.map((p) => p.title),
    ).toEqual([
      'Atlas Practice Series — Listening Sample Test 2',
      'Atlas Practice Series — Listening Sample Test 9',
      'Atlas Practice Series — Listening Sample Test 10',
    ]);
  });

  it('sorts Atlas and Nova independently', () => {
    const sections = groupListeningExamPapers([
      { slug: 'nova-practice-series-listening-20', title: 'Nova Practice Series 20' },
      { slug: 'atlas-practice-series-listening-11', title: 'Atlas Practice Series 11' },
      { slug: 'nova-practice-series-listening-03', title: 'Nova Practice Series 3' },
      { slug: 'atlas-practice-series-listening-02', title: 'Atlas Practice Series 2' },
    ]);

    expect(
      sections.find((section) => section.id === 'atlas-practice-series')?.papers.map((p) => p.title),
    ).toEqual(['Atlas Practice Series 2', 'Atlas Practice Series 11']);
    expect(
      sections.find((section) => section.id === 'nova-practice-series')?.papers.map((p) => p.title),
    ).toEqual(['Nova Practice Series 3', 'Nova Practice Series 20']);
  });
});
