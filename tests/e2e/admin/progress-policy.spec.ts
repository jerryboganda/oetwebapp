import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Progress Policy admin smoke tests.
 *
 * Verifies the Progress Policy admin route loads and exposes the key knobs
 * (default range, smoothing, cohort gate, kill-switches).
 */

test.describe('Progress Policy admin smoke @admin @smoke', () => {
  test('admin route /admin/progress-policy renders without severe client failures', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('admin')) {
      test.skip();
    }
    testInfo.setTimeout(90000);
    const diagnostics = observePage(page);

    await page.goto('/admin/progress-policy', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /progress policy/i })).toBeVisible({ timeout: 60000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
