/**
 * ============================================================================
 * OET Rulebook — Canonical Types
 * ============================================================================
 *
 * Shared types for the rulebook rule engine. Authored against Dr. Ahmed
 * Hesham's Writing and Speaking rulebooks, with profession-aware tagging
 * from day one so future professions (Nursing, Dentistry, etc.) drop in
 * without schema churn.
 *
 * This file is the TypeScript mirror of the JSON schema at
 * `rulebooks/schema/rulebook.schema.json`. Keep the two in sync.
 * ============================================================================
 */

export type RuleKind = 'writing' | 'speaking' | 'grammar' | 'pronunciation' | 'vocabulary' | 'conversation';

export type RuleSeverity = 'critical' | 'major' | 'minor' | 'info';

export type ExamProfession =
  | 'medicine'
  | 'nursing'
  | 'dentistry'
  | 'pharmacy'
  | 'physiotherapy'
  | 'veterinary'
  | 'optometry'
  | 'radiography'
  | 'occupational-therapy'
  | 'speech-pathology'
  | 'podiatry'
  | 'dietetics';

export type LetterType =
  | 'routine_referral'
  | 'urgent_referral'
  | 'discharge'
  | 'transfer'
  | 'non_medical_referral'
  | 'specialist_to_gp';

/**
 * The six official OET Writing task types. These are the ONLY valid task types
 * for authoring UI, scenario-type filters, and AI grounding. "advisory" and
 * "response" are NOT valid OET Writing task types and must never appear here.
 *
 * Source: Dr. Ahmed Hesham corrections + official OET Writing exam rules.
 */
export const WRITING_LETTER_TYPES: readonly LetterType[] = [
  'routine_referral',
  'urgent_referral',
  'discharge',
  'transfer',
  'non_medical_referral',
  'specialist_to_gp',
] as const;

/** Human-friendly labels for each canonical writing letter type. */
export const WRITING_LETTER_TYPE_LABELS: Readonly<Record<LetterType, string>> = {
  routine_referral: 'Routine Referral',
  urgent_referral: 'Urgent Referral',
  discharge: 'Discharge Letter',
  transfer: 'Transfer Letter',
  non_medical_referral: 'Referral to Non-Medical Professional',
  specialist_to_gp: 'Referral to GP',
} as const;

export type SpeakingCardType =
  | 'first_visit_routine'
  | 'first_visit_emergency'
  | 'follow_up'
  | 'examination'
  | 'already_known_patient'
  | 'breaking_bad_news';

/** Canonical 13-stage consultation stages (speaking). */
export type SpeakingTurnStage =
  | 'greeting'
  | 'opening'
  | 'listening'
  | 'empathy'
  | 'permission'
  | 'questions'
  | 'diagnosis'
  | 'causes'
  | 'lifestyle'
  | 'treatment'
  | 'reassurance'
  | 'recap'
  | 'closure';

/** A single rulebook rule — content-as-code, machine-checkable when `checkId` is set. */
export interface Rule {
  id: string; // e.g. "R03.4" or "RULE_27"
  section: string; // section id from rulebook (e.g. "03")
  title: string;
  body: string;
  severity: RuleSeverity;
  appliesTo?: 'all' | string[]; // letter types or card types
  turnStage?: SpeakingTurnStage;
  examples?: { good?: string[]; bad?: string[] };
  exemplarPhrases?: string[];
  forbiddenPatterns?: string[];
  checkId?: string; // engine detector to run for this rule
  params?: Record<string, unknown>;
}

export interface RulebookSection {
  id: string;
  title: string;
  order?: number;
}

export interface Rulebook {
  version: string;
  kind: RuleKind;
  profession: ExamProfession;
  publishedAt?: string;
  authoritySource?: string;
  sections: RulebookSection[];
  rules: Rule[];
  tables?: Record<string, unknown>;
  stateMachines?: Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// Lint findings
// ---------------------------------------------------------------------------

export interface LintFinding {
  ruleId: string;
  severity: RuleSeverity;
  message: string;
  quote?: string;
  start?: number;
  end?: number;
  fixSuggestion?: string;
}

export interface WritingLintInput {
  letterText: string;
  letterType: LetterType;
  recipientSpecialty?: string;
  recipientName?: string | null;
  patientAge?: number | null;
  patientIsMinor?: boolean;
  diagnosisKeywords?: string[];
  caseNotesMarkers?: {
    smokingMentioned?: boolean;
    drinkingMentioned?: boolean;
    allergyMentioned?: boolean;
    atopicCondition?: boolean;
    patientInitiatedReferral?: boolean;
    consentDocumented?: boolean;
    followUpDate?: string | null;
    resultsEnclosed?: boolean;
  };
  profession?: ExamProfession;
}

// ---------------------------------------------------------------------------
// Speaking audit
// ---------------------------------------------------------------------------

export interface SpeakingTurn {
  speaker: 'candidate' | 'patient' | 'interlocutor';
  text: string;
  startMs?: number;
  endMs?: number;
  stage?: SpeakingTurnStage;
}

export interface SpeakingAuditInput {
  transcript: SpeakingTurn[];
  cardType: SpeakingCardType;
  profession?: ExamProfession;
  /** Required for RULE_44 silence measurement (breaking_bad_news only). */
  silenceAfterDiagnosisMs?: number;
}

// ---------------------------------------------------------------------------
// AI prompt grounding
// ---------------------------------------------------------------------------

export interface AiGroundingContext {
  kind: RuleKind;
  profession: ExamProfession;
  /** Optional letter type / card type to narrow the rulebook section. */
  letterType?: LetterType;
  cardType?: SpeakingCardType;
  /** Task label for the prompt (e.g. "score", "coach", "correct"). */
  task: 'score' | 'coach' | 'correct' | 'summarise' | 'generate_feedback' | 'generate_content' | 'generate_grammar_lesson';
  /** Candidate country code — feeds into the pass threshold in Writing scoring. */
  candidateCountry?: string | null;
}

/**
 * Fully-assembled prompt payload that any AI call must use. Contains the
 * rulebook rules, the scoring threshold applicable to the candidate, the
 * reply format contract, and hard instructions that the AI must NOT
 * invent rules outside this rulebook.
 */
export interface AiGroundedPrompt {
  /** System prompt — rulebook grounding + mission invariants. */
  system: string;
  /** Short user-facing task instruction that the caller can prepend to content. */
  taskInstruction: string;
  /** Metadata echoed back to the caller for audit. */
  metadata: {
    rulebookVersion: string;
    rulebookKind: RuleKind;
    profession: ExamProfession;
    scoringPassMark: number;
    scoringGrade: 'B' | 'C+';
    appliedRulesCount: number;
  };
}
