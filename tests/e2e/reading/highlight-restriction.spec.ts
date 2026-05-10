import { expect, test } from '@playwright/test';

/**
 * Reading rulebook A R10.8 (CRITICAL) — in Parts B and C the learner may
 * highlight the passage and the question stem, but NOT the answer choices.
 *
 * Implementation contract enforced here:
 *   - Passage containers carry `data-reading-highlight-scope="passage"`.
 *   - Stem `<h3>` carries `data-reading-highlight-scope="stem"`.
 *   - Each MCQ choice `<label>` carries `data-reading-answer-choice="true"`
 *     and MUST NOT carry any `data-reading-highlight-scope` attribute.
 *
 * The test stubs the reading-papers API so it does not depend on backend
 * seed state. It asserts the structural pin: choice elements never expose
 * a highlight scope, regardless of which part is rendered.
 */

const PAPER_ID = 'rule-r108-paper';
const ATTEMPT_ID = 'rule-r108-attempt';

test.describe('Reading R10.8 — choice highlight restriction @reading @rulebook', () => {
  test.beforeEach(async ({ page }) => {
    const now = Date.now();
    const partADeadline = new Date(now + 15 * 60 * 1000).toISOString();
    const partBCDeadline = new Date(now + 60 * 60 * 1000).toISOString();
    const startedAt = new Date(now).toISOString();

    await page.route(/\/v1\/reading-papers\/papers\/[^/]+\/structure$/, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          paper: { id: PAPER_ID, title: 'R10.8 fixture paper', slug: PAPER_ID, subtestCode: 'reading' },
          parts: [
            {
              id: 'part-b',
              partCode: 'B',
              timeLimitMinutes: 45,
              maxRawScore: 6,
              instructions: null,
              texts: [
                {
                  id: 'b-text-1',
                  displayOrder: 1,
                  title: 'Workplace memo',
                  source: 'Fixture',
                  bodyHtml: '<p>Short workplace extract for R10.8 highlight check.</p>',
                  wordCount: 9,
                  topicTag: null,
                },
              ],
              questions: [
                {
                  id: 'b-q-1',
                  readingTextId: 'b-text-1',
                  displayOrder: 1,
                  points: 1,
                  questionType: 'MultipleChoice3',
                  stem: 'What is the purpose of the memo?',
                  options: [
                    { value: 'A', label: 'To inform staff of a policy change' },
                    { value: 'B', label: 'To request leave approval' },
                    { value: 'C', label: 'To announce a meeting time' },
                  ],
                },
              ],
            },
          ],
        }),
      });
    });

    await page.route(/\/v1\/reading-papers\/papers\/[^/]+\/attempts$/, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          attemptId: ATTEMPT_ID,
          startedAt,
          deadlineAt: partBCDeadline,
          partADeadlineAt: partADeadline,
          partBCDeadlineAt: partBCDeadline,
          answeredCount: 0,
          canResume: true,
          paperTitle: 'R10.8 fixture paper',
          partATimerMinutes: 15,
          partBCTimerMinutes: 45,
          partABreakAvailable: false,
          partABreakResumed: true,
          partBCTimerPausedAt: null,
          partBCPausedSeconds: 0,
          partABreakMaxSeconds: 600,
        }),
      });
    });
  });

  test('B/C answer choices have data-reading-answer-choice but no highlight scope', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    await page.goto(`/reading/paper/${PAPER_ID}`, { waitUntil: 'domcontentloaded' });
    await page.getByRole('button', { name: /start attempt/i }).click();

    const stem = page.locator('[data-reading-highlight-scope="stem"]');
    await expect(stem).toBeVisible({ timeout: 30000 });

    const choices = page.locator('[data-reading-answer-choice="true"]');
    await expect(choices).toHaveCount(3);

    // R10.8: no choice element carries any highlight scope marker.
    const scopedChoiceCount = await page
      .locator('[data-reading-answer-choice="true"][data-reading-highlight-scope]')
      .count();
    expect(scopedChoiceCount).toBe(0);

    // And no descendant of a choice carries a highlight scope marker either.
    const scopedDescendantCount = await page
      .locator('[data-reading-answer-choice="true"] [data-reading-highlight-scope]')
      .count();
    expect(scopedDescendantCount).toBe(0);
  });
});
