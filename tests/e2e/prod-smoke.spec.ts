import { expect, test, type ConsoleMessage, type Response } from '@playwright/test';

/**
 * Production smoke — Round 14
 * -----------------------------------------------------------------------------
 * Runs against LIVE production with a real learner login.
 *
 * Why separate from the normal e2e suite:
 *  - targets https://app.oetwithdrhesham.co.uk (not localhost)
 *  - uses credentials from env; nothing is hardcoded or committed
 *  - single-worker, single-browser, no state mutations
 *
 * Run locally (PowerShell):
 *   $env:PROD_LEARNER_EMAIL  = "you@example.com"
 *   $env:PROD_LEARNER_PASSWORD = "yourpassword"
 *   npx playwright test tests/e2e/prod-smoke.spec.ts --project=chromium --workers=1
 *
 * The test FAILS if:
 *  - sign-in does not succeed
 *  - any visited route returns 4xx/5xx for its main document
 *  - any JS error shows up in the console
 *  - any /v1/* API call returns 5xx
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;

test.describe.configure({ mode: 'serial' });

test.skip(
  !EMAIL || !PASSWORD,
  'Set PROD_LEARNER_EMAIL and PROD_LEARNER_PASSWORD env vars to run prod smoke.',
);

const LEARNER_SURFACES: Array<{ path: string; label: string }> = [
  { path: '/', label: 'dashboard home' },
  { path: '/study-plan', label: 'study plan' },
  { path: '/progress', label: 'progress' },
  { path: '/readiness', label: 'readiness' },
  { path: '/reading', label: 'reading' },
  { path: '/listening', label: 'listening' },
  { path: '/writing', label: 'writing' },
  { path: '/speaking', label: 'speaking' },
  { path: '/mocks', label: 'mocks' },
  { path: '/billing', label: 'billing' },
  { path: '/exam-guide', label: 'exam guide (RSC)' },
  { path: '/feedback-guide', label: 'feedback guide (RSC)' },
];

test('prod — learner journey end-to-end', async ({ page, context }) => {
  const consoleErrors: string[] = [];
  const apiFailures: string[] = [];

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() === 'error') {
      // Filter known-noise (third-party beacons, favicon, chunk-prefetch cancellation)
      const text = msg.text();
      if (/favicon|beacon|cancel/i.test(text)) return;
      consoleErrors.push(text);
    }
  });

  page.on('response', (res: Response) => {
    const url = res.url();
    if (url.includes('/v1/') && res.status() >= 500) {
      apiFailures.push(`${res.status()} ${url}`);
    }
  });

  // 1. Sign in
  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveURL(/\/sign-in/);

  await page.getByLabel(/email/i).fill(EMAIL!);
  await page.getByLabel(/password/i).fill(PASSWORD!);
  await page.getByRole('button', { name: /sign in/i }).click();

  // 2. Land on dashboard
  await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 30_000 });

  // Cookie sanity — there should be at least one auth cookie set
  const cookies = await context.cookies();
  expect(cookies.length, 'auth cookies present').toBeGreaterThan(0);

  // 3. Walk each learner surface; assert the main document responds and no JS error is thrown
  for (const surface of LEARNER_SURFACES) {
    const resp = await page.goto(`${PROD_URL}${surface.path}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000,
    });
    expect(resp, `${surface.label} response`).toBeTruthy();
    expect(
      resp!.status(),
      `${surface.label} (${surface.path}) status`,
    ).toBeLessThan(400);

    // Wait for the next-data / react hydration marker
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => {
      // A couple surfaces (dashboard) keep polling; domcontentloaded is enough
    });

    // Screenshot for human spot-check if the run is recorded
    await page.screenshot({
      path: `playwright-report-prod/${surface.label.replace(/\s+/g, '-')}.png`,
      fullPage: false,
    });
  }

  // 4. Sign out if the button exists (learner shell usually has a menu)
  const signOut = page.getByRole('button', { name: /sign out|log out/i }).first();
  if (await signOut.isVisible().catch(() => false)) {
    await signOut.click();
    await page.waitForURL(/\/sign-in/, { timeout: 10_000 }).catch(() => {});
  }

  // 5. Aggregate assertions
  expect(consoleErrors, 'no console errors').toEqual([]);
  expect(apiFailures, 'no API 5xx').toEqual([]);
});
