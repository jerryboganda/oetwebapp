import { expect, type Page } from '@playwright/test';

export type SeededRole = 'learner' | 'expert' | 'admin';

export const seededAccounts: Record<SeededRole, { email: string; password: string }> = {
  learner: { email: 'learner@oet-prep.dev', password: 'Password123!' },
  expert: { email: 'expert@oet-prep.dev', password: 'Password123!' },
  admin: { email: 'admin@oet-prep.dev', password: 'Password123!' },
};

export const authStatePaths: Record<SeededRole, string> = {
  learner: 'playwright/.auth/learner.json',
  expert: 'playwright/.auth/expert.json',
  admin: 'playwright/.auth/admin.json',
};

export async function signInThroughUi(page: Page, role: SeededRole) {
  const account = seededAccounts[role];

  await page.goto('/sign-in');
  await expect(page.getByRole('heading', { name: /login to your account|access your workspace/i })).toBeVisible();

  await page.getByLabel(/email/i).fill(account.email);
  await page.getByLabel(/^password$/i).fill(account.password);
  await page.locator('form').getByRole('button', { name: /^sign in$/i }).click();

  if (role === 'learner') {
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();
    return;
  }

  await expect(page).toHaveURL(/\/mfa\/setup/);
  await expect(page.getByRole('heading', { name: /set up authenticator mfa/i })).toBeVisible();
  await expect(page.getByText('Authenticator code')).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'OTP digit 1' })).toBeVisible();
}
