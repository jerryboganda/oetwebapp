import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { getSeededWritingSubmissionId } from '../fixtures/api-auth';
import { waitForSessionGuardToClear } from '../fixtures/auth';

test.describe('Expert writing queue navigation @expert @smoke', () => {
  // The real production entry-point for an expert marking a writing letter: open the
  // writing review queue inside the expert console (/expert/queue/assigned, backed by
  // /v1/tutors/writing/queue), claim the seeded submission if it is still pending, then
  // open it in the submission-keyed marking workspace. The other writing specs deep-link
  // the seeded submission id directly and never exercise this queue → review navigation.
  test('expert opens a writing review from the queue', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    test.setTimeout(120_000); // cold dev compile of /expert/queue/assigned + /expert/review/writing + claim exceeds default 60s budget

    const diagnostics = observePage(page);
    const submissionId = getSeededWritingSubmissionId();
    const rowPattern = new RegExp(submissionId.slice(0, 8));

    await page.goto('/expert/queue/assigned');
    await waitForSessionGuardToClear(page);

    await expect(page.getByRole('heading', { name: /writing review queue/i })).toBeVisible({ timeout: 30_000 });

    // The seeded submission renders as its 8-char id prefix in the queue table.
    const row = page.getByRole('row', { name: rowPattern });
    await expect(row).toBeVisible({ timeout: 30_000 });

    // Idempotent against a persistent backend: claim only while still pending. Once
    // claimed the row exposes an "Open review" link instead of the "Claim" button, and
    // SubmitMarkingReviewAsync never reverts the assignment, so it stays queue-visible.
    const claimButton = row.getByRole('button', { name: /^claim$/i });
    if (await claimButton.isVisible().catch(() => false)) {
      await claimButton.click();
    }

    const openLink = page.getByRole('row', { name: rowPattern }).getByRole('link', { name: /open review/i });
    await expect(openLink).toBeVisible({ timeout: 30_000 });
    await openLink.click();

    // Lands on the submission-keyed marking workspace (NOT a review-… ReviewRequest id),
    // and the V2 marking surface renders rather than 404-ing.
    await expect(page).toHaveURL(new RegExp(`/expert/review/writing/${submissionId}`), { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: /rubric scores/i })).toBeVisible({ timeout: 30_000 });

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
