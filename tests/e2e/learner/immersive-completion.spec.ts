import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { installFakeRecordingMedia } from '../fixtures/media';

test.describe('Learner immersive completion workflows @learner', () => {
  test('listening player supports answering every question type and reaches results', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    page.on('dialog', (dialog) => dialog.accept());

    await page.goto('/listening/player/lt-001');
    await expect(page.getByRole('heading', { name: /ready to start\?/i })).toBeVisible();

    await page.getByRole('button', { name: /start audio & task/i }).click();
    await expect(page.getByRole('button', { name: /submit answers/i })).toBeVisible();

    await page.getByRole('button', { name: /^Increasing breathlessness at night$/i }).click();
    await page.getByLabel('Answer for question 2').fill('3-4 times per week');
    await page.getByRole('button', { name: /^Combination inhaler$/i }).click();

    await page.getByRole('button', { name: /submit answers/i }).click();

    await expect(page).toHaveURL(/\/listening\/results\/lt-001$/);
    await expect(page.getByText(/detailed review/i)).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('writing player autosaves, protects unsaved navigation, and submits successfully', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    const diagnostics = observePage(page);
    const content = [
      'Dear Dr Patterson,',
      'I am writing to refer Mrs Eleanor Vance following her recent admission after surgery.',
      'She still requires wound monitoring, pain review, and clear escalation advice for community follow-up.',
    ].join(' ');

    await page.goto('/writing/player?taskId=wt-001');
    await expect(page.getByLabel('Writing editor')).toBeVisible();

    await page.getByLabel('Writing editor').fill(content);
    await expect(page.getByText(/saving\.\.\./i)).toBeVisible();
    await expect(page.getByText(/^Saved$/i)).toBeVisible({ timeout: 15000 });

    const pendingContent = `${content} Please review the wound again tomorrow.`;
    await page.getByLabel('Writing editor').fill(pendingContent);
    await expect(page.getByText(/saving\.\.\./i)).toBeVisible();

    const leaveTrigger = page.getByRole('button', { name: /leave writing task/i });
    await leaveTrigger.click();

    const leaveDialog = page.getByRole('dialog', { name: /leave writing task\?/i });
    await expect(leaveDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(leaveDialog).toHaveCount(0);
    await expect(leaveTrigger).toBeFocused();

    const submitTrigger = page.getByRole('button', { name: /^submit$/i });
    await submitTrigger.click();

    const submitDialog = page.getByRole('dialog', { name: /submit your response\?/i });
    await expect(submitDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(submitDialog).toHaveCount(0);
    await expect(submitTrigger).toBeFocused();

    await submitTrigger.click();
    await page.getByRole('button', { name: /confirm submit/i }).click();

    await expect(page).toHaveURL(/\/writing\/result\?id=/);
    await expect(page.getByRole('heading', { name: /evaluation summary/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /request expert review/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking task supports pause-resume, keyboard-safe dialogs, and result completion', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    await installFakeRecordingMedia(page);
    const diagnostics = observePage(page);

    await page.goto('/speaking/task/st-001?mode=self');
    await expect(page.getByRole('heading', { name: /ready to record/i })).toBeVisible();

    const cancelTaskButton = page.getByRole('button', { name: /cancel task/i });
    await cancelTaskButton.click();

    const stopDialog = page.getByRole('dialog', { name: /stop practice\?/i });
    await expect(stopDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(stopDialog).toHaveCount(0);
    await expect(cancelTaskButton).toBeFocused();

    await page.getByRole('button', { name: /start recording/i }).click();
    await expect(page.getByRole('heading', { name: /recording your response/i })).toBeVisible();

    await page.getByRole('button', { name: /pause recording/i }).click();
    await expect(page.getByRole('heading', { name: /recording paused/i })).toBeVisible();

    await page.getByRole('button', { name: /resume recording/i }).click();
    await expect(page.getByRole('heading', { name: /recording your response/i })).toBeVisible();

    const submitButton = page.getByRole('button', { name: /submit recording/i });
    await submitButton.click();

    const finishDialog = page.getByRole('dialog', { name: /finish task\?/i });
    await expect(finishDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(finishDialog).toHaveCount(0);
    await expect(submitButton).toBeFocused();

    await submitButton.click();
    await page.getByRole('button', { name: /submit for evaluation/i }).click();

    await expect(page).toHaveURL(/\/speaking\/results\//);
    await expect(page.getByRole('heading', { name: /performance summary/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /review transcript/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /request expert review/i })).toBeVisible();

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
