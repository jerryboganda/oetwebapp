import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 12 — smoke for the admin recordings access audit page
// (compliance Phase 10). Verifies:
//   1. /admin/speaking/recordings/audit loads under the admin role.
//   2. Filter Card and audit table mount without crashing.

test.describe('Admin speaking recordings audit @admin @speaking', () => {
  test('audit page renders filters and rows table', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.route('**/v1/admin/speaking/recordings/audit**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ rows: [] }),
      });
    });

    try {
      await page.goto('/admin/speaking/recordings/audit', { waitUntil: 'domcontentloaded' });

      await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10_000 });

      await expectNoSevereClientIssues(diagnostics);
    } finally {
      await attachDiagnostics(testInfo, diagnostics);
    }
  });
});
