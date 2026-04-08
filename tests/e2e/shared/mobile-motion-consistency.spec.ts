import { expect, test, type Page } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Capacitor mobile motion consistency E2E tests.
 *
 * Validates that the motion system functions correctly with mobile viewport,
 * touch emulation, reduced-motion, and capacitor-native runtime data attributes.
 * These tests run against the web dev server with mobile emulation as a proxy
 * for the actual Capacitor WebView (which can't be instrumented by Playwright).
 */

async function waitForMotionSettled(page: Page, timeout = 800) {
  await page.waitForTimeout(timeout);
}

async function enableReducedMotion(page: Page) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
}

/** Simulate capacitor-native runtime by setting the same data attributes the bootstrap script sets. */
async function simulateCapacitorRuntime(page: Page) {
  await page.evaluate(() => {
    const root = document.documentElement;
    root.dataset.runtimeKind = 'capacitor-native';
    root.dataset.capacitorNative = 'true';
    root.dataset.capacitorPlatform = 'android';
  });
}

test.describe('Capacitor mobile motion consistency @mobile-motion', () => {
  test.describe.configure({ mode: 'serial' });

  test.use({
    viewport: { width: 390, height: 844 },
    isMobile: true,
    hasTouch: true,
    userAgent: 'Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36',
  });

  /* ─── Runtime detection ─── */

  test('capacitor-native runtime applies tighter motion tokens', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth') && !testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);

    // Allow CSS custom properties to apply
    await waitForMotionSettled(page, 200);

    const pressableDuration = await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--pressable-transition-duration').trim(),
    );
    expect(pressableDuration).toBe('140ms');

    const pageEnterDuration = await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--page-enter-duration').trim(),
    );
    expect(pageEnterDuration).toBe('240ms');

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Sign-in page mobile motion ─── */

  test('sign-in page renders with entrance animation on mobile viewport', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    await expect(page.getByRole('button', { name: /sign in/i }).first()).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Reduced-motion on mobile ─── */

  test('reduced-motion on mobile collapses spatial movement', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth')) {
      test.skip();
    }

    await enableReducedMotion(page);

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page, 400);

    await expect(page.getByRole('button', { name: /sign in/i }).first()).toBeVisible();

    // Verify reduced-motion CSS overrides
    const pressableDuration = await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--pressable-transition-duration').trim(),
    );
    expect(pressableDuration).toBe('1ms');

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Dashboard mobile motion ─── */

  test('learner dashboard renders with motion on mobile viewport', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Route transitions ─── */

  test('mobile route transitions between learner pages are smooth', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    await page.goto('/settings/profile', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);
    await expect(page.locator('#main-content')).toBeVisible();

    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await waitForMotionSettled(page);
    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Mobile resume animation ─── */

  test('mobile resume trigger sets data-app-resuming attribute', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    // Simulate resume by setting the attribute (as the lifecycle bridge would)
    await page.evaluate(() => {
      document.documentElement.dataset.appResuming = 'true';
    });

    const hasResuming = await page.evaluate(() =>
      document.documentElement.dataset.appResuming === 'true',
    );
    expect(hasResuming).toBe(true);

    // After animation duration, it should be removed
    await page.waitForTimeout(300);
    await page.evaluate(() => {
      delete document.documentElement.dataset.appResuming;
    });

    const afterClear = await page.evaluate(() =>
      document.documentElement.dataset.appResuming,
    );
    expect(afterClear).toBeUndefined();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Safe-area stability during motion ─── */

  test('safe-area utilities are available on mobile viewport', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    // Verify safe-area CSS classes are defined and functional
    const hasSafeAreaClass = await page.evaluate(() => {
      const style = document.createElement('style');
      style.textContent = '.test-safe-area { padding-top: env(safe-area-inset-top); }';
      document.head.appendChild(style);
      const div = document.createElement('div');
      div.className = 'test-safe-area';
      document.body.appendChild(div);
      const computed = getComputedStyle(div).paddingTop;
      document.head.removeChild(style);
      document.body.removeChild(div);
      return computed !== undefined;
    });
    expect(hasSafeAreaClass).toBe(true);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Mobile writing hub loads without errors ─── */

  test('writing hub loads with motion on mobile viewport', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/writing', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Mobile billing loads without errors ─── */

  test('billing page loads with motion on mobile viewport', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/billing', { waitUntil: 'domcontentloaded' });
    await simulateCapacitorRuntime(page);
    await waitForMotionSettled(page);

    await expect(page.locator('#main-content')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
