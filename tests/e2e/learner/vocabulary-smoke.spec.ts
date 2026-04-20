import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Vocabulary smoke @learner @smoke', () => {
  test('learner vocabulary hub renders without severe client failures', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/vocabulary');
    await expect(page).toHaveURL(/\/vocabulary/);
    await expect(page.getByRole('heading', { name: 'Vocabulary', level: 1 })).toBeVisible();
    await expect(page.getByText(/Build your medical English vocabulary/i)).toBeVisible();

    // Quick-access links to flashcards, quiz, browse, history.
    await expect(page.getByRole('link', { name: /flashcard review/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /vocabulary quiz/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /browse terms/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /quiz history/i })).toBeVisible();

    // Navigate to browse.
    await page.getByRole('link', { name: /browse terms/i }).click();
    await expect(page).toHaveURL(/\/vocabulary\/browse/);
    await expect(page.getByRole('heading', { name: 'Browse Vocabulary', level: 1 })).toBeVisible();

    // Navigate to quiz.
    await page.goto('/vocabulary/quiz');
    await expect(page.getByRole('heading', { name: 'Vocabulary Quiz', level: 1 })).toBeVisible();

    // Navigate to history.
    await page.goto('/vocabulary/quiz/history');
    await expect(page.getByRole('heading', { name: 'Quiz History', level: 1 })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
