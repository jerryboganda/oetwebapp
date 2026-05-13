import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

// Light-weight smoke around the Listening practice-mode entry: prove the
// `app/listening/player/[id]/page.tsx` route boots against the seeded
// `lt-001` sample paper, the intro card renders, and clicking Start
// progresses to Part A (the first question stem from lt-001 becomes
// visible). The full end-to-end submit→results flow is exercised by
// `tests/e2e/learner/immersive-completion.spec.ts`; we intentionally do
// not re-run it here.
test.describe('Listening practice happy path @learner @listening', () => {
  test('practice mode loads lt-001, intro renders, learner reaches Part A', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(120_000);
    const diagnostics = observePage(page);

    // In practice mode the V2 strict advance endpoint is NOT invoked
    // (that path is exam/home only). We still record any v2/advance
    // network traffic so the diagnostic attachment makes regressions
    // obvious if the practice surface ever starts hitting the strict
    // FSM by accident.
    const advanceCalls: string[] = [];
    page.on('request', (req) => {
      const url = req.url();
      if (/\/v1\/listening\/v2\/attempts\/[^/]+\/advance/.test(url)) {
        advanceCalls.push(`${req.method()} ${url}`);
      }
    });

    await page.goto('/listening/player/lt-001?mode=practice', { waitUntil: 'domcontentloaded' });

    // Intro / "before you start" card must appear for the seeded sample
    // paper. If lt-001 is ever removed from the seeded fixture this is
    // the first thing to fail and is the cheapest possible smoke signal.
    await expect(page.getByRole('heading', { name: /before you start/i })).toBeVisible({ timeout: 60_000 });
    await expect(page.getByRole('button', { name: /start audio & task/i })).toBeVisible();

    await page.getByRole('button', { name: /start audio & task/i }).click();

    // Part A renders: the first MCQ stem of lt-001 ("Increasing
    // breathlessness at night") becomes visible. This is the
    // immersive-completion spec's anchor for "Part A is on screen".
    await expect(page.getByRole('button', { name: /^Increasing breathlessness at night$/i }))
      .toBeVisible({ timeout: 60_000 });

    // Practice mode must NOT hit the strict V2 advance endpoint at all.
    expect(advanceCalls, 'practice mode should never call /v1/listening/v2/.../advance').toEqual([]);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
