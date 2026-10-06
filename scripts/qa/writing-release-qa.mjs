// Writing 15-minute RELEASE acceptance (owner patch 7 Oct 2026, acceptance check #2).
// One disposable, NON-allowlisted learner submits the Writing free sample through the real UI, then the run
// proves the full release behaviour live:
//   1. the countdown starts at 15:00 right after submit;
//   2. a page reload does NOT reset it (it keeps falling from the pre-reload reading);
//   3. closing the browser and reopening ~75 s later does NOT reset it either (reopens on the SAME device);
//   4. Post Submissions shows the letter without stranding it;
//   5. the learner API NEVER reports the result before the server release instant, and at the release
//      instant the result is released WITHOUT any interaction (no click, no refetch triggered by the test);
//   6. the results page then opens with the score panel.
// Run ONLY from .github/workflows/writing-release-qa.yml (admin secrets + browser deps live there).
// Evidence: $QA_OUT_DIR/evidence/release-qa.json + screenshots. The learner is purged after the evidence
// is written (the harness cleanup policy). No letter/case-note text is printed.
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as api from './writing-prod-qa/api.mjs';
import { ENDPOINTS, ROUTES, TEST_IDS } from './writing-prod-qa/contract.mjs';
import { deriveDeviceId, scriptFor, syntheticEmail } from './writing-prod-qa/lib.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const OUT = process.env.QA_OUT_DIR || 'release-qa-out';
const EVIDENCE = path.join(OUT, 'evidence');
const RUN_KEY = process.env.GITHUB_RUN_ID || `adhoc-${Date.now()}`;
const log = (...args) => console.log(new Date().toISOString().slice(11, 19), ...args);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const tid = (id) => `[data-testid="${id}"]`;
const REOPEN_MS = 75_000;

function writeEvidence(name, value) {
  fs.mkdirSync(EVIDENCE, { recursive: true });
  fs.writeFileSync(path.join(EVIDENCE, name), `${JSON.stringify(value, null, 2)}\n`);
}

/** mm:ss -> seconds; null when the timer is absent (it hides once the window has elapsed). */
async function readReleaseTimer(page) {
  const text = await page.locator(tid('writing-release-timer')).first()
    .innerText({ timeout: 5_000 }).catch(() => null);
  if (!text) return null;
  const m = /^(\d{1,2}):(\d{2})$/.exec(text.trim());
  return m ? Number(m[1]) * 60 + Number(m[2]) : null;
}

async function rawSubmission(session, submissionId) {
  // ?qa=raw marks this read as harness server-truth (the fault_mode=client route in the main harness never rewrites it).
  const res = await session.api(ENDPOINTS.submission(submissionId) + '?qa=raw');
  if (res.status !== 0 && res.status >= 300) throw new Error(`GET submission: HTTP ${res.status}`);
  return res.body;
}

/** Reopened browser: cookies survive but the session snapshot lives in sessionStorage, so re-sign-in on the
 * SAME device when the app sends us to sign-in (exactly what the main harness does). */
async function reopenSession(b, learner) {
  const session = await b.openSession({ deviceId: learner.deviceId, storageState: learner.storageState, log: (...a) => log(...a) });
  await session.page.goto(b.appUrl('/writing'), { waitUntil: 'domcontentloaded' });
  const signedOut = await session.page.waitForURL((u) => u.pathname.startsWith(ROUTES.signIn), { timeout: 15_000 }).then(() => true, () => false);
  for (let i = 0; i < 20 && !signedOut && !session.bearer(); i += 1) await sleep(500);
  if (signedOut || !session.bearer()) {
    await b.signIn(session, { email: learner.email, password: learner.password });
    for (let i = 0; i < 40 && !session.bearer(); i += 1) await sleep(500);
  }
  if (session.userId() && session.userId() !== learner.userId) throw new Error(`reopened as ${session.userId()}, expected ${learner.userId}`);
  return session;
}

