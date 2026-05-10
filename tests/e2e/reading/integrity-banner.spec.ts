import { expect, test } from '@playwright/test';

/**
 * Reading rulebook B §7 — academic-integrity reminder.
 *
 * The Reading paper player MUST display a one-line confidentiality banner
 * before Part A starts (mock launch screen + active Part A toolbar) and on
 * the inter-part break screen, but only when the attempt is in mock/exam
 * mode. Learning / Drill / MiniTest / ErrorBank attempts MUST NOT show it.
 *
 * The banner copy is verbatim and pinned via the
 * `data-testid="reading-integrity-banner"` selector and the literal text.
 */

const BANNER_TEXT =
  'Exam content is confidential. Do not disclose, copy, or share OET test material. Cheating or rule violations may lead to disqualification.';

const PAPER_ID = 'rule-rb7-paper';
const LEARNING_ATTEMPT_ID = 'rule-rb7-learning-attempt';

function fixtureStructureBody() {
  return JSON.stringify({
    paper: { id: PAPER_ID, title: 'Rulebook B §7 fixture', slug: PAPER_ID, subtestCode: 'reading' },
    parts: [],
  });
}

test.describe('Reading rulebook B §7 — integrity banner @reading @rulebook', () => {
  test('mock-mode launch screen shows the verbatim integrity banner', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    await page.route(/\/v1\/reading-papers\/papers\/[^/]+\/structure$/, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: fixtureStructureBody(),
      });
    });

    // Default URL (no ?mode=) → mock/exam launch path. Banner must render.
    await page.goto(`/reading/paper/${PAPER_ID}`, { waitUntil: 'domcontentloaded' });

    const banner = page.getByTestId('reading-integrity-banner');
    await expect(banner).toBeVisible({ timeout: 30000 });
    await expect(banner).toHaveText(BANNER_TEXT);
  });

  test('learning-mode launch screen does NOT show the integrity banner', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    await page.route(/\/v1\/reading-papers\/papers\/[^/]+\/structure$/, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: fixtureStructureBody(),
      });
    });

    // The learning-mode launch path renders the "Loading your … practice
    // attempt…" copy and intentionally hides the start-attempt button and
    // the integrity banner; the banner is only required for mock attempts.
    await page.goto(`/reading/paper/${PAPER_ID}?mode=learning`, { waitUntil: 'domcontentloaded' });

    await expect(page.getByText(/loading your learning practice attempt/i))
      .toBeVisible({ timeout: 30000 });
    await expect(page.getByTestId('reading-integrity-banner')).toHaveCount(0);
  });

  test('resumed Learning attempt does NOT show the integrity banner', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const now = Date.now();
    const partADeadline = new Date(now + 15 * 60 * 1000).toISOString();
    const partBCDeadline = new Date(now + 60 * 60 * 1000).toISOString();
    const startedAt = new Date(now).toISOString();

    await page.route(/\/v1\/reading-papers\/papers\/[^/]+\/structure$/, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: fixtureStructureBody(),
      });
    });

    await page.route(/\/v1\/reading-papers\/attempts\/[^/]+$/, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: LEARNING_ATTEMPT_ID,
          paperId: PAPER_ID,
          status: 'InProgress',
          mode: 'Learning',
          scopeQuestionIds: null,
          startedAt,
          deadlineAt: partBCDeadline,
          submittedAt: null,
          rawScore: null,
          scaledScore: null,
          maxRawScore: 42,
          partADeadlineAt: partADeadline,
          partBCDeadlineAt: partBCDeadline,
          partABreakAvailable: false,
          partABreakResumed: true,
          partBCTimerPausedAt: null,
          partBCPausedSeconds: 0,
          partABreakMaxSeconds: 600,
          answeredCount: 0,
          totalQuestions: 0,
          canResume: true,
          answers: [],
          showExplanations: false,
        }),
      });
    });

    await page.goto(
      `/reading/paper/${PAPER_ID}?attemptId=${LEARNING_ATTEMPT_ID}`,
      { waitUntil: 'domcontentloaded' },
    );

    // Wait for the attempt-loaded surface to render (toolbar timer is the
    // canonical signal that the attempt has been hydrated).
    await expect(page.getByRole('timer').first()).toBeVisible({ timeout: 30000 });
    await expect(page.getByTestId('reading-integrity-banner')).toHaveCount(0);
  });
});
