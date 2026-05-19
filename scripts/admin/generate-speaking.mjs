#!/usr/bin/env node
/**
 * generate-speaking.mjs
 *
 * Bulk-generates draft ContentPaper rows (SubtestCode="speaking") via the
 * admin API + AI gateway-grounded prompts (DO Serverless), then saves the
 * authored speaking role-play structure via the speaking-structure editor,
 * then attempts to publish.
 *
 * Default distribution: 12 medicine + 4 nursing + 4 other (dentistry, pharmacy,
 * physiotherapy, optometry) = 20 papers. Each paper carries 2 role-play
 * scenarios in `speakingStructure.roleplayCount=2` plus a primary candidate
 * card; the interlocutor's hidden card is included in the same structure.
 *
 * IMPORTANT — publish gate:
 *   Speaking papers require primary assets {RoleCard, AssessmentCriteria,
 *   WarmUpQuestions} per `IContentPaperService.RequiredRolesFor("speaking")`.
 *   This script does NOT upload assets — they have to be authored or imported
 *   separately. When publish fails because of missing assets, we log the
 *   precise error and continue. The created Draft paper + speakingStructure
 *   are preserved for admin curation.
 *
 * Usage:
 *   node scripts/admin/generate-speaking.mjs [--dry-run] [--profession m,n,…]
 *                                            [--count N] [--no-publish]
 *                                            [--healthcheck]
 *
 *   --dry-run        Show what would be created; do not call admin or AI.
 *   --profession ..  CSV of profession slugs to include (default: medicine,
 *                    nursing,dentistry,pharmacy,physiotherapy,optometry).
 *   --count N        Total papers to create across all professions
 *                    (default 20, distributed 12/4/4 per spec).
 *   --no-publish     Skip publish attempt.
 *   --healthcheck    Run shared healthcheck and exit.
 *
 * Required env: AI__ApiKey (DO Serverless). Defaults for API_BASE,
 * ADMIN_EMAIL, ADMIN_PASSWORD live in `_lib.mjs`.
 */

import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, aiChatJson, logFailure,
  healthcheck, progress, slugify, makeProvenance, abortIfFailureCascade, sleep,
} from './_lib.mjs';

// -----------------------------------------------------------------------------
// Topic catalog — generic OET-Speaking scenarios (no copyrighted material).
// Each topic produces ONE ContentPaper containing TWO role-play scenarios.
// -----------------------------------------------------------------------------

const TOPICS = {
  medicine: [
    { topic: 'Asthma management with parent', clinical: 'asthma' },
    { topic: 'Type 2 diabetes diet counselling', clinical: 'diabetes' },
    { topic: 'Smoking cessation with reluctant patient', clinical: 'smoking_cessation' },
    { topic: 'Hypertension lifestyle counselling', clinical: 'hypertension' },
    { topic: 'Antibiotic stewardship for viral URTI', clinical: 'antibiotics' },
    { topic: 'Statin therapy informed decision', clinical: 'cholesterol' },
    { topic: 'Migraine management follow-up', clinical: 'migraine' },
    { topic: 'Breaking news of abnormal mammogram', clinical: 'breaking_bad_news' },
    { topic: 'Anxiety screening in primary care', clinical: 'mental_health' },
    { topic: 'Iron deficiency anaemia in pregnancy', clinical: 'antenatal' },
    { topic: 'Adolescent contraception counselling', clinical: 'sexual_health' },
    { topic: 'Acute low back pain return-to-work', clinical: 'musculoskeletal' },
  ],
  nursing: [
    { topic: 'Wound care for diabetic foot ulcer in elderly patient', clinical: 'wound_care' },
    { topic: 'Medication reconciliation at hospital admission', clinical: 'medication_reconciliation' },
    { topic: 'Discharge planning after hip replacement', clinical: 'discharge_planning' },
    { topic: 'Falls prevention education for outpatient', clinical: 'falls_prevention' },
  ],
  dentistry: [
    { topic: 'Oral hygiene advice for periodontal patient', clinical: 'periodontal' },
    { topic: 'Pre-extraction consent for impacted third molar', clinical: 'oral_surgery' },
  ],
  pharmacy: [
    { topic: 'Asthma inhaler technique counselling', clinical: 'asthma_inhaler' },
    { topic: 'Warfarin INR follow-up with anxious patient', clinical: 'anticoagulation' },
  ],
  physiotherapy: [
    { topic: 'Post-stroke home exercise programme review', clinical: 'neuro_rehab' },
  ],
  optometry: [
    { topic: 'Glaucoma eye-drop adherence counselling', clinical: 'glaucoma' },
  ],
};

