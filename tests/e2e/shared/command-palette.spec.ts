import AxeBuilder from '@axe-core/playwright';
import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

/**
 * Ctrl/⌘K command palette (DESIGN.md §6) in each role's shell.
 *
 * Opens from the visible trigger, stacks above the mobile bottom nav, keeps
 * the header inside the viewport, has no serious axe violations and navigates
 * to the active option. Data-driven: the spec follows whichever option is
 * active and never asserts a nav label.
 */

type Role = 'learner' | 'expert' | 'admin';

// `/subscriptions` adds a page action (cart) to the learner header: the
// tightest header row at 360px.
const JOURNEYS: Record<Role, { start: string; headerRoutes: string[] }> = {
  learner: { start: '/dashboard', headerRoutes: ['/dashboard', '/subscriptions'] },
  expert: { start: '/expert', headerRoutes: ['/expert'] },
  admin: { start: '/admin', headerRoutes: ['/admin'] },
};

function roleForProject(projectName: string): Role | null {
  if (projectName === 'chromium-learner') return 'learner';
  if (projectName === 'chromium-expert') return 'expert';
  if (projectName === 'chromium-admin') return 'admin';
  return null;
}

async function openShell(page: Page, request: APIRequestContext, role: Role, route: string) {
  // The post-login app-download modal is a dialog that would block clicks and the hotkey.
  await page.addInitScript(() => window.sessionStorage.setItem('oet_app_promo_dismissed', 'true'));
  await page.goto(route, { waitUntil: 'domcontentloaded', timeout: 120_000 });
  await waitForSessionGuardToClear(page, {
    recover: () => recoverBrowserSession(page, request, role, route),
  });
  await expect(page.getByRole('main').first()).toBeVisible({ timeout: 30_000 });
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);
  // First-run onboarding tours (driver.js) are dialogs too; the hotkey will not stack on one.
  await page.locator('.driver-popover-close-btn').click({ timeout: 2_000 }).catch(() => undefined);
}

/** Right edge of the rightmost visible control in the visible top nav. */
function headerRightEdge(page: Page) {
  return page.evaluate(() => {
    // The shell's top nav, not a page's own <header> inside <main>.
    const header = Array.from(document.querySelectorAll('header')).find((el) => !el.closest('main') && el.getClientRects().length > 0);
    const controls = Array.from(header?.querySelectorAll('a, button') ?? []).filter((el) => el.getClientRects().length > 0);
    return Math.max(0, ...controls.map((el) => el.getBoundingClientRect().right));
  });
}

/** In the header, or (learner, below md) at the top of the menu drawer. */
function searchTrigger(page: Page) {
  return page.getByRole('banner').or(page.locator('#mobile-menu')).getByRole('button', { name: /^search/i });
}

async function openFromTrigger(page: Page) {
  const trigger = searchTrigger(page);
  const menu = page.getByRole('button', { name: /open menu/i });
  // Wait for the header to render one of them rather than counting once.
  await expect(trigger.or(menu).first()).toBeVisible({ timeout: 30_000 });
  if (!(await trigger.first().isVisible())) await menu.click();
  await trigger.first().click();
}

test.describe('Command palette @visual', () => {
  for (const width of [360, 1280] as const) {
    test(`opens from the visible trigger and navigates at ${width}px`, async ({ page, request }, testInfo) => {
      const role = roleForProject(testInfo.project.name);
      test.skip(!role, 'runs on chromium role projects only');
      test.setTimeout(180_000);
      const { start, headerRoutes } = JOURNEYS[role!];

      await page.setViewportSize({ width, height: width < 768 ? 800 : 900 });
      // Single-active-session: mint a fresh session for this role first.
      await recoverBrowserSession(page, request, role!, start);

      for (const route of headerRoutes) {
        await openShell(page, request, role!, route);
        expect(await headerRightEdge(page), `${route} header controls fit ${width}px`).toBeLessThanOrEqual(width);
      }

      await openShell(page, request, role!, start);
      await openFromTrigger(page);
      const dialog = page.getByRole('dialog', { name: 'Search' });
      const combobox = dialog.getByRole('combobox', { name: 'Search' });
      await expect(dialog).toBeVisible();
      await expect(combobox).toBeFocused();

      if (width === 360) {
        // Portaled to <body> above the fixed bottom nav, not painted under it.
        const bottomLayer = await page.evaluate(() =>
          document
            .elementFromPoint(window.innerWidth / 2, window.innerHeight - 8)
            ?.closest('[role="dialog"]')
            ?.getAttribute('aria-label'),
        );
        expect(bottomLayer).toBe('Search');
      }

      const axe = await new AxeBuilder({ page })
        .include('[role="dialog"][aria-label="Search"]')
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze();
      const blocking = axe.violations.filter((violation) => ['critical', 'serious'].includes(violation.impact ?? ''));
      expect(blocking, 'the open palette has no serious or critical axe violations').toEqual([]);

      await testInfo.attach(`palette-${role}@${width}.png`, { body: await page.screenshot(), contentType: 'image/png' });

      await combobox.press('ArrowDown');
      const href = await dialog.locator('[role="option"][aria-selected="true"]').getAttribute('data-href');
      expect(href).toBeTruthy();
      await combobox.press('Enter');
      await page.waitForURL((url) => url.pathname === href);
      await expect(dialog).toBeHidden();
    });
  }

  test('Ctrl+K toggles and Escape returns focus to the trigger at 1280px', async ({ page, request }, testInfo) => {
    const role = roleForProject(testInfo.project.name);
    test.skip(!role, 'runs on chromium role projects only');
    test.setTimeout(180_000);
    const { start } = JOURNEYS[role!];

    await page.setViewportSize({ width: 1280, height: 900 });
    await recoverBrowserSession(page, request, role!, start);
    await openShell(page, request, role!, start);
    const dialog = page.getByRole('dialog', { name: 'Search' });

    await page.keyboard.press('Control+k');
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Control+k');
    await expect(dialog).toBeHidden();

    const trigger = searchTrigger(page);
    await trigger.click();
    await expect(dialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });
});
