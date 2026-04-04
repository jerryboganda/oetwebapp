import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Authentication flows @auth @smoke', () => {
  test.describe.configure({ mode: 'serial' });

  test('protected learner routes redirect unauthenticated users to sign in', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/reading');

    await expect(page).toHaveURL(/\/sign-in\?next=%2Freading/);
    await expect(page.getByRole('heading', { name: /login to your account|access your workspace/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics, { allowAuthRedirectNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('sign in screen links to learner registration and routes into password recovery', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });

    await page.getByRole('link', { name: /create an account/i }).click();
    await expect(page).toHaveURL(/\/register/);
    await expect(page.getByRole('heading', { name: /register your account/i })).toBeVisible();
    await expect(page.getByLabel(/first name/i)).toBeVisible();
    await expect(page.getByLabel(/last name/i)).toBeVisible();

    await page.getByRole('link', { name: /login/i }).click();
    await expect(page.getByRole('heading', { name: /login to your account|access your workspace/i })).toBeVisible();

    await page.getByRole('link', { name: /forgot password\?/i }).click();
    await expect(page).toHaveURL(/\/forgot-password/);
    await expect(page.getByRole('heading', { name: /find your account/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('forgot password routes to OTP verification for seeded learner email', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/forgot-password');
    await page.getByLabel('Email').fill('learner@oet-prep.dev');
    await page.locator('form').getByRole('button', { name: /send otp/i }).click();

    await expect(page).toHaveURL(/\/forgot-password\/verify\?email=learner%40oet-prep\.dev/);
    await expect(page.getByRole('heading', { name: /check your email/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('reset password form blocks mismatched passwords client-side', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/reset-password?email=learner%40oet-prep.dev');
    await page.getByLabel('Reset code').fill('123456');
    await page.getByLabel(/^new password$/i).fill('Password123!');
    await page.getByLabel(/^confirm password$/i).fill('Password999!');
    await page.locator('form').getByRole('button', { name: /reset password/i }).click();

    await expect(page.getByText(/passwords must match before you continue/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('verify email page shows a recoverable notice when no email is provided', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/verify-email');

    await expect(page.getByText(/a valid email address is required before verification can continue/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