// Default distribution per spec: 12 medicine + 4 nursing + 4 other.
const DEFAULT_DISTRIBUTION = [
  ...TOPICS.medicine.slice(0, 12).map(t => ({ profession: 'medicine', ...t })),
  ...TOPICS.nursing.slice(0, 4).map(t => ({ profession: 'nursing', ...t })),
  // "Other professions": 4 entries spread across dentistry/pharmacy/physio/optometry.
  ...TOPICS.dentistry.slice(0, 1).map(t => ({ profession: 'dentistry', ...t })),
  ...TOPICS.pharmacy.slice(0, 1).map(t => ({ profession: 'pharmacy', ...t })),
  ...TOPICS.physiotherapy.slice(0, 1).map(t => ({ profession: 'physiotherapy', ...t })),
  ...TOPICS.optometry.slice(0, 1).map(t => ({ profession: 'optometry', ...t })),
];

// -----------------------------------------------------------------------------
// AI prompt — produces the speakingStructure JSON the editor accepts.
// -----------------------------------------------------------------------------

const SYSTEM_PROMPT = (profession) => `You are an OET Speaking sub-test content author writing role-play scenarios for ${profession} candidates. Output ONLY valid JSON matching the requested schema. Use plausible, original case content — do NOT reproduce any official OET, CBLA, or Cambridge material. Every role-play must be patient-centred and clinically realistic for ${profession}. British English. No emoji. No markdown.`;

const USER_PROMPT = ({ profession, topic, clinical }) => `Generate an OET Speaking practice paper for a ${profession} candidate on the topic: "${topic}".

Return JSON exactly matching this shape (use these keys, do not add or omit fields, do not nest extra wrappers):

{
  "candidateCard": {
    "setting": "<clinical setting, 1 sentence>",
    "candidateRole": "<the candidate's professional role, e.g. 'GP at a suburban clinic'>",
    "patientRole": "<the patient/client role, e.g. 'Mrs Aisha Kumar, mother of 8-year-old Rohan'>",
    "background": "<3-6 sentences of case context, history, presenting issue, prior consultations; no PHI>",
    "task": "<concise task brief, 1-2 sentences telling the candidate what to do>",
    "tasks": [
      "<role objective bullet 1>",
      "<role objective bullet 2>",
      "<role objective bullet 3>",
      "<role objective bullet 4>",
      "<role objective bullet 5>"
    ]
  },
  "interlocutorCard": {
    "patientProfile": "<5-8 sentences of hidden information the interlocutor uses while playing the patient: emotional state, concerns, beliefs, prior knowledge, what to disclose if asked>",
    "cuePrompts": [
      "<cue 1: 'If the candidate does X, say Y'>",
      "<cue 2>",
      "<cue 3>",
      "<cue 4>"
    ]
  },
  "warmUpQuestions": [
    "<short rapport-building question 1>",
    "<short rapport-building question 2>",
    "<short rapport-building question 3>"
  ],
  "patientEmotion": "<one of: anxious | reluctant | distressed | neutral | sceptical | embarrassed | angry | sad | hopeful>",
  "communicationGoal": "<one sentence describing the candidate's primary communication goal>",
  "clinicalTopic": "${clinical}",
  "criteriaFocus": [
    "relationship_building",
    "understanding_patient_perspective",
    "providing_structure",
    "information_gathering",
    "information_giving"
  ],
  "prepTimeSeconds": 180,
  "roleplayTimeSeconds": 300,
  "roleplayCount": 2,
  "complianceNotes": "<one short sentence noting any cultural/safety considerations for this scenario>"
}

Constraints:
- Exactly 5 entries in "candidateCard.tasks" (role objectives).
- Exactly 4 entries in "interlocutorCard.cuePrompts".
- Exactly 3 entries in "warmUpQuestions".
- "roleplayCount" must be 2 (this paper contains two practice run-throughs of the same scenario for retry/comparison).
- All strings British English, plain text, no markdown or HTML.
- Do not include any field that is not in the schema above.`;

// -----------------------------------------------------------------------------
// Main
// -----------------------------------------------------------------------------

const flags = parseFlags();

