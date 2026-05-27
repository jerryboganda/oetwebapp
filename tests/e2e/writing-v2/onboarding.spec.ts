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

    // Step 0 — Welcome page. The page redirects to /writing if onboarding is
    // already complete, so we accept either landing on welcome or being
    // bounced to /writing. The first attempt always lands on the wizard;
    // we then click Start which navigates to the first wizard step.
    await page.goto('/writing/welcome', { waitUntil: 'domcontentloaded' });

    // If the learner is already onboarded the welcome page redirects to
    // /writing — that's still a valid pass (it proves the profile API
    // returns a non-null profile). Walk the rest of the wizard anyway so
    // the test stays meaningful: navigate directly to profession.
    if (!page.url().includes('/writing/welcome')) {
      // Already onboarded; navigate straight to profession step to re-run
      // the wizard. The backend's PUT endpoints are idempotent.
      await page.goto('/writing/profile-setup/profession', { waitUntil: 'domcontentloaded' });
    } else {
      await page
        .getByRole('link', { name: /start onboarding for OET Writing/i })
        .click();
      await page.waitForURL(/\/writing\/profile-setup\/profession/, { timeout: 30_000 });
    }

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
    await page.getByRole('button', { name: /\bLT-RR\b/ }).click();
    await page.getByRole('button', { name: /\bLT-DG\b/ }).click();
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

    // Verify the profile landed via API.
    const session = await signInApi(request, 'learner');
    const response = await request.get(`${API_BASE_URL}/v1/writing/profile`, {
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
