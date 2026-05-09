import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';
import { waitForSessionGuardToClear } from '../fixtures/auth';

const keyboardModifier = process.platform === 'darwin' ? 'Meta' : 'Control';

async function fillAllRubricScores(page: Page, value = '5') {
  // Capture aria-labels up front; the page can re-render (audio playback, draft
  // auto-saves, AI panel updates) and invalidate nth-based locators mid-loop,
  // which manifests as a `selectOption` test-timeout under cold dev load.
  const scoreSelects = page.locator('select[aria-label^="Score for "]');
  const labels = await scoreSelects.evaluateAll((nodes) =>
    nodes.map((node) => (node as HTMLSelectElement).getAttribute('aria-label') ?? ''),
  );

  expect(labels.length, 'Expected at least one rubric score selector').toBeGreaterThan(0);

  for (const label of labels) {
    if (!label) continue;
    await page.locator(`select[aria-label="${label.replace(/"/g, '\\"')}"]`).selectOption(value);
  }
}

test.describe('Tutor review completion workflows @expert', () => {
  test('writing review validates missing fields, supports shortcut save, and submits successfully', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/writing + multi-step interaction + final submit redirect

    const diagnostics = observePage(page);
    const finalComment = `QA final writing review ${Date.now()}`;
    const { reviewRequestId } = await createDisposableWritingReviewRequest(request);

    await page.goto(`/expert/review/writing/${reviewRequestId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches review detail + history + learner context in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible({ timeout: 30_000 });

    await page.getByRole('button', { name: /submit review/i }).click();
    await expect(page.getByText(/please complete all \d+ rubric score\(s\) before submitting\./i)).toBeVisible();

    await fillAllRubricScores(page);

    await page.getByRole('button', { name: /submit review/i }).click();
    await expect(page.getByText(/please provide a final overall comment\./i)).toBeVisible();

    await page.getByLabel('Final overall comment').fill(finalComment);
    await page.keyboard.press(`${keyboardModifier}+S`);

    // The success toast auto-dismisses after 5s (components/ui/alert.tsx) which races test polling under cold dev load;
    // assert the durable indicators instead — "Unsaved" badge clearing and the persisted comment value.
    await expect(page.getByText(/^Unsaved$/i)).toHaveCount(0, { timeout: 30_000 });
    await expect(page.getByLabel('Final overall comment')).toHaveValue(finalComment);

    await page.keyboard.press(`${keyboardModifier}+Enter`);

    await expect(page.getByText(/review submitted successfully\./i)).toBeVisible();
    await expect(page).toHaveURL(/\/expert\/queue$/);
    await expect(page.locator('main').getByRole('heading', { name: /^review queue$/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking review navigates supporting tabs and submits successfully', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/speaking + tab navigation + submit exceeds default 60s budget

    const diagnostics = observePage(page);
    const finalComment = `QA final speaking review ${Date.now()}`;
    const { reviewRequestId } = await createDisposableSpeakingReviewRequest(request);

    await page.goto(`/expert/review/speaking/${reviewRequestId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches review detail + history + learner context in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible({ timeout: 30_000 });

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

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('writing review supports a real rework request after validating the reason field', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/writing + rework submit + queue redirect

    const diagnostics = observePage(page);
    const { reviewRequestId } = await createDisposableWritingReviewRequest(request);

    await page.goto(`/expert/review/writing/${reviewRequestId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches review detail + history + learner context in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible({ timeout: 30_000 });

    await page.getByRole('button', { name: /request rework/i }).click();
    await page.getByRole('button', { name: /submit rework/i }).click();
    await expect(page.getByText(/please provide a reason for the rework request\./i)).toBeVisible();

    await page.getByLabel('Rework reason').fill('Please tighten the opening purpose and reduce non-essential history before final review.');
    await page.getByRole('button', { name: /submit rework/i }).click();

    await expect(page.getByText(/rework request submitted\./i)).toBeVisible();
    await expect(page).toHaveURL(/\/expert\/queue$/);
    await expect(page.locator('main').getByRole('heading', { name: /^review queue$/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
