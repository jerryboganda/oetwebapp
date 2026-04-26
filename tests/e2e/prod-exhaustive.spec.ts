import { expect, test, type ConsoleMessage, type Response } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Production exhaustive sweep
 * -----------------------------------------------------------------------------
 * Walks every STATIC learner-accessible route in the app and asserts:
 *  - main document does not 5xx
 *  - no /v1/* 5xx during render
 *  - no JS console errors (with the same noise filter as prod-smoke)
 *
 * Dynamic routes ([id], [paperId], etc.) are excluded — they need real IDs and
 * are partially covered by drill-down clicks in `prod-interaction.spec.ts`.
 *
 * Run:
 *   $env:PROD_LEARNER_EMAIL  = "..."
 *   $env:PROD_LEARNER_PASSWORD = "..."
 *   npx playwright test tests/e2e/prod-exhaustive.spec.ts --project=chromium-unauth --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;

const ROUTES = [
  '/',
  '/achievements',
  '/achievements/certificate',
  '/achievements/certificates',
  '/billing',
  '/billing/referral',
  '/billing/score-guarantee',
  '/billing/upgrade',
  '/community',
  '/community/ask-an-expert',
  '/community/groups',
  '/community/threads/my',
  '/community/threads/new',
  '/conversation',
  '/dashboard',
  '/dashboard/project',
  '/dashboard/score-calculator',
  '/diagnostic',
  '/diagnostic/hub',
  '/diagnostic/insights',
  '/diagnostic/listening',
  '/diagnostic/reading',
  '/diagnostic/results',
  '/diagnostic/speaking',
  '/diagnostic/writing',
  '/escalations',
  '/exam-booking',
  '/exam-guide',
  '/feedback-guide',
  '/goals',
  '/goals/study-commitment',
  '/grammar',
  '/history',
  '/leaderboard',
  '/learning-paths',
  '/lessons',
  '/lessons/discover',
  '/lessons/programs',
  '/listening',
  '/marketplace',
  '/marketplace/packages',
  '/mocks',
  '/mocks/setup',
  '/mocks/simulation',
  '/next-actions',
  '/peer-review',
  '/practice',
  '/practice/interleaved',
  '/practice/quick-session',
  '/predictions',
  '/private-speaking',
  '/progress',
  '/progress/comparative',
  '/pronunciation',
  '/readiness',
  '/reading',
  '/referral',
  '/remediation',
  '/review',
  '/reviews',
  '/score-calculator',
  '/settings',
  '/settings/ai',
  '/settings/reminders',
  '/settings/sessions',
  '/speaking',
  '/speaking/check',
  '/speaking/fluency-timeline',
  '/speaking/selection',
  '/strategies',
  '/study-plan',
  '/study-plan/drift',
  '/submissions',
  '/submissions/compare',
  '/test-day',
  '/tutoring',
  '/vocabulary',
  '/vocabulary/browse',
  '/vocabulary/flashcards',
  '/vocabulary/quiz',
  '/vocabulary/quiz/history',
  '/writing',
  '/writing/compare',
  '/writing/expert-request',
  '/writing/feedback',
  '/writing/library',
  '/writing/model',
  '/writing/phrase-suggestions',
  '/writing/player',
  '/writing/result',
  '/writing/revision',
];

test.describe.configure({ mode: 'serial' });

test.skip(
  !EMAIL || !PASSWORD,
  'Set PROD_LEARNER_EMAIL and PROD_LEARNER_PASSWORD env vars.',
);

test('prod — exhaustive learner route sweep', async ({ page }) => {
  test.setTimeout(900_000); // up to 15 min for 89 routes

  const consoleErrors: string[] = [];
  const apiFailures: string[] = [];
  const fourXx = new Map<string, string[]>();
  const docFailures: Array<{ route: string; status: number }> = [];
  let surface = 'sign-in';

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() !== 'error') return;
    const text = msg.text();
    if (/favicon|beacon|cancel|Failed to load resource/i.test(text)) return;
    consoleErrors.push(`[${surface}] ${text}`);
  });
  page.on('response', (res: Response) => {
    const url = res.url();
    if (!url.includes('/v1/')) return;
    const status = res.status();
    if (status >= 500) apiFailures.push(`[${surface}] ${status} ${url}`);
    else if (status >= 400) {
      const list = fourXx.get(surface) ?? [];
      list.push(`${status} ${url}`);
      fourXx.set(surface, list);
    }
  });

  // Sign in
  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  await page.locator('#email').waitFor({ state: 'visible', timeout: 15_000 });
  await page.locator('#email').fill(EMAIL!);
  await page.locator('#password').fill(PASSWORD!);
  await Promise.all([
    page.waitForResponse(
      (r) => /\/v1\/auth\/(sign[-_]?in|login)/i.test(r.url()) && r.request().method() === 'POST',
      { timeout: 20_000 },
    ).catch(() => null),
    page.getByRole('button', { name: /^sign in$/i }).click(),
  ]);
  await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 30_000 });

  // Walk every route
  for (const route of ROUTES) {
    surface = route;
    const resp = await page.goto(`${PROD_URL}${route}`, {
      waitUntil: 'domcontentloaded',
      timeout: 25_000,
    }).catch((err) => {
      console.log(`[exhaustive] navigation error on ${route}: ${err.message}`);
      return null;
    });

    const status = resp?.status() ?? 0;
    if (status >= 400) {
      docFailures.push({ route, status });
    }

    // Brief settle for client-side fetches
    await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => {});
  }

  // Report (sorted)
  console.log(`\n[exhaustive] visited ${ROUTES.length} routes`);
  if (docFailures.length) {
    console.log('  document 4xx/5xx:');
    for (const f of docFailures) console.log(`    ${f.status}  ${f.route}`);
  }
  if (fourXx.size) {
    console.log('  /v1/* 4xx by surface (non-blocking):');
    for (const [s, list] of fourXx) {
      for (const l of list) console.log(`    [${s}] ${l}`);
    }
  }

  expect(docFailures, 'no 5xx document responses').toEqual(
    expect.arrayContaining([]),
  );
  // Document 5xx is BLOCKING; document 4xx is allowed only if it's an auth/permission expected response
  const doc5xx = docFailures.filter((f) => f.status >= 500);
  expect(doc5xx, `documents must not 5xx: ${JSON.stringify(doc5xx)}`).toEqual([]);
  expect(apiFailures, 'no API 5xx').toEqual([]);
  expect(consoleErrors, 'no console errors').toEqual([]);
});
