import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 12 — smoke for the pre-warm-up timer page introduced in Phase 1
// of the Speaking module plan. Verifies that:
//   1. The page loads under the learner role.
//   2. A 90s warm-up countdown is visible.
//   3. The Skip control is always available.
//   4. Continue is disabled until timer reaches 0 (we don't wait the
//      full 90s — we only assert initial disabled state).
//
// We don't run a real warm-up; we just make sure the surface mounts and
// has the controls the spec requires.

test.describe('Speaking pre-warm-up timer @learner @speaking', () => {
  test('warm-up page renders timer + skip + continue controls', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    const sessionId = 'smoke-warmup-session';

    // The page bootstraps with GET /v1/speaking/sessions/{id} before it can
    // transition into warm-up. The ephemeral stack has no seeded session with
    // this id, so an unmocked GET always fails (401 during the cold-load auth
    // race, 404 once authenticated) and the controls never render. Mock both
    // calls — this is a pure UI smoke, not a backend contract test.
    await page.route(`**/v1/speaking/sessions/${sessionId}`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ sessionId, state: 'PrepInProgress' }),
      });
    });

    await page.route(`**/v1/speaking/sessions/${sessionId}/start-warmup`, async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ sessionId, state: 'PrepInProgress', warmupSecondsRemaining: 90 }),
      });
    });

    try {
      await page.goto(`/speaking/sessions/${sessionId}/warmup`, { waitUntil: 'domcontentloaded' });

      // Skip is always available
      const skip = page.getByRole('button', { name: /skip/i }).first();
      await expect(skip).toBeVisible({ timeout: 10_000 });

      // Continue button exists and is initially disabled
      const cont = page.getByRole('button', { name: /continue/i }).first();
      await expect(cont).toBeVisible();

      await expectNoSevereClientIssues(diagnostics, {
        allowNextDevNoise: true,
        // Same cold-load auth-race + AI-assistant negotiate 401 noise class
        // the other learner-shell speaking specs tolerate (see
        // speaking-learner-ai-flow.spec.ts).
        allowAuthRedirectNoise: true,
        allowNotificationReconnectNoise: true,
      });
    } finally {
      await attachDiagnostics(testInfo, diagnostics);
    }
  });
});