async function main() {
  const admin = api.createAdminClient({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD });
  const password = `Qa-${randomBytes(18).toString('base64url')}`;
  console.log(`::add-mask::${password}`);
  const b = await import('./writing-prod-qa/browser.mjs');
  const evidence = { runKey: RUN_KEY, startedAt: new Date().toISOString(), steps: [], problems: [] };
  const step = (name, facts) => { evidence.steps.push({ name, at: new Date().toISOString(), ...facts }); log(name, JSON.stringify(facts ?? {})); };
  const problem = (message) => { evidence.problems.push(message); log('PROBLEM:', message); };

  let learner = null;
  let session = null;
  try {
    // ---- provision a fresh, NON-allowlisted learner (medicine; the free sample needs no credits) ----
    const email = syntheticEmail(RUN_KEY, 'release');
    const created = await api.createLearner(admin, { email, name: `WQA release ${RUN_KEY}`, professionId: 'medicine', password });
    learner = { userId: created.userId, email, password, deviceId: deriveDeviceId(`${RUN_KEY}:release`) };
    step('learner-provisioned', { userId: learner.userId });

    // ---- open the free sample through the real UI ----
    session = await b.openSession({ deviceId: learner.deviceId, log: (...a) => log(...a) });
    await b.signIn(session, { email, password });
    for (let i = 0; i < 40 && !session.bearer(); i += 1) await sleep(500);
    if (session.userId() !== learner.userId) throw new Error(`signed in as ${session.userId()}, expected ${learner.userId}`);

    const offers = (await session.api(ENDPOINTS.freeSamples)).body;
    const offer = Array.isArray(offers) ? offers.find((o) => o?.state === 'available') : null;
    if (!offer?.contentId) throw new Error(`no available writing free sample (${JSON.stringify(offers?.map?.((o) => o?.state) ?? offers)})`);
    step('free-sample-offer', { contentId: offer.contentId });
    const scripts = JSON.parse(fs.readFileSync(path.join(here, 'writing-prod-qa', 'scripts.json'), 'utf8')).scripts;
    const script = scriptFor(scripts, 'medicine', 'routine');
    if (!script?.text) throw new Error('no medicine routine script in scripts.json');

    await session.page.goto(b.appUrl(ROUTES.practice(offer.contentId)), { waitUntil: 'domcontentloaded' });
    await b.waitForWritingPhase(session, 'real');
    await b.typeText(session.page, script.text);
    const draft = await b.waitDraftEquals(session, offer.contentId, script.text, 15_000);
    if (!draft.ok) problem('the server draft did not equal the typed text within 15 s');

    // ---- submit; the app navigates to the grading page ----
    const submissionId = await b.submit(session);
    evidence.submissionId = submissionId;
    step('submitted', { submissionId });

    const raw0 = await rawSubmission(session, submissionId);
    step('post-submit-server-truth', {
      status: raw0?.status, releaseState: raw0?.releaseState, releaseAt: raw0?.releaseAt, serverNow: raw0?.serverNow,
    });
    if (raw0?.releaseState === 'released') problem('the server released the result at once (learner must not be allowlisted)');
    if (!raw0?.releaseAt) problem('releaseAt is absent for a non-allowlisted learner');
    const releaseMs = raw0?.releaseAt ? Date.parse(raw0.releaseAt) : null;
    if (releaseMs) {
      const windowSec = Math.round((releaseMs - Date.parse(raw0.serverNow ?? new Date().toISOString())) / 1000);
      if (windowSec < 14 * 60 || windowSec > 15 * 60) problem(`release window is ${windowSec}s, expected 15:00`);
    }

    // ---- 1. the countdown is on the grading page, reading just under 15:00 ----
    const page = session.page;
    await page.locator(tid('writing-release-countdown')).first().waitFor({ state: 'visible', timeout: 30_000 });
    const atSubmit = await readReleaseTimer(page);
    step('countdown-at-submit', { seconds: atSubmit });
    if (atSubmit === null) problem('the release countdown is not visible after submit');
    else if (atSubmit > 15 * 60 || atSubmit < 14 * 60 + 30) problem(`the countdown started at ${atSubmit}s, expected just under 15:00`);
    await page.locator(tid('writing-release-countdown')).first().screenshot({ path: path.join(OUT, 'countdown-at-submit.png') }).catch(() => undefined);

    // ---- 2. reload: the countdown keeps falling, never resets to 15:00 ----
    await sleep(8_000);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator(tid('writing-release-countdown')).first().waitFor({ state: 'visible', timeout: 30_000 });
    const afterReload = await readReleaseTimer(page);
    step('countdown-after-reload', { seconds: afterReload });
    if (afterReload === null) problem('the countdown vanished after a reload');
    else if (atSubmit !== null && afterReload >= atSubmit) problem(`the countdown reset on reload (${atSubmit}s -> ${afterReload}s)`);

    // ---- 3. close the browser, reopen ~75 s later on the same device: still falling, not reset ----
    learner.storageState = await session.context.storageState();
    const beforeClose = await readReleaseTimer(page);
    await session.close();
    await sleep(REOPEN_MS);
    session = await reopenSession(b, learner);
    await session.page.goto(b.appUrl(ROUTES.grading(submissionId)), { waitUntil: 'domcontentloaded' });
    await session.page.locator(tid('writing-release-countdown')).first().waitFor({ state: 'visible', timeout: 30_000 });
    const afterReopen = await readReleaseTimer(page);
    step('countdown-after-reopen', { seconds: afterReopen, closedMs: REOPEN_MS });
    if (afterReopen === null) problem('the countdown is gone after reopen (before the release time)');
    else {
      if (beforeClose !== null && afterReopen >= beforeClose) problem(`the countdown reset/paused across reopen (${beforeClose}s -> ${afterReopen}s)`);
      const expected = beforeClose - Math.round(REOPEN_MS / 1000) - 15; // re-sign-in + reload slack
      if (afterReopen < expected) problem(`the countdown lost time across reopen (${beforeClose}s -> ${afterReopen}s over ${REOPEN_MS}ms)`);
    }

    // ---- 4. Post Submissions shows the letter, not stranded ----
    await session.page.goto(b.appUrl(ROUTES.postSubmissions), { waitUntil: 'domcontentloaded' });
    const row = session.page.locator(`${tid(TEST_IDS.postSubmissionRow)}[data-scenario-id="${offer.contentId}"]`).first();
    if (!(await row.waitFor({ state: 'visible', timeout: 30_000 }).then(() => true, () => false))) {
      problem('the submission row is missing on Post Submissions');
    } else step('post-submissions-row', { state: await row.getAttribute('data-state') });
    await session.page.screenshot({ path: path.join(OUT, 'post-submissions.png'), fullPage: false }).catch(() => undefined);

    // ---- 5. wait for the release: server truth must flip at the release instant, untouched ----
    await session.page.goto(b.appUrl(ROUTES.grading(submissionId)), { waitUntil: 'domcontentloaded' });
    const deadline = Date.now() + 25 * 60_000;
    let released = null;
    let sawEarlyRelease = false;
    let lastServerNow = null;
    while (Date.now() < deadline) {
      const raw = await rawSubmission(session, submissionId);
      const nowMs = raw?.serverNow ? Date.parse(raw.serverNow) : Date.now();
      lastServerNow = raw?.serverNow ?? null;
      if (raw?.releaseState === 'released') {
        released = { at: new Date().toISOString(), status: raw.status, serverNow: raw.serverNow };
        if (releaseMs && nowMs < releaseMs - 10_000) sawEarlyRelease = true;
        break;
      }
      await sleep(5_000);
    }
    const timerNearZero = await readReleaseTimer(session.page);
    step('release-observed', { ...released, sawEarlyRelease, timerReadingAfter: timerNearZero });
    if (!released) problem('the result was NEVER released within 25 min of waiting');
    if (sawEarlyRelease) problem('the server released the result BEFORE the 15-minute instant');
    if (released && releaseMs) {
      const lateByS = Math.round((Date.now() - releaseMs) / 1000);
      if (lateByS > 90) problem(`the release landed ~${lateByS}s after the release instant (grading was still incomplete at 00:00)`);
      if (timerNearZero !== null) problem(`the countdown is still rendered after release (reading ${timerNearZero}s)`);
    }

    // ---- 6. the results page opens with the score panel, without any interaction from here on ----
    await session.page.goto(b.appUrl(ROUTES.results(submissionId)), { waitUntil: 'domcontentloaded' });
    const scoreVisible = await session.page.locator(tid(TEST_IDS.scorePanel)).first()
      .waitFor({ state: 'visible', timeout: 60_000 }).then(() => true, () => false);
    if (!scoreVisible) problem('the results page did not show the score panel after release');
    step('results-opened', { scoreVisible, serverNow: lastServerNow });
    await session.page.screenshot({ path: path.join(OUT, 'results-released.png'), fullPage: false }).catch(() => undefined);

    // ---- provider evidence: who graded it, and every reviewer call ----
    const [gradeRows, reviewRows] = await Promise.all([
      api.usageRows(admin, { userId: learner.userId }),
      admin.get(`/v1/admin/ai/usage?${new URLSearchParams({ featureCode: 'writing.grade.review', userId: learner.userId, pageSize: '50' })}`).catch(() => null),
    ]);
    evidence.usage = {
      grade: gradeRows.map((r) => ({ providerId: r.providerId, model: r.model, outcome: r.outcome, errorCode: r.errorCode, createdAt: r.createdAt })),
      review: (reviewRows?.rows ?? []).map((r) => ({ providerId: r.providerId, model: r.model, outcome: r.outcome, errorCode: r.errorCode, createdAt: r.createdAt })),
    };
    evidence.finishedAt = new Date().toISOString();
  } finally {
    writeEvidence('release-qa.json', evidence);
    await session?.close().catch(() => undefined);
    if (learner?.userId) {
      await api.deleteLearner(admin, learner.userId, RUN_KEY)
        .then(() => log('learner purged'))
        .catch((e) => log(`LEARNER PURGE FAILED (${learner.userId}): ${e.message}`));
    }
    await b.closeBrowsers().catch(() => undefined);
  }

  if (evidence.problems.length) {
    console.error(`RELEASE QA FAILED: ${evidence.problems.length} problem(s)`);
    for (const p of evidence.problems) console.error(` - ${p}`);
    process.exit(1);
  }
  log('RELEASE QA PASSED');
}

main().catch((error) => {
  console.error('RELEASE QA CRASHED:', error);
  process.exit(1);
});
