import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

// Strict exam mode surface invariant: before the FSM can leave `intro`,
// the learner must see the extracted intro surface and R10 readiness gate.
//
// SCOPE OF THIS SPEC
// ------------------
// This is the learner-page smoke level of the strict-exam invariant.
// It validates that:
//   1. The exam-mode route mounts the new `ListeningIntroCard`
//      (data-testid="listening-intro-card").
//   2. The pre-start surface still surfaces the readiness probe (tech
//      check) so the FSM cannot start without it.
//
// The full forward-only FSM walk (Part A -> Part B -> Part C without any
// back affordance, V2 confirm-token two-step handshake, one-play-only
// audio enforcement) is covered deterministically backend-side in
// `backend/tests/OetLearner.Api.Tests/Listening/
// ListeningV2AdvanceEndpointTests.cs` and the `ListeningFsmTransitions`
// suite. Re-asserting it from the E2E layer would need a headless
// audio-probe shim and a 40-minute attempt-budget walk; the page
// contract here is the minimum-viable learner-side surface check that
// runs on every CI pass.
test.describe('Listening exam mode strict locks @learner @listening', () => {
  test('exam mode renders intro card gated behind tech readiness', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/listening/player/lt-001?mode=exam', { waitUntil: 'domcontentloaded' });

    // The new extracted component must mount.
    await expect(page.getByTestId('listening-intro-card')).toBeVisible();

    // Pre-start surface still shows the canonical "Before you start" heading
    // from the extracted ListeningIntroCard.
    await expect(page.getByRole('heading', { name: /before you start/i })).toBeVisible();

    // In strict exam mode the readiness probe is required before Start
    // becomes enabled; the intro card must surface a readiness affordance.
    await expect(page.getByRole('heading', { name: /audio readiness check/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /play audio probe/i })).toBeVisible();
    await expect(page.getByRole('button', { name: /start audio & task/i })).toBeDisabled();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
