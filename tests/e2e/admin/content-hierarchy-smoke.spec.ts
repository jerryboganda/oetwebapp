import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

const contentHierarchyRoutes = [
  {
    path: '/admin/content/import',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /content import/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /import json/i })).toBeVisible();
    },
  },
  {
    path: '/admin/content/dedup',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /deduplication/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /run dedup scan/i })).toBeVisible();
    },
  },
  {
    path: '/admin/content/media',
    assertions: async (page: Page) => {
      await expect(page.getByRole('heading', { name: /media asset/i })).toBeVisible();
      await expect(page.getByRole('button', { name: /run audit/i })).toBeVisible();
    },
  },
];

test.describe('Admin content hierarchy smoke @admin @smoke', () => {
  for (const route of contentHierarchyRoutes) {
    test(`admin route ${route.path} renders correctly`, async ({ page }, testInfo) => {
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
