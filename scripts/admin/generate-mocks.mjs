/**
 * generate-mocks.mjs
 *
 * Creates "Full" MockBundles by composing one published ContentPaper per
 * sub-test (listening, reading, writing, speaking) in canonical OET order.
 *
 * Walks the admin UI flow exactly like a human would:
 *
 *   1. POST /v1/admin/mock-bundles
 *        { title, mockType:"full", professionId, appliesToAllProfessions,
 *          sourceProvenance, ... }
 *   2. POST /v1/admin/mock-bundles/{id}/sections (×4: L,R,W,S)
 *        Section subtest is derived server-side from the paper's SubtestCode,
 *        so we only pass { contentPaperId }. Sections are ordered by call sequence.
 *   3. POST /v1/admin/mock-bundles/{id}/publish
 *
 * Publish-gate (see MockService.ValidatePublishGate) requires:
 *   - sourceProvenance non-empty
 *   - exactly 4 sections in order listening, reading, writing, speaking
 *   - every section's ContentPaper.Status == Published
 *   - every paper's SourceProvenance non-empty
 *   - profession matches (or bundle.appliesToAllProfessions=true)
 *
 * Usage:
 *   node scripts/admin/generate-mocks.mjs [flags]
 *
 *   --count N            Number of bundles to create (default 10).
 *   --profession <slug>  Pin to one profession; otherwise rotates through the
 *                        canonical OET professions.
 *   --resume             Subtract bundles that already exist with the title
 *                        prefix "OET Full Mock #" so re-runs top up to N.
 *   --dry-run            Plan only — log selections, no API writes.
 *   --healthcheck        Run lib healthcheck and exit.
 */

import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, logFailure,
  healthcheck, progress, makeProvenance,
} from './_lib.mjs';

const flags = parseFlags();

// Rotation pool — OET sub-tests are graded per profession, so each Full mock is
// pinned to one. Order chosen to match the docs/PRODUCT-MANUAL profession list.
const PROFESSION_ROTATION = [
  'medicine', 'nursing', 'dentistry', 'pharmacy',
  'physiotherapy', 'occupational-therapy', 'optometry', 'podiatry',
  'dietetics', 'radiography', 'speech-pathology', 'veterinary',
];

const SUBTESTS = ['listening', 'reading', 'writing', 'speaking'];

const TITLE_PREFIX = 'OET Full Mock #';

/**
 * Fetch up to `pageSize` published papers for a subtest+profession. Returns
 * an array of { id, title, subtestCode, professionId, appliesToAllProfessions, ... }.
 * The backend admin papers endpoint returns a flat array (not paginated wrapper).
 */
