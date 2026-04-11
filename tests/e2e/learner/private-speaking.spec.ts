import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Private Speaking Sessions — Learner @learner @private-speaking', () => {
  test('learner private-speaking page renders the hero and session booking UI', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    const sessionBanner = page.getByText(/checking your session/i);

    await page.goto('/private-speaking', { waitUntil: 'domcontentloaded' });
    if (await sessionBanner.isVisible().catch(() => false)) {
      await page.goto('/private-speaking', { waitUntil: 'domcontentloaded' });
    }

    await expect(sessionBanner).toBeHidden({ timeout: 90000 });

    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('learner private-speaking page shows tutor listing or empty state', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    const sessionBanner = page.getByText(/checking your session/i);

    await page.goto('/private-speaking', { waitUntil: 'domcontentloaded' });
    if (await sessionBanner.isVisible().catch(() => false)) {
      await page.goto('/private-speaking', { waitUntil: 'domcontentloaded' });
    }

    await expect(sessionBanner).toBeHidden({ timeout: 90000 });

    // The page should either render tutor cards or an appropriate message
    const heroHeading = page.getByRole('heading', { name: /private speaking sessions/i });
    await expect(heroHeading).toBeVisible({ timeout: 45000 });

    // The booking UI should have either a week navigation or a "not available" message
    const weekNav = page.getByRole('button', { name: /next week|previous week/i });
    const notAvailable = page.getByText(/not currently available|no tutors/i);
    const hasWeekNav = await weekNav.first().isVisible().catch(() => false);
    const hasNotAvailable = await notAvailable.first().isVisible().catch(() => false);

    expect(hasWeekNav || hasNotAvailable).toBe(true);

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('learner private-speaking success page renders with confirmation message', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/private-speaking/success', { waitUntil: 'domcontentloaded' });

    const heading = page.getByRole('heading', { name: /booking confirmed|session booked/i });
    await expect(heading).toBeVisible({ timeout: 45000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('learner private-speaking cancel page renders with cancellation message', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/private-speaking/cancel', { waitUntil: 'domcontentloaded' });

    const heading = page.getByRole('heading', { name: /payment cancelled|booking cancelled/i });
    await expect(heading).toBeVisible({ timeout: 45000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
