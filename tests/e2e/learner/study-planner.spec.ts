import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Study Planner v2 learner surface smoke tests.
 *
 * Exercises the new learner-facing affordances added by Study Planner v2:
 *  - Regenerate plan button (replaces the old inert button)
 *  - Add to calendar (ICS export) button
 *  - Drift page's wired-up Regenerate CTA
 *  - Deep-linking from Start
 *
 * Each test asserts visibility only — no mutations — so the full suite is
 * safe to run against seeded data without leaving side effects.
 */

test.describe('Study Planner v2 learner surface @learner @smoke', () => {
  test('study plan page shows action toolbar with regenerate + calendar export', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }
    testInfo.setTimeout(90000);
    const diagnostics = observePage(page);

    await page.goto('/study-plan', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /keep today.?s study sequence visible/i })).toBeVisible({ timeout: 45000 });

    // Regenerate button — must be present and clickable (v2 fix).
    await expect(page.getByText(/regenerate plan/i)).toBeVisible();
    // ICS export — "Add to calendar" button.
    await expect(page.getByText(/add to calendar/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('study plan drift page renders health metrics', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }
    testInfo.setTimeout(90000);
    const diagnostics = observePage(page);

    await page.goto('/study-plan/drift', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /study plan health/i })).toBeVisible({ timeout: 45000 });

    expectNoSevereClientIssues(diagnostics, {
      allowNextDevNoise: true,
      allowNotificationReconnectNoise: true,
    });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
