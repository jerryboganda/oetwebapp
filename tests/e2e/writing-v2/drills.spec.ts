import { expect, test } from '@playwright/test';
import { recoverBrowserSession } from '../fixtures/auth-bootstrap';

test.describe('Writing V2 drills @writing-v2 @smoke', () => {
  test('opening drill selection, feedback and reset roundtrip', async ({
    page,
    request,
  }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await recoverBrowserSession(page, request, 'learner', '/writing/drills');
    await expect(
      page.getByRole('heading', { name: /targeted writing drills/i }),
    ).toBeVisible({ timeout: 30_000 });

    const openingCard = page.getByRole('link', { name: /opening paragraphs/i }).first();
    await expect(openingCard).toBeVisible({ timeout: 30_000 });
    await openingCard.click();
    await expect(page).toHaveURL(/\/writing\/drills\/opening$/);

    const firstDrill = page.locator('main a[href^="/writing/drills/opening/"]').first();
    await expect(firstDrill).toBeVisible({ timeout: 30_000 });
    await firstDrill.click();
    await expect(page).toHaveURL(/\/writing\/drills\/opening\/[^/]+$/);

    const choices = page.getByRole('group', { name: 'Pick the strongest opening sentence:' });
    await expect(choices).toBeVisible({ timeout: 30_000 });
    const submitButton = page.getByRole('button', { name: 'Submit', exact: true });
    await expect(submitButton).toBeDisabled();
    const selectedOpening = await choices.locator('label').first().innerText();
    await choices.locator('label').first().click();
    await expect(choices.getByRole('radio').first()).toBeChecked();
    await expect(submitButton).toBeEnabled();
    await submitButton.click();

    const resultHeading = page.getByRole('heading', { name: 'Result', exact: true });
    await expect(resultHeading).toBeVisible();
    await expect(page.getByText(/^(Pass|Review needed)$/)).toBeVisible();
    const feedback = page.getByRole('listitem').filter({ hasText: selectedOpening });
    await expect(feedback).toBeVisible();
    await expect(feedback.locator('p').last()).not.toHaveText('');
    await expect(choices.getByRole('radio').first()).toBeDisabled();

    await page.getByRole('button', { name: 'Try again', exact: true }).click();
    await expect(resultHeading).toHaveCount(0);
    await expect(choices.getByRole('radio').first()).not.toBeChecked();
    await expect(choices.getByRole('radio').first()).toBeEnabled();
    await expect(submitButton).toBeDisabled();
  });
});
