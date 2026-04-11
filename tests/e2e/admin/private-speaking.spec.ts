import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Private Speaking Sessions — Admin @admin @private-speaking', () => {
  test('admin private-speaking page renders the management dashboard', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/admin/private-speaking', { waitUntil: 'domcontentloaded' });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    // Verify admin summary cards render
    const summaryCards = page.locator('[data-slot="summary-card"], [class*="summary"]');
    const hasCards = await summaryCards.first().isVisible().catch(() => false);

    // Verify configuration panel renders
    const configPanel = page.getByText(/module configuration|session duration|session price/i);
    const hasConfig = await configPanel.first().isVisible().catch(() => false);

    expect(hasCards || hasConfig).toBe(true);

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin private-speaking page shows tutor management section', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/admin/private-speaking', { waitUntil: 'domcontentloaded' });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    // The tutor management section should be present
    const tutorSection = page.getByText(/create tutor profile|tutor profiles|tutors/i);
    await expect(tutorSection.first()).toBeVisible({ timeout: 15000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin private-speaking page shows booking and audit log sections', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/admin/private-speaking', { waitUntil: 'domcontentloaded' });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    // Scroll down to see bookings and audit log sections
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));

    const bookingsSection = page.getByText(/bookings|recent bookings/i);
    const auditSection = page.getByText(/audit log|recent audit/i);

    const hasBookings = await bookingsSection.first().isVisible().catch(() => false);
    const hasAudit = await auditSection.first().isVisible().catch(() => false);

    expect(hasBookings || hasAudit).toBe(true);

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
