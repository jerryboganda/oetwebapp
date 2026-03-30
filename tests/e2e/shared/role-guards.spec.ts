import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

test.describe('Role protection @security @smoke', () => {
  test('learner session cannot access expert or admin routes', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const main = page.getByRole('main');

    await page.goto('/expert');
    await expect(page).toHaveURL(/\/$/);
    await expect(main.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

    await page.goto('/admin');
    await expect(page).toHaveURL(/\/$/);
    await expect(main.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('expert session cannot access admin routes', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-expert') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const main = page.getByRole('main');
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/expert$/);
    await expect(main.getByText(/dashboard|expert/i).first()).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin session cannot access expert routes', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const main = page.getByRole('main');
    await page.goto('/expert');
    await expect(page).toHaveURL(/\/admin$/);
    await expect(main.getByText(/operations|admin/i).first()).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
