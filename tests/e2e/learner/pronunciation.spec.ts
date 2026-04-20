import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';

test.describe('Pronunciation module smoke @learner @smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test('pronunciation hub loads, filters, and links into a drill', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    testInfo.setTimeout(120000);
    const diagnostics = observePage(page);

    await page.goto('/pronunciation', { waitUntil: 'domcontentloaded' });
    await waitForSessionGuardToClear(page, {
      recover: () => page.goto('/pronunciation', { waitUntil: 'domcontentloaded' }),
      timeoutMs: 60_000,
    });

    const hubHeading = page.getByRole('heading', { name: /pronunciation drills/i });
    await expect(hubHeading).toBeVisible({ timeout: 45000 });

    // Library filters use real labelled controls for accessibility.
    const difficulty = page.getByLabel(/filter drills by difficulty/i);
    await expect(difficulty).toBeVisible({ timeout: 30000 });

    const focus = page.getByLabel(/filter drills by focus area/i);
    await expect(focus).toBeVisible({ timeout: 30000 });

    // Switch to the phoneme focus and confirm cards re-render.
    await focus.selectOption('phoneme');
    await expect(page.getByText(/pronunciation drill cards/i)).toBeVisible({ timeout: 30000 });

    // Click the first drill card (labelled "Open pronunciation drill ...").
    const firstDrillLink = page.locator('a[aria-label^="Open pronunciation drill"]').first();
    await expect(firstDrillLink).toBeVisible({ timeout: 30000 });
    await firstDrillLink.click();

    // Drill page landing: heading + recorder region.
    await expect(page).toHaveURL(/\/pronunciation\/[^/]+/);
    await expect(page.getByRole('region', { name: /record your attempt/i })).toBeVisible({ timeout: 45000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('drill page renders without severe client errors for a known seed drill', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }
    const diagnostics = observePage(page);
    await page.goto('/pronunciation/pd-001', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('region', { name: /record your attempt/i })).toBeVisible({ timeout: 60000 });
    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
