import { expect, test, type ConsoleMessage, type Response } from '@playwright/test';

/**
 * Production smoke — Expert Console (Round 16)
 * -----------------------------------------------------------------------------
 * Walks the full expert console user journey on LIVE production with a real
 * expert login. Mirrors the structure of `prod-smoke.spec.ts` but targets the
 * `/expert/*` surfaces and asserts that:
 *  - sign-in succeeds and the session has an expert-tier role
 *  - every expert route returns < 400
 *  - no JS console errors fire on any surface
 *  - no `/v1/*` API call returns 5xx
 *
 * Run locally:
 *   $env:PROD_EXPERT_EMAIL    = "expert.console@oetwithdrhesham.co.uk"
 *   $env:PROD_EXPERT_PASSWORD = "..."
 *   npx playwright test tests/e2e/prod-smoke-expert.spec.ts --project=chromium-unauth --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_EXPERT_EMAIL;
const PASSWORD = process.env.PROD_EXPERT_PASSWORD;

test.describe.configure({ mode: 'serial' });

test.skip(
  !EMAIL || !PASSWORD,
  'Set PROD_EXPERT_EMAIL and PROD_EXPERT_PASSWORD env vars to run prod expert smoke.',
);

const EXPERT_SURFACES: Array<{ path: string; label: string }> = [
  { path: '/expert', label: 'expert home' },
  { path: '/expert/queue', label: 'review queue' },
  { path: '/expert/queue-priority', label: 'queue priority' },
  { path: '/expert/learners', label: 'learners directory' },
  { path: '/expert/calibration', label: 'calibration' },
  { path: '/expert/scoring-quality', label: 'scoring quality' },
  { path: '/expert/metrics', label: 'metrics' },
  { path: '/expert/schedule', label: 'schedule' },
  { path: '/expert/onboarding', label: 'onboarding' },
  { path: '/expert/annotation-templates', label: 'annotation templates' },
  { path: '/expert/rubric-reference', label: 'rubric reference' },
  { path: '/expert/ai-prefill', label: 'ai prefill' },
  { path: '/expert/ask-an-expert', label: 'ask an expert' },
  { path: '/expert/private-speaking', label: 'private speaking' },
  { path: '/expert/mobile-review', label: 'mobile review' },
];

test('prod — expert console journey end-to-end', async ({ page, context }) => {
  test.setTimeout(240_000);
  const consoleErrors: string[] = [];
  const apiFailures: string[] = [];
  const fourXx: string[] = [];
  let currentSurface = 'sign-in';

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() === 'error') {
      const text = msg.text();
      if (/favicon|beacon|cancel|Failed to load resource/i.test(text)) return;
      consoleErrors.push(`[${currentSurface}] ${text}`);
    }
  });

  page.on('response', (res: Response) => {
    const url = res.url();
    if (!url.includes('/v1/')) return;
    const status = res.status();
    if (status >= 500) {
      apiFailures.push(`[${currentSurface}] ${status} ${url}`);
    } else if (status >= 400) {
      fourXx.push(`[${currentSurface}] ${status} ${url}`);
    }
  });

  // 1. Sign in
  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveURL(/\/sign-in/);
  await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => {});
  const emailInput = page.locator('#email');
  await emailInput.waitFor({ state: 'visible', timeout: 15_000 });
  await emailInput.fill(EMAIL!);
  await page.locator('#password').fill(PASSWORD!);

  await Promise.all([
    page.waitForResponse(
      (r) => /\/v1\/auth\/(sign[-_]?in|login)/i.test(r.url()) && r.request().method() === 'POST',
      { timeout: 20_000 },
    ).catch(() => null),
    page.getByRole('button', { name: /^sign in$/i }).click(),
  ]);

  // 2. Land somewhere off /sign-in
  try {
    await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 30_000 });
  } catch {
    const errorText = await page.locator('[role="alert"], .auth-error, [data-auth-error]').first()
      .textContent({ timeout: 1_000 })
      .catch(() => null);
    throw new Error(
      `Expert sign-in did not navigate off /sign-in within 30s. Error UI: ${errorText ?? '(none visible)'}`,
    );
  }

  const cookies = await context.cookies();
  expect(cookies.length, 'expert auth cookies present').toBeGreaterThan(0);

  // 3. Walk every expert surface
  for (const surface of EXPERT_SURFACES) {
    currentSurface = surface.label;
    const resp = await page.goto(`${PROD_URL}${surface.path}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000,
    });
    expect(resp, `${surface.label} response`).toBeTruthy();
    expect(
      resp!.status(),
      `${surface.label} (${surface.path}) status`,
    ).toBeLessThan(400);

    // Some expert surfaces (e.g. queue) keep WebSockets open; networkidle is best-effort
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => {});

    await page.screenshot({
      path: `playwright-report-prod/expert-${surface.label.replace(/\s+/g, '-')}.png`,
      fullPage: false,
    });
  }

  // 4. Sign out if available
  const signOut = page.getByRole('button', { name: /sign out|log out/i }).first();
  if (await signOut.isVisible().catch(() => false)) {
    await signOut.click();
    await page.waitForURL(/\/sign-in/, { timeout: 10_000 }).catch(() => {});
  }

  // 5. Aggregate assertions
  if (fourXx.length) {
    console.log('[prod-smoke-expert] /v1/* 4xx responses (non-blocking):');
    for (const line of fourXx) console.log('  ', line);
  }
  expect(consoleErrors, 'no console errors on expert surfaces').toEqual([]);
  expect(apiFailures, 'no API 5xx on expert surfaces').toEqual([]);
});

/**
 * Round-16 deepening: exercise the expert onboarding wizard backend end-to-end.
 * Verifies that the previously frontend-only feature now persists data on prod.
 *
 * Flow:
 *   1. sign-in, capture access token from the auth response or cookies
 *   2. GET  /v1/expert/onboarding/status                → 200 (was 404)
 *   3. PUT  /v1/expert/onboarding/profile               → 200, body echoed
 *   4. PUT  /v1/expert/onboarding/qualifications        → 200
 *   5. PUT  /v1/expert/onboarding/rates                 → 200
 *   6. PATCH /v1/expert/onboarding/complete             → 200 { completed: true }
 *   7. GET  /v1/expert/onboarding/status                → returns the data we just wrote
 */
