import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

// `?mode=paper` is a retained legacy query value. Computer-based delivery is
// the only supported learner surface, so the player must normalize that value
// to the strict exam mode before calling the backend.
//
// SCOPE OF THIS SPEC
// ------------------
// This is the *learner-page contract* level of the computer-only invariant.
// It validates that:
//   1. The legacy paper query mounts the strict `ListeningIntroCard`.
//   2. The browser calls the session endpoint with `mode=exam`, never
//      reintroducing the disabled paper mode to the API.
//
// The actual free-navigation across all three Parts requires a seeded
// multi-part sample paper (lt-001 currently only ships Part A). Section
// jumping behavior is covered deterministically at the unit level in
// `components/domain/listening/player/ListeningSectionStepper.test.tsx`
// (via the new extracted stepper component's data-state contract).
test.describe('Listening legacy paper query fails closed @learner @listening', () => {
  test('legacy paper query normalizes to computer exam mode', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const sessionRequests: string[] = [];
    page.on('request', (req) => {
      if (req.url().includes('/v1/listening-papers/papers/lt-001/session')) {
        sessionRequests.push(req.url());
      }
    });

    await page.goto('/listening/player/lt-001?mode=paper', { waitUntil: 'domcontentloaded' });

    await expect(page.getByTestId('listening-intro-card')).toBeVisible();
    await expect(page.getByRole('heading', { name: /before you start/i })).toBeVisible();

    expect(sessionRequests.length).toBeGreaterThan(0);
    expect(sessionRequests.every((url) => new URL(url).searchParams.get('mode') === 'exam')).toBe(true);
    expect(sessionRequests.some((url) => new URL(url).searchParams.get('mode') === 'paper')).toBe(false);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