async function main() {
  if (flags.healthcheck) {
    startRun('generate-speaking-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  startRun('generate-speaking');

  const dryRun = !!flags['dry-run'];
  const skipPublish = !!flags['no-publish'];
  const totalCount = Number.isFinite(parseInt(flags.count, 10)) ? parseInt(flags.count, 10) : DEFAULT_DISTRIBUTION.length;
  const allowedProfs = (flags.profession ? String(flags.profession) : 'medicine,nursing,dentistry,pharmacy,physiotherapy,optometry')
    .split(',').map(s => s.trim().toLowerCase()).filter(Boolean);

  const targets = DEFAULT_DISTRIBUTION
    .filter(t => allowedProfs.includes(t.profession))
    .slice(0, totalCount);

  console.log(`Targets: ${targets.length} papers across professions: ${[...new Set(targets.map(t => t.profession))].join(', ')}`);
  console.log(`Mode: ${dryRun ? 'DRY RUN' : 'LIVE'}  Publish: ${skipPublish ? 'skipped' : 'attempt after structure save'}`);

  let created = 0, structureSaved = 0, published = 0, publishBlocked = 0, failed = 0, consecutiveFailures = 0;

  for (let i = 0; i < targets.length; i++) {
    const t = targets[i];
    const label = `${t.profession}/${t.topic}`;
    console.log(progress(i + 1, targets.length, label));

    if (abortIfFailureCascade(consecutiveFailures, 'generate-speaking')) break;

    if (dryRun) {
      console.log(`  (dry-run) would POST /v1/admin/papers (subtestCode=speaking, profession=${t.profession})`);
      console.log(`  (dry-run) would AI-generate speakingStructure for "${t.topic}"`);
      console.log(`  (dry-run) would PUT  /v1/admin/papers/{id}/speaking-structure`);
      if (!skipPublish) console.log(`  (dry-run) would POST /v1/admin/papers/{id}/publish`);
      continue;
    }

    // 1) AI-generate the speaking structure.
    let structure;
    try {
      const reply = await aiChatJson({
        system: SYSTEM_PROMPT(t.profession),
        user: USER_PROMPT(t),
        temperature: 0.7,
        maxTokens: 4096,
      });
      structure = reply.json;
      if (!structure || typeof structure !== 'object' || !structure.candidateCard) {
        throw new Error(`AI returned malformed structure (no candidateCard)`);
      }
    } catch (e) {
      logFailure('speaking-ai', label, e);
      failed++; consecutiveFailures++;
      continue;
    }

    // 2) Create the Draft paper.
    const provenance = makeProvenance({ kind: 'speaking', profession: t.profession, model: CONFIG.ai.chatModel });
    const tagsCsv = Array.isArray(structure.criteriaFocus) && structure.criteriaFocus.length
      ? structure.criteriaFocus.slice(0, 8).join(',')
      : 'relationship_building,information_giving';

    const createBody = {
      subtestCode: 'speaking',
      title: `${capitalize(t.profession)} Speaking — ${t.topic}`,
      slug: `speaking-${t.profession}-${slugify(t.topic)}-${shortId()}`,
      professionId: t.profession,
      appliesToAllProfessions: false,
      difficulty: 'standard',
      estimatedDurationMinutes: 12,
      cardType: t.clinical || null,
      letterType: null,
      priority: 0,
      tagsCsv,
      sourceProvenance: provenance,
    };

    const createRes = await adminFetch('/v1/admin/papers', { method: 'POST', body: createBody });
    if (!createRes.ok) {
      logFailure('speaking-create', label, new Error(`${createRes.status}: ${JSON.stringify(createRes.data).slice(0, 300)}`));
      failed++; consecutiveFailures++;
      continue;
    }
    const paperId = createRes.data?.id;
    if (!paperId) {
      logFailure('speaking-create', label, new Error(`no id in response: ${JSON.stringify(createRes.data).slice(0, 200)}`));
      failed++; consecutiveFailures++;
      continue;
    }
    created++;
    consecutiveFailures = 0;
    console.log(`  ✓ created paper ${paperId}`);

    // 3) Save the authored speakingStructure.
    const structPut = await adminFetch(`/v1/admin/papers/${paperId}/speaking-structure`, {
      method: 'PUT',
      body: { structure },
    });
    if (!structPut.ok) {
      logFailure('speaking-structure', { label, paperId }, new Error(`${structPut.status}: ${JSON.stringify(structPut.data).slice(0, 300)}`));
      failed++;
    } else {
      structureSaved++;
      const v = structPut.data?.validation;
      const ok = v?.isValid ?? v?.IsValid;
      const issueCount = (v?.issues || v?.Issues || []).length;
      console.log(`  ✓ structure saved  validation.isValid=${ok}  issues=${issueCount}`);
    }

    // 4) Publish attempt (expected to fail until RoleCard / AssessmentCriteria
    //    / WarmUpQuestions primary assets are attached out-of-band).
    if (!skipPublish) {
      const pub = await adminFetch(`/v1/admin/papers/${paperId}/publish`, { method: 'POST', body: {} });
      if (pub.ok) {
        published++;
        console.log(`  ✓ published ${paperId}`);
      } else {
        const errMsg = (pub.data?.error || JSON.stringify(pub.data) || '').toString().slice(0, 300);
        if (/Missing required primary asset/i.test(errMsg)) {
          publishBlocked++;
          console.log(`  ⓘ publish blocked (expected — needs assets): ${errMsg}`);
        } else {
          logFailure('speaking-publish', { label, paperId }, new Error(`${pub.status}: ${errMsg}`));
          failed++;
        }
      }
    }

    // Polite pause.
    await sleep(150);
  }

  endRun({
    targets: targets.length,
    created,
    structureSaved,
    published,
    publishBlocked,
    failed,
  });
  process.exit(failed > 0 && created === 0 ? 1 : 0);
}

function capitalize(s) { return s ? s[0].toUpperCase() + s.slice(1) : s; }
function shortId() { return Math.random().toString(36).slice(2, 8); }

main().catch(e => {
  console.error(e);
  process.exit(2);
});