async function listPublishedPapers({ subtest, profession }) {
  const r = await adminFetch('/v1/admin/papers', {
    method: 'GET',
    query: { subtest, profession, status: 'published', pageSize: 200 },
  });
  if (!r.ok) {
    throw new Error(`GET /v1/admin/papers (${subtest}, ${profession}) failed ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
  }
  return Array.isArray(r.data) ? r.data : (r.data?.items || []);
}

/**
 * Pick an unused published paper matching subtest+profession. The backend
 * query also returns all-profession papers. Cross-profession substitution and
 * reuse are intentionally disabled so mock bundles never mask content gaps.
 */
async function pickPaper({ subtest, profession, used }) {
  const candidates = await listPublishedPapers({ subtest, profession });
  const pick = candidates.find(p => !used.has(p.id));
  return pick ? { paper: pick } : { paper: null };
}

async function countExistingFullBundles() {
  const r = await adminFetch('/v1/admin/mock-bundles', { method: 'GET', query: { mockType: 'full' } });
  if (!r.ok) return 0;
  const rows = Array.isArray(r.data) ? r.data : (r.data?.items || []);
  return rows.filter(b => typeof b.title === 'string' && b.title.startsWith(TITLE_PREFIX)).length;
}

async function createBundle({ title, profession, dryRun }) {
  const body = {
    title,
    mockType: 'full',
    subtestCode: null,
    professionId: profession,
    appliesToAllProfessions: false,
    sourceProvenance: makeProvenance({
      kind: 'full mock bundle',
      profession,
      model: CONFIG.ai.chatModel,
    }),
    priority: 0,
    tagsCsv: 'ai-generated,full-mock',
    difficulty: 'standard',
    sourceStatus: 'ai_generated',
    qualityStatus: 'ready',
    releasePolicy: 'standard',
    topicTagsCsv: null,
    skillTagsCsv: null,
    watermarkEnabled: false,
    randomiseQuestions: false,
  };
  if (dryRun) {
    console.log(`    (dry-run) POST /v1/admin/mock-bundles  title="${title}" profession=${profession}`);
    return { id: `dry-${Date.now()}`, dry: true };
  }
  const r = await adminFetch('/v1/admin/mock-bundles', { method: 'POST', body });
  if (!r.ok) {
    throw new Error(`create bundle failed ${r.status}: ${JSON.stringify(r.data).slice(0, 400)}`);
  }
  return r.data;
}

async function addSection({ bundleId, paperId, dryRun }) {
  if (dryRun) {
    console.log(`    (dry-run) POST /v1/admin/mock-bundles/${bundleId}/sections  paperId=${paperId}`);
    return;
  }
  const r = await adminFetch(`/v1/admin/mock-bundles/${bundleId}/sections`, {
    method: 'POST',
    body: { contentPaperId: paperId, sectionOrder: null, timeLimitMinutes: null, reviewEligible: null },
  });
  if (!r.ok) {
    throw new Error(`add section failed ${r.status}: ${JSON.stringify(r.data).slice(0, 400)}`);
  }
}

async function publishBundle({ bundleId, dryRun }) {
  if (dryRun) {
    console.log(`    (dry-run) POST /v1/admin/mock-bundles/${bundleId}/publish`);
    return;
  }
  const r = await adminFetch(`/v1/admin/mock-bundles/${bundleId}/publish`, { method: 'POST', body: {} });
  if (!r.ok) {
    throw new Error(`publish bundle failed ${r.status}: ${JSON.stringify(r.data).slice(0, 400)}`);
  }
}

async function main() {
  if (flags.healthcheck) {
    startRun('generate-mocks-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  const runId = startRun('generate-mocks');
  const dryRun = !!flags['dry-run'];
  const want = Math.max(1, parseInt(flags.count ?? '10', 10) || 10);
  const pinnedProfession = flags.profession ? String(flags.profession) : null;
  const resume = !!flags.resume;

  let alreadyHave = 0;
  if (resume) {
    alreadyHave = await countExistingFullBundles();
    console.log(`Resume: ${alreadyHave} bundles already exist with prefix "${TITLE_PREFIX}".`);
  }
  const toCreate = Math.max(0, want - alreadyHave);
  console.log(`Plan: create ${toCreate} new MockBundle(s) (target=${want}, mode=${dryRun ? 'dry-run' : 'live'}).`);

  // Track paperIds we've consumed in this run so we never assign the same
  // paper to two bundles. (Backend allows reuse, but spreading is healthier
  // for learner variety.)
  const used = new Set();

  let created = 0, failed = 0, published = 0;

  for (let i = 0; i < toCreate; i++) {
    const bundleNumber = alreadyHave + i + 1;
    const profession = pinnedProfession ?? PROFESSION_ROTATION[i % PROFESSION_ROTATION.length];
    const title = `${TITLE_PREFIX}${bundleNumber} — ${profession}`;
    console.log(progress(i + 1, toCreate, `${title}`));

    // Resolve all 4 papers BEFORE creating the bundle. If any subtest is
    // missing, skip the entire bundle so we never leave a half-built draft.
    const picks = {};
    let skip = false;
    for (const subtest of SUBTESTS) {
      try {
        const { paper } = await pickPaper({ subtest, profession, used });
        if (!paper) {
          console.log(`    ⚠ no unused published ${subtest} paper available for ${profession} or all-profession — skipping bundle.`);
          logFailure('mocks', { title, subtest, profession }, new Error(`no unused published ${subtest} paper available for ${profession}`));
          skip = true;
          break;
        }
        console.log(`    · ${subtest}: ${paper.title} (${paper.id})`);
        picks[subtest] = paper;
      } catch (e) {
        logFailure('mocks', { title, subtest }, e);
        skip = true;
        break;
      }
    }
    if (skip) {
      failed++;
      continue;
    }

    let bundle;
    try {
      bundle = await createBundle({ title, profession, dryRun });
    } catch (e) {
      logFailure('mocks', { title }, e);
      failed++;
      continue;
    }

    // Mark papers as used only after the bundle row is created.
    for (const subtest of SUBTESTS) used.add(picks[subtest].id);

    // Add sections in canonical OET order.
    let sectionOk = true;
    for (const subtest of SUBTESTS) {
      try {
        await addSection({ bundleId: bundle.id, paperId: picks[subtest].id, dryRun });
      } catch (e) {
        logFailure('mocks', { title, bundleId: bundle.id, subtest }, e);
        sectionOk = false;
        break;
      }
    }
    if (!sectionOk) {
      failed++;
      continue;
    }

    try {
      await publishBundle({ bundleId: bundle.id, dryRun });
      published++;
    } catch (e) {
      logFailure('mocks', { title, bundleId: bundle.id, phase: 'publish' }, e);
      // Bundle was created + sectioned but not published — count as created.
    }
    created++;
  }

  endRun({ created, published, failed, dryRun, runId });
  process.exit(failed > 0 && created === 0 ? 1 : 0);
}

main().catch(e => {
  console.error('generate-mocks fatal:', e?.stack || e);
  process.exit(2);
});
