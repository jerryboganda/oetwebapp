import { describe, expect, it } from 'vitest';
import {
  buildListeningExamSubSections,
  LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS,
  LISTENING_EXAM_PART_SEQUENCE,
  listeningAudioRequiresAuth,
  listeningExamPartOrder,
  normalizeExamPartCode,
} from './listening-exam-sections';
import type { ListeningSessionDto, ListeningSessionQuestionDto } from './listening-api';

function question(partCode: string, number: number): ListeningSessionQuestionDto {
  return { id: `q${number}`, number, partCode, text: `Q${number}`, type: 'short_answer', options: [], points: 1 };
}

function mcq(partCode: string, number: number): ListeningSessionQuestionDto {
  return { id: `q${number}`, number, partCode, text: `Q${number}`, type: 'multiple_choice_3', options: ['a', 'b', 'c'], points: 1 };
}

type Extract = NonNullable<ListeningSessionDto['paper']['extracts']>[number];

function extract(partCode: Extract['partCode'], overrides: Partial<Extract> = {}): Extract {
  return {
    partCode,
    displayOrder: 0,
    kind: 'consultation',
    title: `${partCode} extract`,
    accentCode: null,
    speakers: [],
    audioStartMs: null,
    audioEndMs: null,
    audioUrl: null,
    timeLimitSeconds: null,
    ...overrides,
  };
}

function session(
  extracts: Extract[],
  questions: ListeningSessionQuestionDto[],
): Pick<ListeningSessionDto, 'paper' | 'questions'> {
  return {
    paper: {
      id: 'p1',
      sourceKind: 'content_paper',
      title: 'Paper',
      slug: 'paper',
      difficulty: 'medium',
      estimatedDurationMinutes: 40,
      scenarioType: 'oet_listening',
      audioUrl: null,
      questionPaperUrl: null,
      audioAvailable: true,
      audioUnavailableReason: null,
      assetReadiness: { audio: true, questionPaper: false, answerKey: true, audioScript: false },
      transcriptPolicy: 'per_item_post_attempt',
      extracts,
    },
    questions,
  };
}

describe('listening-exam-sections', () => {
  it('orders the ten exam sub-sections A1 → A2 → B1..B6 → C1 → C2', () => {
    expect(LISTENING_EXAM_PART_SEQUENCE).toEqual([
      'A1', 'A2', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'C1', 'C2',
    ]);
  });

  it('derives sub-sections in canonical order regardless of DTO array order', () => {
    const s = session(
      // Deliberately scrambled input order.
      [extract('C2'), extract('A1'), extract('B3'), extract('A2')],
      [question('B3', 27), question('A1', 1), question('C2', 40), question('A2', 13)],
    );
    const sections = buildListeningExamSubSections(s);
    expect(sections.map((x) => x.partCode)).toEqual(['A1', 'A2', 'B3', 'C2']);
    // Index is the contiguous one-way cursor (0..n), not the canonical rank.
    expect(sections.map((x) => x.index)).toEqual([0, 1, 2, 3]);
  });

  it('includes a sub-section that has questions but no extract metadata', () => {
    const s = session([], [question('A1', 1), question('A1', 2)]);
    const sections = buildListeningExamSubSections(s);
    expect(sections).toHaveLength(1);
    expect(sections[0].partCode).toBe('A1');
    expect(sections[0].questions).toHaveLength(2);
    expect(sections[0].audioUrl).toBeNull();
  });

  it('includes an audio-only sub-section that has an extract but no questions', () => {
    const s = session([extract('A1', { audioUrl: '/v1/listening/audio/abc.wav' })], []);
    const sections = buildListeningExamSubSections(s);
    expect(sections).toHaveLength(1);
    expect(sections[0].questions).toHaveLength(0);
    expect(sections[0].audioUrl).toBe('/v1/listening/audio/abc.wav');
  });

  it('sorts questions within a sub-section by question number', () => {
    const s = session([extract('B1')], [mcq('B1', 26), mcq('B1', 25)]);
    const [b1] = buildListeningExamSubSections(s);
    expect(b1.questions.map((q) => q.number)).toEqual([25, 26]);
  });

  it('resolves the per-sub-section countdown, falling back to the default', () => {
    const s = session(
      [extract('A1', { timeLimitSeconds: 45 }), extract('A2', { timeLimitSeconds: null })],
      [question('A1', 1), question('A2', 13)],
    );
    const [a1, a2] = buildListeningExamSubSections(s);
    expect(a1.timeLimitSeconds).toBe(45);
    expect(a2.timeLimitSeconds).toBe(LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS);
  });

  it('flags uploaded media URLs as requiring auth and TTS wavs as anonymous', () => {
    const s = session(
      [
        extract('A1', { audioUrl: '/v1/media/abc123/content' }),
        extract('A2', { audioUrl: '/v1/listening/audio/def456.wav' }),
      ],
      [question('A1', 1), question('A2', 13)],
    );
    const [a1, a2] = buildListeningExamSubSections(s);
    expect(a1.audioRequiresAuth).toBe(true);
    expect(a2.audioRequiresAuth).toBe(false);
  });

  it('floors legacy bare part codes onto the first sub-section of their part', () => {
    expect(normalizeExamPartCode('A')).toBe('A1');
    expect(normalizeExamPartCode('B')).toBe('B1');
    expect(normalizeExamPartCode('C')).toBe('C1');
    expect(normalizeExamPartCode('b3')).toBe('B3');
    expect(normalizeExamPartCode('  C2 ')).toBe('C2');
    expect(normalizeExamPartCode('D')).toBeNull();
    expect(normalizeExamPartCode(null)).toBeNull();
  });

  it('exposes a stable canonical ordering rank', () => {
    expect(listeningExamPartOrder('A1')).toBeLessThan(listeningExamPartOrder('B1'));
    expect(listeningExamPartOrder('B6')).toBeLessThan(listeningExamPartOrder('C1'));
    expect(listeningExamPartOrder('ZZ')).toBe(Number.MAX_SAFE_INTEGER);
  });

  it('treats a missing audio url as not requiring auth', () => {
    expect(listeningAudioRequiresAuth(null)).toBe(false);
    expect(listeningAudioRequiresAuth(undefined)).toBe(false);
  });
});
