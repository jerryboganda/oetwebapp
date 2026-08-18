export type ReadingExamCategoryId =
  | 'anna-hartford'
  | 'atlas-practice-series'
  | 'jayden-book'
  | 'nova-practice-series'
  | 'very-difficult-reading-exams'
  | 'other';

export interface ReadingExamCategory {
  id: Exclude<ReadingExamCategoryId, 'other'>;
  title: string;
  description: string;
  matchers: string[];
}

/**
 * Official Full Reading Exam book folders. Keep this list in screenshot order
 * from the complete Reading library (Anna Hartford → Very Difficult).
 * Do not invent extra series names.
 */
export const READING_EXAM_CATEGORIES: ReadingExamCategory[] = [
  {
    id: 'anna-hartford',
    title: 'Anna Hartford',
    description: 'Anna Hartford Reading papers.',
    matchers: ['anna-hartford', 'anna hartford'],
  },
  {
    id: 'atlas-practice-series',
    title: 'Atlas Practice Series',
    description: 'Atlas Practice Series Reading papers.',
    matchers: ['atlas-practice-series', 'atlas practice series', 'atlas-practice'],
  },
  {
    id: 'jayden-book',
    title: 'Jayden Book',
    description: 'Jayden Book official Reading papers.',
    matchers: ['jayden-book', 'jayden book'],
  },
  {
    id: 'nova-practice-series',
    title: 'Nova Practice Series',
    description: 'Nova Practice Series Reading papers.',
    matchers: ['nova-practice-series', 'nova practice series', 'nova-practice'],
  },
  {
    id: 'very-difficult-reading-exams',
    title: 'VERY DIFFICULT READING EXAMS',
    description: 'High-difficulty Reading papers.',
    matchers: ['very-difficult-reading-exams', 'very difficult reading exams', 'very-difficult'],
  },
];

export interface ReadingExamCategoryPaper {
  slug?: string | null;
  title?: string | null;
  tagsCsv?: string | null;
}

function paperHaystack(paper: ReadingExamCategoryPaper): string {
  return [paper.tagsCsv, paper.slug, paper.title]
    .filter((value): value is string => Boolean(value && value.trim()))
    .join(' ')
    .toLowerCase()
    .replace(/[_]+/g, '-');
}

export function resolveReadingExamCategoryId(
  paper: ReadingExamCategoryPaper,
): ReadingExamCategoryId {
  const haystack = paperHaystack(paper);
  for (const category of READING_EXAM_CATEGORIES) {
    if (category.matchers.some((matcher) => haystack.includes(matcher))) {
      return category.id;
    }
  }
  return 'other';
}

export interface ReadingExamCategorySection<T extends ReadingExamCategoryPaper> {
  id: ReadingExamCategoryId;
  title: string;
  description: string;
  papers: T[];
}

export function groupReadingExamPapers<T extends ReadingExamCategoryPaper>(
  papers: T[],
): ReadingExamCategorySection<T>[] {
  const grouped = new Map<ReadingExamCategoryId, T[]>();
  for (const category of READING_EXAM_CATEGORIES) {
    grouped.set(category.id, []);
  }
  grouped.set('other', []);

  for (const paper of papers) {
    grouped.get(resolveReadingExamCategoryId(paper))!.push(paper);
  }

  const sections: ReadingExamCategorySection<T>[] = READING_EXAM_CATEGORIES.map((category) => ({
    id: category.id,
    title: category.title,
    description: category.description,
    papers: grouped.get(category.id) ?? [],
  }));

  const other = grouped.get('other') ?? [];
  if (other.length > 0) {
    sections.push({
      id: 'other',
      title: 'Other papers',
      description: 'Published Reading papers that are not in a named book series yet.',
      papers: other,
    });
  }

  return sections;
}
