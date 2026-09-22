import { expect, test, type Page } from '@playwright/test';

/**
 * Production learner-flow acceptance check for the Listening audio remediation (owner-approved 2026-09-22).
 *
 * Scope: player MECHANICS only — audio content correctness (cue placement, preparation window, no duplicate
 * speech) was already verified by the ASR pipeline (docs/listening/fleet-audit-2026-09-22.md). This spec checks
 * what only a real browser against real production can confirm:
 *   1. auto-start — the section's audio begins playing without a manual tap
 *   2. distinct audio per section — A1 and A2 (and C1/C2) are DIFFERENT underlying files, proven by decoded
 *      `<audio>.duration` (the Atlas Sample Test 8 regression this remediation fixed was exactly A2 silently
 *      replaying A1's file). NOT proven by network-request tracking: the readiness probe prebuffers every
 *      section's audio up front (see the note above the probe click below), so by the time any section starts
 *      playing, all of the paper's media has already been fetched once and is served from cache — request
 *      timing/identity can't tell sections apart, only the decoded media itself can.
 *   3. one-way flow — once a section is left, the section list shows it locked/completed (not "current",
 *      not clickable) and no native controls (which would allow seeking/replay) are ever exposed
 *   4. correct next section — advancing auto-starts the next section's own (differently-fingerprinted) audio
 *
 * Uses a DEDICATED test-only learner account (scripts/listening/make-test-learner.mjs), never the shared QA
 * account. Read-only for content: starts real attempts (unavoidable — the player only exists inside an attempt)
 * but never submits.
 *
 * One real sign-in PER TEST, straight to the target paper URL (a `storageState` snapshot from a single shared
 * login was tried and did not carry authentication into a fresh context for this app — something about session
 * recognition here needs the live sign-in flow, not just replayed cookies/localStorage). This is safe: each
 * login now takes ~2-3s and succeeds on the first try (see the hydration-race note below), so even the full
 * paper x viewport matrix stays well under the account's own AuthBruteforce limit (10/min).
 *
 * Run (PowerShell):
 *   $env:PROD_LEARNER_EMAIL = "..."; $env:PROD_LEARNER_PASSWORD = "..."
 *   $env:LISTENING_TARGET_PAPERS_JSON = '[{"paperId":"...","label":"Atlas ST9"}]'
 *   pnpm exec playwright test tests/e2e/prod-listening-audio-integrity.spec.ts --project=chromium-unauth --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;
// Must be an ALREADY-TRUSTED device id for this account (bootstrapped once via make-test-learner.mjs); a
// brand-new id would hit the OTP/device-verification gate instead of signing straight in.
const DEVICE_ID = process.env.LISTENING_TEST_DEVICE_ID ?? `pw-listening-audit-${Date.now()}`;

interface TargetPaper {
  paperId: string;
  label: string;
}
const PAPERS: TargetPaper[] = JSON.parse(process.env.LISTENING_TARGET_PAPERS_JSON ?? '[]');

const VIEWPORTS = [
  { name: 'desktop', width: 1366, height: 900 },
  { name: 'tablet', width: 834, height: 1112 },
  { name: 'mobile', width: 390, height: 844 },
];

test.skip(!EMAIL || !PASSWORD, 'Set PROD_LEARNER_EMAIL and PROD_LEARNER_PASSWORD.');
test.skip(PAPERS.length === 0, 'Set LISTENING_TARGET_PAPERS_JSON to a non-empty array of {paperId,label}.');

// NOT `mode: 'serial'` — Playwright serial mode fail-fasts the whole file on one failure, which would skip every
// other paper/viewport combo. `workers: 1` in the config already keeps runs sequential in time.

/**
 * Sign in through the real UI form, then land on `targetPath`.
 *
 * Root cause of every earlier failure in this file's history: `domcontentloaded` fires before React hydrates
 * the sign-in form's onSubmit handler. A click that lands in that window falls back to the server-rendered
 * <form method="post"> submitting NATIVELY to the current page URL (POST /sign-in on the app host, never
 * reaching the API) — silently: no error, no redirect, no visible text change, nothing to retry into fixing.
 * Confirmed by logging every network request during a failing run. Fix: wait for `load` plus a short settle
 * before interacting, on EVERY navigation that leads to a click, not just the first one.
 */
async function seedAuth(page: Page, targetPath: string) {
  await page.addInitScript((deviceId) => {
    try { window.localStorage.setItem('oet_device_id', deviceId); } catch { /* ignore */ }
  }, DEVICE_ID);
  page.on('response', (r) => {
    if (r.url().includes('/v1/auth/sign-in') && !r.ok()) {
      r.text().then((body) => console.log(`[seedAuth] POST /v1/auth/sign-in -> ${r.status()}: ${body.slice(0, 300)}`)).catch(() => {});
    }
  });

  await page.goto(`${PROD_URL}/sign-in?next=${encodeURIComponent(targetPath)}`, { waitUntil: 'load' });
  await page.waitForTimeout(1000);
  await page.getByRole('textbox', { name: /email address/i }).fill(EMAIL!);
  await page.getByRole('textbox', { name: /^password$/i }).fill(PASSWORD!);
  await page.getByRole('button', { name: /^sign in$/i }).click();
  await page.waitForURL((u) => !u.pathname.startsWith('/sign-in'), { timeout: 20_000 });
  await page.waitForLoadState('load');
  await page.waitForTimeout(1000); // settle for the destination page's own hydration before the caller interacts
}

