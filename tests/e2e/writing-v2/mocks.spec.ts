import { expect, test } from '@playwright/test';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

test.describe('Writing V2 mocks @writing-v2 @smoke', () => {
  test('published mock starts with case notes and a locked reading phase', async ({
    page,
    request,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await recoverBrowserSession(page, request, 'learner', '/writing/mocks');

    await expect(
      page.getByRole('heading', {
        name: /mocks under strict exam conditions/i,
      }),
    ).toBeVisible({ timeout: 30_000 });

    const startButton = page.getByRole('button', { name: 'Start strict mock', exact: true }).first();
    await expect(startButton).toBeVisible({ timeout: 30_000 });

    const startPromise = page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname.endsWith('/v1/writing/mocks/start')
        && response.request().method() === 'POST',
      { timeout: 30_000 },
    );
    await startButton.click();
    const startResponse = await startPromise;
    expect(startResponse.ok(), `Strict mock start returned HTTP ${startResponse.status()}`).toBe(true);

    await page.waitForURL(/\/writing\/mocks\/session\/[^/]+(?:\?.*)?$/, {
      timeout: 30_000,
    });

    await expect(
      page.getByRole('region', {
        name: /case notes/i,
      }),
    ).toBeVisible({ timeout: 30_000 });

    const readingWindow = page.getByRole('dialog', { name: /^reading window/i });
    await expect(readingWindow).toBeVisible({ timeout: 30_000 });
    await expect(readingWindow.getByRole('heading', { name: 'Case notes', exact: true })).toBeVisible();
    await expect(readingWindow.getByRole('list').first().getByRole('listitem').first()).toBeVisible();
    await expect(readingWindow.getByRole('progressbar', { includeHidden: true }))
      .toHaveAttribute('aria-valuenow', /^[1-9]\d*$/);
    await expect(readingWindow.getByRole('button', { name: /skip|start writing/i })).toHaveCount(0);

    const editor = page.locator('#mock-editor[contenteditable]');
    await expect(editor).toBeAttached({ timeout: 30_000 });
    await expect(editor).toHaveAttribute('contenteditable', 'false');
    await page.keyboard.press('Escape');
    await expect(readingWindow).toBeVisible();
  });
});
