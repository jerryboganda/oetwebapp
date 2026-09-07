/**
 * FINAL 2026-09-06 — Speaking card taxonomy (candidate-visible).
 *
 * Distinct from the hidden admin `SpeakingCardType` (scorer guidance, never
 * serialized to learners): these nine primary categories ARE the main
 * catalogue filter and may appear as chips on learner cards.
 *
 * The deterministic priority rules in `classifySpeakingCard` implement §8B:
 * explicit completed-examination start → new ED arrival → already-known /
 * inpatient care → first visit → return/follow-up → behavioural
 * (Breaking Bad News / Angry / Reluctant) → Other Cards with a review flag.
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
}

export interface SpeakingCardClassification {
  primary: SpeakingPrimaryCategory;
  secondaryTags: SpeakingBehaviouralTag[];
  /** True when the card fell through to Other Cards — needs human review. */
  needsReview: boolean;
}

const examStartPattern =
  /you have (just|finished|completed|now finished)[^.\n]{0,60}examin/i;
const examOpeningPattern = /thank you for letting me examine you/i;

const emergencySettingPattern = /emergency department|\bED\b|emergency room|\bA&E\b/i;
const emergencyArrivalPattern =
  /just arriv|recently arriv|arrived (just |recently |with|to|at)|presents? (now|today|with|to|at)|just (came|came in|presented|walked in)|new arrival|brought in|rushed in|by ambulance|triaged/i;
const knownCarePattern =
  /for hours|for \d+ (hours|days)|observed (for|over)|under observation|already (known|managed|under|admitted)|under (our|your|hospital|their) care|managed (in|for|on the)|known to (us|the)|admit(ted)? (to|for|on)|transfer(red)? to.{0,30}(unit|ward|hospital|palliative)|palliative (unit|ward|care)|inpatient|in-patient|\bward\b|discharge|pre-?op(erative)?|post-?op|ICU|intensive care|surgery|operation|undergo(ing|ne)? (surgery|an? operation)|hospital stay/i;

const firstVisitPattern =
  /first (visit|time|presentation|attendance|consultation|appointment)|present(s|ed|ing)? for the first time|new patient|new referral|initial (visit|consultation|presentation|assessment)|never (seen|visited|attended) before|first-?ever/i;

const followUpPattern =
  // NOTE: "review visit" is intentionally NOT a trigger — a medication-review
  // outreach visit (e.g. nursing home) is an unknown scenario that must fall
  // through to Other Cards + review flag (§8C QA). "Review of/appointment"
  // still signals a return visit.
  /follow.?up|return(ing|ed|s)? (for|to|visit|appointment)|(last|previous) visit|since the last|review (of|appointment)|test results?|results?.{0,20}(are|show|confirm|of)|side effects?|treatment progress|progress since|progression|came back|coming back|second visit|re-?attendance|ongoing (treatment|care|management)|continu(e[sd]?|ing) (treatment|management|care)|check-?up|recall (visit|appointment)|monitoring/i;

const badNewsPattern =
  /break(ing)? bad news|cancer|malignan|terminal|serious diagnosis|grave news|has died|death|life.?threatening|palliative|chemotherapy|oncology|poor prognosis|bad news/i;
const angryPattern =
  /\bangry\b|anger|furious|\bupset\b|complain(t|ed|ing|s)?|dissatisf|annoyed|irritat|raised a complaint|formal complaint|unhappy with|aggressive/i;
