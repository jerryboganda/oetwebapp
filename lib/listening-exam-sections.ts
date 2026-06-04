/**
 * Listening EXAM player section model.
 *
 * Distinct from `lib/listening-sections.ts` (owned by the legacy
 * `app/listening/player/[id]` diagnostic/mock player, which rolls Part B up
 * into a single "B" section). The restructured exam player at
 * `app/listening/paper/[paperId]` treats every Part B extract as its own
 * navigable sub-section, giving ten forward-only sub-sections in order:
 *
 *   A1 → A2 → B1 → B2 → B3 → B4 → B5 → B6 → C1 → C2
 *
 * Each sub-section carries its own audio file, its own countdown timer, and
 * its own questions. The ordered sequence is DERIVED from the session DTO
 * (`paper.extracts` + `questions`, both keyed by `partCode`) rather than being
 * hard-coded, so a paper that only authors a subset of sub-sections still
 * plays the ones it has, in canonical order.
 */

import type {
  ListeningExtractMetadataDto,
  ListeningExtractPartCode,
  ListeningSessionDto,
  ListeningSessionQuestionDto,
} from '@/lib/listening-api';

/** The ten exam sub-section part codes, in forward-only play order. */
export type ListeningExamPartCode =
  | 'A1' | 'A2'
  | 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6'
  | 'C1' | 'C2';

export const LISTENING_EXAM_PART_SEQUENCE: readonly ListeningExamPartCode[] = [
  'A1', 'A2', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'C1', 'C2',
] as const;

/** Default per-sub-section countdown (seconds) when the paper authors none. */
export const LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS = 90;

/** Stable canonical ordering rank for a part code (lower sorts first). */
export function listeningExamPartOrder(partCode: string): number {
  const index = (LISTENING_EXAM_PART_SEQUENCE as readonly string[]).indexOf(partCode);
  return index === -1 ? Number.MAX_SAFE_INTEGER : index;
}

const PART_LABEL: Record<ListeningExamPartCode, string> = {
  A1: 'Part A — Extract 1',
  A2: 'Part A — Extract 2',
  B1: 'Part B — Extract 1',
  B2: 'Part B — Extract 2',
  B3: 'Part B — Extract 3',
  B4: 'Part B — Extract 4',
  B5: 'Part B — Extract 5',
  B6: 'Part B — Extract 6',
  C1: 'Part C — Extract 1',
  C2: 'Part C — Extract 2',
};

/**
 * One resolved exam sub-section: its part code, audio URL + countdown, the
 * questions that belong to it, and pre-resolved presentation metadata.
 */
export interface ListeningExamSubSection {
  /** Zero-based position in the derived ordered sequence (== one-way cursor). */
  index: number;
  partCode: ListeningExamPartCode;
  label: string;
  title: string;
  /** Per-sub-section audio URL (uploaded `/v1/media` or TTS `.wav`); may be null. */
  audioUrl: string | null;
  /** True when the audio URL is an authenticated `/v1/media/{id}/content` URL. */
  audioRequiresAuth: boolean;
  /** Resolved countdown for this sub-section (already defaulted). */
  timeLimitSeconds: number;
  questions: ListeningSessionQuestionDto[];
  extract: ListeningExtractMetadataDto | null;
}

/**
 * Normalise a raw extract/question part code onto one of the ten exam sub-section
 * codes. The backend already emits A1/A2/B1..B6/C1/C2, but legacy/JSON papers
 * may still surface a bare "A", "B", or "C" — floor those to the first
 * sub-section of their part so a not-yet-split paper still appears.
 */
export function normalizeExamPartCode(
  raw: string | null | undefined,
): ListeningExamPartCode | null {
  if (!raw) return null;
  const code = raw.trim().toUpperCase();
  if ((LISTENING_EXAM_PART_SEQUENCE as readonly string[]).includes(code)) {
    return code as ListeningExamPartCode;
  }
  if (code === 'A') return 'A1';
  if (code === 'B') return 'B1';
  if (code === 'C') return 'C1';
  return null;
}

/** A `/v1/media/{id}/content` URL needs the Bearer token; a TTS `.wav` does not. */
export function listeningAudioRequiresAuth(audioUrl: string | null | undefined): boolean {
  if (!audioUrl) return false;
  return audioUrl.includes('/v1/media/');
}

/**
 * Build the ordered list of exam sub-sections from a session DTO.
 *
 * A sub-section is INCLUDED when it has at least one question OR an authored
 * extract (so an audio-only intro extract still shows, and a question group
 * with no extract metadata still plays). Sub-sections are returned in the
 * canonical A1→…→C2 order regardless of the DTO's array order, and their
 * `index` is the contiguous one-way cursor the advance-section endpoint expects.
 */
export function buildListeningExamSubSections(
  session: Pick<ListeningSessionDto, 'paper' | 'questions'>,
): ListeningExamSubSection[] {
  const extractByPart = new Map<ListeningExamPartCode, ListeningExtractMetadataDto>();
  for (const extract of session.paper.extracts ?? []) {
    const code = normalizeExamPartCode(extract.partCode);
    if (!code) continue;
    // First-wins within a part code (extracts are pre-ordered by displayOrder).
    if (!extractByPart.has(code)) extractByPart.set(code, extract);
  }

  const questionsByPart = new Map<ListeningExamPartCode, ListeningSessionQuestionDto[]>();
  for (const question of session.questions) {
    const code = normalizeExamPartCode(question.partCode);
    if (!code) continue;
    const bucket = questionsByPart.get(code);
    if (bucket) bucket.push(question);
    else questionsByPart.set(code, [question]);
  }
  for (const bucket of questionsByPart.values()) {
    bucket.sort((a, b) => a.number - b.number);
  }

  const sections: ListeningExamSubSection[] = [];
  for (const partCode of LISTENING_EXAM_PART_SEQUENCE) {
    const extract = extractByPart.get(partCode) ?? null;
    const questions = questionsByPart.get(partCode) ?? [];
    if (!extract && questions.length === 0) continue;

    const audioUrl = extract?.audioUrl ?? null;
    const authoredLimit = extract?.timeLimitSeconds ?? null;
    sections.push({
      index: sections.length,
      partCode,
      label: PART_LABEL[partCode],
      title: extract?.title?.trim() || PART_LABEL[partCode],
      audioUrl,
      audioRequiresAuth: listeningAudioRequiresAuth(audioUrl),
      timeLimitSeconds: authoredLimit && authoredLimit > 0
        ? authoredLimit
        : LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS,
      questions,
      extract,
    });
  }

  return sections;
}
