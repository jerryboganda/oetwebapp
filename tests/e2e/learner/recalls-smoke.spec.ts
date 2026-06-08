import { expect, test } from '@playwright/test';

/**
 * Recalls module smoke — verifies the unified recalls landing, the three
 * sub-pages, and the legacy redirect contracts wired in next.config.ts.
 * See docs/RECALLS-MODULE-PLAN.md.
 */
test.describe('Recalls module smoke @learner @smoke', () => {
  test('legacy /vocabulary redirects to /recalls/words', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/vocabulary');
    await expect(page).toHaveURL(/\/recalls\/words$/);
  });

  test('legacy /review redirects to /recalls/words', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/review');
    await expect(page).toHaveURL(/\/recalls\/words$/);
  });

  test('legacy /vocabulary/quiz redirects to /recalls/words', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/vocabulary/quiz');
    await expect(page).toHaveURL(/\/recalls\/words$/);
  });

  test('Recalls landing renders the Words and Favourites tabs', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/recalls');
    await expect(page).toHaveURL(/\/recalls$/);
    await expect(page.getByRole('heading', { level: 1 })).toContainText(/recalls/i);
    // The remaining tabs should be present on the landing.
    await expect(page.getByText(/Vocabulary banks/i)).toBeVisible();
    await expect(page.getByText(/Saved words to review later/i)).toBeVisible();
  });
});
