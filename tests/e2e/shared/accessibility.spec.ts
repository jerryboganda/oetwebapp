import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

async function tabUntilFocused(page: Parameters<typeof AxeBuilder>[0]['page'], locator: Locator, maxTabs = 12) {
  for (let index = 0; index < maxTabs; index += 1) {
    await page.keyboard.press('Tab');
    if (await locator.evaluate((node) => node === document.activeElement)) {
      return;
    }
  }
}

async function expectNoSeriousAxeViolations(page: Parameters<typeof AxeBuilder>[0]['page']) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa'])
    .analyze();

  const blockingViolations = results.violations.filter((violation) => ['critical', 'serious'].includes(violation.impact ?? ''));
  expect(blockingViolations, 'Critical and serious axe violations should remain empty').toEqual([]);
}

test.describe('Accessibility smoke @a11y', () => {
  test('sign in screen is keyboard-reachable and free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in');

    const submitButton = page.getByRole('button', { name: /^sign in$/i }).first();
    await tabUntilFocused(page, submitButton);
    await expect(submitButton).toBeFocused();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('learner dashboard is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/');
    await expect(page.getByRole('heading', { name: /keep today'?s priorities and exam signals in view/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('settings profile is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/settings/profile');
    await expect(page.getByRole('heading', { name: /keep profile settings clear before you change them/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('expert queue is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/expert/queue');
    await expect(page.getByText(/review queue|queue/i).first()).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin content library is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/admin/content');
    await expect(page.getByText(/content library|content/i).first()).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
