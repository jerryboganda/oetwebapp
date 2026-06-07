import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, ensureSeededWritingReviewClaimed } from '../fixtures/api-auth';
import { waitForSessionGuardToClear } from '../fixtures/auth';

test.describe('Tutor review workflows @expert @smoke', () => {
  // V2 marking has no separate draft-save; edits live in component state until the
  // Submit review POST. This covers the real interactive rubric editing on the V2
  // TutorMarkingWorkspace: the per-criterion stepper updates the input + the live
  // raw total, a per-criterion comment is captured, and the review submits.
  test('writing review supports rubric edits and live total', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/writing + interactive edit + submit exceeds default 60s budget

    const diagnostics = observePage(page);
    const contentComment = `QA writing rubric edit ${Date.now()}`;
    const submissionId = await ensureSeededWritingReviewClaimed(request);

    await page.goto(`/expert/review/writing/${submissionId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches the marking context (submission + scenario + pre-assessment) in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByRole('heading', { name: /rubric scores/i })).toBeVisible({ timeout: 30_000 });

    // Step C1 Purpose up to 3 via the rubric steppers (RubricPanel.tsx aria-labels).
    const purposeInput = page.getByRole('spinbutton', { name: /C1 Purpose/ });
    await page.getByRole('button', { name: 'Increase C1 Purpose' }).click();
    await expect(purposeInput).toHaveValue('1');

    // Set the remaining criteria so the rubric is complete, then assert the live raw total.
    await page.getByRole('spinbutton', { name: /C1 Purpose/ }).fill('3');
    for (const label of [/C2 Content/, /C3 Conciseness/, /C4 Genre/, /C5 Organisation/, /C6 Language/]) {
      await page.getByRole('spinbutton', { name: label }).fill('6');
    }
    // Raw total = 3 + 6*5 = 33 out of 38, shown in the rubric panel's "Raw total" readout.
    const rubricPanel = page.getByRole('region', { name: /rubric scores/i });
    await expect(rubricPanel.getByText('33/38')).toBeVisible();

    // Per-criterion comment textarea (exact label disambiguates it from the "C2 Content (0–7)" rubric input).
    await page.getByLabel('C2 Content', { exact: true }).fill(contentComment);
    await expect(page.getByLabel('C2 Content', { exact: true })).toHaveValue(contentComment);

    await page.getByRole('button', { name: /submit review/i }).click();

    // A successful submit pushes /expert/queue (app/expert/review/writing/[reviewRequestId]/page.tsx).
    await expect(page).toHaveURL(/\/expert\/queue$/, { timeout: 30_000 });
    await expect(page.getByRole('main', { name: /review queue/i })).toBeVisible();

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
