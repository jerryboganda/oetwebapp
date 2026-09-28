import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

/**
 * Visual QA sweep: representative routes per role at four widths.
 *
 * Asserts the layout contract from DESIGN.md §7 (no horizontal page scroll)
 * and attaches a full-page screenshot per route × width for human review.
 * Console/network diagnostics are attached but not asserted — QA Smoke owns
 * that gate; this sweep owns layout. No pixel baselines on purpose: content
 * is data-driven, so snapshots would be flaky.
 */

const WIDTHS = [360, 768, 1280, 1920] as const;

type Role = 'unauth' | 'learner' | 'expert' | 'admin';

const ROUTES_BY_ROLE: Record<Role, string[]> = {
  unauth: ['/sign-in', '/register', '/pricing'],
  learner: ['/dashboard', '/listening', '/reading', '/writing', '/speaking', '/mocks', '/progress', '/billing', '/settings'],
  expert: ['/expert'],
  admin: ['/admin', '/admin/users', '/admin/billing', '/admin/content'],
};

function roleForProject(projectName: string): Role | null {
  if (projectName === 'chromium-unauth') return 'unauth';
  if (projectName === 'chromium-learner') return 'learner';
  if (projectName === 'chromium-expert') return 'expert';
  if (projectName === 'chromium-admin') return 'admin';
  return null; // one engine is enough for a layout sweep
}

async function horizontalOverflow(page: Page) {
  return page.evaluate(() => {
    const root = document.scrollingElement ?? document.documentElement;
    return root.scrollWidth - root.clientWidth;
  });
}

test.describe('Visual QA sweep @visual', () => {
  for (const width of WIDTHS) {
    test(`no horizontal overflow at ${width}px`, async ({ page, request }, testInfo) => {
      const role = roleForProject(testInfo.project.name);
      test.skip(!role, 'visual sweep runs on chromium role projects only');
      test.setTimeout(300_000);

      await page.setViewportSize({ width, height: width < 768 ? 800 : 900 });
      const routes = ROUTES_BY_ROLE[role!];
      const failures: string[] = [];

      // Single-active-session: the cached storage state may already be
      // revoked by another shard, so mint a fresh session for this role first.
      const authRole = role === 'unauth' ? null : role!;
      if (authRole) await recoverBrowserSession(page, request, authRole, routes[0]);

      for (const route of routes) {
        const diagnostics = observePage(page);
        await page.goto(route, { waitUntil: 'domcontentloaded', timeout: 120_000 });
        if (authRole) {
          await waitForSessionGuardToClear(page, {
            recover: () => recoverBrowserSession(page, request, authRole, route),
          });
        }
        if (authRole) await expect(page.getByRole('main').first()).toBeVisible({ timeout: 30_000 });
        await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);

        const overflow = await horizontalOverflow(page);
        // 1px tolerance for sub-pixel rounding.
        if (overflow > 1) failures.push(`${route} overflows by ${overflow}px at ${width}px`);

        await testInfo.attach(`${role}${route.replace(/\//g, '_')}@${width}.png`, {
          body: await page.screenshot({ fullPage: true }),
          contentType: 'image/png',
        });

        diagnostics.detach();
        await attachDiagnostics(testInfo, diagnostics);
      }

      expect(failures, failures.join('\n')).toEqual([]);
    });
  }
});
