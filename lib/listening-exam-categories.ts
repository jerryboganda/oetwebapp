import { compareExamSeriesPapers } from './exam-series-order';

export type ListeningExamCategoryId = 'atlas-practice-series' | 'nova-practice-series' | 'other';

export interface ListeningExamCategory {
  id: Exclude<ListeningExamCategoryId, 'other'>;
  title: string;
  description: string;
  matchers: string[];
}

/**
 * Learner Listening series folders. Atlas and Nova only — do not copy
 * Anna / Jayden / Very Difficult Reading folders into Listening.
 */
export const LISTENING_EXAM_CATEGORIES: ListeningExamCategory[] = [
  {
    id: 'atlas-practice-series',
    title: 'Atlas Practice Series',
    description: 'Atlas Practice Series Listening papers.',
    matchers: ['atlas-practice-series', 'atlas practice series', 'atlas-practice'],
  },
  {
    id: 'nova-practice-series',
    title: 'Nova Practice Series',
    description: 'Nova Practice Series Listening papers.',
    matchers: ['nova-practice-series', 'nova practice series', 'nova-practice'],
  },
];

export interface ListeningExamCategoryPaper {
  slug?: string | null;
  title?: string | null;
  tagsCsv?: string | null;
}

function paperHaystack(paper: ListeningExamCategoryPaper): string {
  return [paper.tagsCsv, paper.slug, paper.title]
    .filter((value): value is string => Boolean(value && value.trim()))
    .join(' ')
    .toLowerCase()
    .replace(/[_]+/g, '-');
}

export function resolveListeningExamCategoryId(
  paper: ListeningExamCategoryPaper,
): ListeningExamCategoryId {
  const haystack = paperHaystack(paper);
  for (const category of LISTENING_EXAM_CATEGORIES) {
    if (category.matchers.some((matcher) => haystack.includes(matcher))) {
      return category.id;
    }
  }
  return 'other';
}

export interface ListeningExamCategorySection<T extends ListeningExamCategoryPaper> {
  id: Exclude<ListeningExamCategoryId, 'other'>;
  title: string;
  description: string;
  papers: T[];
}

export function groupListeningExamPapers<T extends ListeningExamCategoryPaper>(
  papers: T[],
): ListeningExamCategorySection<T>[] {
  const grouped = new Map<Exclude<ListeningExamCategoryId, 'other'>, T[]>();
  for (const category of LISTENING_EXAM_CATEGORIES) {
    grouped.set(category.id, []);
  }

  for (const paper of papers) {
    const categoryId = resolveListeningExamCategoryId(paper);
    if (categoryId === 'other') continue;
    grouped.get(categoryId)!.push(paper);
  }

  // Each folder is sorted independently, ascending by the exam number already
  // written into the title. Safe to sort in place: these arrays were built by
  // this function, never handed in by the caller.
  return LISTENING_EXAM_CATEGORIES.map((category) => ({
    id: category.id,
    title: category.title,
    description: category.description,
    papers: (grouped.get(category.id) ?? []).sort(compareExamSeriesPapers(category.matchers)),
  }));
}
