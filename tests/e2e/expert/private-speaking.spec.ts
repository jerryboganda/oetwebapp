import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Private Speaking Sessions — Expert @expert @private-speaking', () => {
  test('expert private-speaking page renders the session management UI', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/expert/private-speaking', { waitUntil: 'domcontentloaded' });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('expert private-speaking page shows session sections', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/expert/private-speaking', { waitUntil: 'domcontentloaded' });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    // Check for session management sections
    const upcomingSessions = page.getByText(/upcoming sessions/i);
    const pastSessions = page.getByText(/past sessions/i);
    const availabilityRules = page.getByText(/weekly availability|availability rules/i);
    const notAvailable = page.getByText(/not currently available/i);

    const hasUpcoming = await upcomingSessions.first().isVisible().catch(() => false);
    const hasPast = await pastSessions.first().isVisible().catch(() => false);
    const hasAvailability = await availabilityRules.first().isVisible().catch(() => false);
    const hasNotAvailable = await notAvailable.first().isVisible().catch(() => false);

    // At least one section or the "not available" message should render
    expect(hasUpcoming || hasPast || hasAvailability || hasNotAvailable).toBe(true);

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('expert private-speaking page has availability management controls', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/expert/private-speaking', { waitUntil: 'domcontentloaded' });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    // Expert should either see availability rule management controls
    // or a "not available" indicator
    const addButton = page.getByRole('button', { name: /add rule|add availability|save/i });
    const notAvailable = page.getByText(/not currently available/i);

    const hasAddButton = await addButton.first().isVisible().catch(() => false);
    const hasNotAvailable = await notAvailable.first().isVisible().catch(() => false);

    expect(hasAddButton || hasNotAvailable).toBe(true);

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
