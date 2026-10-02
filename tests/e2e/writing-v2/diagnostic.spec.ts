import { expect, test } from '@playwright/test';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

test.describe('Writing V2 practice start @writing-v2 @smoke', () => {
  test('practice library opens a non-skippable reading session with a locked editor', async ({
    page,
    request,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await recoverBrowserSession(page, request, 'learner', '/writing/practice/library');
    const practiceTask = page.locator('main a[href^="/writing/practice/session/"]').first();
    await expect(practiceTask).toBeVisible({ timeout: 30_000 });
    await practiceTask.click();
    await expect(page).toHaveURL(/\/writing\/practice\/session\/[^/]+$/);

    const readingWindow = page.getByRole('dialog', { name: /^reading window/i });
    await expect(readingWindow).toBeVisible({ timeout: 30_000 });
    await expect(readingWindow.getByRole('progressbar', { includeHidden: true }))
      .toHaveAttribute('aria-valuenow', /^[1-9]\d*$/);
    await expect(readingWindow.getByRole('button', { name: /skip|start writing/i }))
      .toHaveCount(0);

    const editor = page.locator('#practice-editor');
    await expect(editor).toBeAttached({ timeout: 30_000 });
    await expect(editor).not.toBeEditable();
    await page.keyboard.press('Escape');
    await expect(readingWindow).toBeVisible();
  });
});
