import { expect, test } from '@playwright/test';
import { seedProdAuth } from './fixtures/prod-auth';

/**
 * Production performance budget — measures key learner routes.
 *
 * Asserts:
 *  - Time-to-first-paint (FCP) < 3000ms
 *  - DOMContentLoaded < 5000ms
 *  - Document fully loaded < 8000ms
 *
 * Uses API-based auth seeding (rate-limit safe).
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;

const ROUTES = [
  '/dashboard',
  '/practice',
  '/reading',
  '/writing',
  '/listening',
  '/vocabulary',
  '/billing',
];

const BUDGET = {
  fcp: 3000,
  dcl: 5000,
  load: 8000,
};

test.describe.configure({ mode: 'serial' });
test.skip(!EMAIL || !PASSWORD, 'Set PROD_LEARNER_EMAIL / PROD_LEARNER_PASSWORD.');

test('prod — performance budget on key learner routes', async ({ page, context }) => {
  test.setTimeout(360_000);

  await seedProdAuth(page, context, { email: EMAIL!, password: PASSWORD! });

  // Warm cache
  await page.goto(`${PROD_URL}/dashboard`, { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => {});

  const results: Array<{ route: string; fcp: number; dcl: number; load: number }> = [];
  const violations: string[] = [];

  for (const route of ROUTES) {
    await page.goto(`${PROD_URL}${route}`, { waitUntil: 'load', timeout: 30_000 });

    const metrics = await page.evaluate(() => {
      const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined;
      const fcpEntry = performance.getEntriesByName('first-contentful-paint')[0] as PerformanceEntry | undefined;
      return {
        fcp: fcpEntry ? Math.round(fcpEntry.startTime) : -1,
        dcl: nav ? Math.round(nav.domContentLoadedEventEnd) : -1,
        load: nav ? Math.round(nav.loadEventEnd) : -1,
      };
    });

    results.push({ route, ...metrics });

    if (metrics.fcp > BUDGET.fcp) violations.push(`${route} FCP ${metrics.fcp}ms > ${BUDGET.fcp}ms`);
    if (metrics.dcl > BUDGET.dcl) violations.push(`${route} DCL ${metrics.dcl}ms > ${BUDGET.dcl}ms`);
    if (metrics.load > BUDGET.load) violations.push(`${route} LOAD ${metrics.load}ms > ${BUDGET.load}ms`);
  }

  // eslint-disable-next-line no-console
  console.log('\n[perf] route metrics:');
  for (const r of results) {
    // eslint-disable-next-line no-console
    console.log(`  ${r.route.padEnd(20)}  fcp=${String(r.fcp).padStart(5)}ms  dcl=${String(r.dcl).padStart(5)}ms  load=${String(r.load).padStart(5)}ms`);
  }

  expect(violations, `perf budget violations:\n${violations.join('\n')}`).toEqual([]);
});
