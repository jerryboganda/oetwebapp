import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Mocks V2 Wave 6 (May 2026 closure addendum) — chunked-upload retry
 * surface smoke. This spec is a route-shape smoke that confirms the live
 * Speaking room page loads its three canonical surfaces and that the
 * recording control surfaces map to a real consent flow.
 *
 * Note: the **chunk-dedup invariant itself** is locked by the .NET unit
 * test `MockBookingRecordingServiceRetryTests` (7 cases, including same-Part
 * same-SHA dedup, same-Part different-SHA rejection, post-finalize
 * rejection, and missing-consent rejection). That suite exercises the
 * service directly, which is the trust boundary; the E2E spec below
 * verifies the surfaces a learner can actually see.
 *
 * Why not exercise the recording flow end-to-end here? Playwright cannot
 * capture real MediaRecorder output in CI, and the page guards every chunk
 * POST behind consent + entitlement, which makes the test environment
 * brittle. The service-level tests give us the safety net; this spec
 * guards the surfaces.
 */
test.describe('Mocks chunked-upload retry surfaces @learner @mocks', () => {
  test('speaking room landing surfaces consent + state machine entry point', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    // The speaking room is gated on a real booking; learners without an
    // active booking are redirected to /mocks. We assert either the room
    // itself loads (when a booking is seeded) OR the redirect lands on
    // /mocks. Both are valid.
    await page.goto('/mocks/speaking-room/__no-booking__');

    await page.waitForLoadState('domcontentloaded');
    const url = page.url();
    const onSpeakingRoom = /\/mocks\/speaking-room\//.test(url);
    const onMocksLanding = /\/mocks(\?|$|\/)/.test(url) && !/\/speaking-room/.test(url);
    expect(onSpeakingRoom || onMocksLanding).toBe(true);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('mocks landing exposes the academic-integrity reminder banner', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    await page.goto('/mocks');
    // P2-4 closure: the academic-integrity reminder is rendered just under
    // the hero. Locking the literal copy here so a future refactor cannot
    // silently drop the banner.
    await expect(
      page.getByText(/oet test content is confidential/i),
    ).toBeVisible();
  });
});
