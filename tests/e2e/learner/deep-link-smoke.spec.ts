import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const learnerDeepLinks = [
  {
    path: '/reading/player/rt-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /hospital-acquired infections: prevention strategies/i })).toBeVisible();
      await expect(page.getByText(/question 1 of 3/i)).toBeVisible();
      await expect(page.getByRole('button', { name: /^submit$/i })).toBeVisible();
    },
  },
  {
    path: '/mocks/report/mock-report-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /overall performance/i })).toBeVisible();
      await expect(page.getByText(/area for improvement/i)).toBeVisible();
      await expect(page.getByRole('link', { name: /update study plan/i })).toBeVisible();
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

      await page.goto(route.path);
      await expect(page).toHaveURL(new RegExp(route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await route.assertions(page);

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
