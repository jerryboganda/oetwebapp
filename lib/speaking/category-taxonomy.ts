/**
 * FINAL 2026-09-09 — Speaking card taxonomy (candidate-visible).
 *
 * Distinct from the hidden admin `SpeakingCardType` (scorer guidance, never
 * serialized to learners): these nine primary categories ARE the main
 * catalogue filter and may appear as chips on learner cards.
 *
 * The deterministic §8B classification engine (priority rules, regex
 * patterns, negation/historical-context guards) now lives ONLY in the
 * backend — `backend/src/OetLearner.Api/Services/Speaking/SpeakingCardClassifier.cs`.
 * This file used to carry a byte-for-byte port of that engine so a "Suggest"
 * button in the admin editor could classify a draft card client-side; that
 * created a two-implementation risk (a regex fix here could silently diverge
 * from the server). The editor now calls the authenticated
 * `POST /v1/admin/speaking/role-play-cards/classification-preview` endpoint
 * instead, so the server is the only classification authority. This file
 * keeps just the shared taxonomy constants/types the UI still needs for
 * labels and dropdown options.
 */

export const SPEAKING_PRIMARY_CATEGORIES = [
  'First Visit',
  'Second Visit / Follow-up',
  'Already Known Patient',
  'Examination Card',
  'Emergency / Emergency Department',
  'Breaking Bad News',
  'Angry Patient',
  'Reluctant Patient',
  'Other Cards',
] as const;

export type SpeakingPrimaryCategory = (typeof SPEAKING_PRIMARY_CATEGORIES)[number];

export const SPEAKING_BEHAVIOURAL_TAGS = ['Breaking Bad News', 'Angry', 'Reluctant'] as const;

export type SpeakingBehaviouralTag = (typeof SPEAKING_BEHAVIOURAL_TAGS)[number];

export interface SpeakingCardClassifiable {
  scenarioTitle?: string | null;
  setting?: string | null;
  background?: string | null;
  tasks?: string[] | null;
  clinicalTopic?: string | null;
  patientEmotion?: string | null;
  patientName?: string | null;
  candidateRole?: string | null;
  interlocutorRole?: string | null;
  communicationGoal?: string | null;
}

export interface SpeakingCardClassification {
  primary: SpeakingPrimaryCategory;
  secondaryTags: SpeakingBehaviouralTag[];
  /** True when the card fell through to Other Cards — needs human review. */
  needsReview: boolean;
}

/** Options for the catalogue category filter, in display order. */
export function speakingCategoryFilterOptions(): { id: string; label: string }[] {
  return SPEAKING_PRIMARY_CATEGORIES.map((category) => ({ id: category, label: category }));
}

export function isSpeakingPrimaryCategory(value: string): value is SpeakingPrimaryCategory {
  return (SPEAKING_PRIMARY_CATEGORIES as readonly string[]).includes(value);
}
