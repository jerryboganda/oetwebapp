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

  test('legacy /review redirects to /recalls/cards', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/review');
    await expect(page).toHaveURL(/\/recalls\/cards$/);
  });

  test('legacy /vocabulary/quiz redirects to /recalls/cards', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/vocabulary/quiz');
    await expect(page).toHaveURL(/\/recalls\/cards$/);
  });

  test('Recalls landing renders the four-tab aggregator', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/recalls');
    await expect(page).toHaveURL(/\/recalls$/);
    await expect(page.getByRole('heading', { level: 1 })).toContainText(/recalls/i);
    // The four tabs should all be present on the landing.
    await expect(page.getByText(/Vocabulary banks/i)).toBeVisible();
    await expect(page.getByText(/Spaced-repetition review/i)).toBeVisible();
    await expect(page.getByText(/Listen, recognise, type/i)).toBeVisible();
    await expect(page.getByText(/Mastery & weak areas/i)).toBeVisible();
  });

  test('Recalls cards page exposes the quiz mode picker', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) test.skip();
    await page.goto('/recalls/cards');
    await expect(page.getByText(/Listen & type/i)).toBeVisible();
    await expect(page.getByText(/High-risk spelling/i)).toBeVisible();
    await expect(page.getByText(/Starred only/i)).toBeVisible();
  });
});
