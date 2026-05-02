/**
 * ============================================================================
 * Writing Drills — Types & Schemas
 * ============================================================================
 *
 * Drill types correspond to the OET Writing platform spec sections 12B + 12C:
 *   - relevance:    Case-note selection trainer (mark relevant / irrelevant)
 *   - opening:      Opening paragraph builder (purpose-first sentence)
 *   - ordering:     Paragraph ordering (logical sequencing for the reader)
 *   - expansion:    Sentence expansion (note form -> full sentence)
 *   - tone:         Formal-tone converter (informal -> professional register)
 *   - abbreviation: Abbreviation checker (when to expand vs keep)
 *
 * Drills are content-driven (JSON), graded deterministically client-side,
 * and never invoke the AI gateway. This keeps them safe to ship without
 * touching the mission-critical AI / scoring / rulebook contracts.
 * ============================================================================
 */

import { z } from 'zod';

// ---------------------------------------------------------------------------
// Common
// ---------------------------------------------------------------------------

export const DrillTypeSchema = z.enum([
  'relevance',
  'opening',
  'ordering',
  'expansion',
  'tone',
  'abbreviation',
]);
export type DrillType = z.infer<typeof DrillTypeSchema>;

export const ProfessionSchema = z.enum([
  'medicine',
  'nursing',
  'pharmacy',
  'physiotherapy',
  'dentistry',
  'occupational_therapy',
  'radiography',
  'podiatry',
  'dietetics',
  'optometry',
  'speech_pathology',
  'veterinary',
]);
export type Profession = z.infer<typeof ProfessionSchema>;

export const LetterTypeSchema = z.enum([
  'referral',
  'urgent_referral',
  'discharge',
  'transfer',
  'advice',
  'update',
  'non_medical_referral',
]);
export type LetterType = z.infer<typeof LetterTypeSchema>;

export const DrillDifficultySchema = z.enum(['intro', 'core', 'exam']);
export type DrillDifficulty = z.infer<typeof DrillDifficultySchema>;

const DrillBaseSchema = z.object({
  id: z.string().min(1),
  type: DrillTypeSchema,
  profession: ProfessionSchema,
  letterType: LetterTypeSchema.optional(),
  title: z.string().min(1),
  brief: z.string().min(1),
  difficulty: DrillDifficultySchema,
  estimatedMinutes: z.number().int().positive().max(20),
  // Cross-references back to the canonical rulebook so feedback can cite a rule.
  rulebookRefs: z.array(z.string()).default([]),
});

// ---------------------------------------------------------------------------
// 1. Relevance drill
// ---------------------------------------------------------------------------

export const CaseNoteSchema = z.object({
  id: z.string().min(1),
  category: z.string().min(1),
  text: z.string().min(1),
  // The authored ground truth; never sent to the learner UI.
  expected: z.enum(['relevant', 'irrelevant', 'optional']),
  rationale: z.string().min(1),
});
export type CaseNote = z.infer<typeof CaseNoteSchema>;

export const RelevanceDrillSchema = DrillBaseSchema.extend({
  type: z.literal('relevance'),
  scenario: z.object({
    patient: z.string().min(1),
    writerRole: z.string().min(1),
    recipientRole: z.string().min(1),
    purpose: z.string().min(1),
  }),
  notes: z.array(CaseNoteSchema).min(8).max(40),
});
export type RelevanceDrill = z.infer<typeof RelevanceDrillSchema>;

// ---------------------------------------------------------------------------
// 2. Opening paragraph builder
// ---------------------------------------------------------------------------

export const OpeningChoiceSchema = z.object({
  id: z.string().min(1),
  text: z.string().min(1),
  // 'best' = strong opening; 'acceptable' = passable; 'weak' = wrong purpose / register.
  quality: z.enum(['best', 'acceptable', 'weak']),
  rationale: z.string().min(1),
  flags: z
    .array(z.enum(['unclear_purpose', 'wrong_reader', 'informal_tone', 'too_long', 'note_form']))
    .default([]),
});
export type OpeningChoice = z.infer<typeof OpeningChoiceSchema>;

export const OpeningDrillSchema = DrillBaseSchema.extend({
  type: z.literal('opening'),
  scenario: z.object({
    patient: z.string().min(1),
    writerRole: z.string().min(1),
    recipientRole: z.string().min(1),
    purpose: z.string().min(1),
  }),
  choices: z.array(OpeningChoiceSchema).min(3).max(6),
});
export type OpeningDrill = z.infer<typeof OpeningDrillSchema>;

// ---------------------------------------------------------------------------
// 3. Paragraph ordering
// ---------------------------------------------------------------------------

export const OrderingItemSchema = z.object({
  id: z.string().min(1),
  text: z.string().min(1),
  role: z.enum(['opening', 'current', 'history', 'request', 'closing']),
});
export type OrderingItem = z.infer<typeof OrderingItemSchema>;

export const OrderingDrillSchema = DrillBaseSchema.extend({
  type: z.literal('ordering'),
  brief: z.string().min(1),
  items: z.array(OrderingItemSchema).min(3).max(8),
  // Authored target order, expressed as item IDs.
  expectedOrder: z.array(z.string()).min(3).max(8),
});
export type OrderingDrill = z.infer<typeof OrderingDrillSchema>;

