/**
 * generate-conversation.mjs
 *
 * Bulk-generate ConversationTemplate rows and publish them.
 *
 * Direct local LLM authoring is disabled. Conversation template drafting must
 * route through the backend grounded AI draft endpoint before publication.
 *
 * We stamp provenance into `patientVoice._provenance` so it is preserved on
 * the entity and visible in `/v1/admin/conversation/templates/{id}` detail.
 *
 * Discovered endpoints (backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs):
 *   GET  /v1/admin/conversation/templates                  (list, filter by profession/status)
 *   GET  /v1/admin/conversation/templates/{id}             (detail)
 *   POST /v1/admin/conversation/templates                  (create → status="draft")
 *   PUT  /v1/admin/conversation/templates/{id}             (update)
 *   POST /v1/admin/conversation/templates/{id}/publish     (publish gate)
 *   POST /v1/admin/conversation/templates/{id}/archive     (archive)
 *
 * Publish gate (AdminService.ContentAdmin.cs PublishConversationTemplateAsync):
 *   - title, scenario, role_description, patient_context required
 *   - objectives.Length >= 3
 *   - estimated_duration_seconds > 0
 *   - task_type_code ∈ {"oet-roleplay","oet-handover"}
 *
 * Usage:
 *   node scripts/admin/generate-conversation.mjs [flags]
 *
 *   --dry-run                    Plan only; no AI calls, no writes.
 *   --profession <slug>          Only generate for this profession.
 *   --count-per-profession N     Default 3 (12 professions × 3 ≈ 36 templates).
 *   --resume                     Skip professions/slots already covered (read from list endpoint).
 *   --no-publish                 Create as draft only, do not publish.
 *   --healthcheck                Run _lib healthcheck and exit.
 *   --chat-model <id>            Override CONFIG.ai.chatModel.
 *   --api-base <url>             Override CONFIG.apiBase.
 */

import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, logFailure,
  healthcheck, progress, sleep, slugify,
} from './_lib.mjs';

const flags = parseFlags();

// Canonical OET-12 profession slugs. ProfessionId on ConversationTemplate is
// a free-form string (no FK validation in CreateConversationTemplateAsync),
// but these match the seeded ProfessionReference codes where present.
const ALL_PROFESSIONS = [
  'medicine',
  'nursing',
  'dentistry',
  'dietetics',
  'occupational-therapy',
  'optometry',
  'pharmacy',
  'physiotherapy',
  'podiatry',
  'radiography',
  'speech-pathology',
  'veterinary-science',
];

const TASK_TYPES = ['oet-roleplay', 'oet-handover'];

// Allowed difficulty values (entity default = "medium").
const DIFFICULTIES = ['easy', 'medium', 'hard'];

