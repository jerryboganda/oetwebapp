import { expect, type Page } from '@playwright/test';

export type SeededRole = 'learner' | 'expert' | 'admin';

export const seededAccounts: Record<SeededRole, { email: string; password: string }> = {
  learner: { email: 'learner@oet-prep.dev', password: 'Password123!' },
  expert: { email: 'expert@oet-prep.dev', password: 'Password123!' },
  admin: { email: 'admin@oet-prep.dev', password: 'Password123!' },
};

export const authStatePathsByProject = {
  'chromium-learner': 'playwright/.auth/chromium-learner.json',
  'chromium-expert': 'playwright/.auth/chromium-expert.json',
  'chromium-admin': 'playwright/.auth/chromium-admin.json',
  'firefox-learner': 'playwright/.auth/firefox-learner.json',
  'firefox-expert': 'playwright/.auth/firefox-expert.json',
  'firefox-admin': 'playwright/.auth/firefox-admin.json',
  'webkit-learner': 'playwright/.auth/webkit-learner.json',
  'webkit-expert': 'playwright/.auth/webkit-expert.json',
  'webkit-admin': 'playwright/.auth/webkit-admin.json',
  'mobile-chromium-learner': 'playwright/.auth/mobile-chromium-learner.json',
  'mobile-webkit-learner': 'playwright/.auth/mobile-webkit-learner.json',
  'sydney-learner': 'playwright/.auth/sydney-learner.json',
} as const;

export const authStateTargets = [
  { projectName: 'chromium-learner', role: 'learner', path: authStatePathsByProject['chromium-learner'] },
  { projectName: 'chromium-expert', role: 'expert', path: authStatePathsByProject['chromium-expert'] },
  { projectName: 'chromium-admin', role: 'admin', path: authStatePathsByProject['chromium-admin'] },
  { projectName: 'firefox-learner', role: 'learner', path: authStatePathsByProject['firefox-learner'] },
  { projectName: 'firefox-expert', role: 'expert', path: authStatePathsByProject['firefox-expert'] },
  { projectName: 'firefox-admin', role: 'admin', path: authStatePathsByProject['firefox-admin'] },
  { projectName: 'webkit-learner', role: 'learner', path: authStatePathsByProject['webkit-learner'] },
  { projectName: 'webkit-expert', role: 'expert', path: authStatePathsByProject['webkit-expert'] },
  { projectName: 'webkit-admin', role: 'admin', path: authStatePathsByProject['webkit-admin'] },
  { projectName: 'mobile-chromium-learner', role: 'learner', path: authStatePathsByProject['mobile-chromium-learner'] },
  { projectName: 'mobile-webkit-learner', role: 'learner', path: authStatePathsByProject['mobile-webkit-learner'] },
  { projectName: 'sydney-learner', role: 'learner', path: authStatePathsByProject['sydney-learner'] },
] as const satisfies ReadonlyArray<{
  projectName: keyof typeof authStatePathsByProject;
  role: SeededRole;
  path: string;
}>;

export const authStatePaths: Record<SeededRole, string> = {
  learner: authStatePathsByProject['chromium-learner'],
  expert: authStatePathsByProject['chromium-expert'],
  admin: authStatePathsByProject['chromium-admin'],
};

export async function waitForSessionGuardToClear(
  page: Page,
  options: {
    recover?: () => Promise<unknown>;
    initialTimeoutMs?: number;
    timeoutMs?: number;
  } = {},
) {
  const sessionBanner = page.getByText(/checking your session/i);

  if (await sessionBanner.isVisible().catch(() => false)) {
    const clearedWithoutRecovery = await sessionBanner
      .waitFor({ state: 'hidden', timeout: options.initialTimeoutMs ?? 30_000 })
      .then(() => true)
      .catch(() => false);

    if (!clearedWithoutRecovery) {
      await (options.recover?.() ?? page.reload({ waitUntil: 'domcontentloaded' }));
    }
  }

  const timeoutMs = options.timeoutMs ?? 90_000;
  const clearedAfterRecovery = await sessionBanner
    .waitFor({ state: 'hidden', timeout: timeoutMs })
    .then(() => true)
    .catch(() => false);

  if (!clearedAfterRecovery && options.recover) {
    await options.recover();
    const clearedAfterSecondRecovery = await sessionBanner
      .waitFor({ state: 'hidden', timeout: 30_000 })
      .then(() => true)
      .catch(() => false);

    if (!clearedAfterSecondRecovery) {
      // Last resort: hard reload clears all client-side state that may keep
      // the session guard stuck (common in WebKit under CI matrix load).
      await page.reload({ waitUntil: 'domcontentloaded' });
    }
  }

  await expect(sessionBanner).toBeHidden({ timeout: timeoutMs });
}

export async function signInThroughUi(page: Page, role: SeededRole) {
  const account = seededAccounts[role];

  await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
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
