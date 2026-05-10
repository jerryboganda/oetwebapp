import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';

const learnerDeepLinks = [
  {
    path: '/reading/player/rt-001',
    assertions: async (page: Page) => {
      await expect(page).toHaveURL(/\/reading(?:\?|$)/, { timeout: 60000 });
      await expect(page.getByRole('heading', { name: /reading/i })).toBeVisible({ timeout: 60000 });
    },
  },
  {
    path: '/mocks/report/mock-report-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /overall performance/i })).toBeVisible({ timeout: 60000 });
      await expect(page.getByText(/area for improvement/i)).toBeVisible({ timeout: 60000 });
      await expect(page.getByRole('link', { name: /update study plan/i })).toBeVisible({ timeout: 60000 });
    },
  },
];

async function openDeepLink(page: Page, path: string) {
  await page.goto(path, { waitUntil: 'domcontentloaded' });
  await waitForSessionGuardToClear(page, {
    recover: () => page.goto(path, { waitUntil: 'domcontentloaded' }),
    initialTimeoutMs: 15_000,
  });

  const transientNotFound = page.getByRole('heading', { name: /task not found|report not found/i });
  if (await transientNotFound.isVisible().catch(() => false)) {
    await page.goto(path, { waitUntil: 'domcontentloaded' });
    await waitForSessionGuardToClear(page, {
      recover: () => page.goto(path, { waitUntil: 'domcontentloaded' }),
      initialTimeoutMs: 15_000,
    });
  }
}

test.describe('Learner deep-link smoke @learner @smoke', () => {
  for (const route of learnerDeepLinks) {
    test(`learner deep link ${route.path} renders as a stable authenticated entry point`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('learner')) {
        test.skip();
      }

      testInfo.setTimeout(120000);

      await openDeepLink(page, route.path);
      const diagnostics = observePage(page);

      if (!route.path.startsWith('/reading/player/')) {
        await expect(page).toHaveURL(new RegExp(route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      }
      await route.assertions(page);

      expectNoSevereClientIssues(diagnostics, {
        allowNextDevNoise: true,
        allowNotificationReconnectNoise: true,
      });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
