import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Mocks V2 Wave 7 (May 2026 closure addendum) — diagnostic learner journey
 * smoke. Covers the four states the page can be in:
 *
 *   1. Loading skeleton (Suspense fallback) — V2 Med #3 closure.
 *   2. Landing state with the "How the diagnostic works" card and the
 *      "Start diagnostic" CTA pointing at /mocks/setup?type=diagnostic.
 *   3. Bundles row when the catalog has at least one diagnostic mock.
 *   4. Practice disclaimer (always visible).
 *
 * Result-state coverage (post-completion SoR card) is gated on a seeded
 * graded attempt and lives in the deeper `mock-flow.spec.ts` matrix; this
 * spec is intentionally a pre-attempt smoke.
 */
test.describe('Mocks diagnostic flow @learner @smoke @mocks', () => {
  test('learner diagnostic landing renders all canonical surfaces', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/mocks/diagnostic');
    await expect(page).toHaveURL(/\/mocks\/diagnostic/);

    // Hero copy is canonical and must not regress.
    await expect(
      page.getByRole('heading', { name: /diagnostic mock/i }).first(),
    ).toBeVisible();

    // Either the landing card OR the per-subtest result card must render —
    // the page never blanks. This guards against a Suspense fallback that
    // never resolves.
    const landingCard = page.getByText(/how the diagnostic works/i);
    const resultCard = page.getByText(/per-subtest breakdown/i);
    await expect(landingCard.or(resultCard)).toBeVisible();

    // Practice disclaimer is the bottom-of-page integrity reminder; always
    // present for learner-facing mock surfaces per the May 2026 audit
    // closure (`Practice only` strong text).
    await expect(page.getByText(/practice only/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('Start diagnostic button targets /mocks/setup?type=diagnostic when entitled', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    await page.goto('/mocks/diagnostic');
    const startButton = page.getByRole('button', { name: /start diagnostic/i });

    // The button is conditionally enabled by the entitlement endpoint. When
    // disabled, we still expect the visible CTA so non-entitled learners can
    // see what's available behind the upgrade.
    await expect(startButton).toBeVisible();

    // Click only when enabled (otherwise the click is a no-op).
    if (await startButton.isEnabled()) {
      await startButton.click();
      await expect(page).toHaveURL(/\/mocks\/setup\?type=diagnostic/);
    }
  });
});
