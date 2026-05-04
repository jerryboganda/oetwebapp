// ============================================================================
// Profession-Specific Speaking Coaching Guidance
// ============================================================================
//
// Per docs/product-strategy/06_feature_strategy_and_blueprint.md §OET features:
// "Profession-specific speaking coaching — Speaking role-play scenarios,
// warm-up questions, and criterion coaching should adapt to the learner's
// profession."
//
// This module provides profession-aware speaking coaching tips that can be
// surfaced in speaking result feedback, self-practice, and 1-on-1 coaching
// sessions.
// ============================================================================

export interface SpeakingCoachingTip {
  criterionCode: string;
  title: string;
  description: string;
  drillSuggestion: string;
  priority: 'high' | 'medium' | 'low';
}

/** Profession-specific speaking coaching guidance for OET Speaking. */
export const PROFESSION_SPEAKING_COACHING: Readonly<
  Partial<Record<string, SpeakingCoachingTip[]>>
> = {
  medicine: [
    {
      criterionCode: 'relationship_building',
      title: 'Establish clinical rapport quickly',
      description: 'Medical consultations often start with a brief, warm acknowledgment of the patient\'s concern, then move promptly to a focused history. Avoid overly casual small talk or jumping straight to clinical questions.',
      drillSuggestion: 'Record yourself opening three different consultations. Time how long until you ask your first focused clinical question. Aim for 20–30 seconds of rapport.',
      priority: 'high',
    },
    {
      criterionCode: 'information_gathering',
      title: 'Focused history-taking language',
      description: 'Use closed questions efficiently for red flags, then open questions for psychosocial context. Signal transitions clearly: "Let me check a few specific things, then I\'d like to hear more about how this affects you."',
      drillSuggestion: 'Practice the "funnel" structure: open → focused → closed → summarising. Record and count your question types.',
      priority: 'high',
    },
    {
      criterionCode: 'explanation_planning',
      title: 'Explain procedures with chunked information',
      description: 'Patients retain information in 2–3 chunks. Explain what you will do, why, and what the patient will feel — then check understanding before moving on.',
      drillSuggestion: 'Explain a lumbar puncture or colonoscopy prep in ≤90 seconds using the chunk-check-chunk method.',
      priority: 'high',
    },
  ],
  nursing: [
    {
      criterionCode: 'relationship_building',
      title: 'Empathetic presence and active listening',
      description: 'Nursing interactions often involve emotional disclosure (fear, pain, loss). Use reflective statements ("It sounds like you\'re worried about...") and minimal encouragers ("I see", "Go on") more frequently than directive questions.',
      drillSuggestion: 'Listen to a patient monologue and respond using only reflections and encouragers for 60 seconds.',
      priority: 'high',
    },
    {
      criterionCode: 'information_gathering',
      title: 'Holistic assessment questions',
      description: 'Nursing histories should systematically cover physical, emotional, social, and functional domains. Use a framework (e.g. Roper-Logan-Tierney) and signal when you are shifting domains.',
      drillSuggestion: 'Conduct a 3-minute holistic admission assessment covering mobility, hygiene, sleep, mood, and support network.',
      priority: 'high',
    },
    {
      criterionCode: 'explanation_planning',
      title: 'Patient-education clarity for self-care',
      description: 'Nurses often teach wound care, medication timing, or mobility exercises. Use teach-back: explain, demonstrate, ask the patient to explain back, correct gently, re-check.',
      drillSuggestion: 'Teach a mock patient how to administer an insulin injection. Use the teach-back loop at least twice.',
      priority: 'high',
    },
  ],
  dentistry: [
    {
      criterionCode: 'relationship_building',
      title: 'Calm anxious patients with anticipatory reassurance',
      description: 'Dental anxiety is common. Name the anxiety early ("Many people feel nervous about this"), give a clear signal system ("Raise your hand if you need a break"), and describe sensations before they occur.',
      drillSuggestion: 'Role-play reassuring an anxious patient before a local anaesthetic injection. Time: 45 seconds.',
      priority: 'high',
    },
    {
      criterionCode: 'explanation_planning',
      title: 'Explain oral conditions with visual analogy',
      description: 'Patients cannot see inside their own mouths easily. Use hand gestures, simple diagrams, or analogies ("The gum has pulled back like a sleeve rolling up") to make the condition concrete.',
      drillSuggestion: 'Explain periodontal disease, caries, or a cracked tooth using only analogies and gestures — no dental jargon.',
      priority: 'high',
    },
  ],
  pharmacy: [
    {
      criterionCode: 'information_gathering',
      title: 'Medication-history reconciliation',
      description: 'Pharmacist consultations require systematic medication review. Use a structured checklist: current meds, allergies, OTC/herbal, adherence barriers, side effects, recent changes.',
      drillSuggestion: 'Conduct a 2-minute medication reconciliation for a patient on 5+ medications. Do not miss a single drug or allergy.',
      priority: 'high',
    },
    {
      criterionCode: 'explanation_planning',
      title: 'Counsel on dosing and interactions in plain language',
      description: 'Translate pharmacokinetic concepts into daily-life terms: "Take this with food so it doesn\'t irritate your stomach" rather than "enhance bioavailability and reduce GI adverse events."',
      drillSuggestion: 'Explain warfarin-diet interactions, or statin-muscle-pain warning signs, to a layperson in ≤60 seconds.',
      priority: 'high',
    },
  ],
  physiotherapy: [
    {
      criterionCode: 'relationship_building',
      title: 'Goal-oriented rapport',
      description: 'Physiotherapy patients are often motivated by functional goals (walk to the shop, play with grandchildren). Open with the goal, not the diagnosis: "What would you like to be able to do that you can\'t do now?"',
      drillSuggestion: 'Open three different physio assessments with a goal-focused question and no reference to the referrer\'s diagnosis for 30 seconds.',
      priority: 'high',
    },
    {
      criterionCode: 'explanation_planning',
      title: 'Explain exercise prescription behaviourally',
      description: 'Patients rarely adhere to abstract exercise programmes. Tie each exercise to a specific daily activity, specify frequency/duration/progression, and set a self-monitoring cue ("Do these before your morning coffee").',
      drillSuggestion: 'Prescribe a 3-exercise home programme for low back pain. Each exercise must link to a real daily task and have a clear cue.',
      priority: 'high',
    },
  ],
};

/**
 * Get speaking coaching tips for a profession. Returns empty array if not
 * mapped so callers never need to null-check.
 */
export function getProfessionSpeakingCoachingTips(profession: string): SpeakingCoachingTip[] {
  const key = profession.toLowerCase().replace(/\s+/g, '_');
  return PROFESSION_SPEAKING_COACHING[key] ?? [];
}
