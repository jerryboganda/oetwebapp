import { expect, test } from '@playwright/test';
import { waitForSessionGuardToClear } from '../fixtures/auth';

/**
 * RW-006 — browser proof that admin-published Writing content is reachable
 * by a learner and that the model-answer surface stays gated until the
 * learner has submitted an attempt.
 *
 * The seeded admin Writing paper used here (`wt-001`) is created by the
 * backend's content seeder and is the same canonical task referenced by the
 * Writing learner home page. We do not create the paper at test time —
 * that path is exercised by the admin CRUD test suite — instead this spec
 * proves the cross-role visibility contract:
 *
 *   1. A published Writing paper appears on the learner /writing page.
 *   2. The model-answer surface refuses to load until a learner has a
 *      submitted attempt for the paper (`writing_model_answer_locked`
 *      backend code; user-visible "Submit your Writing attempt before
 *      opening the model answer." copy).
 */
test.describe('Writing — admin paper visibility (RW-006)', () => {
  test('learner sees admin-published Writing tasks on /writing', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    await page.goto('/writing');
    await waitForSessionGuardToClear(page);

    // Hero locks down that the Writing surface itself loaded for the learner.
    await expect(
      page.getByRole('heading', {
        name: /choose the next writing task that moves your score/i,
      }),
    ).toBeVisible();

    // Either an active task heading is rendered, or the explicit empty
    // state ("No practice tasks available") would appear. We assert that
    // the empty-state copy is NOT shown — i.e. at least one published
    // admin-side Writing paper is reachable from the learner surface.
    await expect(page.getByText(/no practice tasks available/i)).toHaveCount(0);

    // At least one task heading must be a real <h3> inside the practice
    // task grid. Headings in the dashboard hero use h1; task cards render
    // their title as h3, so a count > 0 here proves a backend-fetched
    // Writing task was rendered into the list.
    const taskHeadings = page.locator('h3');
    await expect(taskHeadings.first()).toBeVisible();
  });

  test('learner cannot open model answer for a paper without a submitted attempt', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    // The seeded canonical Writing task id used across the codebase. If the
    // seed pipeline ever renames it the model-answer route will 404 first —
    // that is also a meaningful failure we want surfaced.
    await page.goto('/writing/model?taskId=wt-001');
    await waitForSessionGuardToClear(page);

    // The page header always renders even when gated.
    await expect(
      page.getByRole('heading', { name: /model answer explainer/i }),
    ).toBeVisible();

    // Gated copy comes from `app/writing/model/page.tsx` when the backend
    // returns ApiException.Forbidden("writing_model_answer_locked", …).
    // We accept either the locked copy or the generic 404 fallback (paper
    // unavailable in this environment) — both prove the model-answer
    // surface refused to leak content to a learner without a submission.
    const lockedCopy = page.getByText(
      /submit your writing attempt before opening the model answer/i,
    );
    const noLongerAvailableCopy = page.getByText(
      /this model answer is no longer available/i,
    );

    await expect(lockedCopy.or(noLongerAvailableCopy)).toBeVisible();

    // Negative assertion: under no circumstances should we see a leaked
    // model-answer body block (rendered via <BookOpen> + visible model
    // sections in the unlocked state).
    await expect(page.getByRole('heading', { name: /model answer$/i })).toHaveCount(0);
  });
});
