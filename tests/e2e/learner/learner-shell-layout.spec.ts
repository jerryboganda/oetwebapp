import AxeBuilder from '@axe-core/playwright';
import { expect, test, type APIRequestContext, type Page, type TestInfo } from '@playwright/test';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

/**
 * The learner shell is mounted once, by app/(learner)/layout.tsx. A client
 * navigation keeps the chrome mounted, every route renders one #main-content
 * and one workspace container, and focus, public and self-chromed routes keep
 * the chrome they had when each page mounted its own shell.
 */

const MAIN_NAV = 'nav[aria-label="Main navigation"]';
const BOTTOM_NAV = 'nav[aria-label="Mobile navigation"]';
const WORKSPACE = '[data-testid="learner-workspace-container"]';

// WebKit shards lose the learner session under CI matrix load (see
// learner-smoke.spec.ts); Chromium, Firefox and Sydney cover these checks.
function skipUnlessLearner(testInfo: TestInfo) {
  const project = testInfo.project.name;
  test.skip(!project.includes('learner') || project.includes('webkit'), 'learner chromium/firefox projects only');
}

// First-run onboarding tours (driver.js) overlay the page and would swallow clicks.
async function dismissTour(page: Page) {
  await page.locator('.driver-popover-close-btn').click({ timeout: 2_000 }).catch(() => undefined);
}

async function openAsLearner(page: Page, request: APIRequestContext, route: string) {
  // The post-login app-download modal is a dialog that would block nav clicks.
  await page.addInitScript(() => window.sessionStorage.setItem('oet_app_promo_dismissed', 'true'));
  const recover = () => recoverBrowserSession(page, request, 'learner', route);
  // Single-active-session: the first visit mints a fresh session, which also lands on `route`.
  if (page.url().startsWith('http')) {
    await page.goto(route, { waitUntil: 'domcontentloaded', timeout: 120_000 });
  } else {
    await recover();
  }
  await waitForSessionGuardToClear(page, { recover });
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);
  await dismissTour(page);
}

/** Client-navigates through a real sidebar (desktop) or bottom-nav (mobile) link. */
async function navigateByShellNav(page: Page) {
  const link = page
    .locator(`${MAIN_NAV}:visible, ${BOTTOM_NAV}:visible`)
    .first()
    .locator('a[href^="/"]:not([href="/"]):not([href^="/placement"]):visible')
    .first();
  const href = await link.getAttribute('href');
  expect(href, 'the shell nav should link to a learner section').toBeTruthy();
  await dismissTour(page);
  await link.click();
  await page.waitForURL((url) => url.pathname === href);
}

test.describe('Learner shell layout @learner @smoke', () => {
  test('keeps one persistent shell across a client navigation', async ({ page, request }, testInfo) => {
    skipUnlessLearner(testInfo);
    test.setTimeout(180_000);

    await openAsLearner(page, request, '/dashboard');
    await expect(page.locator('#main-content')).toBeVisible({ timeout: 60_000 });
    await expect(page.locator('#main-content')).toHaveCount(1);
    await expect(page.locator(WORKSPACE)).toHaveCount(1);

    await page.evaluate(() => {
      document.querySelector('header')!.dataset.e2ePersist = '1';
    });
    await navigateByShellNav(page);

    await expect(page.locator('header[data-e2e-persist="1"]'), 'the header must not remount').toHaveCount(1);
    await expect(page.locator('#main-content')).toHaveCount(1);
    await expect(page.locator(WORKSPACE)).toHaveCount(1);
  });

  test('closes the mobile menu on browser back', async ({ page, request }, testInfo) => {
    skipUnlessLearner(testInfo);
    test.setTimeout(180_000);

    await page.setViewportSize({ width: 360, height: 800 });
    await openAsLearner(page, request, '/dashboard');
    await expect(page.locator(BOTTOM_NAV)).toBeVisible({ timeout: 60_000 });
    await navigateByShellNav(page);

    await page.getByRole('button', { name: 'Open menu', exact: true }).click();
    const menu = page.locator('[role="dialog"]').filter({ has: page.locator('#mobile-menu') });
    await expect(menu).toBeVisible();

    await page.goBack();
    await page.waitForURL((url) => url.pathname === '/dashboard');
    await expect(menu).toHaveCount(0);
  });

  test('keeps focus, header-action and self-chromed routes on their own chrome', async ({ page, request }, testInfo) => {
    skipUnlessLearner(testInfo);
    test.setTimeout(240_000);

    await openAsLearner(page, request, '/onboarding');
    await expect(page.locator('#main-content')).toBeVisible({ timeout: 60_000 });
    await expect(page.locator(MAIN_NAV)).toHaveCount(0);
    await expect(page.locator(BOTTOM_NAV)).toHaveCount(0);
    // The focus title shows at `sm:` and up only.
    if ((page.viewportSize()?.width ?? 0) >= 640) {
      await expect(page.locator('header').first().getByText('Getting Started')).toBeVisible();
    }

    await openAsLearner(page, request, '/subscriptions');
    await expect(page.locator('header').first().getByRole('button', { name: /^Cart \(/ })).toBeVisible({ timeout: 60_000 });

    await openAsLearner(page, request, '/listening/strategies');
    await expect(page.getByRole('heading', { name: 'Strategy Library' })).toBeVisible({ timeout: 60_000 });
    await expect(page.locator(MAIN_NAV)).toHaveCount(0);
    await expect(page.locator(BOTTOM_NAV)).toHaveCount(0);
    await expect(page.locator(WORKSPACE)).toHaveCount(0);
  });

  test('learner dashboard has no serious or critical axe violations', async ({ page, request }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-learner', 'one engine is enough for axe');
    test.setTimeout(180_000);

    await openAsLearner(page, request, '/dashboard');
    // The AuthGuard placeholder renders the same h1, so wait for the real
    // workspace (it only mounts once the session guard has cleared).
    await expect(page.locator(`#main-content ${WORKSPACE}`)).toBeVisible({ timeout: 90_000 });
    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible({ timeout: 90_000 });
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    const blocking = results.violations.filter((violation) => ['critical', 'serious'].includes(violation.impact ?? ''));
    expect(blocking, 'Critical and serious axe violations should remain empty').toEqual([]);
  });

  test('public speaking reference renders signed out', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium-unauth', 'unauthenticated project only');
    test.setTimeout(180_000);

    await page.goto('/speaking/assessment-criteria', { waitUntil: 'domcontentloaded', timeout: 120_000 });
    await expect(page.getByRole('heading', { name: 'Speaking Assessment Criteria' })).toBeVisible({ timeout: 60_000 });
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);
    expect(new URL(page.url()).pathname).toBe('/speaking/assessment-criteria');
    await expect(page.locator('#main-content')).toHaveCount(1);
    await expect(page.locator(WORKSPACE)).toHaveCount(1);
  });
});
