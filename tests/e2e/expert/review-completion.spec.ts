import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';

const keyboardModifier = process.platform === 'darwin' ? 'Meta' : 'Control';

async function fillAllRubricScores(page: Page, value = '5') {
  const scoreSelects = page.locator('select[aria-label^="Score for "]');
  const count = await scoreSelects.count();

  expect(count, 'Expected at least one rubric score selector').toBeGreaterThan(0);

  for (let index = 0; index < count; index += 1) {
    await scoreSelects.nth(index).selectOption(value);
  }
}

test.describe('Expert review completion workflows @expert', () => {
  test('writing review validates missing fields, supports shortcut save, and submits successfully', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const finalComment = `QA final writing review ${Date.now()}`;
    const { reviewRequestId } = await createDisposableWritingReviewRequest(request);

    await page.goto(`/expert/review/writing/${reviewRequestId}`);
    await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();

    await page.getByRole('button', { name: /submit review/i }).click();
    await expect(page.getByText(/please complete all \d+ rubric score\(s\) before submitting\./i)).toBeVisible();

    await fillAllRubricScores(page);

    await page.getByRole('button', { name: /submit review/i }).click();
    await expect(page.getByText(/please provide a final overall comment\./i)).toBeVisible();

    await page.getByLabel('Final overall comment').fill(finalComment);
    await page.keyboard.press(`${keyboardModifier}+S`);

    await expect(page.getByRole('status').filter({ hasText: /draft saved successfully\./i })).toBeVisible();
    await expect(page.getByText(/^Unsaved$/i)).toHaveCount(0);
    await expect(page.getByLabel('Final overall comment')).toHaveValue(finalComment);

    await page.keyboard.press(`${keyboardModifier}+Enter`);

    await expect(page.getByText(/review submitted successfully\./i)).toBeVisible();
    await expect(page).toHaveURL(/\/expert\/queue$/);
    await expect(page.locator('main').getByRole('heading', { name: /^review queue$/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking review navigates supporting tabs and submits successfully', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const finalComment = `QA final speaking review ${Date.now()}`;
    const { reviewRequestId } = await createDisposableSpeakingReviewRequest(request);

    await page.goto(`/expert/review/speaking/${reviewRequestId}`);
    await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible();

    await page.getByRole('tab', { name: /role card/i }).click();
    await expect(page.getByRole('region', { name: /role card details/i })).toContainText(/provide a clinical handover/i);

    await page.getByRole('tab', { name: /ai flags/i }).click();
    const aiJumpButton = page.getByRole('button', { name: /go to .*s/i }).first();
    await expect(aiJumpButton).toBeVisible();
    await aiJumpButton.click();

    await fillAllRubricScores(page);
    await page.getByLabel('Final overall comment').fill(finalComment);
    await page.getByRole('button', { name: /submit review/i }).click();

    await expect(page.getByText(/review submitted successfully\./i)).toBeVisible();
    await expect(page).toHaveURL(/\/expert\/queue$/);
    await expect(page.locator('main').getByRole('heading', { name: /^review queue$/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('writing review supports a real rework request after validating the reason field', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const { reviewRequestId } = await createDisposableWritingReviewRequest(request);

    await page.goto(`/expert/review/writing/${reviewRequestId}`);
    await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();

    await page.getByRole('button', { name: /request rework/i }).click();
    await page.getByRole('button', { name: /submit rework/i }).click();
    await expect(page.getByText(/please provide a reason for the rework request\./i)).toBeVisible();

    await page.getByLabel('Rework reason').fill('Please tighten the opening purpose and reduce non-essential history before final review.');
    await page.getByRole('button', { name: /submit rework/i }).click();

    await expect(page.getByText(/rework request submitted\./i)).toBeVisible();
    await expect(page).toHaveURL(/\/expert\/queue$/);
    await expect(page.locator('main').getByRole('heading', { name: /^review queue$/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
