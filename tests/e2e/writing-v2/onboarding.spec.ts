import { expect, test } from '@playwright/test';
import { signInApi } from '../fixtures/api-auth';

/**
 * Writing V2 — onboarding wizard smoke (4-step wizard then diagnostic redirect).
 * Tags: @writing-v2 @smoke
 *
 * Walks the learner from /writing/welcome through the 4 profile-setup steps,
 * submits the confirm form, and asserts the saved profile via GET /v1/writing/profile.
 *
 * Scope: chromium-learner only (auth-state files for firefox/webkit/mobile carry
 * the same learner identity; running the full wizard once on chromium is enough
 * to validate the flow without re-seeding the same profile in every browser).
 */

const API_BASE_URL = (
  process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198'
).replace(/\/$/, '');

test.describe('Writing V2 onboarding @writing-v2 @smoke', () => {
  test('welcome → profession → goals → focus → confirm → diagnostic', async ({
    page,
    request,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    // Step 0 — Welcome page. The page redirects to /writing when the learner
    // is already onboarded, so we cannot reliably click the "Start" CTA on
    // re-runs. Hit the welcome page once for the smoke metric and then
    // navigate directly to the first wizard step (idempotent — the backend's
    // PUT endpoints accept re-saves and the spec covers that explicitly).
    await page.goto('/writing/welcome', { waitUntil: 'domcontentloaded' }).catch(() => null);
    await page.goto('/writing/profile-setup/profession', { waitUntil: 'domcontentloaded' });

    // Step 1 — Profession
    await expect(
      page.getByRole('heading', { name: /tell us who you are/i }),
    ).toBeVisible();
    await page.getByRole('radio', { name: /medicine/i }).check();
    await page.getByRole('button', { name: /^continue$/i }).click();
    await page.waitForURL(/\/writing\/profile-setup\/goals/, { timeout: 30_000 });

    // Step 2 — Goals (target=B, days=5, minutes=45)
    await expect(
      page.getByRole('heading', { name: /set your target and your weekly budget/i }),
    ).toBeVisible();
    // Target band buttons render text "A", "B+", "B", "C+", "C"; use exact
    // match so we don't accidentally hit B+.
    await page.getByRole('button', { name: 'B', exact: true }).click();
    // Days per week
    await page.getByLabel(/days per week/i).fill('5');
    // Minutes per day
    await page.getByLabel(/minutes per day/i).fill('45');
    await page.getByRole('button', { name: /^continue$/i }).click();
    await page.waitForURL(/\/writing\/profile-setup\/focus/, { timeout: 30_000 });

    // Step 3 — Focus (LT-RR + LT-DG minimum 2)
    await expect(
      page.getByRole('heading', { name: /what letter types do you need most/i }),
    ).toBeVisible();
    // The toggles are buttons with aria-pressed. The accessible name is the
    // concatenation of the Badge code + the friendly label, e.g.
    // "LT-RR Routine referral". Match on the unique LT-XX code.
    //
    // The focus page reads previously-saved wizard state from sessionStorage
    // inside a useEffect. We must wait for that hydration to settle before
    // clicking — otherwise the late state load overwrites our toggle side
    // effects, leaving the form in whatever state was previously persisted.
    // The "Picked" highlight value increments after each toggle; polling it
    // proves React state is in sync with the visible aria-pressed state.
    await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => null);
    const ltRr = page.getByRole('button', { name: /\bLT-RR\b/ });
    const ltDg = page.getByRole('button', { name: /\bLT-DG\b/ });
    // Ensure both toggles end up pressed regardless of any pre-loaded state.
    if ((await ltRr.getAttribute('aria-pressed')) !== 'true') {
      await ltRr.click();
    }
    if ((await ltDg.getAttribute('aria-pressed')) !== 'true') {
      await ltDg.click();
    }
    // Confirm at least 2 selections (the form's minimum) are pressed before
    // we hit Continue; otherwise the page surfaces an InlineAlert instead of
    // navigating.
    await expect(ltRr).toHaveAttribute('aria-pressed', 'true');
    await expect(ltDg).toHaveAttribute('aria-pressed', 'true');
    await page.getByRole('button', { name: /^continue$/i }).click();
    await page.waitForURL(/\/writing\/profile-setup\/confirm/, { timeout: 30_000 });

    // Step 4 — Confirm. Click Save and continue; this calls
    // POST /v1/writing/profile + POST /v1/writing/onboarding/complete and then
    // redirects to /writing/diagnostic.
    await expect(
      page.getByRole('heading', { name: /review and confirm/i }),
    ).toBeVisible();
    await page.getByRole('button', { name: /save and continue/i }).click();
    await page.waitForURL(/\/writing\/diagnostic\b/, { timeout: 45_000 });

    // Verify the profile landed via API. The V2 GET endpoint lives under the
    // v2/ sub-prefix (the bare /v1/writing/profile slot is taken by the
    // legacy V1 surface and only accepts POST — see WritingOnboardingEndpoints).
    const session = await signInApi(request, 'learner');
    const response = await request.get(`${API_BASE_URL}/v1/writing/v2/profile`, {
      headers: { Authorization: `Bearer ${session.accessToken}` },
    });
    expect(
      response.ok(),
      `GET /v1/writing/profile failed: ${response.status()} ${await response.text().catch(() => '')}`,
    ).toBeTruthy();
    const profile = (await response.json()) as {
      profession?: string;
      targetBand?: string;
      daysPerWeek?: number;
      minutesPerDay?: number;
      letterTypeFocus?: string[];
      onboardingCompletedAt?: string | null;
    };
    expect(profile.profession?.toLowerCase()).toBe('medicine');
    expect(profile.targetBand).toBe('B');
    expect(profile.daysPerWeek).toBe(5);
    expect(profile.minutesPerDay).toBe(45);
    expect(profile.letterTypeFocus).toEqual(
      expect.arrayContaining(['LT-RR', 'LT-DG']),
    );
    expect(profile.onboardingCompletedAt).toBeTruthy();
  });
});
