import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

test.describe('Billing smoke @smoke @billing', () => {
  test('learner can navigate the billing center tabs', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/billing');
    await expect(page).toHaveURL(/\/billing/);

    // Hero "Your billing center" is visible
    await expect(page.getByText('Your billing center')).toBeVisible();

    // Click the "Credits & Add-ons" tab and verify the wallet currency tiles render
    await page.getByRole('tab', { name: /credits & add-ons/i }).click();

    // Wait for the panel to actually become active (tab uses framer-motion which
    // can race the click). The tab's aria-selected flips to true once mounted.
    await expect(page.getByRole('tab', { name: /credits & add-ons/i })).toHaveAttribute('aria-selected', 'true', { timeout: 10_000 });

    // Wallet tiles render with formatted currency (e.g. "$10.00", "A$10.00",
    // or "AU$10.00" depending on the wallet's currency code).
    const tilesGrid = page.locator('button', { hasText: /\$\s*\d+(\.\d{2})?/ });
    await expect(tilesGrid.first()).toBeVisible({ timeout: 15_000 });

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
