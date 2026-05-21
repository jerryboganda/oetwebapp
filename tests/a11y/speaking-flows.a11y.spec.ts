import { test } from '@playwright/test';
import { runAxe } from './helpers/axe-runner';

test.describe('Speaking expert + admin surfaces — a11y', () => {
  test('expert speaking queue', async ({ page }) => {
    await page.goto('/expert/speaking/queue');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin speaking cards list', async ({ page }) => {
    await page.goto('/admin/content/speaking/role-play-cards');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin speaking mock-sets', async ({ page }) => {
    await page.goto('/admin/content/speaking/mock-sets');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin speaking analytics', async ({ page }) => {
    await page.goto('/admin/analytics/speaking');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin private speaking calibration', async ({ page }) => {
    await page.goto('/admin/private-speaking/calibration');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });
});
