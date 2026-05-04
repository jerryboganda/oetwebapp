// ============================================================================
// Profession-Specific Writing Remediation Guidance
// ============================================================================
//
// Per docs/product-strategy/06_feature_strategy_and_blueprint.md §OET features:
// "Profession-specific writing remediation — The product should coach nurses,
// doctors, pharmacists, and others differently where the communication
// patterns differ."
//
// This module provides profession-aware remediation tips that can be
// surfaced in writing result feedback, revision mode, and compare-attempt
// analytics. It is a frontend data layer; backend scoring still owns the
// canonical criterion scores.
// ============================================================================

import type { Profession } from './types/expert';

export interface ProfessionRemediationTip {
  criterionCode: string;
  title: string;
  description: string;
  exampleWeak: string;
  exampleStrong: string;
  priority: 'high' | 'medium' | 'low';
}

/** Profession-specific remediation guidance for OET Writing. */
export const PROFESSION_REMEDIATION_TIPS: Readonly<
  Partial<Record<Profession, ProfessionRemediationTip[]>>
> = {
  medicine: [
    {
      criterionCode: 'purpose',
      title: 'Referral purpose clarity',
      description: 'Medical referrals often involve multiple stakeholders (GP, specialist, patient). Explicitly name the intended action for each reader.',
      exampleWeak: 'I am writing about this patient.',
      exampleStrong: 'I am referring Mr Smith for urgent cardiac assessment and requesting that his current antihypertensive regimen be reviewed.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Relevant clinical detail density',
      description: 'Medical letters need focused history and examination findings. Avoid listing every negative finding; select the data that supports the referral question.',
      exampleWeak: 'He has no headaches, no dizziness, no nausea.',
      exampleStrong: 'Relevant negatives: no chest pain, no syncope, no paroxysmal nocturnal dyspnoea.',
      priority: 'high',
    },
    {
      criterionCode: 'conciseness_clarity',
      title: 'Efficient clinical narrative',
      description: 'Doctors often overwrite. Use bullet points for medication lists and timeline summaries to improve scannability.',
      exampleWeak: 'He is taking medication A and also medication B and recently medication C was added.',
      exampleStrong: 'Current medications:\n• Amlodipine 5 mg od\n• Atorvastatin 20 mg nocte\n• Bisoprolol 2.5 mg od (added 2 weeks ago)',
      priority: 'medium',
    },
  ],
  nursing: [
    {
      criterionCode: 'purpose',
      title: 'Nursing handover purpose',
      description: 'Nursing letters often transfer care. State what the receiving nurse needs to do, monitor, or report.',
      exampleWeak: 'I am writing about the patient.',
      exampleStrong: 'This handover summarises Mrs Lee\'s post-operative status and the wound care schedule you will need to continue.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Holistic assessment inclusion',
      description: 'Nursing assessments should include psychosocial, mobility, and skin-integrity factors alongside clinical data.',
      exampleWeak: 'The patient had surgery and is recovering.',
      exampleStrong: 'Post-op day 3 following total knee replacement. Mobilising with a frame × 10 m. Skin intact. Expressing anxiety about discharge and home care.',
      priority: 'high',
    },
    {
      criterionCode: 'genre_style',
      title: 'Patient-centred tone',
      description: 'Nursing writing often benefits from a warmer, more collaborative tone while maintaining professional boundaries.',
      exampleWeak: 'The patient must do the exercises.',
      exampleStrong: 'Mrs Lee has agreed to a graduated exercise programme and understands she can contact the ward if pain increases.',
      priority: 'medium',
    },
  ],
  dentistry: [
    {
      criterionCode: 'purpose',
      title: 'Dental referral intent',
      description: 'Dental referrals often request a specific procedure or specialist opinion. State the procedure and urgency clearly.',
      exampleWeak: 'Please see this patient.',
      exampleStrong: 'I am referring Mr Patel for extraction of tooth 38 under local anaesthesia, due to recurrent pericoronitis and patient preference.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Odontogram and radiograph references',
      description: 'Reference specific teeth, surfaces, and radiograph dates so the receiving clinician can locate the issue quickly.',
      exampleWeak: 'There is a problem with a tooth.',
      exampleStrong: 'Bitewing radiographs (dated 15/03/2026) show caries extending into dentine on the distal surface of tooth 46.',
      priority: 'high',
    },
  ],
  pharmacy: [
    {
      criterionCode: 'purpose',
      title: 'Medication review request',
      description: 'Pharmacy letters often centre on reconciliation, interactions, or adherence. State the exact question for the pharmacist.',
      exampleWeak: 'Can you check the medicines?',
      exampleStrong: 'Please review Mrs Khan\'s medication list for potential interactions between her new antifungal and existing statin therapy.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Accurate drug nomenclature',
      description: 'Use generic names, correct dosages, and routes. Avoid ambiguous abbreviations (e.g. write "units" not "u").',
      exampleWeak: 'He takes insulin.',
      exampleStrong: 'Humalog (insulin lispro) 100 units/ml, 6 units subcutaneously before meals.',
      priority: 'high',
    },
  ],
  physiotherapy: [
    {
      criterionCode: 'purpose',
      title: 'Functional goal setting',
      description: 'Physiotherapy referrals should specify the functional target (e.g. independent transfers, gait re-education) not just the diagnosis.',
      exampleWeak: 'Please treat the patient.',
      exampleStrong: 'I am referring Mr Okafor for gait re-education following right total hip replacement (post-op week 4), aiming for independent community ambulation with a stick.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Baseline functional measures',
      description: 'Include baseline range of motion, strength grades, and current mobility aids to allow outcome tracking.',
      exampleWeak: 'He can walk a little.',
      exampleStrong: 'Baseline: TUG 18 s, 10MWT 0.4 m/s, hip flexion AROM 70° (limited by pain).',
      priority: 'medium',
    },
  ],
  occupational_therapy: [
    {
      criterionCode: 'purpose',
      title: 'ADL-focused referral',
      description: 'OT referrals should specify which activities of daily living are affected and the desired level of independence.',
      exampleWeak: 'She needs occupational therapy.',
      exampleStrong: 'I am referring Ms Garcia for upper-limb rehabilitation and ADL retraining following left CVA, with a goal of independent dressing and meal preparation.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Home environment context',
      description: 'Include home layout, carer support, and equipment already in place so the OT can plan a safe discharge.',
      exampleWeak: 'She lives at home.',
      exampleStrong: 'Lives in a ground-floor flat with one step at entrance. Husband is primary carer. No equipment currently in place.',
      priority: 'medium',
    },
  ],
  dietetics: [
    {
      criterionCode: 'purpose',
      title: 'Nutritional intervention request',
      description: 'Dietetic referrals should state the nutritional concern, the intended outcome (e.g. weight stabilisation, texture modification), and the timeline.',
      exampleWeak: 'Please advise on diet.',
      exampleStrong: 'I am referring Mr Anders for nutritional support following 8% unintentional weight loss over 3 months, aiming for weight stabilisation and oral nutrition supplementation review.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Dietary intake detail',
      description: 'Include estimated intake, texture tolerance, allergies, and supplement history so the dietitian can calculate needs accurately.',
      exampleWeak: 'He does not eat much.',
      exampleStrong: 'Estimated intake ~1200 kcal/day. Texture: soft diet tolerated. Allergies: none. Supplements: Fortisip 200 ml × 2/day (started 2 weeks ago).',
      priority: 'medium',
    },
  ],
  speech_pathology: [
    {
      criterionCode: 'purpose',
      title: 'Communication/swallow goal',
      description: 'Speech pathology referrals should distinguish between communication and swallowing concerns and specify the baseline function.',
      exampleWeak: 'She has speech problems.',
      exampleStrong: 'I am referring Mrs Brennan for swallowing assessment following aspiration pneumonia; baseline: thin liquids trigger coughing, puree tolerated.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Aspiration risk detail',
      description: 'For swallow referrals, include chest status, oxygen needs, and recent swallow observations to guide instrumental assessment timing.',
      exampleWeak: 'She coughs when eating.',
      exampleStrong: 'Chest clear on auscultation today. O2 sats 94% on room air. Coughs with thin liquids; no cough with yoghurt consistency.',
      priority: 'high',
    },
  ],
  radiography: [
    {
      criterionCode: 'purpose',
      title: 'Imaging request justification',
      description: 'Radiography referrals (or requests for imaging reports) should state the clinical question the image should answer, not just the body part.',
      exampleWeak: 'Please X-ray the leg.',
      exampleStrong: 'I am requesting a weight-bearing AP and lateral knee radiograph to assess joint-space narrowing and osteophyte burden for surgical planning.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Prior imaging cross-reference',
      description: 'Reference previous imaging dates and findings so the radiologist can assess interval change.',
      exampleWeak: 'There was an X-ray before.',
      exampleStrong: 'Knee radiographs from 12/01/2026 reported moderate medial compartment OA. Please compare for interval change.',
      priority: 'medium',
    },
  ],
  podiatry: [
    {
      criterionCode: 'purpose',
      title: 'Foot-care referral goal',
      description: 'Podiatry referrals should specify whether the goal is wound management, offloading, nail care, or biomechanical assessment.',
      exampleWeak: 'Please look at his feet.',
      exampleStrong: 'I am referring Mr Osei for diabetic foot ulcer offloading and footwear review; ulcer plantar 1st MTP, present 3 weeks, no signs of infection.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Vascular and neuropathy status',
      description: 'Include pedal pulse status, monofilament results, and ABI so the podiatrist can gauge healing potential.',
      exampleWeak: 'Circulation seems okay.',
      exampleStrong: 'Dorsalis pedis pulses palpable bilaterally. Monofilament: reduced sensation L>R. ABI 0.92 (R), 0.89 (L).',
      priority: 'medium',
    },
  ],
  optometry: [
    {
      criterionCode: 'purpose',
      title: 'Visual function concern',
      description: 'Optometry referrals should specify the visual complaint, the suspected underlying cause, and what the optometrist should assess.',
      exampleWeak: 'She cannot see well.',
      exampleStrong: 'I am referring Mrs Patel for visual field assessment and IOP measurement; she reports progressive peripheral vision loss in her left eye over 6 months.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Current correction and medication',
      description: 'Include current spectacle prescription, topical eye medications, and systemic drugs with ocular side effects.',
      exampleWeak: 'She wears glasses.',
      exampleStrong: 'Current spectacles: R -2.50 DS, L -2.75 DS (prescribed 2024). Using latanoprost 0.005% nocte OD. Systemic: prednisolone 5 mg od for PMR.',
      priority: 'medium',
    },
  ],
  veterinary: [
    {
      criterionCode: 'purpose',
      title: 'Animal referral intent',
      description: 'Veterinary referrals should specify the species, signalment, presenting complaint, and the requested specialist action.',
      exampleWeak: 'Please see the dog.',
      exampleStrong: 'I am referring Bella, a 6-year-old female neutered Labrador, for orthopaedic assessment of chronic left forelimb lameness; requesting CT elbow bilaterally.',
      priority: 'high',
    },
    {
      criterionCode: 'content',
      title: 'Vaccination and medication history',
      description: 'Include vaccination status, parasite control, and current medications (with doses in mg/kg) so the referral clinic can plan anaesthesia safely.',
      exampleWeak: 'She has had her injections.',
      exampleStrong: 'Vaccinations current (DHPPi + L4, due 08/2026). On Bravecto q3mo. Current meds: carprofen 75 mg po sid, gabapentin 100 mg po bid.',
      priority: 'medium',
    },
  ],
} as const;

/**
 * Get remediation tips for a profession. Returns a safe empty array if the
 * profession is not yet mapped, so callers never need to null-check.
 */
export function getProfessionRemediationTips(profession: string): ProfessionRemediationTip[] {
  const key = profession.toLowerCase().replace(/\s+/g, '_') as Profession;
  return PROFESSION_REMEDIATION_TIPS[key] ?? [];
}
