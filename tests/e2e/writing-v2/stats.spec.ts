import { expect, test } from '@playwright/test';

/**
 * Writing V2 — stats dashboard smoke.
 * Tags: @writing-v2 @smoke
 *
 * The stats page mounts the BandHistory chart unconditionally and
 * conditionally renders Criteria radar, Readiness widget, letter-types
 * table, canon top-violations list, time-management card, sub-skill
 * mastery, and activity heatmap. With an un-graded learner most of these
 * panels render their empty states.
 *
 * Smoke scope: assert the page hero + at least one of the widgets renders.
 *
 * Scope: chromium-learner only.
 */

test.describe('Writing V2 stats dashboard @writing-v2 @smoke', () => {
  test('stats page renders hero + at least one widget', async ({
    page,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await page.goto('/writing/stats', { waitUntil: 'domcontentloaded' });

    await expect(
      page.getByRole('heading', { name: /track every dimension of your writing progress/i }),
    ).toBeVisible({ timeout: 30_000 });

    // BandHistory section header is always rendered, even with no data
    // (the chart underneath shows an empty state).
    await expect(
      page.getByRole('heading', { name: /raw score over time/i }),
    ).toBeVisible({ timeout: 30_000 });

    // At least ONE conditional widget should render eventually. We poll the
    // set of candidate headings and pass if any appears within 30s — this
    // absorbs empty-state variability per learner while still catching a
    // catastrophic render failure (no widget at all).
    const candidateHeadings = [
      /criteria — current vs target/i,
      /letter type performance/i,
      /canon violations — top hits/i,
      /time management/i,
      /sub-skill mastery/i,
      /activity heatmap/i,
      /readiness/i,
    ];

    await expect
      .poll(
        async () => {
          for (const re of candidateHeadings) {
            const found = await page
              .getByRole('heading', { name: re })
              .first()
              .isVisible()
              .catch(() => false);
            if (found) return true;
          }
          return false;
        },
        { timeout: 30_000, message: 'Expected at least one stats widget heading to appear' },
      )
      .toBe(true);
  });
});
