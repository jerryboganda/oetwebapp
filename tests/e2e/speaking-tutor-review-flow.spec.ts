import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from './fixtures/diagnostics';

// Phase 7 (G.7) of the OET Speaking module plan — Playwright smoke for
// the tutor review surface (Phase 4 deliverable).
//
//   1. Log in as expert tutor (storage state from auth.setup.ts).
//   2. Navigate to /expert/speaking/queue.
//   3. Claim a session.
//   4. Submit scores via CriterionRubricForm.
//   5. (Expectation) The learner sees the tutor column populated on
//      the results page. We assert the tutor's success state at the
//      end of the rubric submission so the spec stays single-context.

test.describe('Speaking tutor review flow @expert @speaking', () => {
  test('expert claims a session and submits tutor assessment scores', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    await page.goto('/expert/speaking/queue', { waitUntil: 'domcontentloaded' });
    await expect(
      page.getByRole('heading', { name: /(speaking )?queue|review queue/i }),
    ).toBeVisible({ timeout: 30000 });

    // Claim the first available session. The queue UI may render a
    // claim button per-row OR a single primary button — accept either.
    const claim = page
      .getByRole('button', { name: /^claim( session| review)?$/i })
      .first();
    if (await claim.isVisible({ timeout: 5000 }).catch(() => false)) {
      await claim.click();
    } else {
      // Fall back to clicking the first session link.
      const firstSession = page.getByRole('link', { name: /(session|review)\s*#?[a-z0-9-]+/i }).first();
      if (await firstSession.isVisible().catch(() => false)) {
        await firstSession.click();
      }
    }

    // Land on the assessment page.
    await page.waitForURL(/\/expert\/speaking\/(sessions|reviews)\/[^/]+\/assess/i, { timeout: 30000 })
      .catch(async () => {
        // Some routers nest the assessment under /reviews/<id>.
        await page.waitForURL(/\/expert\/.+/, { timeout: 5000 }).catch(() => undefined);
      });

    // Submit a minimal rubric. Slider input values vary in number of
    // increments — use form-level type="range" fallback so the test is
    // resilient to the rendering style.
    const rubricSliders = page.locator('[role="slider"], input[type="range"]');
    const count = await rubricSliders.count();
    for (let i = 0; i < Math.min(count, 9); i++) {
      const slider = rubricSliders.nth(i);
      // Type Tab then ArrowRight twice — keyboard-driven so any styled
      // slider implementation responds. Some sliders are linguistic
      // (0-6) and some clinical (0-3); 2 increments is safe.
      await slider.focus();
      await slider.press('ArrowRight');
      await slider.press('ArrowRight');
    }

    // Submit the rubric.
    await page
      .getByRole('button', { name: /(submit|publish|finalise|finalize)( tutor)? (assessment|scores)?/i })
      .first()
      .click();

    // Tutor submission success.
    await expect(
      page.getByText(/(submitted|saved|published|sent to learner)/i).first(),
    ).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
