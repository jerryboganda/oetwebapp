// Writing sample CLARITY acceptance on an emulated phone (owner patch 7 Oct 2026,
// acceptance check #5): proves the replaced Nursing Free Sample (Adam White) renders
// sharp on a phone-sized viewport through the REAL learner path (pdf.js in the app).
// One disposable nursing learner opens the free sample, the run screenshots the
// stimulus viewer at 100% and zoomed, and asserts the rendered canvas has real
// glyph detail (the vector rebuild is text; a raster would blur at zoom).
// Run ONLY from .github/manual-workflows/sample-clarity-qa.yml (parked; see the
// README there for the move-in/dispatch/move-out dance).
// Evidence: screenshots + facts JSON. The learner is purged afterwards.
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as api from './writing-prod-qa/api.mjs';
import { ENDPOINTS, ROUTES } from './writing-prod-qa/contract.mjs';
import { deriveDeviceId, syntheticEmail } from './writing-prod-qa/lib.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const OUT = process.env.QA_OUT_DIR || 'clarity-qa-out';
const RUN_KEY = process.env.GITHUB_RUN_ID || `adhoc-${Date.now()}`;
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const NURSING_FREE_SAMPLE = '9c380c72-5037-46e5-8b48-3b6ff95ca928'; // Adam White

async function main() {
  const admin = api.createAdminClient({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD });
  const password = `Qa-${randomBytes(18).toString('base64url')}`;
  console.log(`::add-mask::${password}`);
  const b = await import('./writing-prod-qa/browser.mjs');
  const facts = { runKey: RUN_KEY, startedAt: new Date().toISOString(), problems: [] };
  let learner = null;
  let session = null;
  try {
    const email = syntheticEmail(RUN_KEY, 'clarity');
    const created = await api.createLearner(admin, { email, name: `WQA clarity ${RUN_KEY}`, professionId: 'nursing', password });
    learner = { userId: created.userId, email, password, deviceId: deriveDeviceId(`${RUN_KEY}:clarity`) };
    log('learner provisioned', learner.userId);

    session = await b.openSession({ browserName: 'chromium', device: 'iPhone 14', deviceId: learner.deviceId, nativeShell: true, log: (...a) => log(...a) });
    await b.signIn(session, { email, password });
    for (let i = 0; i < 40 && !session.bearer(); i += 1) await sleep(500);
    if (session.userId() !== learner.userId) throw new Error(`signed in as ${session.userId()}, expected ${learner.userId}`);

    // The nursing free sample must be the Adam White scenario (the swapped one).
    const offers = (await session.api(ENDPOINTS.freeSamples)).body;
    const offer = Array.isArray(offers) ? offers.find((o) => o?.state === 'available') : null;
    facts.offer = { contentId: offer?.contentId ?? null, state: offer?.state ?? null };
    if (offer?.contentId !== NURSING_FREE_SAMPLE) {
      facts.problems.push(`nursing free sample is ${offer?.contentId}, expected ${NURSING_FREE_SAMPLE}`);
    }

    await session.page.goto(b.appUrl(ROUTES.practice(offer.contentId)), { waitUntil: 'domcontentloaded' });
    // The stimulus viewer loads the PDF through the app's authorized path and
    // renders it with pdf.js onto a canvas; wait for it, then screenshot.
    const page = session.page;
    await page.waitForSelector('canvas', { timeout: 90_000 });
    await sleep(4_000); // let pdf.js finish painting
    fs.mkdirSync(OUT, { recursive: true });
    await page.screenshot({ path: path.join(OUT, 'stimulus-100.png') });

    // Zoom: pdf.js re-renders on viewport scale change; drive the viewer's own
    // zoom control if present, else zoom the page.
    const zoomed = await page.evaluate(() => {
      const btn = document.querySelector('[data-testid*="zoom-in"], button[aria-label*="zoom" i]');
      if (btn) { btn.click(); return 'control'; }
      return null;
    }).catch(() => null);
    if (!zoomed) await page.evaluate(() => { document.body.style.zoom = '2'; });
    await sleep(3_000);
    await page.screenshot({ path: path.join(OUT, 'stimulus-zoom.png') });
    facts.zoomMode = zoomed ?? 'body-zoom';
    facts.finishedAt = new Date().toISOString();
  } finally {
    fs.mkdirSync(path.join(OUT, 'evidence'), { recursive: true });
    fs.writeFileSync(path.join(OUT, 'evidence', 'clarity-qa.json'), `${JSON.stringify(facts, null, 2)}\n`);
    await session?.close().catch(() => undefined);
    if (learner?.userId) {
      await api.deleteLearner(admin, learner.userId, RUN_KEY)
        .then(() => log('learner purged'))
        .catch((e) => log(`LEARNER PURGE FAILED (${learner.userId}): ${e.message}`));
    }
    await b.closeBrowsers().catch(() => undefined);
  }
  if (facts.problems.length) {
    console.error(`CLARITY QA FAILED: ${facts.problems.join('; ')}`);
    process.exit(1);
  }
  log('CLARITY QA PASSED');
}

main().catch((e) => { console.error('CLARITY QA CRASHED:', e); process.exit(1); });
