import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

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

test.describe('Privileged workspaces @smoke', () => {
  test('expert session reaches authenticator setup branch', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/mfa/setup?next=/expert');

    await expect(page.getByRole('heading', { name: /set up authenticator mfa/i })).toBeVisible();
    await expect(page.getByText('Authenticator code')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'OTP digit 1' })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin session reaches authenticator setup branch', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/mfa/setup?next=/admin');

    await expect(page.getByRole('heading', { name: /set up authenticator mfa/i })).toBeVisible();
    await expect(page.getByText('Authenticator code')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'OTP digit 1' })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  for (const route of expertRoutes) {
    test(`expert route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('expert')) {
        test.skip();
      }

      const diagnostics = observePage(page);
      await page.goto(route.path);
      const main = page.getByRole('main');

      await expect(main.getByText(route.text).first()).toBeVisible();

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }

  for (const route of adminRoutes) {
    test(`admin route ${route.path} renders without severe client failures`, async ({ page }, testInfo) => {
      if (!testInfo.project.name.includes('admin')) {
        test.skip();
      }

      const diagnostics = observePage(page);
      await page.goto(route.path);
      const main = page.getByRole('main');

      await expect(main.getByText(route.text).first()).toBeVisible();

      expectNoSevereClientIssues(diagnostics);
      diagnostics.detach();
      await attachDiagnostics(testInfo, diagnostics);
    });
  }
});
