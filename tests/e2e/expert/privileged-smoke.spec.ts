import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';

const expertRoutes = [
  { path: '/expert', text: /dashboard|expert/i },
  { path: '/expert/queue', text: /review queue|queue/i },
  { path: '/expert/calibration', text: /calibration/i },
  { path: '/expert/metrics', text: /metrics/i },
  { path: '/expert/schedule', text: /schedule/i },
  { path: '/expert/learners', text: /learners/i },
];

const adminRoutes = [
  { path: '/admin', text: /operations|admin/i },
  { path: '/admin/content', text: /content library|content/i },
  { path: '/admin/criteria', text: /criteria|rubrics/i },
  { path: '/admin/taxonomy', text: /taxonomy/i },
  { path: '/admin/flags', text: /feature flags|flags/i },
  { path: '/admin/users', text: /user ops|users/i },
  { path: '/admin/billing', text: /billing/i },
  { path: '/admin/audit-logs', text: /audit logs/i },
];

async function expectPrivilegedAuthResolution(
  page: Parameters<typeof observePage>[0],
  targetPath: '/expert' | '/admin',
  workspaceText: RegExp,
) {
  await page.goto(`/mfa/setup?next=${encodeURIComponent(targetPath)}`, { waitUntil: 'domcontentloaded' });

  const setupHeading = page.getByRole('heading', { name: /set up authenticator mfa/i });
  const setupLoadingNotice = page.getByText(/preparing your authenticator secret and recovery codes/i);
  const otpInput = page.getByRole('textbox', { name: 'OTP digit 1' });
  const workspace = page.getByRole('main').getByText(workspaceText).first();

  const resolvedBranch = await Promise.any([
    expect(setupHeading).toBeVisible({ timeout: 15_000 }).then(() => 'setup' as const),
    expect(workspace).toBeVisible({ timeout: 15_000 }).then(() => 'workspace' as const),
  ]);

  if (resolvedBranch === 'setup') {
    await Promise.any([
      expect(setupLoadingNotice).toBeVisible({ timeout: 10_000 }),
      expect(otpInput).toBeVisible({ timeout: 10_000 }),
      expect(workspace).toBeVisible({ timeout: 10_000 }),
    ]);
    return;
  }

  await expect(workspace).toBeVisible();
}

test.describe('Privileged workspaces @smoke', () => {
  test('expert session resolves privileged auth branch', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await expectPrivilegedAuthResolution(page, '/expert', /dashboard|expert/i);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin session resolves privileged auth branch', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await expectPrivilegedAuthResolution(page, '/admin', /operations|admin/i);

    expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  for (const route of expertRoutes) {
    test(`expert route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('expert')) {
        test.skip();
      }

      test.setTimeout(120_000); // page.goto + session-guard wait + cold dev compile + render under firefox/webkit can exceed default 60s budget

      const diagnostics = observePage(page);
      await page.goto(route.path);
      await waitForSessionGuardToClear(page);
      const main = page.getByRole('main');

      await expect(main.getByText(route.text).first()).toBeVisible({ timeout: 30_000 }); // cold dev compile + first data fetch can exceed 10s

      expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }

  for (const route of adminRoutes) {
    test(`admin route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('admin')) {
        test.skip();
      }

      test.setTimeout(120_000); // page.goto + session-guard wait + cold dev compile + render under firefox/webkit can exceed default 60s budget

      const diagnostics = observePage(page);
      await page.goto(route.path);
      await waitForSessionGuardToClear(page);
      const main = page.getByRole('main');

      await expect(main.getByText(route.text).first()).toBeVisible({ timeout: 30_000 }); // cold dev compile + first data fetch can exceed 10s

      expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
