import { test } from '@playwright/test';
import { runAxe } from './helpers/axe-runner';

test.describe('Auth / Dashboard / Skills — accessibility', () => {
  test('sign-in', async ({ page }) => {
    await page.goto('/sign-in');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('dashboard home', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('reading home', async ({ page }) => {
    await page.goto('/reading');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('writing home', async ({ page }) => {
    await page.goto('/writing');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('listening home', async ({ page }) => {
    await page.goto('/listening');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('billing', async ({ page }) => {
    await page.goto('/billing');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin', async ({ page }) => {
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });
});
