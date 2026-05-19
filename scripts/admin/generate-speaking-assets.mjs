#!/usr/bin/env node
/**
 * generate-speaking-assets.mjs
 *
 * This legacy direct-AI asset generator is disabled. Speaking assets must be
 * authored/imported through backend grounded services or curated upload flows.
 *
 * Publish-gate per IContentPaperService.RequiredRolesFor("speaking"):
 *   { RoleCard, AssessmentCriteria, WarmUpQuestions }  (all must be primary)
 *
 * Usage:
 *   node scripts/admin/generate-speaking-assets.mjs [--limit N] [--dry-run]
 *
 * Required env: AI__ApiKey + API_BASE + ADMIN_EMAIL + ADMIN_PASSWORD
 * (run via scripts/admin/run-bulk.sh).
 */

import {
  parseFlags, startRun, endRun, adminFetch, logFailure,
  uploadMediaAsset, progress, sleep,
} from './_lib.mjs';

const flags = parseFlags();
const LIMIT = Number(flags.limit || 0) || 0;
const DRY_RUN = !!flags['dry-run'];

const SYS_PROMPT =
  'You are an OET (Occupational English Test) Speaking content author. ' +
  'Generate authentic, publication-quality role-play materials for ' +
  'healthcare professionals practising for the Speaking subtest. Produce ' +
  'ONLY the requested markdown — no preamble, no commentary, no JSON.';

function roleCardPrompt({ title, profession, topic }) {
  return `Generate an OET Speaking RoleCard (candidate card) in markdown for a ` +
    `${profession} role-play titled "${title}".\n\n` +
    `Scenario topic: ${topic}\n\n` +
    `Required sections (use these exact H2 headers):\n` +
    `## Setting\n` +
    `## Your Role\n` +
    `## Patient / Carer Background\n` +
    `## Task\n` +
    `Under "## Task" provide 4–6 bullet-point sub-tasks the candidate must address.\n` +
    `Use plain markdown. ~250–400 words total. Do not include answers or model responses.`;
}

function assessmentCriteriaPrompt({ title, profession }) {
  return `Generate the OET Speaking Assessment Criteria reference card in markdown ` +
    `to accompany the ${profession} role-play "${title}".\n\n` +
    `Cover the 9 standard OET Speaking assessment criteria:\n` +
    `1. Intelligibility\n2. Fluency\n3. Appropriateness of Language\n4. Resources of Grammar and Expression\n` +
    `5. Relationship building\n6. Understanding and incorporating the patient's perspective\n` +
    `7. Providing structure\n8. Information gathering\n9. Information giving\n\n` +
    `For each criterion give one H3 heading and a brief 2–3 sentence description of what the assessor looks for, ` +
    `plus 1–2 concrete examples relevant to this scenario. Use plain markdown. ~500–700 words total.`;
}

function warmUpPrompt({ title, profession, topic }) {
  return `Generate WarmUpQuestions in markdown for the OET Speaking role-play ` +
    `"${title}" (${profession}, topic: ${topic}).\n\n` +
    `Provide 8–10 numbered warm-up questions a candidate can rehearse with a study partner ` +
    `before the role-play. Mix profession-specific clinical questions with general ` +
    `English fluency questions. Each question one line, numbered "1." through "10." ` +
    `Use plain markdown. Do not include answers.`;
}

async function genMarkdown({ system, user, label }) {
  throw new Error(`Direct speaking asset AI authoring is disabled for ${label}. Use backend grounded speaking draft/import flows instead.`);
}

async function uploadAndAttach({ paperId, role, filename, body, title, displayOrder }) {
  const buf = Buffer.from(body, 'utf8');
  const assetId = await uploadMediaAsset(buf, {
    filename,
    mimeType: 'text/markdown',
    kind: 'document',
    intendedRole: role,
  });
  const attach = await adminFetch(`/v1/admin/papers/${paperId}/assets`, {
    method: 'POST',
    body: {
      role,
      mediaAssetId: assetId,
      part: null,
      title,
      displayOrder,
      makePrimary: true,
    },
  });
  if (!attach.ok) {
    throw new Error(`attach ${role} failed (${attach.status}): ${JSON.stringify(attach.data).slice(0, 300)}`);
  }
  return assetId;
}

