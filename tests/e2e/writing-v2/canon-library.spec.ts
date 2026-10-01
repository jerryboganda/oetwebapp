import { expect, test } from '@playwright/test';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

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
    request,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await recoverBrowserSession(page, request, 'learner', '/writing/canon');
    await expect(
      page.getByRole('heading', {
        name: /dr ahmed's writing rules in one place/i,
      }),
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

    const ruleLink = ruleArticles.first().getByRole('link', { name: /practise this rule/i });
    await expect(ruleLink).toHaveAttribute('href', /^\/writing\/canon\/[^/]+$/);
    await ruleLink.click();
    await expect(page).toHaveURL(/\/writing\/canon\/[^/]+$/);

    const detailHeading = page.getByRole('heading', {
      name: /rule metadata/i,
    });
    await expect(detailHeading).toBeVisible();
    await expect(page.getByRole('heading', { name: /^correct$/i })).toBeVisible();
    await expect(page.getByRole('heading', { name: /^incorrect$/i })).toBeVisible();
  });
});
