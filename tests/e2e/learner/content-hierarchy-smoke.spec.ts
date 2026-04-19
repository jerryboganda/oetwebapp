import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const learnerContentRoutes = [
  {
    path: '/lessons/programs',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /learning programs/i })).toBeVisible();
    },
  },
  {
    path: '/lessons/discover',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /discover video lessons/i })).toBeVisible();
      await expect(page.getByPlaceholder(/search/i)).toBeVisible();
    },
  },
  {
    path: '/marketplace/packages',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /content packages/i })).toBeVisible();
    },
  },
];

test.describe('Learner content routes smoke @learner @smoke', () => {
  for (const route of learnerContentRoutes) {
    test(`learner route ${route.path} renders correctly`, async ({ page }, testInfo) => {
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
