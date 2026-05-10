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

  partB?.texts.forEach((text) => {
    pages.push({
      id: `part-b-${text.id}`,
      label: `Part B extract ${text.displayOrder}`,
      partCode: 'B',
      textIds: [text.id],
      questionIds: partB.questions.filter((question) => question.readingTextId === text.id).map((question) => question.id),
      kind: 'mixed',
    });
  });

  pages.push(...buildPartCPagePairs(structure));
  return pages;
}

export function buildPartCPagePairs(structure: ReadingLearnerStructureDto): ReadingPaperBookletPage[] {
  const partC = structure.parts.find((part) => part.partCode === 'C');
  if (!partC) return [];

  return partC.texts.flatMap((text) => {
    const questionIds = partC.questions
      .filter((question) => question.readingTextId === text.id)
      .map((question) => question.id);
    return [
      {
        id: `part-c-text-${text.id}`,
        label: `Part C text ${text.displayOrder}`,
        partCode: 'C' as const,
        textIds: [text.id],
        questionIds: [],
        kind: 'texts' as const,
      },
      {
        id: `part-c-questions-${text.id}`,
        label: `Part C questions ${text.displayOrder}`,
        partCode: 'C' as const,
        textIds: [],
        questionIds,
        kind: 'questions' as const,
      },
    ];
  });
}

export function formatWallTimer(seconds: number): string {
  const minutes = Math.max(0, Math.floor(seconds / 60));
  const remainingSeconds = Math.max(0, seconds % 60);
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}