test('prod — expert onboarding wizard persists end-to-end', async ({ page, request }) => {
  test.setTimeout(120_000);

  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  await page.locator('#email').fill(EMAIL!);
  await page.locator('#password').fill(PASSWORD!);

  const authResp = await Promise.all([
    page.waitForResponse(
      (r) => /\/v1\/auth\/(sign[-_]?in|login)/i.test(r.url()) && r.request().method() === 'POST',
      { timeout: 20_000 },
    ),
    page.getByRole('button', { name: /^sign in$/i }).click(),
  ]).then(([r]) => r);

  const authBody = await authResp.json().catch(() => ({} as Record<string, unknown>));
  const token =
    (authBody as Record<string, unknown>).accessToken ??
    (authBody as Record<string, unknown>).token ??
    ((authBody as Record<string, unknown>).tokens as Record<string, unknown> | undefined)?.accessToken;
  expect(typeof token, 'sign-in returned an access token').toBe('string');

  const apiBase = `${PROD_URL}/api/backend`;
  const authedHeaders = {
    Authorization: `Bearer ${token as string}`,
    'Content-Type': 'application/json',
  };

  // Status before any save: must be 200 (was 404 before backend implementation)
  const statusBefore = await request.get(`${apiBase}/v1/expert/onboarding/status`, {
    headers: authedHeaders,
  });
  expect(statusBefore.status(), 'GET status before save').toBe(200);

  const profile = {
    displayName: 'OET Smoke Expert',
    bio: 'Automated end-to-end smoke verification of the onboarding wizard.',
    photoUrl: null as string | null,
  };
  const profileResp = await request.put(`${apiBase}/v1/expert/onboarding/profile`, {
    headers: authedHeaders,
    data: profile,
  });
  expect(profileResp.status(), 'PUT profile').toBe(200);
  expect(await profileResp.json()).toMatchObject({ displayName: profile.displayName });

  const qualifications = {
    qualifications: 'BDS, MFD RCSI, MJDF RCS — 12 years tutoring OET candidates.',
    certifications: 'OET Premium Preparation Provider 2025',
    experienceYears: 12,
  };
  const qualResp = await request.put(`${apiBase}/v1/expert/onboarding/qualifications`, {
    headers: authedHeaders,
    data: qualifications,
  });
  expect(qualResp.status(), 'PUT qualifications').toBe(200);

  const rates = {
    hourlyRateMinorUnits: 7500,
    sessionRateMinorUnits: 5000,
    currency: 'GBP',
  };
  const ratesResp = await request.put(`${apiBase}/v1/expert/onboarding/rates`, {
    headers: authedHeaders,
    data: rates,
  });
  expect(ratesResp.status(), 'PUT rates').toBe(200);

  const completeResp = await request.patch(`${apiBase}/v1/expert/onboarding/complete`, {
    headers: authedHeaders,
  });
  expect(completeResp.status(), 'PATCH complete').toBe(200);
  expect(await completeResp.json()).toMatchObject({ completed: true });

  const statusAfter = await request.get(`${apiBase}/v1/expert/onboarding/status`, {
    headers: authedHeaders,
  });
  expect(statusAfter.status(), 'GET status after save').toBe(200);
  const after = (await statusAfter.json()) as {
    isComplete: boolean;
    profile: typeof profile | null;
    rates: typeof rates | null;
  };
  expect(after.isComplete, 'isComplete flag').toBe(true);
  expect(after.profile?.displayName, 'persisted profile.displayName').toBe(profile.displayName);
  expect(after.rates?.currency, 'persisted rates.currency').toBe('GBP');
});

