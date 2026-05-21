import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 12 — smoke for the admin Speaking analytics aggregation
// (Phase 7 admin analytics deliverable). Verifies:
//   1. /admin/analytics/speaking loads under the admin role.
//   2. The three analytics cards render without throwing on
//      empty/array/rows-shaped payloads.

test.describe('Admin speaking analytics @admin @speaking', () => {
  test('analytics page renders three sections with empty data', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.route('**/v1/expert/speaking/analytics/class**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ rows: [] }),
      });
    });
    await page.route('**/v1/expert/speaking/analytics/tutor-consistency**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ rows: [] }),
      });
    });
    await page.route('**/v1/admin/speaking/analytics/content-difficulty**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ rows: [] }),
      });
    });

    try {
      await page.goto('/admin/analytics/speaking', { waitUntil: 'domcontentloaded' });

      await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10_000 });

      await expectNoSevereClientIssues(diagnostics);
    } finally {
      await attachDiagnostics(testInfo, diagnostics);
    }
  });
});
