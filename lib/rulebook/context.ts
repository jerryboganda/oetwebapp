import type { LetterType, SpeakingCardType, WritingLintInput } from './types';
import {
  fromEngineLetterType,
  type CanonicalLetterType,
} from '@/lib/writing/letter-types';

type MinimalWritingTask = {
  title?: string | null;
  scenarioType?: string | null;
  caseNotes?: string | null;
};

function normalize(value: string | null | undefined): string {
  return (value ?? '').toLowerCase();
}

/**
 * Heuristic mapper from existing UI task metadata to canonical rulebook
 * letter types. This keeps the current mock/API task model usable until
 * backend content metadata grows a first-class `letterType` field.
 */
export function inferWritingLetterType(task: MinimalWritingTask): LetterType {
  const title = normalize(task.title);
  const scenario = normalize(task.scenarioType);
  const notes = normalize(task.caseNotes);
  const all = `${title}\n${scenario}\n${notes}`;

  if (/suspected cancer|query malignancy|rule out malignancy|abnormal mass|biopsy suspicious/.test(all)) {
    return 'urgent_referral';
  }
  if (/\burgent\b|asap|acute|immediate assessment|admit to hospital|hospital admission/.test(all)) {
    return 'urgent_referral';
  }
  if (/discharge|ready for discharge|discharged/.test(all)) {
    return 'discharge';
  }
  if (/transfer|icu|ward handover|handover/.test(all)) {
    return 'transfer';
  }
  if (/occupational therapist|occupational therapy|physiotherapist|physiotherapy|social worker|psychologist|dietitian|speech pathologist|speech pathology|care facility manager|pharmacist/.test(all)) {
    return 'non_medical_referral';
  }
  if (/specialist to gp|to gp|family dentist|gp follow-up|gp review/.test(all)) {
    return 'specialist_to_gp';
  }
  return 'routine_referral';
}

/**
 * Backend-canonical equivalent of {@link inferWritingLetterType}.
 *
 * Returns one of the six {@link CanonicalLetterType} codes that match the
 * backend `WritingContentStructure.LetterType` wire vocabulary. Use this
 * when the inferred type is going to be POSTed, persisted, or analysed —
 * NOT when it is being passed straight to the local rule engine (use
 * `inferWritingLetterType` for that).
 */
export function inferCanonicalLetterType(task: MinimalWritingTask): CanonicalLetterType {
  return fromEngineLetterType(inferWritingLetterType(task));
}

/**
 * Pull lightweight rulebook-relevant signals from case notes. This is not a
 * full semantic parser; it simply gives the linter enough structured hints to
 * enforce the highest-value writing rules in today's UI.
 */
export function deriveWritingCaseNotesMarkers(caseNotes: string | null | undefined): NonNullable<WritingLintInput['caseNotesMarkers']> {
  const text = normalize(caseNotes);
  const followUpMatch =
    caseNotes?.match(/follow[- ]?up\s*:?\s*([^\n.]+)/i) ??
    caseNotes?.match(/review\s*:?\s*([^\n.]+)/i) ??
    caseNotes?.match(/appointment\s*:?\s*([^\n.]+)/i);

  return {
    smokingMentioned: /smok|cigarette|tobacco/.test(text),
    drinkingMentioned: /\b(alcohol|drink(s|ing)?|units per week)\b/.test(text),
    allergyMentioned: /\b(allerg|nkda|nka)\b/.test(text),
    atopicCondition: /\b(asthma|eczema|hay fever|allergic rhinitis|atopic)\b/.test(text),
    patientInitiatedReferral: /\b(patient requested|upon (his|her) request|at .* request)\b/.test(text),
    consentDocumented: /\b(consent|fully informed|discussed with patient|safety plan completed)\b/.test(text),
    followUpDate: followUpMatch?.[1]?.trim() ?? null,
    resultsEnclosed: /\b(enclosed|attached|please find enclosed|copy of results|copy of imaging)\b/.test(text),
  };
}

/** Infer the speaking card type from page/task/transcript titles. */
export function inferSpeakingCardType(source: string | null | undefined): SpeakingCardType {
  const text = normalize(source);
  if (/cancer|bad news|serious diagnosis|malignanc|terminal/.test(text)) return 'breaking_bad_news';
  if (/examination|physical exam|examination card/.test(text)) return 'examination';
  if (/follow[- ]?up|review visit|return visit|test result/.test(text)) return 'follow_up';
  if (/already known|known patient|see you again|nice to see you again/.test(text)) return 'already_known_patient';
  if (/emergency|acute|er\b|ed\b|a&e|collapse|ambulance/.test(text)) return 'first_visit_emergency';
  return 'first_visit_routine';
}
