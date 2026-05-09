import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';
import { waitForSessionGuardToClear } from '../fixtures/auth';

test.describe('Tutor review workflows @expert @smoke', () => {
  test('writing review supports rubric edits and draft saves', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/writing + draft save round-trip exceeds default 60s budget

    const diagnostics = observePage(page);
    const purposeComment = `QA writing draft ${Date.now()}`;
    const { reviewRequestId } = await createDisposableWritingReviewRequest(request);

    await page.goto(`/expert/review/writing/${reviewRequestId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches review detail + history + learner context in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible({ timeout: 30_000 });

    await page.getByLabel('Score for Purpose').selectOption('5');
    await page.getByLabel('Comment for Purpose').fill(purposeComment);
    await expect(page.getByText(/unsaved/i)).toBeVisible();

    await page.getByRole('button', { name: /save draft/i }).click();

    // The success toast auto-dismisses after 5s (components/ui/alert.tsx) which races test polling under cold dev load;
    // assert the durable indicators instead — "Last saved:" timestamp and the "Unsaved" badge clearing.
    await expect(page.getByText(/last saved:/i)).toBeVisible({ timeout: 30_000 });
    await expect(page.getByLabel('Comment for Purpose')).toHaveValue(purposeComment);
    // DBG: dump dirty log before the unsaved assertion
    await page.waitForTimeout(2000);
    const log = await page.evaluate(() => (window as unknown as { __dirtyLog?: string[] }).__dirtyLog ?? []);
    console.log('[DIRTY LOG]\n' + log.join('\n'));
    await expect(page.getByText(/unsaved/i)).toHaveCount(0);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking review keeps role-card navigation and draft-save flow stable', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/speaking + tab navigation exceeds default 60s budget

    const diagnostics = observePage(page);
    const finalComment = `QA speaking draft ${Date.now()}`;
    const { reviewRequestId } = await createDisposableSpeakingReviewRequest(request);

    await page.goto(`/expert/review/speaking/${reviewRequestId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches review detail + history + learner context in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible({ timeout: 30_000 });

    await page.getByRole('tab', { name: /role card/i }).click();
    await expect(page.getByRole('region', { name: /role card details/i })).toContainText(/hospital surgical ward/i);
    await expect(page.getByRole('region', { name: /role card details/i })).toContainText(/provide a clinical handover\./i);

    await page.getByRole('tab', { name: /ai flags/i }).click();
    await expect(page.getByRole('button', { name: /go to .*s/i }).first()).toBeVisible();

    await page.getByLabel('Score for Intelligibility').selectOption('5');
    await page.getByLabel('Final overall comment').fill(finalComment);
    await expect(page.getByText(/unsaved/i)).toBeVisible();

    await page.getByRole('button', { name: /save draft/i }).click();

    // The success toast auto-dismisses after 5s (components/ui/alert.tsx) which races test polling under cold dev load;
    // assert the durable indicators instead — "Last saved:" timestamp and the "Unsaved" badge clearing.
    await expect(page.getByText(/last saved:/i)).toBeVisible({ timeout: 30_000 });
    await expect(page.getByLabel('Final overall comment')).toHaveValue(finalComment);
    await expect(page.getByText(/unsaved/i)).toHaveCount(0);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
