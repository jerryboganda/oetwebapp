/**
 * Writing Task Builder — local draft state model, UI label maps, and DTO
 * conversion helpers.
 *
 * The builder works on a flat, fully-populated `WritingTaskFormState` so every
 * form control is controlled and never `undefined`. We convert to/from the
 * contract DTOs (`WritingTaskUpsertDto` / `WritingTaskDto`) at the API
 * boundary, and to/from the `WritingTaskImportJson` envelope for import/export.
 *
 * Contract: lib/writing/types.ts (do NOT edit). Spec §3/§4/§5/§6/§18/§19.2.
 *
 * NOTE: the contract exports value constants only for professions
 * (`WRITING_PROFESSIONS`, `WRITING_PROFESSION_LABELS`). Letter-type /
 * simulation-mode / marking-mode are types with no runtime label maps, so we
 * define the UI maps here (this file is ours to own; types.ts is not).
 */

import type {
  WritingTaskDto,
  WritingTaskUpsertDto,
  WritingTaskImportJson,
  WritingProfession,
  WritingLetterType,
  WritingSimulationMode,
  WritingMarkingMode,
} from '@/lib/writing/types';

// ── UI label maps + option lists (owned here; not in the contract) ──────────

export const WRITING_LETTER_TYPES: WritingLetterType[] = [
  'LT-RR',
  'LT-UR',
  'LT-DG',
  'LT-TR',
  'LT-NM',
  'LT-RP',
];

export const WRITING_LETTER_TYPE_LABELS: Record<WritingLetterType, string> = {
  'LT-RR': 'Routine referral',
  'LT-UR': 'Urgent referral',
  'LT-DG': 'Discharge',
  'LT-TR': 'Transfer',
  'LT-NM': 'Non-medical referral',
  'LT-RP': 'Reply / response',
};

export const WRITING_SIMULATION_MODES: WritingSimulationMode[] = [
  'paper',
  'computer',
  'both',
];

export const WRITING_SIMULATION_MODE_LABELS: Record<WritingSimulationMode, string> = {
  paper: 'Paper',
  computer: 'Computer',
  both: 'Both (paper & computer)',
};

export const WRITING_MARKING_MODES: WritingMarkingMode[] = [
  'tutor',
  'ai_assisted',
  'double',
];

export const WRITING_MARKING_MODE_LABELS: Record<WritingMarkingMode, string> = {
  tutor: 'Tutor only',
  ai_assisted: 'AI-assisted',
  double: 'Double marking',
};

/** Standard fixed instruction lines seeded into a new task (spec §3). */
export const WRITING_DEFAULT_FIXED_INSTRUCTIONS: string[] = [
  'Expand the relevant notes into complete sentences',
  'Do not use note form',
  'Use letter format',
  'The body of the letter should be approximately 180-200 words',
];

export const DEFAULT_WORD_GUIDE_MIN = 180;
export const DEFAULT_WORD_GUIDE_MAX = 200;

export interface WritingTaskFormState {
  // Metadata
  title: string;
  internalCode: string;
  profession: WritingProfession;
  letterType: WritingLetterType;
  difficulty: number;
  simulationModes: WritingSimulationMode;
  markingMode: WritingMarkingMode;
  sourceProvenance: string;
  integrityAcknowledged: boolean;
  // Task prompt
  taskPromptMarkdown: string;
  writerRole: string;
  todayDate: string;
  expectedPurpose: string;
  expectedAction: string;
  // Case Notes PDF (shown on the LEFT during writing) — formerly "stimulus PDF"
  stimulusPdfMediaAssetId: string | null;
  // Answer Sheet PDF (shown on the results page after submission)
  answerSheetPdfMediaAssetId: string | null;
  // Word guide
  wordGuideMin: number;
  wordGuideMax: number;
  fixedInstructions: string[];
}

/** A blank form for "new" mode, optionally seeded. */
export function emptyFormState(seed?: Partial<WritingTaskFormState>): WritingTaskFormState {
  return {
    title: '',
    internalCode: '',
    profession: 'medicine',
    letterType: 'LT-RR',
    difficulty: 3,
    simulationModes: 'both',
    markingMode: 'tutor',
    sourceProvenance: '',
    integrityAcknowledged: false,
    taskPromptMarkdown: '',
    writerRole: '',
    todayDate: '',
    expectedPurpose: '',
    expectedAction: '',
    stimulusPdfMediaAssetId: null,
    answerSheetPdfMediaAssetId: null,
    wordGuideMin: DEFAULT_WORD_GUIDE_MIN,
    wordGuideMax: DEFAULT_WORD_GUIDE_MAX,
    fixedInstructions: [...WRITING_DEFAULT_FIXED_INSTRUCTIONS],
    ...seed,
  };
}

