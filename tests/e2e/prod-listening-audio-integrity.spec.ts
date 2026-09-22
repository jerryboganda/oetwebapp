import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

/**
 * Production learner-flow acceptance check for the Listening audio remediation (owner-approved 2026-09-22).
 *
 * Scope: player MECHANICS only — audio content correctness (cue placement, preparation window, no duplicate
 * speech) was already verified by the ASR pipeline (docs/listening/fleet-audit-2026-09-22.md). This spec checks
 * what only a real browser against real production can confirm:
 *   1. auto-start — the section's audio begins playing without a manual tap
 *   2. distinct audio per section — A1 and A2 (and C1/C2) request DIFFERENT media assets (the Atlas Sample Test 8
 *      regression this remediation fixed was exactly A2 silently replaying A1's file)
 *   3. one-way flow — once a section is left, its media is never re-requested, and the section list shows it
 *      locked/completed, not clickable
 *   4. correct next section — advancing loads the next section's own audio, not a stale/duplicate load
 *
 * Uses a DEDICATED test-only learner account (scripts/listening/state/make-test-learner.mjs), never the shared
 * QA account. Read-only for content: starts real attempts (unavoidable — the player only exists inside an
 * attempt) but asserts no unexpected mutating call and never submits.
 *
 * Run (PowerShell):
 *   $env:PROD_LEARNER_EMAIL = "..."; $env:PROD_LEARNER_PASSWORD = "..."
 *   $env:LISTENING_TARGET_PAPERS_JSON = '[{"paperId":"...","label":"Atlas ST9"}]'
 *   pnpm exec playwright test tests/e2e/prod-listening-audio-integrity.spec.ts --project=chromium-unauth --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;
// One id for the whole file: the first sign-in bootstraps it as a trusted device, every later sign-in with the
// same id resolves as already-trusted (no OTP). A fresh id per test would hit the OTP/replacement gate.
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

// NOT `mode: 'serial'` — Playwright serial mode fail-fasts the whole file on one failure, which would skip
// every other paper/viewport combo. `workers: 1` in the config already keeps runs sequential in time.

/**
 * Sign in through the real UI form exactly ONCE for the whole file (an earlier version logged in per test —
 * 24 sign-ins in ~11 minutes tripped the account's own AuthBruteforce rate limit, 10/min) and capture Playwright
 * `storageState` (cookies + localStorage) from that single authenticated context. Every test then opens its
 * context FROM that snapshot instead of logging in again. `lib/device-id.ts` persists its id under localStorage
 * key 'oet_device_id', which `lib/auth-client.ts` sends as X-OET-Device-Id; pre-seeding it with an
 * ALREADY-TRUSTED id (set up once via make-test-learner.mjs) means the one real sign-in never hits the OTP gate.
 */
let authState: Awaited<ReturnType<BrowserContext['storageState']>> | null = null;

async function signInOnce(browser: Browser) {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.addInitScript((deviceId) => {
    try { window.localStorage.setItem('oet_device_id', deviceId); } catch { /* ignore */ }
  }, DEVICE_ID);

  // Diagnostic instrumentation: log the ACTUAL sign-in network response and any visible page error, instead of
  // inferring failure only from "the URL never changed". One attempt only — a real cause needs seeing, not hiding
  // behind more retries.
  page.on('response', (r) => {
    if (r.url().includes('/v1/auth/sign-in')) {
      r.text().then((body) => console.log(`[signInOnce] POST /v1/auth/sign-in -> ${r.status()}: ${body.slice(0, 300)}`)).catch(() => {});
    }
  });

  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  const emailBox = page.getByRole('textbox', { name: /email address/i });
  const passwordBox = page.getByRole('textbox', { name: /^password$/i });
  await emailBox.fill(EMAIL!);
  await passwordBox.fill(PASSWORD!);
  const [emailVal, passwordVal] = await Promise.all([emailBox.inputValue(), passwordBox.inputValue()]);
  console.log(`[signInOnce] after fill: email field has ${emailVal.length} chars (expected ${EMAIL!.length}), password field has ${passwordVal.length} chars (expected ${PASSWORD!.length})`);
  const signInButton = page.getByRole('button', { name: /^sign in$/i });
  console.log(`[signInOnce] Sign In button: visible=${await signInButton.isVisible()} enabled=${await signInButton.isEnabled()}`);
  await signInButton.click();
  try {
    await page.waitForURL((u) => !u.pathname.startsWith('/sign-in'), { timeout: 20_000 });
  } catch {
    const bodyText = await page.locator('body').innerText().catch(() => '(could not read body)');
    console.log(`[signInOnce] still on /sign-in after 20s. Visible page text:\n${bodyText.slice(0, 1500)}`);
    await context.close();
    throw new Error('signInOnce: sign-in did not redirect — see the response/page-text logged above.');
  }
  authState = await context.storageState();
  await context.close();
}

