import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

async function tabUntilFocused(page: Page, locator: Locator, maxTabs = 12) {
  for (let index = 0; index < maxTabs; index += 1) {
    await page.keyboard.press('Tab');
    if (await locator.evaluate((node) => node === document.activeElement)) {
      return;
    }
  }
}

async function expectNoSeriousAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa'])
    .analyze();

  const blockingViolations = results.violations.filter((violation) => ['critical', 'serious'].includes(violation.impact ?? ''));
  expect(blockingViolations, 'Critical and serious axe violations should remain empty').toEqual([]);
}

test.describe('Accessibility smoke @a11y', () => {
  test.describe.configure({ mode: 'serial' });

  test('sign in screen is keyboard-reachable and free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });

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
    await page.goto('/', { waitUntil: 'domcontentloaded' });
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
    await page.goto('/settings/profile', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /keep profile settings clear before you change them/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('billing center is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/billing', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /your billing center/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('writing player is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/writing/player?taskId=wt-001', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('main').getByRole('heading').first()).toBeVisible();
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
    await page.goto('/expert/queue', { waitUntil: 'domcontentloaded' });
    await expect(page.locator('main').getByRole('heading', { name: /^review queue$/i })).toBeVisible();
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
    await page.goto('/admin/content', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /content library/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin audit logs are free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/admin/audit-logs', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /audit logs/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('admin user credit surface is free of critical axe violations', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/admin/users/mock-user-001', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /faisal maqsood/i })).toBeVisible();
    await expectNoSeriousAxeViolations(page);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
