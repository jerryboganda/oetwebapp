import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

/**
 * Visual QA sweep: representative routes per role at four widths.
 *
 * Asserts the layout contract from DESIGN.md §7 (no horizontal page scroll)
 * and attaches a full-page screenshot per route × width for human review.
 * Shell routes scroll inside #main-content, so theirs show the viewport only.
 * Console/network diagnostics are attached but not asserted — QA Smoke owns
 * that gate; this sweep owns layout. No pixel baselines on purpose: content
 * is data-driven, so snapshots would be flaky.
 */

const WIDTHS = [360, 768, 1280, 1920] as const;

type Role = 'unauth' | 'learner' | 'expert' | 'admin';

const ROUTES_BY_ROLE: Record<Role, string[]> = {
  unauth: ['/sign-in', '/register', '/pricing', '/speaking/assessment-criteria'],
  learner: [
    '/dashboard', '/listening', '/reading', '/writing', '/speaking', '/mocks', '/progress', '/billing', '/settings',
    '/subscriptions', '/onboarding',
  ],
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

/**
 * Worst horizontal overflow and where it happened. The AppShell root is
 * overflow-hidden and #main-content scrolls itself, so in-shell overflow never
 * reaches the document: measure main and the header row as elements too.
 */
async function horizontalOverflow(page: Page) {
  return page.evaluate((): [string, number] => {
    const doc = document.scrollingElement ?? document.documentElement;
    const main = document.getElementById('main-content');
    // Staff shells render two TopNavs and hide one per breakpoint.
    const header = Array.from(document.querySelectorAll('header')).find((h) => h.getClientRects().length > 0);
    const rights = header
      ? Array.from(header.querySelectorAll('*'), (el) => el.getBoundingClientRect())
          .filter((r) => r.width > 0 && r.height > 0)
          .map((r) => r.right)
      : [];
    const sources: [string, number][] = [
      ['document', doc.scrollWidth - doc.clientWidth],
      ['main-content', main ? main.scrollWidth - main.clientWidth : 0],
      ['header', Math.round(Math.max(0, ...rights) - window.innerWidth)],
    ];
    return sources.sort((a, b) => b[1] - a[1])[0];
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

        // First-run onboarding tours (driver.js) overlay the page; close so the
        // screenshot shows the real layout. Only affects the seeded CI user.
        await page.locator('.driver-popover-close-btn').click({ timeout: 2_000 }).catch(() => undefined);

        const [source, overflow] = await horizontalOverflow(page);
        // 1px tolerance for sub-pixel rounding.
        if (overflow > 1) failures.push(`${route} overflows (${source}) by ${overflow}px at ${width}px`);

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
