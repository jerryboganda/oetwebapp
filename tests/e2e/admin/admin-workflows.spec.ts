import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Admin workflows @admin @smoke', () => {
  test('admin can create a new content draft and reach the seeded editor flow', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const title = `QA Content Draft ${Date.now()}`;

    await page.goto('/admin/content/new');
    await expect(page.getByRole('heading', { name: /create content/i })).toBeVisible();

    await page.getByLabel('Content Title').fill(title);
    await page.getByLabel('Learner-Facing Description').fill('QA-created content draft used to validate the admin content workflow.');
    await page.getByRole('button', { name: /^save draft$/i }).click();

    await page.waitForURL(/\/admin\/content\/(?!new$)[^/]+$/, { timeout: 60000 });
    await expect(page.getByRole('heading', { name: new RegExp(`edit ${title}`, 'i') })).toBeVisible({ timeout: 30000 });
    await expect(page.getByRole('button', { name: /revisions/i })).toBeVisible();

    await page.getByRole('tab', { name: /criteria mapping/i }).click();
    await expect(page.getByText(/criteria are loaded live from the rubric library/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('user detail credit modal opens with accessible fields and closes cleanly', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/admin/users/mock-user-001');
    await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible({ timeout: 30000 });

    const adjustCreditsButton = page.getByRole('button', { name: /adjust credits/i });
    await adjustCreditsButton.click();

    const dialog = page.getByRole('dialog', { name: /adjust credits/i });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Credit Adjustment')).toBeVisible();
    await expect(dialog.getByLabel('Reason')).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(dialog).toHaveCount(0);
    await expect(adjustCreditsButton).toBeFocused();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