// ---------------------------------------------------------------------------
// 4. Sentence expansion (note form -> full sentence)
// ---------------------------------------------------------------------------

export const ExpansionTargetSchema = z.object({
  id: z.string().min(1),
  noteForm: z.string().min(1),
  // Required tokens (case-insensitive) that a passable expansion should contain
  // (e.g. patient name, key clinical fact, tense/marker word).
  mustInclude: z.array(z.string().min(1)).min(1),
  // Substrings that should NOT appear (e.g. note-form abbreviations, '-', '/').
  mustNotInclude: z.array(z.string()).default([]),
  exemplar: z.string().min(1),
  rationale: z.string().min(1),
});
export type ExpansionTarget = z.infer<typeof ExpansionTargetSchema>;

export const ExpansionDrillSchema = DrillBaseSchema.extend({
  type: z.literal('expansion'),
  targets: z.array(ExpansionTargetSchema).min(1).max(8),
});
export type ExpansionDrill = z.infer<typeof ExpansionDrillSchema>;

// ---------------------------------------------------------------------------
// 5. Formal tone converter
// ---------------------------------------------------------------------------

export const ToneItemSchema = z.object({
  id: z.string().min(1),
  informal: z.string().min(1),
  // One of these substrings (case-insensitive) must appear in a passing answer.
  acceptableFormal: z.array(z.string().min(1)).min(1),
  // Tokens that signal residual informality and should be removed.
  forbidden: z.array(z.string().min(1)).default([]),
  exemplar: z.string().min(1),
  rationale: z.string().min(1),
});
export type ToneItem = z.infer<typeof ToneItemSchema>;

export const ToneDrillSchema = DrillBaseSchema.extend({
  type: z.literal('tone'),
  items: z.array(ToneItemSchema).min(1).max(10),
});
export type ToneDrill = z.infer<typeof ToneDrillSchema>;

// ---------------------------------------------------------------------------
// 6. Abbreviation checker
// ---------------------------------------------------------------------------

export const AbbreviationItemSchema = z.object({
  id: z.string().min(1),
  abbreviation: z.string().min(1),
  context: z.string().min(1), // e.g. "writing to a community pharmacist"
  expected: z.enum(['expand', 'keep']),
  expansion: z.string().min(1), // canonical expansion either way
  rationale: z.string().min(1),
});
export type AbbreviationItem = z.infer<typeof AbbreviationItemSchema>;

export const AbbreviationDrillSchema = DrillBaseSchema.extend({
  type: z.literal('abbreviation'),
  items: z.array(AbbreviationItemSchema).min(3).max(20),
});
export type AbbreviationDrill = z.infer<typeof AbbreviationDrillSchema>;

// ---------------------------------------------------------------------------
// Discriminated union
// ---------------------------------------------------------------------------

export const DrillSchema = z.discriminatedUnion('type', [
  RelevanceDrillSchema,
  OpeningDrillSchema,
  OrderingDrillSchema,
  ExpansionDrillSchema,
  ToneDrillSchema,
  AbbreviationDrillSchema,
]);
export type Drill = z.infer<typeof DrillSchema>;

// Lightweight summary for index pages — strips authored answer keys.
export interface DrillSummary {
  id: string;
  type: DrillType;
  profession: Profession;
  letterType?: LetterType;
  title: string;
  brief: string;
  difficulty: DrillDifficulty;
  estimatedMinutes: number;
}

export function toDrillSummary(d: Drill): DrillSummary {
  return {
    id: d.id,
    type: d.type,
    profession: d.profession,
    letterType: d.letterType,
    title: d.title,
    brief: d.brief,
    difficulty: d.difficulty,
    estimatedMinutes: d.estimatedMinutes,
  };
}

// ---------------------------------------------------------------------------
// Submission + grade result types (shared by all drill types)
// ---------------------------------------------------------------------------

export type DrillSubmission =
  | { type: 'relevance'; selections: Record<string, 'relevant' | 'irrelevant'> }
  | { type: 'opening'; choiceId: string }
  | { type: 'ordering'; order: string[] }
  | { type: 'expansion'; answers: Record<string, string> }
  | { type: 'tone'; answers: Record<string, string> }
  | { type: 'abbreviation'; answers: Record<string, 'expand' | 'keep'> };

export interface DrillFindingPerItem {
  itemId: string;
  correct: boolean;
  feedback: string;
  rulebookRef?: string;
}

export interface DrillGradeResult {
  drillId: string;
  type: DrillType;
  /** 0..1 normalised score — UI converts to per-drill display. */
  score: number;
  /** Same value rounded to 0..100 for display. */
  scorePercent: number;
  /** Pass threshold for the drill: typically 0.7 for core, 0.8 for exam. */
  passed: boolean;
  /** Per-item breakdown — every drill produces at least one. */
  findings: DrillFindingPerItem[];
  /** High-level diagnostic tags (mirror of WRITING markdown sec 20 error tags). */
  errorTags: string[];
  /** Short overall coaching note — never substitutes for teacher feedback. */
  summary: string;
}
