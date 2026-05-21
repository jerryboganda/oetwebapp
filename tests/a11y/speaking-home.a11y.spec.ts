import { test } from '@playwright/test';
import { runAxe } from './helpers/axe-runner';

test.describe('Speaking — accessibility', () => {
  test('home', async ({ page }) => {
    await page.goto('/speaking');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('selection', async ({ page }) => {
    await page.goto('/speaking/selection');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('select-profession', async ({ page }) => {
    await page.goto('/speaking/select-profession');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('drills', async ({ page }) => {
    await page.goto('/speaking/drills');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('pathway', async ({ page }) => {
    await page.goto('/speaking/pathway');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('recordings', async ({ page }) => {
    await page.goto('/speaking/recordings');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('mocks list', async ({ page }) => {
    await page.goto('/speaking/mocks');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });

  test('rulebook', async ({ page }) => {
    await page.goto('/speaking/rulebook');
    await page.waitForLoadState('networkidle');
    await runAxe(page);
  });
});
