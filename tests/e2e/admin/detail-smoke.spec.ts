import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const adminDetailRoutes = [
  {
    path: '/admin/content/lt-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /edit consultation: asthma management review/i })).toBeVisible();
      await expect(page.getByRole('heading', { name: /core content metadata/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /publish/i })).toBeVisible();
    },
  },
  {
    path: '/admin/users/mock-user-001',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible();
      await expect(page.getByText(/operational context/i).first()).toBeVisible();
      await expect(page.getByText(/access controls/i).first()).toBeVisible();
    },
  },
];

test.describe('Admin detail smoke @admin @smoke', () => {
  for (const route of adminDetailRoutes) {
    test(`admin route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('admin')) {
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
