import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Mocks Wave-2 (May-2026 closure) — live-room SignalR surface smoke.
 *
 * This spec is a **route-shape smoke** that confirms the learner-side
 * Speaking room page loads its three canonical surfaces without throwing
 * client-side and without falling back to the global error boundary.
 *
 * Why not exercise the SignalR transition flow end-to-end here?
 *  • The hub requires a real authenticated booking + assigned expert, neither
 *    of which is realistic to seed in CI without leaking production routes.
 *  • The transition state-machine itself is locked by the .NET unit suite
 *    `MockBookingServiceLiveRoomTransitionTests` (admin override, learner
 *    no-show, expert assignment, idempotent ClientTransitionId, monotonic
 *    TransitionVersion). That suite exercises the service+hub directly,
 *    which is the trust boundary.
 *
 * The spec below guards the *learner-visible* surfaces: the speaking-room
 * page must either load (when a booking is seeded) or redirect to /mocks
 * (when the booking is absent). Both outcomes are valid and prove the page
 * does not blow up at import time after the SignalR client wiring landed.
 */
test.describe('Mocks live-room SignalR surfaces @learner @mocks', () => {
  test('speaking room page loads without exploding when booking is missing', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    // Use a deliberately invalid booking id. The page should redirect or
    // render a graceful "no booking" state — never a hard crash.
    await page.goto('/mocks/speaking-room/__no-booking__');
    await page.waitForLoadState('domcontentloaded');

    const url = page.url();
    const onSpeakingRoom = /\/mocks\/speaking-room\//.test(url);
    const onMocksLanding = /\/mocks(\?|$|\/)/.test(url) && !/\/speaking-room/.test(url);
    expect(onSpeakingRoom || onMocksLanding).toBe(true);

    // The Next.js error boundary renders a `data-nextjs-error` marker — if
    // it appears, the SignalR client wiring threw at module-eval time.
    const boundary = await page.locator('[data-nextjs-error]').count();
    expect(boundary).toBe(0);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('expert speaking room page loads without exploding when booking is missing', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/expert/speaking-room/__no-booking__');
    await page.waitForLoadState('domcontentloaded');

    const url = page.url();
    const onExpertRoom = /\/expert\/speaking-room\//.test(url);
    const onExpertLanding = /\/expert(\?|$|\/)/.test(url) && !/\/speaking-room/.test(url);
    expect(onExpertRoom || onExpertLanding).toBe(true);

    const boundary = await page.locator('[data-nextjs-error]').count();
    expect(boundary).toBe(0);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
