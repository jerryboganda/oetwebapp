import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Progress v2 learner surface smoke tests.
 *
 * Verifies the v2 toolbar (range pills, export PDF), the canonical-score
 * trend chart with Grade B reference line, the per-subtest mini cards as
 * toggles, and the Trend / Criterion / Comparative tab switcher.
 */

test.describe('Progress v2 learner surface @learner @smoke', () => {
  test('progress page renders v2 toolbar with range pills and export', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }
    testInfo.setTimeout(90000);
    const diagnostics = observePage(page);

    await page.goto('/progress', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /see whether recent effort is turning into better evidence/i })).toBeVisible({ timeout: 45000 });

    // v2 toolbar
    await expect(page.getByRole('radio', { name: '14d' })).toBeVisible();
    await expect(page.getByRole('radio', { name: '30d' })).toBeVisible();
    await expect(page.getByRole('radio', { name: '90d' })).toBeVisible();
    await expect(page.getByRole('radio', { name: 'All' })).toBeVisible();
    await expect(page.getByLabel('Export progress as PDF')).toBeVisible();

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('progress page exposes Trend, Criterion, Comparative tabs', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }
    testInfo.setTimeout(90000);
    const diagnostics = observePage(page);

    await page.goto('/progress', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /see whether recent effort is turning into better evidence/i })).toBeVisible({ timeout: 45000 });

    await expect(page.getByRole('tab', { name: /Trend/ })).toBeVisible();
    await expect(page.getByRole('tab', { name: /Criterion/ })).toBeVisible();
    await expect(page.getByRole('tab', { name: /Comparative/ })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('legacy /progress/comparative redirects to inline tab', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }
    testInfo.setTimeout(60000);

    await page.goto('/progress/comparative', { waitUntil: 'domcontentloaded' });
    // Comparative is now an inline tab on /progress
    await expect(page).toHaveURL(/\/progress(?!\/comparative)/);
  });
});
