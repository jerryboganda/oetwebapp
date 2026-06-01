import type {
  ListeningExtractMetadataDto,
  ListeningSessionDto,
  ListeningSessionQuestionDto,
} from './listening-api';
import {
  LISTENING_SECTION_LABEL,
  LISTENING_SECTION_SEQUENCE,
  computeListeningSectionMap,
  type ListeningSectionCode,
} from './listening-sections';

/**
 * Listening paper/booklet page model. The audio stays platform-controlled by
 * the FSM player; this builder only produces the ordered answer-booklet pages
 * the candidate writes on:
 *   - Part A1 / A2 → notes-with-gaps pages (one page per section)
 *   - Part B       → one clip page per workplace extract
 *   - Part C1 / C2 → one presentation page per section
 *
 * Pages are derived from `paper.extracts` + `questions` grouped by section.
 * Questions are bound to a page by their canonical `partCode` section, so the
 * page set is correct even for legacy wide-coded (`A` / `C`) papers — those
 * are normalised to A1/A2 and C1/C2 by `computeListeningSectionMap`.
 */

export type ListeningPaperPageKind = 'notes' | 'clip' | 'presentation';

export interface ListeningPaperBookletPage {
  id: string;
  label: string;
  /** Canonical section this page belongs to. */
  section: ListeningSectionCode;
  /** The extract metadata backing this page, when one is authored. */
  extract: ListeningExtractMetadataDto | null;
  /** Question ids printed on this booklet page, in number order. */
  questionIds: string[];
  kind: ListeningPaperPageKind;
}

function pageKindForSection(section: ListeningSectionCode): ListeningPaperPageKind {
  if (section === 'A1' || section === 'A2') return 'notes';
  if (section === 'B') return 'clip';
  return 'presentation';
}

/** Questions for a section, in ascending number order, keyed by the section map. */
function questionsForSection(
  questions: ListeningSessionQuestionDto[],
  sectionMap: Map<string, ListeningSectionCode>,
  section: ListeningSectionCode,
): ListeningSessionQuestionDto[] {
  return questions
    .filter((question) => sectionMap.get(question.id) === section)
    .sort((a, b) => a.number - b.number);
}

/** Part A notes-with-gaps pages — one page per authored A1 / A2 section. */
export function buildPartANotesPages(session: ListeningSessionDto): ListeningPaperBookletPage[] {
  const questions = session.questions ?? [];
  const extracts = session.paper.extracts ?? [];
  const sectionMap = computeListeningSectionMap(questions);
  const pages: ListeningPaperBookletPage[] = [];

  for (const section of ['A1', 'A2'] as const) {
    const sectionQuestions = questionsForSection(questions, sectionMap, section);
    if (sectionQuestions.length === 0) continue;
    const extract = extracts.find((row) => row.partCode === section) ?? null;
    pages.push({
      id: `listening-part-a-${section.toLowerCase()}`,
      label: extract?.title ? `${LISTENING_SECTION_LABEL[section]} — ${extract.title}` : LISTENING_SECTION_LABEL[section],
      section,
      extract,
      questionIds: sectionQuestions.map((question) => question.id),
      kind: 'notes',
    });
  }

  return pages;
}

/** Part B clip pages — one OMR clip page per authored workplace extract. */
export function buildPartBClipPages(session: ListeningSessionDto): ListeningPaperBookletPage[] {
  const questions = session.questions ?? [];
  const extracts = session.paper.extracts ?? [];
  const sectionMap = computeListeningSectionMap(questions);
  const sectionQuestions = questionsForSection(questions, sectionMap, 'B');
  if (sectionQuestions.length === 0) return [];

  const bExtracts = extracts
    .filter((row) => row.partCode === 'B')
    .sort((a, b) => a.displayOrder - b.displayOrder);

  // Without authored per-clip metadata Part B collapses to a single clip page
  // carrying every Part B question (mirrors how the FSM treats Part B as one
  // section). With metadata we split the questions evenly across the clips so
  // each printed page maps to one workplace extract.
  if (bExtracts.length <= 1) {
    const extract = bExtracts[0] ?? null;
    return [
      {
        id: 'listening-part-b-1',
        label: extract?.title ? `${LISTENING_SECTION_LABEL.B} — ${extract.title}` : LISTENING_SECTION_LABEL.B,
        section: 'B',
        extract,
        questionIds: sectionQuestions.map((question) => question.id),
        kind: 'clip',
      },
    ];
  }

  const perClip = Math.ceil(sectionQuestions.length / bExtracts.length);
  return bExtracts.map((extract, index) => {
    const slice = sectionQuestions.slice(index * perClip, (index + 1) * perClip);
    return {
      id: `listening-part-b-${extract.displayOrder}`,
      label: extract.title ? `Part B clip ${index + 1} — ${extract.title}` : `Part B clip ${index + 1}`,
      section: 'B' as const,
      extract,
      questionIds: slice.map((question) => question.id),
      kind: 'clip' as const,
    };
  });
}

/** Part C presentation pages — one page per authored C1 / C2 section. */
export function buildPartCPresentationPages(session: ListeningSessionDto): ListeningPaperBookletPage[] {
  const questions = session.questions ?? [];
  const extracts = session.paper.extracts ?? [];
  const sectionMap = computeListeningSectionMap(questions);
  const pages: ListeningPaperBookletPage[] = [];

  for (const section of ['C1', 'C2'] as const) {
    const sectionQuestions = questionsForSection(questions, sectionMap, section);
    if (sectionQuestions.length === 0) continue;
    const extract = extracts.find((row) => row.partCode === section) ?? null;
    pages.push({
      id: `listening-part-c-${section.toLowerCase()}`,
      label: extract?.title ? `${LISTENING_SECTION_LABEL[section]} — ${extract.title}` : LISTENING_SECTION_LABEL[section],
      section,
      extract,
      questionIds: sectionQuestions.map((question) => question.id),
      kind: 'presentation',
    });
  }

  return pages;
}

/**
 * Ordered booklet page set for the whole paper: A1/A2 notes pages, then Part B
 * clip pages, then C1/C2 presentation pages. Sections with no authored
 * questions are skipped so focus-filtered and partial papers still produce a
 * clean booklet.
 */
export function buildListeningBookletPages(session: ListeningSessionDto): ListeningPaperBookletPage[] {
  return [
    ...buildPartANotesPages(session),
    ...buildPartBClipPages(session),
    ...buildPartCPresentationPages(session),
  ];
}

/** Distinct, ordered sections represented in the booklet page set. */
export function listeningBookletSections(pages: ListeningPaperBookletPage[]): ListeningSectionCode[] {
  const present = new Set(pages.map((page) => page.section));
  return LISTENING_SECTION_SEQUENCE.filter((section) => present.has(section));
}

/** mm:ss wall-timer formatter — matches the Reading paper wall timer. */
export function formatListeningWallTimer(seconds: number): string {
  const minutes = Math.max(0, Math.floor(seconds / 60));
  const remainingSeconds = Math.max(0, seconds % 60);
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

export { pageKindForSection };
