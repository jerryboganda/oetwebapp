import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Learner player workflows @learner @smoke', () => {
  test('reading player preserves answers and review flags across question navigation', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const answer = 'approximately 1 in 10';

    await page.goto('/reading/player/rt-001');
    await expect(page.getByText(/question 1 of 3/i)).toBeVisible();

    const answerInput = page.getByPlaceholder('Type your answer here...');
    await answerInput.fill(answer);

    await page.getByRole('button', { name: /flag this question for review/i }).click();
    await expect(page.getByRole('button', { name: /remove flag from this question/i })).toBeVisible();

    await page.getByRole('button', { name: /^next$/i }).click();
    await expect(page.getByText(/question 2 of 3/i)).toBeVisible();

    await page.getByRole('button', { name: /previous/i }).click();
    await expect(page.getByText(/question 1 of 3/i)).toBeVisible();
    await expect(answerInput).toHaveValue(answer);
    await expect(page.getByRole('button', { name: /remove flag from this question/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
