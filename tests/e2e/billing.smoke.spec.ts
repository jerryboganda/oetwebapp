import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

test.describe('Billing smoke @smoke @billing', () => {
  test('learner sees the billing center with Overview and Invoices only', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/billing');
    await expect(page).toHaveURL(/\/billing/);

    // Hero "Your billing center" is visible
    await expect(page.getByText('Your billing center')).toBeVisible();

    // Only Overview + Invoices tabs remain; the old storefront tabs are gone.
    await expect(page.getByRole('tab', { name: /^overview$/i })).toBeVisible();
    await expect(page.getByRole('tab', { name: /^invoices$/i })).toBeVisible();
    await expect(page.getByRole('tab', { name: /credits & add-ons/i })).toHaveCount(0);
    await expect(page.getByRole('tab', { name: /^plans$/i })).toHaveCount(0);
    await expect(page.getByRole('tab', { name: /ai credits/i })).toHaveCount(0);

    // Click the "Invoices" tab and verify either invoice rows or the empty-state copy
    await page.getByRole('tab', { name: /^invoices$/i }).click();

    const invoiceList = page.locator('ul li');
    const emptyState = page.getByText(
      /No invoices yet\. They will appear after your first paid checkout\./i,
    );

    await expect(async () => {
      const hasRows = (await invoiceList.count()) > 0;
      const hasEmpty = await emptyState.isVisible().catch(() => false);
      expect(hasRows || hasEmpty).toBe(true);
    }).toPass({ timeout: 10_000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
