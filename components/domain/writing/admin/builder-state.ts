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
  WritingCaseNoteSectionDto,
  WritingContentChecklistItemDto,
  WritingModelAnswerParagraphDto,
  WritingRecipientDto,
  WritingProfession,
  WritingLetterType,
  WritingSimulationMode,
  WritingMarkingMode,
  WritingSeverity,
  WritingChecklistRequiredStatus,
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

// ── Stable client-side ids for list rows (DTO sections have no id) ──────────

export function newId(prefix = 'id'): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

/**
 * Case-note section as edited in the UI. The contract DTO is only
 * `{ heading, items }`; we add a transient `key` for stable React lists and
 * reordering. The key is stripped on serialise.
 */
export interface CaseNoteSectionDraft extends WritingCaseNoteSectionDto {
  key: string;
}

/** Checklist item with a guaranteed `id` for list rendering. */
export type ChecklistItemDraft = WritingContentChecklistItemDto;

/** Model-answer paragraph with a guaranteed `id` for list rendering. */
export type ModelAnswerParagraphDraft = WritingModelAnswerParagraphDto;

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
  // Recipient
  recipient: WritingRecipientDto;
  // Case notes
  caseNoteSections: CaseNoteSectionDraft[];
  // Stimulus PDF (optional real exam question-paper PDF)
  stimulusPdfMediaAssetId: string | null;
  // Word guide
  wordGuideMin: number;
  wordGuideMax: number;
  fixedInstructions: string[];
  // Model answer
  modelAnswerText: string;
  modelAnswerParagraphs: ModelAnswerParagraphDraft[];
  // Checklists
  keyContentChecklist: ChecklistItemDraft[];
  irrelevantContentChecklist: ChecklistItemDraft[];
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
    recipient: { name: '', role: '', organisation: '', address: '' },
    caseNoteSections: [],
    stimulusPdfMediaAssetId: null,
    wordGuideMin: DEFAULT_WORD_GUIDE_MIN,
    wordGuideMax: DEFAULT_WORD_GUIDE_MAX,
    fixedInstructions: [...WRITING_DEFAULT_FIXED_INSTRUCTIONS],
    modelAnswerText: '',
    modelAnswerParagraphs: [],
    keyContentChecklist: [],
    irrelevantContentChecklist: [],
    ...seed,
  };
}

function recipientFromDto(r: WritingRecipientDto | null): WritingRecipientDto {
  return {
    name: r?.name ?? '',
    role: r?.role ?? '',
    organisation: r?.organisation ?? '',
    address: r?.address ?? '',
  };
}