const reluctantPattern =
  /refus|reluctant|declin|resist|does ?n[o']t want|do not want|unwilling|hesitant|against (medical )?advice|won.?t (take|attend|have|go|accept|agree|come)|will not (take|attend|have|go|accept|agree|come)|non-?complian/i;

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function candidateText(input: SpeakingCardClassifiable): string {
  let text = [
    input.scenarioTitle,
    input.setting,
    input.background,
    ...(input.tasks ?? []),
    input.clinicalTopic,
  ]
    .filter(Boolean)
    .join('\n');
  // Patient names never carry encounter semantics ("Mrs Ward" must not read
  // as a hospital ward). Strip the full name and the surname token.
  const name = (input.patientName ?? '').trim();
  if (name) {
    const parts = [name, ...name.split(/\s+/)].filter((part) => part.length > 2);
    for (const part of parts) {
      text = text.replace(new RegExp(escapeRegExp(part), 'gi'), '');
    }
  }
  return text;
}

function behaviouralText(input: SpeakingCardClassifiable): string {
  // PatientEmotion is patient behaviour ("angry" matters). CommunicationGoal
  // is candidate-side — except Negotiate/Persuade, which only exist because
  // the patient resists the recommendation.
  const goal = /negotiat|persua/i.test(input.communicationGoal ?? '')
    ? 'patient resists recommendation'
    : '';
  return [candidateText(input), input.patientEmotion, goal].filter(Boolean).join('\n');
}

function detectBehavioural(input: SpeakingCardClassifiable): SpeakingBehaviouralTag[] {
  const text = behaviouralText(input);
  const tags: SpeakingBehaviouralTag[] = [];
  if (badNewsPattern.test(text)) tags.push('Breaking Bad News');
  if (angryPattern.test(text)) tags.push('Angry');
  if (reluctantPattern.test(text)) tags.push('Reluctant');
  return tags;
}

/**
 * Deterministic primary-category classifier (§8B priority rules).
 *
 * Encounter type always wins over behavioural state: Angry / Reluctant /
 * Breaking Bad News become secondary tags when a stronger encounter category
 * (examination framing, emergency arrival, known-patient care, first or
 * return visit) is present.
 *
 * Edge case: background findings phrased "after examination you find…"
 * NEVER trigger Examination Card — only an explicit completed-examination
 * frame ("You have just examined…") does.
 */
export function classifySpeakingCard(input: SpeakingCardClassifiable): SpeakingCardClassification {
  const text = candidateText(input);
  const behavioural = detectBehavioural(input);
  const withSecondary = (primary: SpeakingPrimaryCategory): SpeakingCardClassification => ({
    primary,
    secondaryTags: behavioural.filter((tag) => tag !== primary),
    needsReview: false,
  });

  // Q1 — role play explicitly framed as beginning AFTER a completed exam.
  if (examStartPattern.test(text) || examOpeningPattern.test(text)) {
    return withSecondary('Examination Card');
  }

  // Q2 — patient has JUST arrived in Emergency now (not already managed there).
  if (
    emergencySettingPattern.test(text) &&
    emergencyArrivalPattern.test(text) &&
    !knownCarePattern.test(text)
  ) {
    return withSecondary('Emergency / Emergency Department');
  }

  // Q3 — already under active inpatient / known care (ward, ICU, pre-op,
  // discharge, or already managed in ED).
  if (knownCarePattern.test(text)) {
    return withSecondary('Already Known Patient');
  }

  // Q4 — first-ever attendance.
  if (firstVisitPattern.test(text)) {
    return withSecondary('First Visit');
  }

  // Q5 — return / follow-up / results / side effects / treatment progress.
  if (followUpPattern.test(text)) {
    return withSecondary('Second Visit / Follow-up');
  }

  // Q6 — no encounter category fits: a behavioural scenario on its own, else Other.
  if (behavioural.includes('Breaking Bad News')) {
    return { primary: 'Breaking Bad News', secondaryTags: [], needsReview: false };
  }
  if (behavioural.includes('Angry')) {
    return { primary: 'Angry Patient', secondaryTags: [], needsReview: false };
  }
  if (behavioural.includes('Reluctant')) {
    return { primary: 'Reluctant Patient', secondaryTags: [], needsReview: false };
  }

  // Low-confidence fallback: visible Other Cards + review flag rather than a
  // forced wrong category.
  return { primary: 'Other Cards', secondaryTags: [], needsReview: true };
}

/** Options for the catalogue category filter, in display order. */
export function speakingCategoryFilterOptions(): { id: string; label: string }[] {
  return SPEAKING_PRIMARY_CATEGORIES.map((category) => ({ id: category, label: category }));
}

export function isSpeakingPrimaryCategory(value: string): value is SpeakingPrimaryCategory {
  return (SPEAKING_PRIMARY_CATEGORIES as readonly string[]).includes(value);
}
