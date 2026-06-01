import type { ReadingLearnerStructureDto } from './reading-authoring-api';

export type ReadingPaperPhase = 'partA' | 'partBC' | 'expired';

export interface ReadingPaperDeadlineInput {
  partADeadlineAt: string;
  partBCDeadlineAt: string;
}

export interface ReadingPaperBookletPage {
  id: string;
  label: string;
  partCode: 'A' | 'B' | 'C';
  textIds: string[];
  questionIds: string[];
  kind: 'texts' | 'questions' | 'mixed';
}

export function getReadingPaperPhase(deadlines: ReadingPaperDeadlineInput, nowMs: number): ReadingPaperPhase {
  if (nowMs <= new Date(deadlines.partADeadlineAt).getTime()) return 'partA';
  if (nowMs <= new Date(deadlines.partBCDeadlineAt).getTime()) return 'partBC';
  return 'expired';
}

export function buildPartABookletPages(structure: ReadingLearnerStructureDto): ReadingPaperBookletPage[] {
  const partA = structure.parts.find((part) => part.partCode === 'A');
  if (!partA) return [];
  return partA.texts.map((text) => ({
    id: `part-a-text-${text.id}`,
    label: `Text ${text.displayOrder}`,
    partCode: 'A' as const,
    textIds: [text.id],
    questionIds: [],
    kind: 'texts' as const,
  }));
}

export function buildPartBCBookletPages(structure: ReadingLearnerStructureDto): ReadingPaperBookletPage[] {
  const pages: ReadingPaperBookletPage[] = [];
  const partB = structure.parts.find((part) => part.partCode === 'B');
  const partC = structure.parts.find((part) => part.partCode === 'C');

  // Part B: a single stacked page. Each of the (six) short workplace extracts
  // is paired with its own single 3-option question; the learner scrolls all
  // extract/question pairs on one page instead of paging through them.
  if (partB && (partB.texts.length > 0 || partB.questions.length > 0)) {
    pages.push({
      id: 'part-b',
      label: 'Part B',
      partCode: 'B',
      textIds: partB.texts.map((text) => text.id),
      questionIds: partB.questions.map((question) => question.id),
      kind: 'mixed',
    });
  }

  pages.push(...buildPartCPagePairs(structure));
  return pages;
}

/**
 * Part C: one combined page per long text. Each page carries the passage and
 * its (eight) four-option questions so the player can render the passage on the
 * left with the questions stacked on the right, rather than splitting the text
 * and its questions onto separate pages.
 */
export function buildPartCPagePairs(structure: ReadingLearnerStructureDto): ReadingPaperBookletPage[] {
  const partC = structure.parts.find((part) => part.partCode === 'C');
  if (!partC) return [];

  return partC.texts.map((text) => ({
    id: `part-c-${text.id}`,
    label: `Part C text ${text.displayOrder}`,
    partCode: 'C' as const,
    textIds: [text.id],
    questionIds: partC.questions
      .filter((question) => question.readingTextId === text.id)
      .map((question) => question.id),
    kind: 'mixed' as const,
  }));
}

export function formatWallTimer(seconds: number): string {
  const minutes = Math.max(0, Math.floor(seconds / 60));
  const remainingSeconds = Math.max(0, seconds % 60);
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}