function normaliseChecklistItem(
  it: WritingContentChecklistItemDto,
  ordinal: number,
): ChecklistItemDraft {
  return {
    id: it.id || newId('chk'),
    itemText: it.itemText ?? '',
    category: it.category ?? '',
    importance: it.importance ?? 'medium',
    requiredStatus: it.requiredStatus ?? 'required',
    linkedCaseNoteSection: it.linkedCaseNoteSection ?? null,
    expectedRepresentation: it.expectedRepresentation ?? null,
    commonError: it.commonError ?? null,
    ordinal: typeof it.ordinal === 'number' ? it.ordinal : ordinal,
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
    recipient: recipientFromDto(dto.recipient),
    caseNoteSections: (dto.caseNoteSections ?? []).map((s) => ({
      key: newId('sec'),
      heading: s.heading ?? '',
      items: [...(s.items ?? [])],
    })),
    stimulusPdfMediaAssetId: dto.stimulusPdfMediaAssetId ?? null,
    wordGuideMin: dto.wordGuideMin ?? DEFAULT_WORD_GUIDE_MIN,
    wordGuideMax: dto.wordGuideMax ?? DEFAULT_WORD_GUIDE_MAX,
    fixedInstructions:
      dto.fixedInstructions && dto.fixedInstructions.length > 0
        ? [...dto.fixedInstructions]
        : [...WRITING_DEFAULT_FIXED_INSTRUCTIONS],
    modelAnswerText: dto.modelAnswerText ?? '',
    modelAnswerParagraphs: (dto.modelAnswerParagraphs ?? []).map((p) => ({
      id: p.id || newId('para'),
      text: p.text ?? '',
      rationale: p.rationale ?? '',
      criteria: [...(p.criteria ?? [])],
      included: [...(p.included ?? [])],
      excluded: [...(p.excluded ?? [])],
      languageNotes: p.languageNotes ?? '',
    })),
    keyContentChecklist: (dto.keyContentChecklist ?? []).map((it, i) =>
      normaliseChecklistItem(it, i),
    ),
    irrelevantContentChecklist: (dto.irrelevantContentChecklist ?? []).map((it, i) => ({
      ...normaliseChecklistItem(it, i),
      requiredStatus: 'irrelevant' as const,
    })),
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
    recipient: {
      name: form.recipient.name.trim(),
      role: form.recipient.role.trim(),
      organisation: (form.recipient.organisation ?? '').trim(),
      address: (form.recipient.address ?? '').trim(),
    },
    expectedPurpose: trimOrNull(form.expectedPurpose),
    expectedAction: trimOrNull(form.expectedAction),
    caseNoteSections: form.caseNoteSections.map((s) => ({
      heading: s.heading.trim(),
      items: s.items.map((i) => i.trim()).filter((i) => i.length > 0),
    })),
    fixedInstructions: form.fixedInstructions
      .map((l) => l.trim())
      .filter((l) => l.length > 0),
    wordGuideMin: form.wordGuideMin,
    wordGuideMax: form.wordGuideMax,
    simulationModes: form.simulationModes,
    markingMode: form.markingMode,
    modelAnswerText: form.modelAnswerText.trim(),
    modelAnswerParagraphs: form.modelAnswerParagraphs
      .filter((p) => p.text.trim().length > 0)
      .map((p) => ({
        id: p.id,
        text: p.text.trim(),
        rationale: (p.rationale ?? '').trim(),
        criteria: p.criteria ?? [],
        included: p.included ?? [],
        excluded: p.excluded ?? [],
        languageNotes: (p.languageNotes ?? '').trim(),
      })),
    keyContentChecklist: form.keyContentChecklist.map((it, i) => ({
      id: it.id,
      itemText: it.itemText.trim(),
      category: it.category.trim(),
      importance: it.importance,
      requiredStatus: it.requiredStatus,
      linkedCaseNoteSection: trimOrNull(it.linkedCaseNoteSection ?? ''),
      expectedRepresentation: trimOrNull(it.expectedRepresentation ?? ''),
      commonError: trimOrNull(it.commonError ?? ''),
      ordinal: i,
    })),
    irrelevantContentChecklist: form.irrelevantContentChecklist.map((it, i) => ({
      id: it.id,
      itemText: it.itemText.trim(),
      category: it.category.trim(),
      importance: it.importance,
      requiredStatus: 'irrelevant' as const,
      linkedCaseNoteSection: null,
      expectedRepresentation: null,
      commonError: trimOrNull(it.commonError ?? ''),
      ordinal: i,
    })),
    sourceProvenance: form.sourceProvenance.trim(),
    integrityAcknowledged: form.integrityAcknowledged,
    stimulusPdfMediaAssetId: form.stimulusPdfMediaAssetId ?? null,
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
      sections: form.caseNoteSections.map((s) => ({
        heading: s.heading.trim(),
        items: s.items.map((i) => i.trim()).filter((i) => i.length > 0),
      })),
    },
    writingTask: {
      instruction: form.taskPromptMarkdown.trim(),
      recipient: {
        name: form.recipient.name.trim(),
        role: form.recipient.role.trim(),
        organisation: (form.recipient.organisation ?? '').trim(),
        address: (form.recipient.address ?? '').trim(),
      },
      fixedInstructions: form.fixedInstructions
        .map((l) => l.trim())
        .filter((l) => l.length > 0),
      wordGuide: { min: form.wordGuideMin, max: form.wordGuideMax },
    },
    marking: {
      expectedPurpose: undefIfEmpty(form.expectedPurpose),
      expectedAction: undefIfEmpty(form.expectedAction),
      keyContentChecklist: form.keyContentChecklist.map((it) => ({
        itemText: it.itemText.trim(),
        category: it.category.trim(),
        importance: it.importance,
        requiredStatus: it.requiredStatus,
        linkedCaseNoteSection: it.linkedCaseNoteSection ?? undefined,
        expectedRepresentation: it.expectedRepresentation ?? undefined,
        commonError: it.commonError ?? undefined,
      })),
      irrelevantContentChecklist: form.irrelevantContentChecklist.map((it) => ({
        itemText: it.itemText.trim(),
        category: it.category.trim(),
        commonError: it.commonError ?? undefined,
      })),
      modelAnswer: undefIfEmpty(form.modelAnswerText),
    },
  };
}

/** Word count over a free-text body (used by preview + model answer helper). */
export function countWords(text: string): number {
  const trimmed = text.trim();
  if (!trimmed) return 0;
  return trimmed.split(/\s+/).length;
}

export function makeEmptyChecklistItem(
  requiredStatus: WritingChecklistRequiredStatus,
  ordinal: number,
): ChecklistItemDraft {
  const importance: WritingSeverity = requiredStatus === 'irrelevant' ? 'low' : 'medium';
  return {
    id: newId('chk'),
    itemText: '',
    category: '',
    importance,
    requiredStatus,
    linkedCaseNoteSection: null,
    expectedRepresentation: null,
    commonError: null,
    ordinal,
  };
}

export function makeEmptySection(): CaseNoteSectionDraft {
  return { key: newId('sec'), heading: '', items: [''] };
}

export function makeEmptyParagraph(): ModelAnswerParagraphDraft {
  return {
    id: newId('para'),
    text: '',
    rationale: '',
    criteria: [],
    included: [],
    excluded: [],
    languageNotes: '',
  };
}
