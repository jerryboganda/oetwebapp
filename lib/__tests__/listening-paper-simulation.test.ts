import { describe, expect, it } from 'vitest';
import type { ListeningSessionDto } from '@/lib/listening-api';
import {
  buildListeningBookletPages,
  buildPartANotesPages,
  buildPartBClipPages,
  buildPartCPresentationPages,
  formatListeningWallTimer,
  listeningBookletSections,
} from '@/lib/listening-paper-simulation';

type SessionShape = Pick<ListeningSessionDto, 'paper' | 'questions'>;

function makeSession(overrides: Partial<SessionShape> = {}): ListeningSessionDto {
  const base = {
    paper: {
      id: 'lp-001',
      sourceKind: 'content_paper',
      title: 'OET Listening Paper',
      slug: 'oet-listening-paper',
      difficulty: 'medium',
      estimatedDurationMinutes: 42,
      scenarioType: 'standard',
      audioUrl: 'https://cdn.example/audio.mp3',
      questionPaperUrl: null,
      audioAvailable: true,
      audioUnavailableReason: null,
      assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
      transcriptPolicy: 'per_item_post_attempt',
      extracts: [
        { partCode: 'A1', displayOrder: 1, kind: 'consultation', title: 'A1 extract', accentCode: 'en-GB', speakers: [], audioStartMs: 0, audioEndMs: 60_000 },
        { partCode: 'A2', displayOrder: 2, kind: 'consultation', title: 'A2 extract', accentCode: 'en-AU', speakers: [], audioStartMs: 70_000, audioEndMs: 130_000 },
        { partCode: 'B', displayOrder: 3, kind: 'workplace', title: 'B clip 1', accentCode: 'en-GB', speakers: [], audioStartMs: 140_000, audioEndMs: 160_000 },
        { partCode: 'B', displayOrder: 4, kind: 'workplace', title: 'B clip 2', accentCode: 'en-GB', speakers: [], audioStartMs: 160_000, audioEndMs: 180_000 },
        { partCode: 'C1', displayOrder: 5, kind: 'presentation', title: 'C1 extract', accentCode: 'en-GB', speakers: [], audioStartMs: 190_000, audioEndMs: 250_000 },
        { partCode: 'C2', displayOrder: 6, kind: 'presentation', title: 'C2 extract', accentCode: 'en-US', speakers: [], audioStartMs: 260_000, audioEndMs: 320_000 },
      ],
    },
    attempt: null,
    questions: [
      { id: 'q-a1', number: 1, partCode: 'A1', text: 'A1 note ____ blank', type: 'short_answer', options: [], points: 1 },
      { id: 'q-a2', number: 13, partCode: 'A2', text: 'A2 note ____ blank', type: 'short_answer', options: [], points: 1 },
      { id: 'q-b1', number: 25, partCode: 'B', text: 'B1 decision?', type: 'single_choice', options: ['Refer', 'Monitor', 'Discharge'], points: 1 },
      { id: 'q-b2', number: 26, partCode: 'B', text: 'B2 decision?', type: 'single_choice', options: ['Yes', 'No', 'Maybe'], points: 1 },
      { id: 'q-c1', number: 31, partCode: 'C1', text: 'C1 idea?', type: 'single_choice', options: ['A reason', 'B reason', 'C reason'], points: 1 },
      { id: 'q-c2', number: 39, partCode: 'C2', text: 'C2 idea?', type: 'single_choice', options: ['One', 'Two', 'Three'], points: 1 },
    ],
    modePolicy: {
      mode: 'paper',
      canPause: false,
      canScrub: false,
      onePlayOnly: true,
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
      presentationStyle: 'printable_booklet',
      printableBooklet: true,
      freeNavigation: true,
      unansweredWarningRequired: true,
      finalReviewAllPartsSeconds: 120,
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  } as unknown as ListeningSessionDto;

  return {
    ...base,
    ...overrides,
    paper: { ...base.paper, ...(overrides.paper ?? {}) },
  };
}

describe('buildListeningBookletPages', () => {
  it('produces an ordered A1/A2 → B → C1/C2 page set', () => {
    const pages = buildListeningBookletPages(makeSession());
    expect(pages.map((page) => page.section)).toEqual(['A1', 'A2', 'B', 'B', 'C1', 'C2']);
    expect(pages.map((page) => page.kind)).toEqual(['notes', 'notes', 'clip', 'clip', 'presentation', 'presentation']);
  });

  it('groups questions onto the correct section page', () => {
    const pages = buildListeningBookletPages(makeSession());
    const byId = Object.fromEntries(pages.map((page) => [page.id, page]));
    expect(byId['listening-part-a-a1'].questionIds).toEqual(['q-a1']);
    expect(byId['listening-part-a-a2'].questionIds).toEqual(['q-a2']);
    expect(byId['listening-part-c-c1'].questionIds).toEqual(['q-c1']);
    expect(byId['listening-part-c-c2'].questionIds).toEqual(['q-c2']);
  });

  it('splits Part B questions across one page per workplace extract', () => {
    const bPages = buildPartBClipPages(makeSession());
    expect(bPages).toHaveLength(2);
    expect(bPages[0].questionIds).toEqual(['q-b1']);
    expect(bPages[1].questionIds).toEqual(['q-b2']);
    expect(bPages[0].extract?.title).toBe('B clip 1');
    expect(bPages[1].extract?.title).toBe('B clip 2');
  });

  it('collapses Part B to a single page when no per-clip metadata is authored', () => {
    const session = makeSession({
      paper: {
        ...makeSession().paper,
        extracts: makeSession().paper.extracts!.filter((extract) => extract.partCode !== 'B'),
      },
    });
    const bPages = buildPartBClipPages(session);
    expect(bPages).toHaveLength(1);
    expect(bPages[0].questionIds).toEqual(['q-b1', 'q-b2']);
    expect(bPages[0].extract).toBeNull();
  });

  it('skips sections with no authored questions', () => {
    const session = makeSession({
      questions: makeSession().questions.filter((question) => question.partCode === 'A1'),
    });
    const pages = buildListeningBookletPages(session);
    expect(pages.map((page) => page.section)).toEqual(['A1']);
    expect(listeningBookletSections(pages)).toEqual(['A1']);
  });

  it('normalises legacy wide A / C part codes into A1/A2 and C1/C2 pages', () => {
    const session = makeSession({
      paper: { ...makeSession().paper, extracts: [] },
      questions: [
        { id: 'a-1', number: 1, partCode: 'A', text: 'A1 blank ____', type: 'short_answer', options: [], points: 1 },
        { id: 'a-2', number: 2, partCode: 'A', text: 'A2 blank ____', type: 'short_answer', options: [], points: 1 },
        { id: 'c-1', number: 31, partCode: 'C', text: 'C1 idea?', type: 'single_choice', options: ['A', 'B', 'C'], points: 1 },
        { id: 'c-2', number: 32, partCode: 'C', text: 'C2 idea?', type: 'single_choice', options: ['A', 'B', 'C'], points: 1 },
      ] as ListeningSessionDto['questions'],
    });
    const aPages = buildPartANotesPages(session);
    const cPages = buildPartCPresentationPages(session);
    expect(aPages.map((page) => page.section)).toEqual(['A1', 'A2']);
    expect(cPages.map((page) => page.section)).toEqual(['C1', 'C2']);
    expect(aPages[0].questionIds).toEqual(['a-1']);
    expect(aPages[1].questionIds).toEqual(['a-2']);
  });

  it('returns an empty page set for a paper with no questions', () => {
    const session = makeSession({ questions: [] });
    expect(buildListeningBookletPages(session)).toEqual([]);
  });
});

describe('formatListeningWallTimer', () => {
  it('formats minutes and zero-padded seconds', () => {
    expect(formatListeningWallTimer(0)).toBe('0:00');
    expect(formatListeningWallTimer(65)).toBe('1:05');
    expect(formatListeningWallTimer(600)).toBe('10:00');
  });

  it('clamps negative input to 0:00', () => {
    expect(formatListeningWallTimer(-30)).toBe('0:00');
  });
});