// Topic seeds per profession. The LLM will pick from these (or invent its own
// in the same style) so we get clinically authentic, varied scenarios.
const TOPIC_BANK = {
  medicine: [
    'Asthma exacerbation in a paediatric patient',
    'Hypertension medication non-adherence',
    'Pre-operative anxiety and consent',
    'New type-2 diabetes diagnosis',
    'Suspected DVT in a post-flight traveller',
    'Chronic lower-back pain review',
    'Medication review for elderly polypharmacy',
  ],
  nursing: [
    'Post-operative handover between shifts (SBAR)',
    'Family update for a dementia inpatient',
    'Wound-care discharge teaching',
    'Insulin self-administration education',
    'End-of-life care discussion with relatives',
    'Falls-risk patient escalation to medical team',
    'Pain reassessment after analgesia',
  ],
  dentistry: [
    'Anxious patient before extraction',
    'Post-operative care after wisdom-tooth removal',
    'Discussing root-canal vs extraction options',
    'Paediatric oral-hygiene counselling',
    'Periodontal treatment plan review',
    'Cost discussion for crown work',
    'Bruxism and night-guard recommendation',
  ],
  dietetics: [
    'Renal-diet counselling for CKD stage 3',
    'Coeliac diagnosis education',
    'Weight-management plan for adolescent',
    'Enteral-feeding plan for post-stroke patient',
    'Gestational diabetes meal planning',
    'IBS low-FODMAP introduction',
    'Sports nutrition consult for endurance athlete',
  ],
  'occupational-therapy': [
    'Home-assessment recommendations post-hip-replacement',
    'Hand-therapy plan after carpal-tunnel release',
    'Return-to-work plan for RSI patient',
    'Paediatric sensory-integration parent update',
    'ADL retraining after stroke',
    'Cognitive-strategy training for early dementia',
    'Splinting education for rheumatoid arthritis',
  ],
  optometry: [
    'New presbyopia and progressive-lens options',
    'Diabetic-retinopathy screening result discussion',
    'Contact-lens hygiene counselling',
    'Paediatric amblyopia patching plan',
    'Glaucoma drop adherence review',
    'Dry-eye management plan',
    'Suspected keratoconus referral',
  ],
  pharmacy: [
    'Warfarin counselling for new prescription',
    'Inhaler-technique review for COPD patient',
    'OTC analgesic advice for pregnant patient',
    'Antibiotic course adherence counselling',
    'Polypharmacy review for elderly patient',
    'Smoking-cessation NRT consult',
    'Travel-medicine antimalarial advice',
  ],
  physiotherapy: [
    'Post-ACL-reconstruction rehab progression',
    'Chronic low-back pain self-management plan',
    'Stroke rehabilitation goal-setting',
    'Vestibular-rehab exercises for BPPV',
    'Shoulder-impingement assessment and plan',
    'Paediatric cerebral-palsy parent update',
    'Cardiac-rehab phase-2 program',
  ],
  podiatry: [
    'Diabetic foot-ulcer education',
    'Plantar-fasciitis self-management',
    'In-grown toenail post-procedure care',
    'Custom-orthotic prescription discussion',
    'Charcot-foot risk counselling',
    'Paediatric flat-foot parent reassurance',
    'Footwear advice for runner with overpronation',
  ],
  radiography: [
    'MRI safety screening for patient with implants',
    'Contrast-allergy discussion before CT',
    'Paediatric radiation-dose explanation to parent',
    'Mammography first-screening counselling',
    'Image-guided biopsy pre-procedure check',
    'Claustrophobic patient preparation for MRI',
    'Bone-density (DEXA) result explanation',
  ],
  'speech-pathology': [
    'Post-stroke dysphagia diet-modification education',
    'Stuttering treatment plan with adult patient',
    'Paediatric phonological-delay parent update',
    'Voice-rehab plan for teacher with nodules',
    'Aphasia rehab goal-setting with family',
    'Tracheostomy speaking-valve trial education',
    'AAC device introduction for non-verbal child',
  ],
  'veterinary-science': [
    'Canine osteoarthritis management plan with owner',
    'Feline diabetes new-diagnosis owner education',
    'Pre-anaesthetic discussion before dental scale',
    'Vaccination schedule for new puppy',
    'End-of-life decision counselling with owner',
    'Parasite-prevention plan for outdoor cat',
    'Post-surgical wound-care instructions for owner',
  ],
};

// ─────────────────────────────────────────────────────────────────────────────
// AI: generate one ConversationTemplate JSON.
// ─────────────────────────────────────────────────────────────────────────────

async function aiGenerateTemplate({ profession, taskType, topic, difficulty }) {
  throw new Error(`Direct conversation AI authoring is disabled for ${profession}/${taskType}/${topic}/${difficulty}. Use the backend grounded conversation template draft endpoint.`);
}

// ─────────────────────────────────────────────────────────────────────────────
// Validation (mirror backend publish gate so we fail fast before the POST).
// ─────────────────────────────────────────────────────────────────────────────

