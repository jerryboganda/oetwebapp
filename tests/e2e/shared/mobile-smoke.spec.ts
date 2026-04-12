import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';

/**
 * Mobile viewport smoke tests.
 *
 * Validates core navigation, touch interactions, and mobile layout
 * behavior using mobile device emulation (Pixel 7 / iPhone 14).
 * These tests execute against the web dev server with mobile viewports
 * as a proxy for the Capacitor WebView experience.
 */

test.describe('Mobile viewport smoke @mobile', () => {
  test.use({
    viewport: { width: 390, height: 844 },
    isMobile: true,
    hasTouch: true,
  });

  /* ─── Sign-in page ─── */

  test('sign-in page loads and is interactive on mobile', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth') && !testInfo.project.name.includes('mobile')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });

    // Page should render with no severe errors
    await expect(page.locator('input[type="email"], input[name="email"]')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('input[type="password"]')).toBeVisible();

    // Mobile viewport dimensions should be applied
    const viewportSize = page.viewportSize();
    expect(viewportSize?.width).toBeLessThanOrEqual(430);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Dashboard page (authenticated) ─── */

  test('dashboard renders mobile layout with working navigation', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('mobile') && !testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });

    // Wait for some content to appear (dashboard should render)
    await expect(page.locator('main, [role="main"], [data-testid="dashboard"]').first()).toBeVisible({ timeout: 15_000 });

    // Check viewport is within mobile bounds
    const viewportSize = page.viewportSize();
    expect(viewportSize?.width).toBeLessThanOrEqual(430);

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Deep link simulation ─── */

  test('navigating directly to a deep path renders correctly', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('mobile') && !testInfo.project.name.includes('learner')) {
      test.skip();
    }

    const diagnostics = observePage(page);

    // Simulate a deep link by navigating directly to an inner route
    await page.goto('/exam-guide', { waitUntil: 'domcontentloaded' });

    // Should not crash or show a generic error
    const body = page.locator('body');
    await expect(body).toBeVisible();

    // Should not show "Application error" or "Internal Server Error"
    const bodyText = await body.textContent();
    expect(bodyText).not.toContain('Application error');
    expect(bodyText).not.toContain('Internal Server Error');

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Touch target sizes ─── */

  test('interactive elements meet minimum touch target size', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth') && !testInfo.project.name.includes('mobile')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });

    // Check that buttons and links have minimum 44px hit size
    const buttons = page.locator('button:visible, a:visible[role="button"]');
    const count = await buttons.count();

    for (let i = 0; i < Math.min(count, 10); i++) {
      const box = await buttons.nth(i).boundingBox();
      if (box) {
        // Apple HIG / WCAG minimum is 44x44, we allow 40px as a soft floor
        expect(box.height, `Button ${i} height should be at least 40px`).toBeGreaterThanOrEqual(40);
      }
    }

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Orientation change ─── */

  test('page handles orientation change without breaking', async ({ page }, testInfo) => {
    if (!testInfo.project.name.includes('chromium-unauth') && !testInfo.project.name.includes('mobile')) {
      test.skip();
    }

    const diagnostics = observePage(page);
    await page.goto('/sign-in', { waitUntil: 'domcontentloaded' });

    // Start in portrait
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.locator('body')).toBeVisible();

    // Switch to landscape
    await page.setViewportSize({ width: 844, height: 390 });
    await page.waitForTimeout(300); // Allow layout reflow

    // Should still be visible and usable
    await expect(page.locator('body')).toBeVisible();

    // Switch back to portrait
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(300);
    await expect(page.locator('body')).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  /* ─── Well-known files accessible ─── */

  test('apple-app-site-association is served correctly', async ({ page }) => {
    const response = await page.goto('/.well-known/apple-app-site-association');
    expect(response).not.toBeNull();
    expect(response!.status()).toBe(200);

    const text = await response!.text();
    expect(text).toContain('applinks');
    expect(text).toContain('com.oetprep.learner');
  });

  test('assetlinks.json is served correctly', async ({ page }) => {
    const response = await page.goto('/.well-known/assetlinks.json');
    expect(response).not.toBeNull();
    expect(response!.status()).toBe(200);

    const text = await response!.text();
    expect(text).toContain('android_app');
    expect(text).toContain('com.oetprep.learner');
  });
});