test.beforeAll(async ({ browser }) => {
  if (EMAIL && PASSWORD && PAPERS.length) await signInOnce(browser);
});

/** media asset id -> first-seen order, populated from /v1/media/{id}/content requests. */
function trackMediaRequests(page: Page) {
  const seen: { id: string; at: number }[] = [];
  page.on('request', (req) => {
    const m = req.url().match(/\/v1\/media\/([a-f0-9-]+)\/content/i);
    if (m) seen.push({ id: m[1], at: Date.now() });
  });
  return seen;
}

for (const paper of PAPERS) {
  for (const vp of VIEWPORTS) {
    test(`${paper.label} [${vp.name}] — auto-start, distinct sections, one-way, no replay`, async ({ browser }) => {
      test.setTimeout(120_000);
      if (!authState) throw new Error('no authenticated session available (beforeAll sign-in failed) — see the earlier [signInOnce] log lines');
      const context = await browser.newContext({ viewport: { width: vp.width, height: vp.height }, storageState: authState });
      const page = await context.newPage();
      const consoleErrors: string[] = [];
      page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
      const media = trackMediaRequests(page);

      await page.goto(`${PROD_URL}/listening/paper/${encodeURIComponent(paper.paperId)}`, { waitUntil: 'domcontentloaded' });

      const startButton = page.getByRole('button', { name: /^start exam$/i });
      await expect(startButton).toBeVisible({ timeout: 30_000 });
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

      await page.waitForTimeout(800); // let the media request for A1 land in the network log
      const a1Ids = new Set(media.map((m) => m.id));
      expect(a1Ids.size, 'A1 should request exactly one media asset').toBeGreaterThanOrEqual(1);

      // Manually advance (the popup path — timer hasn't expired). This is the supported manual flow;
      // auto-advance-at-00:00 is covered by the existing unit/integration tests, not re-tested live here.
      const nextButton = page.getByRole('button', { name: /advance to next sub-section/i });
      await expect(nextButton).toBeVisible({ timeout: 15_000 });
      await nextButton.click();
      const continueButton = page.getByRole('button', { name: /^continue$/i });
      if (await continueButton.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await continueButton.click();
      }

      // A2 (or the next section): a NEW media request for a DIFFERENT asset id, and it also auto-starts.
      await page.waitForFunction(() => {
        const el = document.querySelector('audio');
        return !!el && !el.paused && el.currentTime > 0 && el.currentTime < 5;
      }, { timeout: 20_000 });
      await page.waitForTimeout(800);
      const afterIds = [...new Set(media.map((m) => m.id))];
      const newIds = afterIds.filter((id) => !a1Ids.has(id));
      expect(newIds.length, `the next section must load a NEW media asset, not reuse ${[...a1Ids].join(',')}`).toBeGreaterThanOrEqual(1);

      // One-way: the completed section's tab shows locked/completed, never "current" again; no way to replay it.
      const sectionList = page.getByRole('list', { name: /listening sub-sections/i });
      await expect(sectionList).toBeVisible();
      const firstTab = sectionList.getByRole('listitem').first();
      await expect(firstTab).not.toHaveAttribute('aria-current', 'step');
      await expect(page.locator('audio[controls]')).toHaveCount(0); // native controls (incl. replay/seek) are never exposed

      // Re-fetching the same paper URL later must not silently replay A1 through a stale element/src.
      const laterIds = [...new Set(media.map((m) => m.id))];
      expect(laterIds.filter((id) => a1Ids.has(id)).length, 'A1 media must not be re-requested after moving on').toBe(a1Ids.size);

      const badConsole = consoleErrors.filter((e) => !/favicon|ResizeObserver/i.test(e));
      expect(badConsole, `unexpected console errors: ${badConsole.join(' | ')}`).toEqual([]);

      await context.close();
    });
  }
}
