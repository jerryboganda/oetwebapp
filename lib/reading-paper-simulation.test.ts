import {
  buildPartABookletPages,
  buildPartBCBookletPages,
  formatWallTimer,
  getReadingPaperPhase,
} from './reading-paper-simulation';
import type { ReadingLearnerStructureDto } from './reading-authoring-api';

const structure: ReadingLearnerStructureDto = {
  paper: { id: 'paper-1', title: 'Sample', slug: 'sample', subtestCode: 'reading' },
  parts: [
    {
      id: 'part-a',
      partCode: 'A',
      timeLimitMinutes: 15,
      maxRawScore: 20,
      instructions: null,
      texts: [
        { id: 'a-text-1', displayOrder: 1, title: 'A1', source: null, bodyHtml: '<p>A1</p>', wordCount: 10, topicTag: null },
      ],
      questions: [],
    },
    {
      id: 'part-b',
      partCode: 'B',
      timeLimitMinutes: 45,
      maxRawScore: 6,
      instructions: null,
      texts: [
        { id: 'b-text-1', displayOrder: 1, title: 'B1', source: null, bodyHtml: '<p>B1</p>', wordCount: 20, topicTag: null },
      ],
      questions: [
        { id: 'b-q-1', readingTextId: 'b-text-1', displayOrder: 1, points: 1, questionType: 'MultipleChoice3', stem: 'B?', options: ['A', 'B', 'C'] },
      ],
    },
    {
      id: 'part-c',
      partCode: 'C',
      timeLimitMinutes: 45,
      maxRawScore: 16,
      instructions: null,
      texts: [
        { id: 'c-text-1', displayOrder: 1, title: 'C1', source: null, bodyHtml: '<p>C1</p>', wordCount: 300, topicTag: null },
      ],
      questions: [
        { id: 'c-q-1', readingTextId: 'c-text-1', displayOrder: 1, points: 1, questionType: 'MultipleChoice4', stem: 'C?', options: ['A', 'B', 'C', 'D'] },
      ],
    },
  ],
};

describe('reading paper simulation helpers', () => {
  it('calculates Part A, B/C, and expired phases from server deadlines', () => {
    const start = Date.UTC(2026, 0, 1, 10, 0, 0);
    const partADeadlineAt = new Date(start + 15 * 60_000).toISOString();
    const partBCDeadlineAt = new Date(start + 60 * 60_000).toISOString();

    expect(getReadingPaperPhase({ partADeadlineAt, partBCDeadlineAt }, start + 5 * 60_000)).toBe('partA');
    expect(getReadingPaperPhase({ partADeadlineAt, partBCDeadlineAt }, start + 20 * 60_000)).toBe('partBC');
    expect(getReadingPaperPhase({ partADeadlineAt, partBCDeadlineAt }, start + 61 * 60_000)).toBe('expired');
  });

  it('builds separate Part A text pages and combined B/C pages', () => {
    expect(buildPartABookletPages(structure)).toEqual([
      expect.objectContaining({ partCode: 'A', textIds: ['a-text-1'], questionIds: [] }),
    ]);
    expect(buildPartBCBookletPages(structure).map((page) => page.partCode)).toEqual(['B', 'C', 'C']);
  });

  it('formats wall timer values consistently', () => {
    expect(formatWallTimer(0)).toBe('0:00');
    expect(formatWallTimer(605)).toBe('10:05');
  });
});