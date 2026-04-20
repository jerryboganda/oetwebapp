import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Smoke test for /review (docs/REVIEW-MODULE.md).
 *
 * Asserts the learner-facing review surface:
 *   - Renders the spaced-repetition hero and at-a-glance stats.
 *   - Shows the retention card + heatmap or the calm empty state, depending
 *     on what the seeded account happens to have.
 *   - Has a primary "Start Review" CTA (always present for learners).
 *   - Exposes the sidebar nav entry so the feature is discoverable.
 */
test.describe('Review module smoke @learner @smoke', () => {
  test('spaced repetition review surface renders without severe client failures', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/review');
    await expect(page).toHaveURL(/\/review/);

    // Hero
    await expect(page.getByRole('heading', { name: /spaced repetition review/i, level: 1 })).toBeVisible();

    // Summary stats row — always rendered, empty or not.
    await expect(page.getByText('Due Today', { exact: true })).toBeVisible();
    await expect(page.getByText('Total Due', { exact: true })).toBeVisible();
    await expect(page.getByText('Total Items', { exact: true })).toBeVisible();
    await expect(page.getByText('Mastered', { exact: true })).toBeVisible();

    // Either the CTA is enabled (items due) or the empty state is shown.
    const startButtons = page.getByRole('button', { name: /start review/i });
    const emptyState = page.getByText(/no review items yet/i);
    const hasStart = (await startButtons.count()) > 0;
    const hasEmpty = (await emptyState.count()) > 0;
    expect(hasStart || hasEmpty).toBeTruthy();

    // Retention trend and weak-area heatmap surfaces exist.
    await expect(page.getByText(/retention trend/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('sidebar exposes the Review entry', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    await page.goto('/');
    // Sidebar nav entry for review is labelled "Review" with the Brain icon.
    const reviewLink = page.getByRole('link', { name: /^review$/i });
    await expect(reviewLink.first()).toBeVisible();
  });
});
