import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Study Planner v2 admin surface smoke tests.
 *
 * Verifies that every major admin route renders without severe client
 * failures and shows the expected page headings. Each route corresponds to a
 * primary CRUD / monitoring workflow admins need to manage the Study Planner
 * end-to-end.
 */

const studyPlannerAdminRoutes = [
  {
    path: '/admin/study-planner',
    heading: /study planner/i,
    label: 'Admin hub',
  },
  {
    path: '/admin/study-planner/tasks',
    heading: /task templates/i,
    label: 'Task template library',
  },
  {
    path: '/admin/study-planner/templates',
    heading: /plan templates/i,
    label: 'Plan template library',
  },
  {
    path: '/admin/study-planner/rules',
    heading: /assignment rules/i,
    label: 'Assignment rules',
  },
  {
    path: '/admin/study-planner/drift-policy',
    heading: /drift policy/i,
    label: 'Drift policy',
  },
  {
    path: '/admin/study-planner/insights',
    heading: /study planner insights/i,
    label: 'Fleet insights',
  },
];

test.describe('Study Planner admin smoke @admin @smoke', () => {
  for (const route of studyPlannerAdminRoutes) {
    test(`admin route ${route.path} (${route.label}) renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('admin')) {
        test.skip();
      }
      testInfo.setTimeout(90000);
      const diagnostics = observePage(page);

      await page.goto(route.path, { waitUntil: 'domcontentloaded' });
      await expect(page).toHaveURL(new RegExp(route.path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await expect(page.getByRole('heading', { name: route.heading })).toBeVisible({ timeout: 60000 });

      expectNoSevereClientIssues(diagnostics, {
        allowNextDevNoise: true,
        allowNotificationReconnectNoise: true,
      });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});

test.describe('Study Planner admin navigation @admin @smoke', () => {
  test('admin hub links route to all subsections', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('admin')) {
      test.skip();
    }
    testInfo.setTimeout(120000);

    await page.goto('/admin/study-planner', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /study planner/i })).toBeVisible({ timeout: 60000 });

    // Hub exposes a card for each major subsection. Use a partial-match link
    // lookup so the test is resilient to incidental copy tweaks.
    for (const label of ['Task Templates', 'Plan Templates', 'Assignment Rules', 'Drift Policy', 'Insights']) {
      await expect(page.getByRole('link', { name: new RegExp(label, 'i') })).toBeVisible();
    }
  });
});