/** Hydrate the form from a fetched task DTO (fills nullable fields safely). */
export function formStateFromDto(dto: WritingTaskDto): WritingTaskFormState {
  return {
    title: dto.title ?? '',
    internalCode: dto.internalCode ?? '',
    profession: dto.profession,
    letterType: dto.letterType,
    difficulty: dto.difficulty ?? 3,
    simulationModes: dto.simulationModes,
    markingMode: dto.markingMode,
    sourceProvenance: dto.sourceProvenance ?? '',
    integrityAcknowledged: true, // already-saved tasks carry an acknowledgement
    taskPromptMarkdown: dto.taskPromptMarkdown ?? '',
    writerRole: dto.writerRole ?? '',
    todayDate: dto.todayDate ?? '',
    expectedPurpose: dto.expectedPurpose ?? '',
    expectedAction: dto.expectedAction ?? '',
    stimulusPdfMediaAssetId: dto.stimulusPdfMediaAssetId ?? null,
    answerSheetPdfMediaAssetId: dto.answerSheetPdfMediaAssetId ?? null,
    wordGuideMin: dto.wordGuideMin ?? DEFAULT_WORD_GUIDE_MIN,
    wordGuideMax: dto.wordGuideMax ?? DEFAULT_WORD_GUIDE_MAX,
    fixedInstructions:
      dto.fixedInstructions && dto.fixedInstructions.length > 0
        ? [...dto.fixedInstructions]
        : [...WRITING_DEFAULT_FIXED_INSTRUCTIONS],
  };
}

/** Serialise the form to the upsert DTO sent to the API. */
export function formStateToUpsert(form: WritingTaskFormState): WritingTaskUpsertDto {
  const trimOrNull = (v: string): string | null => {
    const t = v.trim();
    return t.length > 0 ? t : null;
  };

  return {
    internalCode: trimOrNull(form.internalCode),
    title: form.title.trim(),
    profession: form.profession,
    letterType: form.letterType,
    difficulty: form.difficulty,
    writerRole: trimOrNull(form.writerRole),
    todayDate: trimOrNull(form.todayDate),
    taskPromptMarkdown: form.taskPromptMarkdown,
    expectedPurpose: trimOrNull(form.expectedPurpose),
    expectedAction: trimOrNull(form.expectedAction),
    fixedInstructions: form.fixedInstructions
      .map((l) => l.trim())
      .filter((l) => l.length > 0),
    wordGuideMin: form.wordGuideMin,
    wordGuideMax: form.wordGuideMax,
    simulationModes: form.simulationModes,
    markingMode: form.markingMode,
    sourceProvenance: form.sourceProvenance.trim(),
    integrityAcknowledged: form.integrityAcknowledged,
    stimulusPdfMediaAssetId: form.stimulusPdfMediaAssetId ?? null,
    answerSheetPdfMediaAssetId: form.answerSheetPdfMediaAssetId ?? null,
  };
}

/**
 * Build the §18 export/import envelope from the current form. Used to download
 * a portable JSON file. Mirrors `WritingTaskImportJson`.
 */
export function formStateToImportJson(form: WritingTaskFormState): WritingTaskImportJson {
  const undefIfEmpty = (v: string): string | undefined => {
    const t = v.trim();
    return t.length > 0 ? t : undefined;
  };

  return {
    taskTitle: form.title.trim(),
    internalCode: undefIfEmpty(form.internalCode),
    profession: form.profession,
    taskType: form.letterType,
    caseNotes: {
      todayDate: undefIfEmpty(form.todayDate),
      candidateRole: undefIfEmpty(form.writerRole),
    },
    writingTask: {
      instruction: form.taskPromptMarkdown.trim(),
      fixedInstructions: form.fixedInstructions
        .map((l) => l.trim())
        .filter((l) => l.length > 0),
      wordGuide: { min: form.wordGuideMin, max: form.wordGuideMax },
    },
    marking: {
      expectedPurpose: undefIfEmpty(form.expectedPurpose),
      expectedAction: undefIfEmpty(form.expectedAction),
    },
  };
}
