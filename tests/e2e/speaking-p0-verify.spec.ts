import { test, expect } from '@playwright/test';

// Throwaway P0 verification: reruns the real record -> submit -> grade
// journey 3x against production after the temporary AI budget override,
// to confirm transcription + assessment actually complete end to end.
// Not part of the permanent suite — delete after use.

const DEVICE_ID = 'acceptance-test-device-fixed-001';
const EMAIL = process.env.SPEAKING_VERIFY_EMAIL!;
const PASSWORD = 'AcceptTest!2026';
const ATTEMPTS = 3;

test('P0: real record -> submit -> grade journey x3', async ({ page }) => {
  await page.addInitScript((deviceId) => {
    window.localStorage.setItem('oet_device_id', deviceId);
  }, DEVICE_ID);

  await page.goto('/sign-in');
  await page.locator('input[name="email"]').fill(EMAIL);
  await page.locator('input[name="password"]').fill(PASSWORD);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL((url) => url.pathname !== '/sign-in', { timeout: 20_000 });

  const closeOnboarding = page.getByRole('dialog', { name: /Welcome to your OET workspace/i }).getByRole('button', { name: /close/i });
  if (await closeOnboarding.isVisible({ timeout: 3000 }).catch(() => false)) {
    await closeOnboarding.click();
  }
  const closeAppDialog = page.getByRole('dialog', { name: /Get the Candidates App/i }).getByRole('button', { name: /^Close$/i });
  if (await closeAppDialog.isVisible({ timeout: 1000 }).catch(() => false)) {
    await closeAppDialog.click();
  }

  for (let i = 1; i <= ATTEMPTS; i++) {
    console.log(`=== ATTEMPT ${i}/${ATTEMPTS} ===`);

    await page.goto('/speaking/selection');
    await page.getByRole('button', { name: /^Start$/i }).first().click();
    await page.waitForURL(/\/speaking\/roleplay\//, { timeout: 20_000 });
    console.log(`ATTEMPT_${i}_CARD_URL`, page.url());

    await page.getByRole('button', { name: /Start Speaking Task/i }).click();
    await page.waitForURL(/\/speaking\/task\//, { timeout: 20_000 });
    console.log(`ATTEMPT_${i}_TASK_URL`, page.url());

    const consent = page.locator('input[type="checkbox"]').first();
    await consent.check();

    await page.getByRole('button', { name: 'Start recording' }).click();
    await page.waitForTimeout(4000);

    const submitBtn = page.getByRole('button', { name: /Submit Recording/i });
    await expect(submitBtn).toBeEnabled({ timeout: 10_000 });
    await submitBtn.click();

    const confirmBtn = page.getByRole('button', { name: /Submit for Evaluation/i });
    await confirmBtn.click({ timeout: 10_000 });

    await page.waitForURL(/\/speaking\/results\//, { timeout: 30_000 });
    console.log(`ATTEMPT_${i}_RESULT_URL`, page.url());

    // Poll the result page for up to 45s for a terminal state (completed or failed).
    let terminal = false;
    for (let poll = 0; poll < 22; poll++) {
      const bodyText = await page.locator('body').innerText();
      if (/Estimated Practice Score|\/\s*500|Speaking Criteria|Strengths|Areas to improve/i.test(bodyText)) {
        console.log(`ATTEMPT_${i}_OUTCOME completed`);
        terminal = true;
        break;
      }
      if (/couldn.t process your recording|could not be completed|try again/i.test(bodyText)) {
        console.log(`ATTEMPT_${i}_OUTCOME failed`);
        terminal = true;
        break;
      }
      await page.waitForTimeout(2000);
    }
    if (!terminal) console.log(`ATTEMPT_${i}_OUTCOME timeout-no-terminal-state`);

    const finalBody = await page.locator('body').innerText();
    console.log(`ATTEMPT_${i}_BODY_SNIPPET`, finalBody.slice(0, 1500));

    // Check for a raw/unhandled error surfaced to the candidate (stack trace,
    // "Exception", "500", unhandled promise text) — never acceptable UI.
    const rawErrorLeak = /System\.[A-Za-z.]+Exception|at OetLearner\.Api|Unhandled exception|TypeError:|ReferenceError:/i.test(finalBody);
    console.log(`ATTEMPT_${i}_RAW_ERROR_LEAK`, rawErrorLeak);
  }
});