async function listDraftSpeakingPapers() {
  // pageSize cap is 100 — paginate.
  const all = [];
  let page = 1;
  while (true) {
    const r = await adminFetch(`/v1/admin/papers?subtest=speaking&status=Draft&page=${page}&pageSize=50`);
    if (!r.ok) throw new Error(`list papers failed (${r.status}): ${JSON.stringify(r.data).slice(0, 200)}`);
    const rows = Array.isArray(r.data) ? r.data : (r.data?.items || []);
    if (rows.length === 0) break;
    all.push(...rows);
    if (rows.length < 50) break;
    page++;
  }
  return all;
}

async function main() {
  startRun('generate-speaking-assets');
  throw new Error('Direct speaking asset AI authoring is disabled. Use backend grounded speaking draft/import flows instead.');

  const drafts = await listDraftSpeakingPapers();
  console.log(`  Found ${drafts.length} Draft speaking paper(s)`);
  if (drafts.length === 0) {
    endRun({ found: 0 });
    return;
  }

  const targets = LIMIT > 0 ? drafts.slice(0, LIMIT) : drafts;
  let assetsDone = 0;
  let published = 0;
  let failed = 0;

  for (let i = 0; i < targets.length; i++) {
    const p = targets[i];
    const paperId = p.id;
    const title = p.title || `speaking-${paperId.slice(0, 8)}`;
    const profession = (p.professionId || p.profession || 'medicine').toLowerCase();
    const topic = p.cardType || p.topic || title;
    const slug = (p.slug || paperId).toLowerCase().replace(/[^a-z0-9-]/g, '-').slice(0, 60);

    console.log(progress(i + 1, targets.length, `${title}  (${profession})  id=${paperId}`));

    if (DRY_RUN) {
      console.log('  ⏭ dry-run; would generate + attach + publish');
      continue;
    }

    try {
      // 1. Generate three markdown bodies sequentially (token budget).
      const roleCardMd = await genMarkdown({
        system: SYS_PROMPT,
        user: roleCardPrompt({ title, profession, topic }),
        label: 'RoleCard',
      });
      console.log(`  ✓ AI RoleCard ${roleCardMd.length} chars`);

      const criteriaMd = await genMarkdown({
        system: SYS_PROMPT,
        user: assessmentCriteriaPrompt({ title, profession }),
        label: 'AssessmentCriteria',
      });
      console.log(`  ✓ AI AssessmentCriteria ${criteriaMd.length} chars`);

      const warmUpMd = await genMarkdown({
        system: SYS_PROMPT,
        user: warmUpPrompt({ title, profession, topic }),
        label: 'WarmUpQuestions',
      });
      console.log(`  ✓ AI WarmUpQuestions ${warmUpMd.length} chars`);

      // 2. Upload + attach as primary assets.
      await uploadAndAttach({
        paperId, role: 'RoleCard', filename: `${slug}-role-card.md`,
        body: roleCardMd, title: 'Role Card (Candidate)', displayOrder: 10,
      });
      console.log('  ✓ RoleCard attached');

      await uploadAndAttach({
        paperId, role: 'AssessmentCriteria', filename: `${slug}-assessment-criteria.md`,
        body: criteriaMd, title: 'Assessment Criteria', displayOrder: 20,
      });
      console.log('  ✓ AssessmentCriteria attached');

      await uploadAndAttach({
        paperId, role: 'WarmUpQuestions', filename: `${slug}-warm-up-questions.md`,
        body: warmUpMd, title: 'Warm-Up Questions', displayOrder: 30,
      });
      console.log('  ✓ WarmUpQuestions attached');

      assetsDone++;

      // 3. Publish.
      const pub = await adminFetch(`/v1/admin/papers/${paperId}/publish`, { method: 'POST', body: {} });
      if (pub.ok) {
        published++;
        console.log(`  ✓ published`);
      } else {
        const err = pub.data?.error || JSON.stringify(pub.data);
        console.log(`  ⚠ publish failed (${pub.status}): ${String(err).slice(0, 250)}`);
        logFailure('speaking-publish', { paperId, title }, new Error(`${pub.status}: ${err}`));
        failed++;
      }
    } catch (e) {
      console.log(`  ✗ ${e.message}`);
      logFailure('speaking-assets', { paperId, title }, e);
      failed++;
    }

    await sleep(250);
  }

  endRun({ found: drafts.length, processed: targets.length, assetsDone, published, failed });
  process.exit(failed > 0 && published === 0 ? 1 : 0);
}

main().catch(e => {
  console.error(e);
  process.exit(2);
});
