/**
 * Rulebook Loader
 *
 * Loads the canonical JSON rulebooks from `rulebooks/` and exposes a
 * lookup interface. Loads are deterministic and cached per
 * (kind, profession) tuple.
 *
 * The TypeScript engine mirrors the .NET engine — keep behaviour identical.
 */

// --- Writing rulebooks (13 professions) ---------------------------------
import writingMedicineV1 from '../../rulebooks/writing/medicine/rulebook.v1.json';
import writingNursingV1 from '../../rulebooks/writing/nursing/rulebook.v1.json';
import writingDentistryV1 from '../../rulebooks/writing/dentistry/rulebook.v1.json';
import writingPharmacyV1 from '../../rulebooks/writing/pharmacy/rulebook.v1.json';
import writingPhysiotherapyV1 from '../../rulebooks/writing/physiotherapy/rulebook.v1.json';
import writingVeterinaryV1 from '../../rulebooks/writing/veterinary/rulebook.v1.json';
import writingOptometryV1 from '../../rulebooks/writing/optometry/rulebook.v1.json';
import writingRadiographyV1 from '../../rulebooks/writing/radiography/rulebook.v1.json';
import writingOccupationalTherapyV1 from '../../rulebooks/writing/occupational-therapy/rulebook.v1.json';
import writingSpeechPathologyV1 from '../../rulebooks/writing/speech-pathology/rulebook.v1.json';
import writingPodiatryV1 from '../../rulebooks/writing/podiatry/rulebook.v1.json';
import writingDieteticsV1 from '../../rulebooks/writing/dietetics/rulebook.v1.json';
import writingOtherAlliedHealthV1 from '../../rulebooks/writing/other-allied-health/rulebook.v1.json';

// --- Speaking rulebooks (6 UI professions) ------------------------------
import speakingMedicineV1 from '../../rulebooks/speaking/medicine/rulebook.v1.json';
import speakingNursingV1 from '../../rulebooks/speaking/nursing/rulebook.v1.json';
import speakingDentistryV1 from '../../rulebooks/speaking/dentistry/rulebook.v1.json';
import speakingPharmacyV1 from '../../rulebooks/speaking/pharmacy/rulebook.v1.json';
import speakingPhysiotherapyV1 from '../../rulebooks/speaking/physiotherapy/rulebook.v1.json';
import speakingOtherAlliedHealthV1 from '../../rulebooks/speaking/other-allied-health/rulebook.v1.json';

// --- Listening rulebooks (4 professions: authoring-shape rules) ---------
import listeningMedicineV1 from '../../rulebooks/listening/medicine/rulebook.v1.json';
import listeningNursingV1 from '../../rulebooks/listening/nursing/rulebook.v1.json';
import listeningDentistryV1 from '../../rulebooks/listening/dentistry/rulebook.v1.json';
import listeningPhysiotherapyV1 from '../../rulebooks/listening/physiotherapy/rulebook.v1.json';
import listeningExamModeV1 from '../../rulebooks/listening/_exam-mode/rulebook.v1.json';

// --- Reading rulebooks (2 professions: authoring-shape rules) -----------
import readingMedicineV1 from '../../rulebooks/reading/medicine/rulebook.v1.json';
import readingNursingV1 from '../../rulebooks/reading/nursing/rulebook.v1.json';
import readingExamModeV1 from '../../rulebooks/reading/_exam-mode/rulebook.v1.json';

// --- Grammar rulebooks (6 UI professions) -------------------------------
import grammarMedicineV1 from '../../rulebooks/grammar/medicine/rulebook.v1.json';
import grammarNursingV1 from '../../rulebooks/grammar/nursing/rulebook.v1.json';
import grammarDentistryV1 from '../../rulebooks/grammar/dentistry/rulebook.v1.json';
import grammarPharmacyV1 from '../../rulebooks/grammar/pharmacy/rulebook.v1.json';
import grammarPhysiotherapyV1 from '../../rulebooks/grammar/physiotherapy/rulebook.v1.json';
import grammarOtherAlliedHealthV1 from '../../rulebooks/grammar/other-allied-health/rulebook.v1.json';

