import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';

const expertRoutes = [
  { path: '/expert', title: 'Dashboard' },
  { path: '/expert/queue', title: 'Review Queue' },
  { path: '/expert/calibration', title: 'Calibration' },
  { path: '/expert/metrics', title: 'Metrics' },
  { path: '/expert/schedule', title: 'Schedule' },
  { path: '/expert/learners', title: 'Learners' },
];

const adminRoutes = [
  { path: '/admin', title: 'Operations' },
  { path: '/admin/content', title: 'Content Hub' },
  { path: '/admin/criteria', title: 'Rubrics & Criteria' },
  { path: '/admin/taxonomy', title: 'Professions' },
  { path: '/admin/flags', title: 'Feature Flags' },
  { path: '/admin/users', title: 'User Operations' },
  { path: '/admin/billing', title: 'Billing Ops' },
  { path: '/admin/audit-logs', title: 'Audit Logs' },
];

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

async function expectRouteShell(page: Parameters<typeof observePage>[0], route: { path: string; title: string }) {
  await page.goto(route.path, { waitUntil: 'domcontentloaded', timeout: 120_000 });
  await expect(page).toHaveURL(new RegExp(`${escapeRegExp(route.path)}(?:[?#].*)?$`));
  await waitForSessionGuardToClear(page);
  await expect(page.getByRole('banner').getByText(route.title, { exact: true }).first()).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('main').first()).toBeVisible();
}

async function expectPrivilegedAuthResolution(
  page: Parameters<typeof observePage>[0],
  targetPath: '/expert' | '/admin',
  workspaceText: RegExp,
) {
  await page.goto(`/mfa/setup?next=${encodeURIComponent(targetPath)}`, { waitUntil: 'domcontentloaded', timeout: 120_000 });

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
      await expectRouteShell(page, route);

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
      await expectRouteShell(page, route);

      expectNoSevereClientIssues(diagnostics, { allowNextDevNoise: true });
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
