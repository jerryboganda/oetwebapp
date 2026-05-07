/**
 * Typed wrappers for the admin endpoints the Mock Builder Wizard orchestrates.
 *
 * These all hit existing endpoints under /v1/admin/papers and
 * /v1/admin/mock-bundles. CSRF + auth + retry are handled by `apiClient`.
 *
 * No new backend endpoints are introduced here — the wizard is purely an
 * orchestration UI over admin surfaces that already ship.
 */

import { apiClient } from '@/lib/api';
import type {
  ContentPaperAssetAttachDto,
  ContentPaperAssetDto,
  ContentPaperCreateDto,
  ContentPaperDto,
  PaperAssetRole,
} from '@/lib/content-upload-api';

// ── Listening structure ────────────────────────────────────────────────

export type ListeningPartCode = 'A1' | 'A2' | 'B' | 'C1' | 'C2';
export type ListeningQuestionType = 'short_answer' | 'multiple_choice_3';

export interface ListeningAuthoredQuestion {
  id: string;
  number: number;
  partCode: ListeningPartCode | string;
  type: ListeningQuestionType;
  stem: string;
  options?: string[];
  correctAnswer: string;
  acceptedAnswers?: string[];
  explanation?: string | null;
  skillTag?: string | null;
  transcriptExcerpt?: string | null;
  distractorExplanation?: string | null;
  points: number;
}

export interface ListeningAuthoredQuestionList {
  questions: ListeningAuthoredQuestion[];
  counts?: Record<string, number>;
}

// ── Reading structure (manifest import) ────────────────────────────────

export type ReadingPartCode = 'A' | 'B' | 'C';

/** Mirrors `ReadingQuestionType` enum on the backend. */
export type ReadingQuestionType =
  | 'WordPool'
  | 'ShortAnswer'
  | 'MultipleChoice4'
  | 'MultipleChoice3'
  | 'TrueFalseNotGiven';

export interface ReadingTextManifest {
  displayOrder: number;
  title: string;
  source?: string | null;
  bodyHtml: string;
  wordCount: number;
  topicTag?: string | null;
}

export interface ReadingQuestionManifest {
  displayOrder: number;
  points: number;
  questionType: ReadingQuestionType;
  stem: string;
  optionsJson: string;
  correctAnswerJson: string;
  acceptedSynonymsJson?: string | null;
  caseSensitive: boolean;
  explanationMarkdown?: string | null;
  skillTag?: string | null;
  readingTextDisplayOrder?: number | null;
}

export interface ReadingPartManifest {
  partCode: ReadingPartCode;
  timeLimitMinutes?: number | null;
  instructions?: string | null;
  texts: ReadingTextManifest[];
  questions: ReadingQuestionManifest[];
}

export interface ReadingStructureManifest {
  parts: ReadingPartManifest[];
}

// ── Writing structure ──────────────────────────────────────────────────

export interface WritingStructurePayload {
  taskPrompt?: string;
  letterType?: string;
  taskDate?: string;
  writerRole?: string;
  recipient?: string;
  purpose?: string;
  caseNotes?: string;
  modelAnswerText?: string;
}

// ── Speaking structure ─────────────────────────────────────────────────

export interface SpeakingStructurePayload {
  candidateCard?: {
    candidateRole?: string;
    setting?: string;
    patientRole?: string;
    task?: string;
    background?: string;
    tasks?: string[];
  };
  interlocutorCard?: {
    patientProfile?: string;
    background?: string;
    hiddenInformation?: string;
    cuePrompts?: string[];
    privateNotes?: string;
  };
  warmUpQuestions?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocus?: string[];
}

// ── Wrappers ───────────────────────────────────────────────────────────

export const fetchAdminContentPaper = (paperId: string) =>
  apiClient.get<ContentPaperDto>(`/v1/admin/papers/${encodeURIComponent(paperId)}`);

export const createPaperDraft = (input: ContentPaperCreateDto) =>
  apiClient.post<ContentPaperDto>('/v1/admin/papers', input);

export const attachPaperAsset = (
  paperId: string,
  body: ContentPaperAssetAttachDto,
) =>
  apiClient.post<ContentPaperAssetDto>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/assets`,
    body,
  );

export const setListeningStructure = (
  paperId: string,
  questions: ListeningAuthoredQuestion[],
) =>
  apiClient.put<ListeningAuthoredQuestionList>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/listening/structure`,
    { questions },
  );

export const ensureReadingCanonical = (paperId: string) =>
  apiClient.post<void>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/reading/ensure-canonical`,
  );

export const importReadingManifest = (
  paperId: string,
  manifest: ReadingStructureManifest,
  replaceExisting = true,
) =>
  apiClient.post<unknown>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/reading/manifest`,
    { manifest, replaceExisting },
  );

export const setReadingPart = (
  paperId: string,
  partCode: ReadingPartCode,
  body: { timeLimitMinutes?: number | null; instructions?: string | null },
) =>
  apiClient.put<unknown>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/reading/parts/${partCode}`,
    body,
  );

export const setWritingStructure = (
  paperId: string,
  structure: WritingStructurePayload,
) =>
  apiClient.put<unknown>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/writing-structure`,
    { structure },
  );

export const setSpeakingStructure = (
  paperId: string,
  structure: SpeakingStructurePayload,
) =>
  apiClient.put<unknown>(
    `/v1/admin/papers/${encodeURIComponent(paperId)}/speaking-structure`,
    { structure },
  );

export const publishPaper = (paperId: string) =>
  apiClient.post<void>(`/v1/admin/papers/${encodeURIComponent(paperId)}/publish`);

export const getRequiredRoles = (subtest: string) =>
  apiClient.get<{ subtest: string; required: PaperAssetRole[] }>(
    `/v1/admin/papers/required-roles/${encodeURIComponent(subtest)}`,
  );
