import { expect, test, type Page } from '@playwright/test';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

/**
 * Route entrance contract (DESIGN.md §5) in the persistent staff shells.
 *
 * A client navigation must keep the frame mounted, never render two
 * `#main-content` at once (the old AnimatePresence exit mounted the new page
 * twice), and play the enter-only `.page-enter`. A hard load never animates,
 * and reduced motion collapses the entrance to effectively zero.
 */

type StaffRole = 'admin' | 'expert';

// Both hrefs are sidebar links in that role's shell (lib/admin-navigation.tsx,
// app/expert/layout.tsx); `start` is also the way back.
const JOURNEYS: Record<StaffRole, { start: string; next: string }> = {
  admin: { start: '/admin', next: '/admin/users' },
  expert: { start: '/expert', next: '/expert/queue' },
};

function roleForProject(projectName: string): StaffRole | null {
  if (projectName === 'chromium-admin') return 'admin';
  if (projectName === 'chromium-expert') return 'expert';
  return null;
}

function mainAnimation(page: Page) {
  return page.locator('#main-content').evaluate((el) => {
    const style = getComputedStyle(el);
    return { name: style.animationName, duration: parseFloat(style.animationDuration) };
  });
}

// First-run onboarding tours (driver.js) overlay the page and would swallow the click.
async function dismissTour(page: Page) {
  await page.locator('.driver-popover-close-btn').click({ timeout: 2_000 }).catch(() => undefined);
}

test.describe('Route transition @visual', () => {
  test('client navigation keeps one main and plays the enter-only entrance', async ({ page, request }, testInfo) => {
    const role = roleForProject(testInfo.project.name);
    test.skip(!role, 'runs on chromium-admin and chromium-expert only');
    test.setTimeout(180_000);
    const { start, next } = JOURNEYS[role!];

    await page.setViewportSize({ width: 1280, height: 900 });
    // Single-active-session: mint a fresh session for this role first.
    await recoverBrowserSession(page, request, role!, start);
    await page.goto(start, { waitUntil: 'domcontentloaded', timeout: 120_000 });
    await waitForSessionGuardToClear(page, {
      recover: () => recoverBrowserSession(page, request, role!, start),
    });
    await expect(page.locator('#main-content')).toBeVisible({ timeout: 30_000 });
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);
    await dismissTour(page);

    expect((await mainAnimation(page)).name, 'a hard load must not animate').toBe('none');

    const nav = page.locator('nav[aria-label="Main navigation"]');
    const navHandle = await nav.elementHandle();
    await page.evaluate(() => {
      const w = window as unknown as { __maxMainCount: number };
      const count = () => document.querySelectorAll('#main-content').length;
      w.__maxMainCount = count();
      new MutationObserver(() => {
        w.__maxMainCount = Math.max(w.__maxMainCount, count());
      }).observe(document.body, { childList: true, subtree: true });
    });

    await nav.locator(`a[href="${next}"]`).first().click();
    await page.waitForURL((url) => url.pathname === next);
    await expect.poll(async () => (await mainAnimation(page)).name).toBe('page-enter');
    expect(await navHandle!.evaluate((el) => el.isConnected), 'the shell nav must persist').toBe(true);
    const maxMainCount = await page.evaluate(() => (window as unknown as { __maxMainCount: number }).__maxMainCount);
    expect(maxMainCount, 'the new page must never be mounted twice').toBeLessThanOrEqual(1);

    await page.emulateMedia({ reducedMotion: 'reduce' });
    await dismissTour(page);
    await nav.locator(`a[href="${start}"]`).first().click();
    await page.waitForURL((url) => url.pathname === start);
    await expect
      .poll(async () => {
        const { name, duration } = await mainAnimation(page);
        return name === 'page-enter' && duration < 0.001;
      })
      .toBe(true);
  });
});
