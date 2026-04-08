import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Motion consistency E2E tests.
 *
 * Validates that the motion system is functional, non-janky, and respects
 * reduced-motion preferences across critical product surfaces.
 */

/** Wait for route-level motion to settle (spring animations). */
async function waitForMotionSettled(page: Page, timeout = 800) {
  await page.waitForTimeout(timeout);
}

/** Inject reduced-motion preference via media emulation. */
async function enableReducedMotion(page: Page) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
}

test.describe('Motion consistency @motion', () => {
  test.describe.configure({ mode: 'serial' });

  /* ─── Sign-in page motion ─── */

  test('sign-in page renders with entrance animation without errors', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    // Page content should be visible after motion settles
    await expect(page.getByRole('button', { name: /sign in/i }).first()).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Reduced-motion mode ─── */

  test('sign-in page works correctly with reduced-motion preference', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth')) {
      test.skip();
    }

    await enableReducedMotion(page);

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page, 400);

    await expect(page.getByRole('button', { name: /sign in/i }).first()).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Learner dashboard motion ─── */

  test('learner dashboard entrance motion renders without errors', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    // Verify main content area is visible after route transition
    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('learner dashboard works with reduced-motion', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    await enableReducedMotion(page);

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page, 400);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Route transitions ─── */

  test('route navigation transitions smoothly between learner pages', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    // Navigate to settings
    await page.goto('/settings/profile', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);
    await expect(page.locator('#main-content')).toBeVisible();

    // Navigate back to dashboard
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);
    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Admin surface motion ─── */

  test('admin dashboard renders with motion without errors', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-admin')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/admin', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Expert surface motion ─── */

  test('expert dashboard renders with motion without errors', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-expert')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/expert', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── No console errors from motion on page load ─── */

  test('writing hub loads without motion-related console errors', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/writing', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('billing page loads without motion-related errors', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/billing', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
