import { expect, test } from '@playwright/test';

/**
 * Writing V2 — drills smoke (catalogue → category → first drill → submit).
 * Tags: @writing-v2 @smoke
 *
 * The drills page renders a "Categories" grid (deterministic) plus a
 * "Pathway Drills" list (depends on seeded drill bank). The categories
 * always include "Opening" via the deterministic block — we navigate into
 * that category page and assert the drill list, then open the first drill
 * card and submit a sample response.
 *
 * Scope: chromium-learner only.
 */

test.describe('Writing V2 drills @writing-v2 @smoke', () => {
  test('drills page → category → first drill submit roundtrip', async ({
    page,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await page.goto('/writing/drills', { waitUntil: 'domcontentloaded' });
    await expect(
      page.getByRole('heading', { name: /targeted writing drills/i }),
    ).toBeVisible({ timeout: 30_000 });

    // The "Opening" category card text comes from the static categories array
    // ("Opening paragraphs"). Click it to navigate into /writing/drills/opening.
    const openingCard = page.getByRole('link', { name: /opening paragraphs/i }).first();
    await expect(openingCard).toBeVisible({ timeout: 30_000 });
    await openingCard.click();
    await page.waitForURL(/\/writing\/drills\/(opening|relevance|ordering|tone|expansion|abbreviation)/, {
      timeout: 30_000,
    });

    // The category page lists drill cards; if the seed is empty we skip the
    // submission half of the test rather than fail (seed scaling is tracked
    // separately in PROGRESS.md known follow-ups).
    const openDrillLinks = page.getByRole('link', { name: /open drill/i });
    const drillCount = await openDrillLinks.count().catch(() => 0);
    if (drillCount === 0) {
      // The /writing/drills aggregate also lists "Pathway Drills" cards.
      // Fall back to those — they go to /writing/drills/practice/{id}.
      await page.goto('/writing/drills', { waitUntil: 'domcontentloaded' });
      const practiceLink = page.getByRole('link', { name: /open drill/i }).first();
      if (await practiceLink.isVisible().catch(() => false)) {
        await practiceLink.click();
      } else {
        test.skip(
          true,
          'No seeded drills in this environment; the drills page renders the empty state.',
        );
        return;
      }
    } else {
      await openDrillLinks.first().click();
    }

    // Drill detail page — wait for any submit-attempt button to mount.
    const submitBtn = page.getByRole('button', { name: /submit attempt/i });
    await expect(submitBtn).toBeVisible({ timeout: 30_000 });

    // Type a sample answer. The deterministic grader rejects whatever we
    // type if it isn't the canonical answer — we only assert that the
    // submit cycle returns a feedback badge (Correct or otherwise).
    const textarea = page.locator('textarea').first();
    await textarea.fill(
      'Mr Doe is a 70-year-old retired teacher referred for assessment of worsening dyspnoea.',
    );
    await submitBtn.click();

    // The feedback badge sits next to the submit button (success or danger).
    // We accept any of the variants because the grader is deterministic and
    // the seeded canonical answers vary per drill.
    const feedbackBadge = page
      .locator('[class*="bg-emerald"], [class*="bg-red"], [class*="bg-amber"]')
      .filter({ hasText: /correct|incorrect|review|good|partial|nice|.+/i })
      .first();
    await expect(feedbackBadge).toBeVisible({ timeout: 30_000 });
  });
});