// --- Vocabulary rulebooks (6 UI professions) ----------------------------
import vocabularyMedicineV1 from '../../rulebooks/vocabulary/medicine/rulebook.v1.json';
import vocabularyNursingV1 from '../../rulebooks/vocabulary/nursing/rulebook.v1.json';
import vocabularyDentistryV1 from '../../rulebooks/vocabulary/dentistry/rulebook.v1.json';
import vocabularyPharmacyV1 from '../../rulebooks/vocabulary/pharmacy/rulebook.v1.json';
import vocabularyPhysiotherapyV1 from '../../rulebooks/vocabulary/physiotherapy/rulebook.v1.json';
import vocabularyOtherAlliedHealthV1 from '../../rulebooks/vocabulary/other-allied-health/rulebook.v1.json';

// --- Pronunciation rulebooks (8 professions) ----------------------------
import pronunciationMedicineV1 from '../../rulebooks/pronunciation/medicine/rulebook.v1.json';
import pronunciationNursingV1 from '../../rulebooks/pronunciation/nursing/rulebook.v1.json';
import pronunciationDentistryV1 from '../../rulebooks/pronunciation/dentistry/rulebook.v1.json';
import pronunciationPharmacyV1 from '../../rulebooks/pronunciation/pharmacy/rulebook.v1.json';
import pronunciationPhysiotherapyV1 from '../../rulebooks/pronunciation/physiotherapy/rulebook.v1.json';
import pronunciationOccupationalTherapyV1 from '../../rulebooks/pronunciation/occupational-therapy/rulebook.v1.json';
import pronunciationOtherAlliedHealthV1 from '../../rulebooks/pronunciation/other-allied-health/rulebook.v1.json';
import pronunciationSpeechPathologyV1 from '../../rulebooks/pronunciation/speech-pathology/rulebook.v1.json';

// --- Conversation rulebooks (6 professions) -----------------------------
import conversationMedicineV1 from '../../rulebooks/conversation/medicine/rulebook.v1.json';
import conversationNursingV1 from '../../rulebooks/conversation/nursing/rulebook.v1.json';
import conversationDentistryV1 from '../../rulebooks/conversation/dentistry/rulebook.v1.json';
import conversationPharmacyV1 from '../../rulebooks/conversation/pharmacy/rulebook.v1.json';
import conversationPhysiotherapyV1 from '../../rulebooks/conversation/physiotherapy/rulebook.v1.json';
import conversationOtherAlliedHealthV1 from '../../rulebooks/conversation/other-allied-health/rulebook.v1.json';

// --- Remediation rulebooks (6 UI professions) ---------------------------
import remediationMedicineV1 from '../../rulebooks/remediation/medicine/rulebook.v1.json';
import remediationNursingV1 from '../../rulebooks/remediation/nursing/rulebook.v1.json';
import remediationDentistryV1 from '../../rulebooks/remediation/dentistry/rulebook.v1.json';
import remediationPharmacyV1 from '../../rulebooks/remediation/pharmacy/rulebook.v1.json';
import remediationPhysiotherapyV1 from '../../rulebooks/remediation/physiotherapy/rulebook.v1.json';
import remediationOtherAlliedHealthV1 from '../../rulebooks/remediation/other-allied-health/rulebook.v1.json';

// --- Assessment criteria (cross-domain rubric definitions) --------------
import writingAssessment from '../../rulebooks/writing/common/assessment-criteria.json';
import speakingAssessment from '../../rulebooks/speaking/common/assessment-criteria.json';
import grammarAssessment from '../../rulebooks/grammar/common/assessment-criteria.json';
import vocabularyAssessment from '../../rulebooks/vocabulary/common/assessment-criteria.json';
import conversationAssessment from '../../rulebooks/conversation/common/assessment-criteria.json';
import pronunciationAssessment from '../../rulebooks/pronunciation/common/assessment-criteria.json';

import type {
  ExamProfession,
  Rulebook,
  RuleKind,
  Rule,
} from './types';

type RulebookKey = `${RuleKind}:${ExamProfession}`;