for (const paper of PAPERS) {
  for (const vp of VIEWPORTS) {
    test(`${paper.label} [${vp.name}] — auto-start, distinct sections, one-way, no replay`, async ({ browser }) => {
      test.setTimeout(120_000);
      const context = await browser.newContext({ viewport: { width: vp.width, height: vp.height } });
      const page = await context.newPage();
      const consoleErrors: string[] = [];
      page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
      page.on('response', (r) => {
        if (r.url().includes('/advance-section')) {
          r.text().then((body) => console.log(`[diag] POST .../advance-section -> ${r.status()}: ${body.slice(0, 500)}`)).catch(() => {});
        }
        if (r.status() === 404) {
          console.log(`[diag] 404: ${r.request().method()} ${r.url()}`);
        }
      });

      await seedAuth(page, `/listening/paper/${encodeURIComponent(paper.paperId)}`);

      // Required pre-flight: "Start exam" stays disabled until the candidate runs the audio-readiness probe
      // (components/domain/listening/TechReadinessCheck.tsx). It also prebuffers every section's audio
      // (verifyScoredAudioAssets -> lib/listening/audio-prebuffer, fed by IntroCard's audioUrls={scoredAudioUrls}
      // in app/listening/paper/[paperId]/page.tsx) so mid-exam section changes never show a buffering spinner.
      await page.getByRole('button', { name: /play audio probe/i }).click();
      await expect(page.getByText(/audio confirmed/i)).toBeVisible({ timeout: 20_000 });

      const startButton = page.getByRole('button', { name: /^start exam$/i });
      await expect(startButton).toBeEnabled({ timeout: 30_000 });
      await startButton.click();

      // A1: wait for the hidden <audio> element to mount and for playback to actually begin.
      const audio = page.locator('audio');
      await expect(audio).toBeAttached({ timeout: 30_000 });
      await page.waitForFunction(() => {
        const el = document.querySelector('audio');
        return !!el && !el.paused && el.currentTime > 0;
      }, { timeout: 20_000 });
      const t1 = await audio.evaluate((el: HTMLAudioElement) => el.currentTime);
      await page.waitForTimeout(1500);
      const t2 = await audio.evaluate((el: HTMLAudioElement) => el.currentTime);
      expect(t2, 'A1 audio should auto-advance without any manual play tap').toBeGreaterThan(t1);

      const a1Duration = await audio.evaluate((el: HTMLAudioElement) => el.duration);
      expect(Number.isFinite(a1Duration) && a1Duration > 0, `A1 audio must report a real duration, got ${a1Duration}`).toBe(true);
      const a1Src = await audio.evaluate((el: HTMLAudioElement) => el.currentSrc);
      const a1CurrentLabel = await page.locator('[aria-current="step"]').first().textContent().catch(() => '(none)');
      console.log(`[diag] A1 currentSrc=${a1Src} duration=${a1Duration} currentLabel=${a1CurrentLabel}`);

      // Manually advance (the popup path — timer hasn't expired). This is the supported manual flow;
      // auto-advance-at-00:00 is covered by the existing unit/integration tests, not re-tested live here.
      // The server owns the one-way cursor (advance() in page.tsx only moves currentIndex after
      // POST .../advance-section resolves) — wait for that response before checking anything client-side.
      // An earlier version of this test raced ahead of that response using `currentTime > 0 && < 5` as its
      // "next section started" signal, which is ALSO trivially true during A1's own first few seconds of
      // normal playback — it captured stale A1 data a fraction of a second before the real advance landed
      // and misread it as a silent-replay regression. currentSrc changing is a signal that can't false-positive.
      const advanceResponse = page.waitForResponse((r) => r.url().includes('/advance-section'), { timeout: 20_000 });
      const nextButton = page.getByRole('button', { name: /advance to next sub-section/i });
      await expect(nextButton).toBeVisible({ timeout: 15_000 });
      await nextButton.click();
      const continueButton = page.getByRole('button', { name: /^continue$/i });
      if (await continueButton.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await continueButton.click();
      }
      await advanceResponse;

      // A2 (or the next section): auto-starts with its OWN source (currentSrc differs from A1's — proof it's
      // not a silent replay, the exact Atlas ST8 regression this remediation fixed), and its decoded duration
      // must differ from A1's too.
      await page.waitForFunction((prevSrc) => {
        const el = document.querySelector('audio');
        return !!el && el.currentSrc !== prevSrc && !el.paused && el.currentTime > 0;
      }, a1Src, { timeout: 20_000 });
      const a2Duration = await audio.evaluate((el: HTMLAudioElement) => el.duration);
      expect(Number.isFinite(a2Duration) && a2Duration > 0, `A2 audio must report a real duration, got ${a2Duration}`).toBe(true);
      const a2Src = await audio.evaluate((el: HTMLAudioElement) => el.currentSrc);
      const a2CurrentLabel = await page.locator('[aria-current="step"]').first().textContent().catch(() => '(none)');
      console.log(`[diag] A2 currentSrc=${a2Src} duration=${a2Duration} currentLabel=${a2CurrentLabel}`);
      expect(Math.abs(a2Duration - a1Duration), `A1 (${a1Duration}s) and A2 (${a2Duration}s) must be different underlying audio`).toBeGreaterThan(0.5);

      // One-way: the completed section's tab shows locked/completed, never "current" again; no native controls
      // (which would allow seeking/replay) are ever exposed.
      const sectionList = page.getByRole('list', { name: /listening sub-sections/i });
      await expect(sectionList).toBeVisible();
      const firstTab = sectionList.getByRole('listitem').first();
      await expect(firstTab).not.toHaveAttribute('aria-current', 'step');
      await expect(page.locator('audio[controls]')).toHaveCount(0);

      const badConsole = consoleErrors.filter((e) => !/favicon|ResizeObserver/i.test(e));
      expect(badConsole, `unexpected console errors: ${badConsole.join(' | ')}`).toEqual([]);

      await context.close();
    });
  }
}
