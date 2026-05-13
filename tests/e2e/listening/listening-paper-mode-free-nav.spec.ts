import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

// `?mode=paper` is the "paper-like" review mode: the OET strict
// forward-only FSM is NOT applied (`session.modePolicy.integrityLock
// Required` is false), so the strict `/advance` protocol must not run on
// initial mount. Playback controls still follow the backend modePolicy.
//
// SCOPE OF THIS SPEC
// ------------------
// This is the *learner-page contract* level of the paper-mode invariant.
// It validates that:
//   1. The paper-mode route mounts the same new `ListeningIntroCard`
//      (data-testid="listening-intro-card") as exam/practice modes.
//   2. No V2 `/advance` POST request fires on initial load — paper mode
//      bypasses the strict FSM entirely.
//
// The actual free-navigation across all three Parts requires a seeded
// multi-part sample paper (lt-001 currently only ships Part A). Section
// jumping behavior is covered deterministically at the unit level in
// `components/domain/listening/player/ListeningSectionStepper.test.tsx`
// (via the new extracted stepper component's data-state contract).
test.describe('Listening paper mode free navigation @learner @listening', () => {
  test('paper mode mounts intro without triggering FSM advance', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const advanceRequests: string[] = [];
    const advanceRequestPattern = /\/v1\/listening\/v2\/attempts\/[^/]+\/advance/;
    page.on('request', (req) => {
      if (advanceRequestPattern.test(req.url())) {
        advanceRequests.push(req.url());
      }
    });

    await page.goto('/listening/player/lt-001?mode=paper', { waitUntil: 'domcontentloaded' });

    await expect(page.getByTestId('listening-intro-card')).toBeVisible();
    await expect(page.getByRole('heading', { name: /before you start/i })).toBeVisible();

    // Paper mode must not call the strict FSM advance endpoint on mount.
    const lateAdvanceRequest = await page.waitForRequest((req) => advanceRequestPattern.test(req.url()), { timeout: 750 })
      .then((req) => req.url())
      .catch(() => null);
    expect(lateAdvanceRequest).toBeNull();
    expect(advanceRequests).toEqual([]);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
