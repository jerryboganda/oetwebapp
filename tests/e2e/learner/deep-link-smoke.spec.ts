import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const learnerDeepLinks = [
  {
    path: '/reading/player/rt-001',
    assertions: async (page: Page) => {
      const main = page.getByRole('main');
      await expect(main).toBeVisible({ timeout: 35000 });
      await expect(main.getByRole('heading', { name: /hospital-acquired infections: prevention strategies/i })).toBeVisible({ timeout: 35000 });
      await expect(page.getByText(/question 1 of 3/i)).toBeVisible({ timeout: 35000 });
      await expect(page.getByRole('button', { name: /^submit$/i })).toBeVisible({ timeout: 35000 });
    },
  },
  {
    path: '/mocks/report/mock-report-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /overall performance/i })).toBeVisible({ timeout: 35000 });
      await expect(page.getByText(/area for improvement/i)).toBeVisible({ timeout: 35000 });
      await expect(page.getByRole('link', { name: /update study plan/i })).toBeVisible({ timeout: 35000 });
    },
  },
];

test.describe('Learner deep-link smoke @learner @smoke', () => {
  for (const route of learnerDeepLinks) {
    test(`learner deep link ${route.path} renders as a stable authenticated entry point`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('learner')) {
        test.skip();
      }

      const diagnostics = observePage(page);
      const sessionBanner = page.getByText(/checking your session/i);

      await page.goto(route.path, { waitUntil: 'domcontentloaded' });
      if (await sessionBanner.isVisible().catch(() => false)) {
        await page.reload({ waitUntil: 'domcontentloaded' });
      }

      await expect(page).toHaveURL(new RegExp(route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await expect(sessionBanner).toBeHidden({ timeout: 90000 });
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