// Central registry — static imports so Next.js bundlers and
// Vitest both resolve at compile time. Extend here when adding a
// profession's rulebook (e.g. writing/nursing/rulebook.v1.json).
const RULEBOOKS: Record<string, Rulebook> = {
  // Writing (13 professions)
  'writing:medicine': writingMedicineV1 as unknown as Rulebook,
  'writing:nursing': writingNursingV1 as unknown as Rulebook,
  'writing:dentistry': writingDentistryV1 as unknown as Rulebook,
  'writing:pharmacy': writingPharmacyV1 as unknown as Rulebook,
  'writing:physiotherapy': writingPhysiotherapyV1 as unknown as Rulebook,
  'writing:veterinary': writingVeterinaryV1 as unknown as Rulebook,
  'writing:optometry': writingOptometryV1 as unknown as Rulebook,
  'writing:radiography': writingRadiographyV1 as unknown as Rulebook,
  'writing:occupational-therapy': writingOccupationalTherapyV1 as unknown as Rulebook,
  'writing:speech-pathology': writingSpeechPathologyV1 as unknown as Rulebook,
  'writing:podiatry': writingPodiatryV1 as unknown as Rulebook,
  'writing:dietetics': writingDieteticsV1 as unknown as Rulebook,
  'writing:other-allied-health': writingOtherAlliedHealthV1 as unknown as Rulebook,
  // Speaking (6 UI professions — dentistry + pharmacy authored 2026-05-27)
  'speaking:medicine': speakingMedicineV1 as unknown as Rulebook,
  'speaking:nursing': speakingNursingV1 as unknown as Rulebook,
  'speaking:dentistry': speakingDentistryV1 as unknown as Rulebook,
  'speaking:pharmacy': speakingPharmacyV1 as unknown as Rulebook,
  'speaking:physiotherapy': speakingPhysiotherapyV1 as unknown as Rulebook,
  'speaking:other-allied-health': speakingOtherAlliedHealthV1 as unknown as Rulebook,
  // Listening (4 professions + exam-mode UX rulebook)
  'listening:medicine': listeningMedicineV1 as unknown as Rulebook,
  'listening:nursing': listeningNursingV1 as unknown as Rulebook,
  'listening:dentistry': listeningDentistryV1 as unknown as Rulebook,
  'listening:physiotherapy': listeningPhysiotherapyV1 as unknown as Rulebook,
  // The exam-mode rulebook encodes the candidate-facing UX rules from
  // the Listening PDF (timing, locks, highlighting, technical requirements).
  // Profession is sentinel 'medicine' because R01.10 makes Listening profession-agnostic.
  'listening-exam-mode:medicine': listeningExamModeV1 as unknown as Rulebook,
  // Reading (2 professions + exam-mode UX rulebook)
  'reading:medicine': readingMedicineV1 as unknown as Rulebook,
  'reading:nursing': readingNursingV1 as unknown as Rulebook,
  'reading-exam-mode:medicine': readingExamModeV1 as unknown as Rulebook,
  // Grammar (6 UI professions)
  'grammar:medicine': grammarMedicineV1 as unknown as Rulebook,
  'grammar:nursing': grammarNursingV1 as unknown as Rulebook,
  'grammar:dentistry': grammarDentistryV1 as unknown as Rulebook,
  'grammar:pharmacy': grammarPharmacyV1 as unknown as Rulebook,
  'grammar:physiotherapy': grammarPhysiotherapyV1 as unknown as Rulebook,
  'grammar:other-allied-health': grammarOtherAlliedHealthV1 as unknown as Rulebook,
  // Vocabulary (6 UI professions)
  'vocabulary:medicine': vocabularyMedicineV1 as unknown as Rulebook,
  'vocabulary:nursing': vocabularyNursingV1 as unknown as Rulebook,
  'vocabulary:dentistry': vocabularyDentistryV1 as unknown as Rulebook,
  'vocabulary:pharmacy': vocabularyPharmacyV1 as unknown as Rulebook,
  'vocabulary:physiotherapy': vocabularyPhysiotherapyV1 as unknown as Rulebook,
  'vocabulary:other-allied-health': vocabularyOtherAlliedHealthV1 as unknown as Rulebook,
  // Pronunciation (8 professions)
  'pronunciation:medicine': pronunciationMedicineV1 as unknown as Rulebook,
  'pronunciation:nursing': pronunciationNursingV1 as unknown as Rulebook,
  'pronunciation:dentistry': pronunciationDentistryV1 as unknown as Rulebook,
  'pronunciation:pharmacy': pronunciationPharmacyV1 as unknown as Rulebook,
  'pronunciation:physiotherapy': pronunciationPhysiotherapyV1 as unknown as Rulebook,
  'pronunciation:occupational-therapy': pronunciationOccupationalTherapyV1 as unknown as Rulebook,
  'pronunciation:other-allied-health': pronunciationOtherAlliedHealthV1 as unknown as Rulebook,
  'pronunciation:speech-pathology': pronunciationSpeechPathologyV1 as unknown as Rulebook,
  // Conversation (6 professions)
  'conversation:medicine': conversationMedicineV1 as unknown as Rulebook,
  'conversation:nursing': conversationNursingV1 as unknown as Rulebook,
  'conversation:dentistry': conversationDentistryV1 as unknown as Rulebook,
  'conversation:pharmacy': conversationPharmacyV1 as unknown as Rulebook,
  'conversation:physiotherapy': conversationPhysiotherapyV1 as unknown as Rulebook,
  'conversation:other-allied-health': conversationOtherAlliedHealthV1 as unknown as Rulebook,
  // Remediation (6 UI professions)
  'remediation:medicine': remediationMedicineV1 as unknown as Rulebook,
  'remediation:nursing': remediationNursingV1 as unknown as Rulebook,
  'remediation:dentistry': remediationDentistryV1 as unknown as Rulebook,
  'remediation:pharmacy': remediationPharmacyV1 as unknown as Rulebook,
  'remediation:physiotherapy': remediationPhysiotherapyV1 as unknown as Rulebook,
  'remediation:other-allied-health': remediationOtherAlliedHealthV1 as unknown as Rulebook,
};

