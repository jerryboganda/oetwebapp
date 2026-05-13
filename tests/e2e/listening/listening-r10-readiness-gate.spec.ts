import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

// Listening V2 R10 — in strict modes (`exam` and `home`), the Start
// button on `app/listening/player/[id]/page.tsx` must be disabled until
// the `TechReadinessCheck` (audio probe) has reported success. Without
// it, the FSM cannot legally advance out of `intro` to `a1_preview`
// (the first strict state).
//
// We assert the cheaper of the two contracts here: the UI gate. The
// server-side gate (POST /v1/listening/v2/attempts/{id}/advance returns
// rejection without a recorded tech-readiness payload) is covered by
// `backend/tests/OetLearner.Api.Tests/Listening/
// ListeningV2AdvanceEndpointTests.cs`.
test.describe('Listening R10 readiness gate @learner @listening', () => {
  test('exam mode Start button is disabled until readiness probe succeeds', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(120_000);
    const diagnostics = observePage(page);

    await page.goto('/listening/player/lt-001?mode=exam', { waitUntil: 'domcontentloaded' });

    // Intro card renders.
    await expect(page.getByRole('heading', { name: /before you start/i })).toBeVisible({ timeout: 60_000 });

    // R10 panel renders before the Start button is usable. The probe
    // CTA label is "Play audio probe" (see
    // components/domain/listening/TechReadinessCheck.tsx).
    await expect(page.getByRole('heading', { name: /audio readiness check/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /play audio probe/i })).toBeVisible();

    // The strict-mode Start CTA — the player file conditionally sets
    //   disabled={isStarting || (strictReadinessRequired && !techReadiness?.audioOk)}
    // on the same `start audio & task` button used in practice mode, so
    // the readiness gate keeps it disabled here.
    const startButton = page.getByRole('button', { name: /start audio & task/i });
    await expect(startButton).toBeVisible();
    await expect(startButton).toBeDisabled();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
