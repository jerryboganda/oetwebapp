import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 12 — smoke for the learner self-service compliance surface
// (Phase 8 deliverable). Verifies that:
//   1. /speaking/recordings loads under the learner role.
//   2. The page shows either the learner's recordings or an empty
//      state — never a 500/console error.

test.describe('Speaking learner recordings list @learner @speaking', () => {
  test('renders recordings page with own recordings or empty state', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.route('**/v1/speaking/recordings/mine**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ rows: [] }),
      });
    });

    try {
      await page.goto('/speaking/recordings', { waitUntil: 'domcontentloaded' });

      // Page heading must be visible
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10_000 });

      await expectNoSevereClientIssues(diagnostics);
    } finally {
      await attachDiagnostics(testInfo, diagnostics);
    }
  });
});
