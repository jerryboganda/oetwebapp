import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, createDisposableWritingReviewRequest } from '../fixtures/api-auth';

test.describe('Expert review workflows @expert @smoke', () => {
  test('writing review supports rubric edits and draft saves', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const purposeComment = `QA writing draft ${Date.now()}`;
    const { reviewRequestId } = await createDisposableWritingReviewRequest(request);

    await page.goto(`/expert/review/writing/${reviewRequestId}`);
    await expect(page.getByRole('heading', { name: /review rubric/i })).toBeVisible();

    await page.getByLabel('Score for Purpose').selectOption('5');
    await page.getByLabel('Comment for Purpose').fill(purposeComment);
    await expect(page.getByText(/unsaved/i)).toBeVisible();

    await page.getByRole('button', { name: /save draft/i }).click();

    await expect(page.getByText(/draft saved successfully\./i)).toBeVisible();
    await expect(page.getByText(/last saved:/i)).toBeVisible();
    await expect(page.getByLabel('Comment for Purpose')).toHaveValue(purposeComment);
    await expect(page.getByText(/unsaved/i)).toHaveCount(0);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking review keeps role-card navigation and draft-save flow stable', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const finalComment = `QA speaking draft ${Date.now()}`;
    const { reviewRequestId } = await createDisposableSpeakingReviewRequest(request);

    await page.goto(`/expert/review/speaking/${reviewRequestId}`);
    await expect(page.getByText(/candidate audio submission/i).first()).toBeVisible();

    await page.getByRole('tab', { name: /role card/i }).click();
    await expect(page.getByRole('region', { name: /role card details/i })).toContainText(/hospital surgical ward/i);
    await expect(page.getByRole('region', { name: /role card details/i })).toContainText(/provide a clinical handover\./i);

    await page.getByRole('tab', { name: /ai flags/i }).click();
    await expect(page.getByRole('button', { name: /go to .*s/i }).first()).toBeVisible();

    await page.getByLabel('Score for Intelligibility').selectOption('5');
    await page.getByLabel('Final overall comment').fill(finalComment);
    await expect(page.getByText(/unsaved/i)).toBeVisible();

    await page.getByRole('button', { name: /save draft/i }).click();

    await expect(page.getByText(/draft saved successfully\./i)).toBeVisible();
    await expect(page.getByText(/last saved:/i)).toBeVisible();
    await expect(page.getByLabel('Final overall comment')).toHaveValue(finalComment);
    await expect(page.getByText(/unsaved/i)).toHaveCount(0);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
