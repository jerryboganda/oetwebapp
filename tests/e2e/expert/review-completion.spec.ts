import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { createDisposableSpeakingReviewRequest, ensureSeededWritingReviewClaimed } from '../fixtures/api-auth';
import { waitForSessionGuardToClear } from '../fixtures/auth';

/**
 * Sets all six OET writing rubric scores on the V2 TutorMarkingWorkspace.
 * Each criterion is a number input (role "spinbutton") whose accessible name
 * comes from its `<label htmlFor="rubric-cN">` in
 * components/domain/writing/marking/RubricPanel.tsx (lines 75-108). C1 Purpose
 * is capped at 3; C2–C6 at 7 (shared.ts CRITERION_MAX), so we clamp per input.
 */
async function fillAllWritingRubricScores(page: Page) {
  const criteria: Array<{ label: RegExp; value: string }> = [
    { label: /C1 Purpose/, value: '3' },
    { label: /C2 Content/, value: '5' },
    { label: /C3 Conciseness & Clarity/, value: '5' },
    { label: /C4 Genre & Style/, value: '5' },
    { label: /C5 Organisation & Layout/, value: '5' },
    { label: /C6 Language/, value: '5' },
  ];
  for (const { label, value } of criteria) {
    const input = page.getByRole('spinbutton', { name: label });
    await input.fill(value);
    await input.blur();
  }
}

async function fillAllRubricScores(page: Page, preferredValue = '5') {
  // Capture aria-labels + each select's option values up front. Speaking reviews
  // mix two rubric scales: linguistic criteria score 0–6 and the OET clinical
  // criteria score 0–3, so a single hard-coded "5" is invalid for the clinical
  // selects. Pick the highest available option that satisfies `preferredValue`
  // (or the highest option overall when the preferred value is out of range).
  const scoreSelects = page.locator('select[aria-label^="Score for "]');
  const entries = await scoreSelects.evaluateAll((nodes) =>
    nodes.map((node) => {
      const select = node as HTMLSelectElement;
      const optionValues = Array.from(select.options)
        .map((option) => option.value)
        .filter((value) => value !== '');
      return {
        label: select.getAttribute('aria-label') ?? '',
        optionValues,
      };
    }),
  );

  expect(entries.length, 'Expected at least one rubric score selector').toBeGreaterThan(0);

  for (const { label, optionValues } of entries) {
    if (!label || optionValues.length === 0) continue;
    const value = optionValues.includes(preferredValue)
      ? preferredValue
      // Pick the highest numeric option as the "good" score for criteria whose
      // scale doesn't include the preferred value (e.g. clinical 0–3 selects).
      : optionValues
          .map((option) => ({ option, n: Number(option) }))
          .filter((entry) => Number.isFinite(entry.n))
          .sort((a, b) => b.n - a.n)[0]?.option ?? optionValues[0];
    await page.locator(`select[aria-label="${label.replace(/"/g, '\\"')}"]`).selectOption(value);
  }
}

test.describe('Tutor review completion workflows @expert', () => {
  // V2 marking is submission-based (TutorMarkingWorkspace). It has no client-side
  // "missing field" validation, no draft/Ctrl+S, and no voice notes — a Submit
  // review always posts to /v1/writing/tutor/reviews/{id}. This covers the real
  // happy path: render the marking surface, score the rubric + overall feedback,
  // submit, and land back on the expert queue.
  test('writing review scores the rubric and submits successfully', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/writing + multi-step interaction + final submit redirect

    const diagnostics = observePage(page);
    const finalComment = `QA final writing review ${Date.now()}`;
    const submissionId = await ensureSeededWritingReviewClaimed(request);

    await page.goto(`/expert/review/writing/${submissionId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches the marking context (submission + scenario + pre-assessment) in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByRole('heading', { name: /rubric scores/i })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/ai pre-analysis/i).first()).toBeVisible();

    await fillAllWritingRubricScores(page);
    await page.getByLabel('Overall feedback').fill(finalComment);

    await page.getByRole('button', { name: /submit review/i }).click();

    // onComplete pushes /expert/queue on a successful submit (app/expert/review/writing/[reviewRequestId]/page.tsx).
    await expect(page).toHaveURL(/\/expert\/queue$/, { timeout: 30_000 });
    await expect(page.getByRole('main', { name: /review queue/i })).toBeVisible();

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
    // After this final submission the queue may be empty for this fixture user; the empty state replaces the
    // "Review queue" hero heading. Assert the workspace landmark, which is rendered in both branches.
    await expect(page.getByRole('main', { name: /review queue/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  // V2 has no "rework request" concept. The closest real, distinct capability on
  // the marking surface is the AI pre-analysis confirm/edit/reject affordance:
  // "Use AI suggestion" applies the estimated bands into the editable rubric, then
  // the marker submits. This drives that path + a content-checklist verdict.
  test('writing review applies the AI pre-analysis suggestion and submits', async ({ page, request }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/review/writing + AI-apply + submit + queue redirect

    const diagnostics = observePage(page);
    const submissionId = await ensureSeededWritingReviewClaimed(request);

    await page.goto(`/expert/review/writing/${submissionId}`);
    await waitForSessionGuardToClear(page);
    // First-load fetches the marking context (submission + scenario + pre-assessment) in parallel; cold dev cache can exceed the 10s default.
    await expect(page.getByRole('heading', { name: /rubric scores/i })).toBeVisible({ timeout: 30_000 });

    // Confirm the AI pre-analysis suggestion applied — the panel shows the
    // "Suggestion applied — edit the rubric and comments freely." confirmation
    // (AiPreAnalysisPanel.tsx). (The compact "Applied" badge sits next to the
    // confidence flag, so an exact /^Applied$/ match is unreliable.)
    const aiPanel = page.getByRole('region', { name: /ai pre-analysis/i });
    await aiPanel.getByRole('button', { name: /use ai suggestion/i }).click();
    await expect(aiPanel.getByText(/suggestion applied/i)).toBeVisible();

    // Record a content-checklist verdict on the first key item (real V2 capability).
    await page.getByRole('radio', { name: 'Included' }).first().click();

    await page.getByLabel('Overall feedback').fill('Applied the AI estimate after checking the rubric and key content coverage.');
    await page.getByRole('button', { name: /submit review/i }).click();

    // onComplete pushes /expert/queue on a successful submit (app/expert/review/writing/[reviewRequestId]/page.tsx).
    await expect(page).toHaveURL(/\/expert\/queue$/, { timeout: 30_000 });
    await expect(page.getByRole('main', { name: /review queue/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
