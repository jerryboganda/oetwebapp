import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Visual QA sweep: representative routes per role at four widths.
 *
 * Asserts the layout contract from DESIGN.md §7 (no horizontal page scroll,
 * no severe client errors) and attaches a full-page screenshot per
 * route × width as a test artifact for human review. No pixel baselines on
 * purpose — content is data-driven, so snapshots would be flaky.
 */

const WIDTHS = [360, 768, 1280, 1920] as const;

const ROUTES_BY_ROLE: Record<'unauth' | 'learner' | 'expert' | 'admin', string[]> = {
  unauth: ['/sign-in', '/register', '/pricing'],
  learner: ['/dashboard', '/listening', '/reading', '/writing', '/speaking', '/mocks', '/progress', '/billing', '/settings'],
  expert: ['/expert'],
  admin: ['/admin', '/admin/users', '/admin/billing', '/admin/content'],
};

function roleForProject(projectName: string): keyof typeof ROUTES_BY_ROLE | null {
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
    test(`no horizontal overflow at ${width}px`, async ({ page }, testInfo) => {
      const role = roleForProject(testInfo.project.name);
      test.skip(!role, 'visual sweep runs on chromium role projects only');
      test.setTimeout(180_000);

      await page.setViewportSize({ width, height: width < 768 ? 800 : 900 });
      const failures: string[] = [];

      for (const route of ROUTES_BY_ROLE[role!]) {
        const diagnostics = observePage(page);
        await page.goto(route, { waitUntil: 'domcontentloaded' });
        await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => undefined);

        const overflow = await horizontalOverflow(page);
        // 1px tolerance for sub-pixel rounding.
        if (overflow > 1) failures.push(`${route} overflows by ${overflow}px at ${width}px`);

        await testInfo.attach(`${role}${route.replace(/\//g, '_') || '_root'}@${width}.png`, {
          body: await page.screenshot({ fullPage: true }),
          contentType: 'image/png',
        });

        expectNoSevereClientIssues(diagnostics);
        diagnostics.detach();
        await attachDiagnostics(testInfo, diagnostics);
      }

      expect(failures, failures.join('\n')).toEqual([]);
    });
  }
});
