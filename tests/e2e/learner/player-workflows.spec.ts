import { expect, test } from '@playwright/test';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Learner player workflows @learner @smoke', () => {
  test('legacy reading player redirects to structured Reading hub', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    testInfo.setTimeout(120000);

    await page.goto('/reading/player/rt-001', { waitUntil: 'domcontentloaded' });
    await waitForSessionGuardToClear(page, {
      recover: () => page.reload({ waitUntil: 'domcontentloaded' }),
      initialTimeoutMs: 15_000,
    });
    await expect(page).toHaveURL(/\/reading(?:\?|$)/, { timeout: 60000 });
    await expect(page.getByRole('heading', { name: /reading/i })).toBeVisible({ timeout: 60000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