const ASSESSMENT_CRITERIA: Partial<Record<RuleKind, unknown>> = {
  writing: writingAssessment as unknown,
  speaking: speakingAssessment as unknown,
  grammar: grammarAssessment as unknown,
  vocabulary: vocabularyAssessment as unknown,
  conversation: conversationAssessment as unknown,
  pronunciation: pronunciationAssessment as unknown,
};

export class RulebookNotFoundError extends Error {
  constructor(kind: RuleKind, profession: ExamProfession) {
    super(
      `OET rulebook not found for kind="${kind}" profession="${profession}". ` +
        `Only rulebooks listed in lib/rulebook/loader.ts are available; add the ` +
        `JSON file under rulebooks/${kind}/${profession}/ and register it in the ` +
        `loader before using it.`,
    );
    this.name = 'RulebookNotFoundError';
  }
}

export function loadRulebook(kind: RuleKind, profession: ExamProfession): Rulebook {
  const key: RulebookKey = `${kind}:${profession}`;
  const book = RULEBOOKS[key];
  if (!book) throw new RulebookNotFoundError(kind, profession);
  return book;
}

export function listRulebooks(): Array<Pick<Rulebook, 'kind' | 'profession' | 'version'>> {
  return Object.values(RULEBOOKS).map((book) => ({
    kind: book.kind,
    profession: book.profession,
    version: book.version,
  }));
}

export function findRule(
  kind: RuleKind,
  profession: ExamProfession,
  ruleId: string,
): Rule | undefined {
  const book = loadRulebook(kind, profession);
  return book.rules.find((r) => r.id === ruleId);
}

export function getAssessmentCriteria(kind: RuleKind): unknown {
  return ASSESSMENT_CRITERIA[kind];
}

/** Filter rules that apply to a given letter type or card type. */
export function rulesApplicableTo(
  book: Rulebook,
  context: string,
): Rule[] {
  return book.rules.filter((rule) => {
    if (!rule.appliesTo || rule.appliesTo === 'all') return true;
    if (Array.isArray(rule.appliesTo)) return rule.appliesTo.includes(context);
    return false;
  });
}

/** Convenience getter for the critical rules only (used by AI grounding prompts). */
export function criticalRules(book: Rulebook): Rule[] {
  return book.rules.filter((r) => r.severity === 'critical');
}
