import { promises as fs } from 'node:fs';
import { expect, test } from '@playwright/test';
import { fetchAdminUserDetailApi } from '../fixtures/api-auth';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Admin deep CRUD and mutation workflows @admin', () => {
  test('admin can create, publish, and inspect revisions for disposable content', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const title = `QA Publishable Content ${Date.now()}`;

    await page.goto('/admin/content/new');
    await expect(page.getByRole('heading', { name: /create content/i })).toBeVisible();

    await page.getByLabel('Content Title').fill(title);
    await page.getByLabel('Learner-Facing Description').fill('Disposable QA content used to validate draft-save, publish, and revision visibility.');
    await page.getByRole('button', { name: /^save draft$/i }).click();

    await page.waitForURL(/\/admin\/content\/(?!new$)[^/]+$/, { timeout: 60000 });
    await expect(page.getByRole('heading', { name: new RegExp(`edit ${title}`, 'i') })).toBeVisible({ timeout: 30000 });

    await page.getByRole('button', { name: /^publish$/i }).click();
    await expect(page.getByText(/content saved and published\./i)).toBeVisible();

    await page.getByRole('button', { name: /revisions/i }).click();
    await page.waitForURL(/\/admin\/content\/[^/]+\/revisions$/, { timeout: 60000 });
    await expect(page.getByRole('heading', { name: /revision history/i })).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin audit logs support filtering, drawer inspection, and CSV export', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/admin/audit-logs');
    await expect(page.getByRole('heading', { name: /audit logs/i })).toBeVisible();

    await page.getByPlaceholder(/search actions, actors, resources, or details/i).fill('Saved Review Draft');
    const filterTrigger = page.getByRole('button', { name: /filter by action/i });
    const eventRow = page.getByRole('button', { name: /saved review draft/i }).first();
    await expect(eventRow).toBeVisible();

    await filterTrigger.click();
    // The filter-bar option button renders "<label> <count>"; do not anchor
    // the regex to end-of-string or the optional count breaks the match.
    const actionOption = page.locator('button[role="checkbox"]').filter({ hasText: /Saved Review Draft/ }).first();
    await expect(actionOption).toBeVisible({ timeout: 15000 });
    await actionOption.click();

    await eventRow.focus();
    await expect(eventRow).toBeFocused();
    await page.keyboard.press('Enter');

    const drawer = page.getByRole('dialog', { name: /audit event detail/i });
    await expect(drawer).toBeVisible();
    await expect(drawer).toContainText(/event id/i);
    await expect(drawer).toContainText(/details/i);

    await page.keyboard.press('Escape');
    await expect(drawer).toHaveCount(0);
    await expect(eventRow).toBeFocused();

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: /export csv/i }).click(),
    ]);

    const downloadPath = testInfo.outputPath(download.suggestedFilename() || 'audit-logs.csv');
    await download.saveAs(downloadPath);
    const stats = await fs.stat(downloadPath);
    expect(stats.size, 'Expected exported audit log CSV to be non-empty').toBeGreaterThan(0);
    await expect(page.getByText(/audit log export downloaded\./i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin user detail supports credit adjustment and protected account actions', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const userId = 'mock-user-001';
    const before = await fetchAdminUserDetailApi(request, userId) as { creditBalance?: number };

    await page.goto(`/admin/users/${userId}`);
    await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible({ timeout: 30000 });

    await page.getByRole('button', { name: /adjust credits/i }).click();
    await page.getByLabel('Credit Adjustment').fill('1');
    await page.getByLabel('Reason').fill(`QA adjustment ${Date.now()}`);
    await page.getByRole('button', { name: /save adjustment/i }).click();

    await expect(page.getByText(/credit balance updated successfully\./i)).toBeVisible();
    await expect.poll(async () => {
      const detail = await fetchAdminUserDetailApi(request, userId) as { creditBalance?: number };
      return detail.creditBalance ?? 0;
    }).toBe((before.creditBalance ?? 0) + 1);

    const resetPasswordButton = page.getByRole('button', { name: /reset password/i });
    if (await resetPasswordButton.count()) {
      await resetPasswordButton.click();
      await expect(page.getByText(/password reset initiated for/i)).toBeVisible();
    }

    const suspendButton = page.getByRole('button', { name: /suspend account/i });
    if (await suspendButton.count()) {
      await suspendButton.click();
      await expect(page.getByText(/account suspended successfully\./i)).toBeVisible();

      const reactivateButton = page.getByRole('button', { name: /reactivate account/i });
      await expect(reactivateButton).toBeVisible();
      await reactivateButton.click();
      await expect(page.getByText(/account reactivated successfully\./i)).toBeVisible();
    }

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
