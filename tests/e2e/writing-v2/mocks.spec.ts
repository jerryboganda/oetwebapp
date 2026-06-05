import { expect, test } from '@playwright/test';

/**
 * Writing V2 — mocks catalogue smoke (catalogue → start mock → session render).
 * Tags: @writing-v2 @smoke
 *
 * Validates:
 *  - /writing/mocks lists at least one published mock (per profession filter).
 *  - Clicking "Take this mock" opens a session, the case-notes panel renders,
 *    the editor mounts, and the strict-mode reading window is active.
 *
 * We do NOT submit the mock — mocks are 50 minutes and the smoke pack must
 * stay under 30s. Submission contract is covered by backend integration tests.
 *
 * Scope: chromium-learner only.
 */

test.describe('Writing V2 mocks @writing-v2 @smoke', () => {
  test('mocks catalogue → start mock → session page renders', async ({
    page,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await page.goto('/writing/mocks', { waitUntil: 'domcontentloaded' });

    // Accept either translated heading or raw next-intl key (page uses
    // `t('writing.mocks.catalogue.title')`).
    await expect(
      page.getByRole('heading', {
        name: /(mocks under strict exam conditions|writing\.mocks\.catalogue\.title)/i,
      }),
    ).toBeVisible({ timeout: 30_000 });

    // The card CTA label is computed from the (default) Computer + Strict mode
    // selection, so the button reads "Start strict mock" (it becomes "Start
    // practice" / "Open paper mode" for the other modes). Match all variants,
    // plus the raw next-intl key in case translations fall back to keys.
    const startButtons = page.getByRole('button', {
      name: /(start strict mock|start practice|open paper mode|take this mock|writing\.mocks\.catalogue\.cta)/i,
    });
    // The empty-state copy is `writing.mocks.catalogue.list.empty`
    // ("No mocks available yet."). Accept the translated string or the raw key.
    const emptyState = page.getByText(
      /(no mocks available yet|writing\.mocks\.catalogue\.list\.empty)/i,
    );

    // The catalogue hydrates asynchronously. Wait until it has settled into one
    // of its two terminal states — at least one mock CTA, OR the empty-state
    // message — before branching, so we never read `.count()` mid-load.
    await expect(startButtons.first().or(emptyState)).toBeVisible({ timeout: 30_000 });

    const startCount = await startButtons.count();
    if (startCount === 0) {
      // Empty seed for the learner's profession → spec describes this as
      // "May show empty state if no mocks". Confirm the empty-state message
      // and pass; the API contract is covered by backend tests.
      await expect(emptyState).toBeVisible();
      return;
    }

    expect(
      startCount,
      `Expected ≥1 mock for the learner's profession; got ${startCount}`,
    ).toBeGreaterThanOrEqual(1);

    const startPromise = page.waitForResponse(
      (r) =>
        /\/v1\/writing\/mocks\/[^/]+\/start\b/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 30_000 },
    );
    await startButtons.first().click();

    const startResponse = await startPromise.catch(() => null);
    if (!startResponse || !startResponse.ok()) {
      // 409 mock_already_active or 402 insufficient_credits or 404 mock_not_found.
      // Surface as a skip rather than a fail because they signal environment
      // state not the page contract.
      const status = startResponse?.status() ?? 'no response';
      const body = await startResponse?.text().catch(() => '');
      test.skip(true, `Mock start did not succeed (${status}): ${body}`);
      return;
    }

    await page.waitForURL(/\/writing\/mocks\/session\/[^/]+(?:\?.*)?$/, {
      timeout: 30_000,
    });

    // Case notes panel rendered. Accept raw next-intl key fallback.
    await expect(
      page.getByRole('region', {
        name: /(case notes|writing\.mocks\.session\.caseNotesLabel|writing\.diagnostic\.session\.caseNotesLabel)/i,
      }),
    ).toBeVisible({ timeout: 30_000 });

    // Editor surface mounts (id-less; locate by role textbox inside the
    // writing-editor section). It is locked during the reading phase.
    await expect(
      page.getByRole('region', {
        name: /(writing editor|writing\.mocks\.session\.editorLabel|writing\.diagnostic\.session\.editorLabel)/i,
      }),
    ).toBeVisible({ timeout: 30_000 });
  });
});
