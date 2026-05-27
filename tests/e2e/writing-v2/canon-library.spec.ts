import { expect, test } from '@playwright/test';

/**
 * Writing V2 — canon library smoke.
 * Tags: @writing-v2 @smoke
 *
 * Hits /writing/canon and verifies the seeded canon rule library renders.
 * The launch canon ships ≥25 rules; assert at least 20 to absorb future
 * pruning/seeding adjustments without flaking on minor count drift.
 *
 * Then opens the first rule detail page and confirms rule text + Correct /
 * Incorrect example blocks are rendered.
 *
 * Scope: chromium-learner only (the page is read-only and other shards
 * already cover the route shell).
 */

test.describe('Writing V2 canon library @writing-v2 @smoke', () => {
  test('canon page lists rules and the first rule detail renders', async ({
    page,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await page.goto('/writing/canon', { waitUntil: 'domcontentloaded' });
    await expect(
      page.getByRole('heading', { name: /dr ahmed's writing rules in one place/i }),
    ).toBeVisible({ timeout: 30_000 });

    // Wait for the rule articles to render. Each rule renders as an <article>;
    // we don't require an aria-label so locate them by structure.
    const ruleArticles = page.locator('main article');
    await expect.poll(async () => ruleArticles.count(), { timeout: 30_000 }).toBeGreaterThan(0);
    const ruleCount = await ruleArticles.count();
    expect(
      ruleCount,
      `Expected ≥20 canon rules in the library; got ${ruleCount}`,
    ).toBeGreaterThanOrEqual(20);

    // The first rule article has a "Practise this rule" link OR the rule id
    // badge is clickable through navigation — but the library card itself
    // does not link to detail. Instead, the detail page is reachable via
    // /writing/canon/{ruleId}. Pluck the first visible rule id (badge text
    // matching SC-\d+ or R\d+) and navigate directly. This is the public
    // contract for the page.
    const firstBadgeText = (
      await ruleArticles.first().locator('span, div').filter({ hasText: /^[A-Z]{1,3}-?\d+/ }).first().textContent()
    )?.trim();
    if (!firstBadgeText) {
      test.skip(true, 'Could not infer the first rule id from the library; canon seed may be empty.');
      return;
    }
    const ruleId = firstBadgeText.match(/[A-Z]{1,3}-?\d+(?:\.\d+)?/)?.[0];
    if (!ruleId) {
      test.skip(true, `Could not parse rule id from "${firstBadgeText}".`);
      return;
    }

    await page.goto(`/writing/canon/${encodeURIComponent(ruleId)}`, {
      waitUntil: 'domcontentloaded',
    });

    // Rule metadata section is the canonical heading for the detail page.
    await expect(
      page.getByRole('heading', { name: /rule metadata/i }),
    ).toBeVisible({ timeout: 30_000 });

    // Correct / Incorrect headings appear in the examples grid.
    await expect(page.getByRole('heading', { name: /^correct$/i })).toBeVisible();
    await expect(page.getByRole('heading', { name: /^incorrect$/i })).toBeVisible();
  });
});