function validateTemplate(t, { profession, taskType }) {
  const issues = [];
  if (!t || typeof t !== 'object') { issues.push('not_an_object'); return issues; }
  if (!t.title || typeof t.title !== 'string') issues.push('title_missing');
  if (!t.scenario || typeof t.scenario !== 'string') issues.push('scenario_missing');
  if (!t.roleDescription || typeof t.roleDescription !== 'string') issues.push('roleDescription_missing');
  if (!t.patientContext || typeof t.patientContext !== 'string') issues.push('patientContext_missing');
  if (!Array.isArray(t.objectives) || t.objectives.length < 3) issues.push('objectives_lt_3');
  if (!Number.isFinite(t.estimatedDurationSeconds) || t.estimatedDurationSeconds <= 0)
    issues.push('estimatedDurationSeconds_invalid');
  if (!TASK_TYPES.includes(taskType)) issues.push('taskType_invalid_internal');
  return issues;
}

// ─────────────────────────────────────────────────────────────────────────────
// Resume helper: list existing published+draft templates per profession so we
// don't keep stacking duplicates on reruns.
// ─────────────────────────────────────────────────────────────────────────────

async function existingCountForProfession(profession) {
  const r = await adminFetch('/v1/admin/conversation/templates', {
    method: 'GET',
    query: { profession, pageSize: 100 },
  });
  if (!r.ok) return 0;
  // Response shape: { total, page, pageSize, items: [...] } per AdminService.ContentAdmin.
  return Number(r.data?.total ?? r.data?.items?.length ?? 0) || 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main
// ─────────────────────────────────────────────────────────────────────────────

async function main() {
  if (flags.healthcheck) {
    startRun('generate-conversation-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  const dryRun = !!flags['dry-run'];
  const noPublish = !!flags['no-publish'];
  const resume = !!flags.resume;
  const onlyProfession = flags.profession ? String(flags.profession).toLowerCase() : null;
  const countPerProfession = Math.max(1, parseInt(String(flags['count-per-profession'] ?? '3'), 10) || 3);

  const professions = onlyProfession
    ? ALL_PROFESSIONS.filter(p => p === onlyProfession)
    : ALL_PROFESSIONS;

  if (professions.length === 0) {
    console.error(`No matching professions. --profession=${onlyProfession} not in ${ALL_PROFESSIONS.join(',')}`);
    process.exit(2);
  }

  startRun('generate-conversation');
  console.log(`Plan: ${countPerProfession} templates × ${professions.length} professions = ${countPerProfession * professions.length} total`);
  console.log(`Dry-run=${dryRun}  Publish=${!noPublish}  Resume=${resume}  Model=${CONFIG.ai.chatModel}`);

  let created = 0, published = 0, failed = 0, skipped = 0;
  const provenanceNote = 'AI-generated via direct LLM (no admin AI-draft endpoint for conversation per AGENTS.md)';

  for (let pi = 0; pi < professions.length; pi++) {
    const profession = professions[pi];
    let baseSkip = 0;
    if (resume) {
      try {
        baseSkip = await existingCountForProfession(profession);
        console.log(`  [${profession}] existing templates in DB: ${baseSkip}`);
      } catch (e) {
        console.log(`  [${profession}] could not query existing count: ${e.message}`);
      }
    }

    const topics = TOPIC_BANK[profession] || [];
    for (let i = 0; i < countPerProfession; i++) {
      const slotIndex = baseSkip + i; // global slot index for this profession
      const taskType = TASK_TYPES[(slotIndex) % TASK_TYPES.length];
      const topic = topics[(slotIndex) % Math.max(1, topics.length)] || `General ${profession} consultation`;
      const difficulty = DIFFICULTIES[(slotIndex) % DIFFICULTIES.length];

      const label = `${profession} #${i + 1}/${countPerProfession} (slot ${slotIndex + 1}, ${taskType}, ${difficulty}) — ${topic}`;
      console.log(progress(pi * countPerProfession + i + 1, professions.length * countPerProfession, label));

      if (dryRun) {
        console.log(`  (dry-run) would call backend grounded draft endpoint → POST /v1/admin/conversation/templates${noPublish ? '' : ' → POST .../publish'}`);
        skipped++;
        continue;
      }

      // 1) Generate via direct LLM.
      let draft;
      try {
        draft = await aiGenerateTemplate({ profession, taskType, topic, difficulty });
      } catch (e) {
        logFailure('conversation-ai', { profession, slotIndex, topic, taskType }, e);
        failed++;
        continue;
      }

      // 2) Validate.
      const issues = validateTemplate(draft, { profession, taskType });
      if (issues.length > 0) {
        logFailure('conversation-validate', { profession, slotIndex, topic, taskType, issues }, new Error(issues.join(',')));
        failed++;
        continue;
      }

      // 3) Compose CreateRequest. Stamp provenance into patientVoice (only place
      //    it survives on the entity — there is no SourceProvenance column).
      const patientVoice = {
        ...(typeof draft.patientVoice === 'object' && draft.patientVoice ? draft.patientVoice : {}),
        _provenance: {
          source: 'ai-direct',
          model: CONFIG.ai.chatModel,
          note: provenanceNote,
          generatedAt: new Date().toISOString(),
          topic,
          difficulty,
        },
      };

      const body = {
        title: String(draft.title).slice(0, 200),
        taskTypeCode: taskType,
        professionId: profession,
        scenario: String(draft.scenario),
        roleDescription: String(draft.roleDescription),
        patientContext: String(draft.patientContext),
        expectedOutcomes: draft.expectedOutcomes ? String(draft.expectedOutcomes) : null,
        difficulty,
        estimatedDurationSeconds: Math.round(Number(draft.estimatedDurationSeconds)),
        objectives: draft.objectives.map(String).filter(Boolean),
        expectedRedFlags: Array.isArray(draft.expectedRedFlags) ? draft.expectedRedFlags.map(String).filter(Boolean) : [],
        keyVocabulary: Array.isArray(draft.keyVocabulary) ? draft.keyVocabulary.map(String).filter(Boolean) : [],
        patientVoice,
      };

      // 4) Create (draft).
      const createRes = await adminFetch('/v1/admin/conversation/templates', { method: 'POST', body });
      if (!createRes.ok) {
        logFailure('conversation-create', { profession, slotIndex, topic, taskType, status: createRes.status }, new Error(JSON.stringify(createRes.data).slice(0, 400)));
        failed++;
        continue;
      }
      const newId = createRes.data?.id;
      if (!newId) {
        logFailure('conversation-create', { profession, slotIndex }, new Error(`no id in response: ${JSON.stringify(createRes.data).slice(0, 200)}`));
        failed++;
        continue;
      }
      created++;
      console.log(`  ✓ created ${newId}  "${body.title}"`);

      // 5) Publish.
      if (!noPublish) {
        const pubRes = await adminFetch(`/v1/admin/conversation/templates/${newId}/publish`, { method: 'POST', body: {} });
        if (!pubRes.ok) {
          logFailure('conversation-publish', { id: newId, profession, status: pubRes.status }, new Error(JSON.stringify(pubRes.data).slice(0, 400)));
          // Leave as draft; not a fatal cascade.
        } else {
          published++;
          console.log(`  ↑ published ${newId}`);
        }
      }

      // Gentle pacing on top of token-bucket so we never burst the admin write
      // policy (60/min).
      await sleep(150);
    }
  }

  endRun({
    professions: professions.length,
    countPerProfession,
    targetTotal: professions.length * countPerProfession,
    created,
    published,
    failed,
    skippedDryRun: skipped,
  });
  process.exit(failed > 0 && created === 0 ? 1 : 0);
}

main().catch(e => {
  console.error('FATAL:', e?.stack || e);
  process.exit(99);
});
