import { test } from '@playwright/test';
import { runAxe } from './helpers/axe-runner';

test.describe('Speaking expert + admin surfaces — a11y', () => {
  test('expert speaking queue', async ({ page }) => {
    await page.goto('/expert/speaking/queue');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin speaking authoring hub', async ({ page }) => {
    await page.goto('/admin/speaking');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('admin speaking new mock set', async ({ page }) => {
    await page.goto('/admin/speaking/mock-sets/new');
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
