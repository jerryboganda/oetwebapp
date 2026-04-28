import { expect, test, type BrowserContext, type ConsoleMessage, type Page, type Response } from '@playwright/test';
import { seedProdAuth } from './fixtures/prod-auth';

/**
 * Production smoke — privileged and edge-case roles.
 *
 * These tests are deliberately read-only. They seed a real production session,
 * walk key role surfaces, and fail only on document 5xx, API 5xx, console
 * errors, or unexpected access failures for the configured role.
 *
 * Run examples:
 *   $env:RUN_PROD_PRIVILEGED_SMOKE="1"
 *   $env:PROD_ADMIN_EMAIL="...";   $env:PROD_ADMIN_PASSWORD="..."
 *   $env:PROD_SPONSOR_EMAIL="..."; $env:PROD_SPONSOR_PASSWORD="..."
 *   $env:PROD_EXPIRED_EMAIL="..."; $env:PROD_EXPIRED_PASSWORD="..."
 *   npm run test:e2e:prod-privileged -- --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';

interface ProdRoleCredentials {
  email?: string;
  password?: string;
}

interface Surface {
  path: string;
  label: string;
  allowedPathPattern?: RegExp;
}

const adminCredentials: ProdRoleCredentials = {
  email: process.env.PROD_ADMIN_EMAIL,
  password: process.env.PROD_ADMIN_PASSWORD,
};

const sponsorCredentials: ProdRoleCredentials = {
  email: process.env.PROD_SPONSOR_EMAIL,
  password: process.env.PROD_SPONSOR_PASSWORD,
};

const expiredCredentials: ProdRoleCredentials = {
  email: process.env.PROD_EXPIRED_EMAIL,
  password: process.env.PROD_EXPIRED_PASSWORD,
};

const ADMIN_SURFACES: Surface[] = [
  { path: '/admin', label: 'admin home' },
  { path: '/admin/content', label: 'content hub' },
  { path: '/admin/users', label: 'user operations' },
  { path: '/admin/billing', label: 'billing operations' },
  { path: '/admin/analytics/quality', label: 'quality analytics' },
];

const SPONSOR_SURFACES: Surface[] = [
  { path: '/sponsor', label: 'sponsor home' },
  { path: '/sponsor/learners', label: 'sponsor learners' },
  { path: '/sponsor/billing', label: 'sponsor billing' },
];

const EXPIRED_SURFACES: Surface[] = [
  { path: '/dashboard', label: 'expired dashboard', allowedPathPattern: /^\/(dashboard|billing|billing\/upgrade)$/ },
  { path: '/billing', label: 'expired billing', allowedPathPattern: /^\/(billing|billing\/upgrade)$/ },
  { path: '/settings', label: 'expired settings', allowedPathPattern: /^\/(settings|billing|billing\/upgrade)$/ },
];

test.describe.configure({ mode: 'serial' });

test.beforeEach(({}, testInfo) => {
  test.skip(
    process.env.RUN_PROD_PRIVILEGED_SMOKE !== '1',
    'Set RUN_PROD_PRIVILEGED_SMOKE=1 to opt in to live privileged production smoke tests.',
  );
  test.skip(
    testInfo.project.name !== 'chromium-unauth',
    'Production privileged smoke is limited to the chromium-unauth project.',
  );
});

async function runReadOnlySurfaceSmoke(
  page: Page,
  context: BrowserContext,
  credentials: Required<ProdRoleCredentials>,
  surfaces: Surface[],
) {
  const consoleErrors: string[] = [];
  const apiFailures: string[] = [];
  const requestFailures: string[] = [];
  const pageErrors: string[] = [];
  const serverFailures: string[] = [];
  const documentFailures: string[] = [];
  let currentSurface = 'seed auth';

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() !== 'error') return;
    const text = msg.text();
    if (/favicon|beacon|cancel|Failed to load resource/i.test(text)) return;
    consoleErrors.push(`[${currentSurface}] ${text}`);
  });

  page.on('pageerror', (error) => {
    pageErrors.push(`[${currentSurface}] ${error.message}`);
  });

  page.on('requestfailed', (request) => {
    const failure = request.failure()?.errorText ?? 'unknown failure';
    if (/ERR_ABORTED|cancel/i.test(failure)) return;
    requestFailures.push(`[${currentSurface}] ${request.method()} ${request.url()} ${failure}`);
  });

  page.on('response', (res: Response) => {
    const url = res.url();
    if (res.status() >= 500) {
      if (url.includes('/v1/')) {
        apiFailures.push(`[${currentSurface}] ${res.status()} ${url}`);
      } else {
        serverFailures.push(`[${currentSurface}] ${res.status()} ${url}`);
      }
    }
  });

  await seedProdAuth(page, context, credentials);

  for (const surface of surfaces) {
    currentSurface = surface.label;
    const response = await page.goto(`${PROD_URL}${surface.path}`, {
      waitUntil: 'domcontentloaded',
      timeout: 30_000,
    });
    await page.waitForLoadState('networkidle', { timeout: 12_000 }).catch(() => {});

    const status = response?.status() ?? 0;
    if (status >= 400) {
      documentFailures.push(`[${surface.label}] ${status} ${surface.path}`);
    }

    const finalPath = new URL(page.url()).pathname;
    const allowedPathPattern = surface.allowedPathPattern ?? new RegExp(`^${surface.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
    if (!allowedPathPattern.test(finalPath)) {
      documentFailures.push(`[${surface.label}] expected ${allowedPathPattern}, got ${finalPath}`);
    }

    await expect(page.locator('main,[role="main"]').first(), `${surface.label} main landmark`).toBeVisible();
  }

  expect(documentFailures, 'no unexpected document access/status failures').toEqual([]);
  expect(apiFailures, 'no API 5xx responses').toEqual([]);
  expect(serverFailures, 'no non-API 5xx responses').toEqual([]);
  expect(pageErrors, 'no uncaught page errors').toEqual([]);
  expect(requestFailures, 'no unexpected request failures').toEqual([]);
  expect(consoleErrors, 'no console errors').toEqual([]);
}

test.describe('production admin smoke', () => {
  test.skip(
    !adminCredentials.email || !adminCredentials.password,
    'Set PROD_ADMIN_EMAIL and PROD_ADMIN_PASSWORD env vars to run prod admin smoke.',
  );

  test('walks key admin surfaces without mutations', async ({ page, context }) => {
    test.setTimeout(180_000);

    await runReadOnlySurfaceSmoke(
      page,
      context,
      adminCredentials as Required<ProdRoleCredentials>,
      ADMIN_SURFACES,
    );
  });
});

test.describe('production sponsor smoke', () => {
  test.skip(
    !sponsorCredentials.email || !sponsorCredentials.password,
    'Set PROD_SPONSOR_EMAIL and PROD_SPONSOR_PASSWORD env vars to run prod sponsor smoke.',
  );

  test('walks key sponsor surfaces without mutations', async ({ page, context }) => {
    test.setTimeout(120_000);

    await runReadOnlySurfaceSmoke(
      page,
      context,
      sponsorCredentials as Required<ProdRoleCredentials>,
      SPONSOR_SURFACES,
    );
  });
});

test.describe('production expired-account smoke', () => {
  test.skip(
    !expiredCredentials.email || !expiredCredentials.password,
    'Set PROD_EXPIRED_EMAIL and PROD_EXPIRED_PASSWORD env vars to run prod expired-account smoke.',
  );

  test('walks safe expired-account surfaces without mutations', async ({ page, context }) => {
    test.setTimeout(120_000);

    await runReadOnlySurfaceSmoke(
      page,
      context,
      expiredCredentials as Required<ProdRoleCredentials>,
      EXPIRED_SURFACES,
    );
  });
});