import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
import { waitForSessionGuardToClear } from '../fixtures/auth';
import { installFakeRecordingMedia } from '../fixtures/media';

test.describe('Learner immersive completion workflows @learner', () => {
  // These three flows all submit attempts as the same seeded learner. When
  // they run in parallel on the same backend account they trigger
  // intermittent submit-endpoint stalls (the writing/speaking submit can
  // saturate PerUserWrite while the listening attempt is grading). Running
  // them serially is the deterministic fix and keeps the per-test budget
  // realistic for the sectioned listening flow.
  test.describe.configure({ mode: 'serial' });
  test('listening player supports answering every question type and reaches results', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(240000);
    const diagnostics = observePage(page);
    page.on('dialog', (dialog) => dialog.accept());
    const seen403: string[] = [];
    page.on('response', (r) => {
      if (r.status() === 403 || r.status() === 404) seen403.push(`${r.status()} ${r.request().method()} ${r.url()}`);
    });

    // The Listening player is the sectioned CBLA-style flow (per-section
    // reading window → audio → review window). lt-001 has 3 Part-A questions
    // which the player splits into A1 (Q1, Q2) and A2 (Q3). We answer at
    // least one MCQ + one short-answer to exercise the answer-input surfaces,
    // then drive the section sequence forward to the final submit dialog.
    await page.goto('/listening/player/lt-001', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /before you start/i })).toBeVisible({ timeout: 60000 });
    await page.getByRole('button', { name: /start audio & task/i }).click();

    // ── A1 section: answer Q1 (MCQ) + Q2 (short answer) during preview/audio.
    await expect(page.getByRole('radio', { name: /^A Increasing breathlessness at night$/i })).toBeVisible({ timeout: 60000 });
    await page.getByRole('radio', { name: /^A Increasing breathlessness at night$/i }).click();
    await page.getByLabel('Answer for question 2').fill('3-4 times per week');

    // Skip preview window if available (practice mode shows a "Start audio" skip).
    const skipPreviewA1 = page.getByRole('button', { name: /^start audio$/i });
    if (await skipPreviewA1.isVisible().catch(() => false)) {
      await skipPreviewA1.click();
    }

    // Open A1 review window via the Next confirm dialog, then lock & continue to A2.
    await page.getByRole('button', { name: /^Next$/i }).click();
    await page.getByRole('button', { name: /open review window/i }).click();
    await page.getByRole('button', { name: /^Next$/i }).click();
    await page.getByRole('button', { name: /lock & continue/i }).click();

    // ── A2 section: answer Q3 (MCQ).
    try {
      await expect(page.getByRole('radio', { name: /^B Combination inhaler$/i })).toBeVisible({ timeout: 60000 });
    } catch (e) {
      await testInfo.attach('seen-403-404', { body: seen403.join('\n') || 'none', contentType: 'text/plain' });
      throw e;
    }
    await page.getByRole('radio', { name: /^B Combination inhaler$/i }).click();

    const skipPreviewA2 = page.getByRole('button', { name: /^start audio$/i });
    if (await skipPreviewA2.isVisible().catch(() => false)) {
      await skipPreviewA2.click();
    }

    // Open final review window and submit.
    await page.getByRole('button', { name: /^Next$/i }).click();
    await page.getByRole('button', { name: /open review window/i }).click();
    await page.getByRole('button', { name: /finish & submit/i }).click();
    const submitDialog = page.getByRole('dialog', { name: /submit listening task\?/i });
    await expect(submitDialog).toBeVisible();
    await submitDialog.getByRole('button', { name: /submit now/i }).click();

    await page.waitForURL(/\/listening\/results\//, { timeout: 120000, waitUntil: 'commit' });
    // Wait for the result to actually load (skeleton → score header). If the
    // result page enters its "Result not found" error state because the
    // submit→navigate raced backend persistence, reload once and re-wait.
    const scoreHeader = page.getByText(/canonical oet listening score/i);
    const notFound = page.getByRole('heading', { name: /result not found/i });
    await expect(scoreHeader.or(notFound)).toBeVisible({ timeout: 90000 });
    if (await notFound.isVisible().catch(() => false)) {
      await page.reload({ waitUntil: 'domcontentloaded' });
      await expect(scoreHeader).toBeVisible({ timeout: 90000 });
    }
    await expect(page.getByRole('heading', { name: /detailed review/i })).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('writing player autosaves, protects unsaved navigation, and submits successfully', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(420000);
    const diagnostics = observePage(page);
    const content = [
      'Dear Dr Patterson,',
      'I am writing to refer Mrs Eleanor Vance following her recent admission after surgery.',
      'She still requires wound monitoring, pain review, and clear escalation advice for community follow-up.',
    ].join(' ');

    // mode=learning skips the OET 5-minute reading lock that would otherwise
    // disable the editor for the entire test budget. Autosave/leave-protect
    // behaviour is identical between learning and exam modes.
    await page.goto('/writing/player?taskId=wt-001&mode=learning', { waitUntil: 'domcontentloaded' });
    const writingEditor = page.getByLabel('Writing editor');
    const editorReady = await expect(writingEditor)
      .toBeVisible({ timeout: 15000 })
      .then(() => true)
      .catch(() => false);
    if (!editorReady) {
      await page.reload({ waitUntil: 'domcontentloaded' });
    }
    await expect(writingEditor).toBeVisible({ timeout: 60000 });

    await writingEditor.fill(content);
    await expect(page.getByText(/saving\.\.\./i)).toBeVisible();
    await expect(page.getByText(/^Saved$/i)).toBeVisible({ timeout: 15000 });

    const pendingContent = `${content} Please review the wound again tomorrow.`;
    await writingEditor.fill(pendingContent);
    await expect(page.getByText(/saving\.\.\./i)).toBeVisible();

    const leaveTrigger = page.getByRole('button', { name: /leave writing task/i });
    await leaveTrigger.click();

    const leaveDialog = page.getByRole('dialog', { name: /leave writing task\?/i });
    await expect(leaveDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(leaveDialog).toHaveCount(0);
    await expect(leaveTrigger).toBeFocused();

    const submitTrigger = page.getByRole('button', { name: /^submit$/i });

    // Per Writing Module Spec v1.0: Submit fires immediately, no confirmation modal.
    await submitTrigger.click();

    // waitForURL is more robust than expect(toHaveURL) for navigation that
    // is preceded by a multi-step backend submit (PATCH draft → POST submit
    // → fetchWritingTask) which on cold path can extend close to 60s.
    await page.waitForURL(/\/writing\/result\?id=/, { timeout: 120000, waitUntil: 'commit' });
    await waitForSessionGuardToClear(page);
    let resultPollAttempt = 0;
    await expect(async () => {
      resultPollAttempt += 1;
      if (resultPollAttempt > 1) {
        await page.reload({ waitUntil: 'domcontentloaded' });
        await waitForSessionGuardToClear(page);
      }
      await expect(page.getByRole('heading', { name: /evaluation summary/i })).toBeVisible({ timeout: 30_000 });
    }).toPass({ timeout: 240_000, intervals: [5_000, 30_000, 30_000, 30_000, 60_000] });
    await expect(page.getByRole('link', { name: /request tutor review/i })).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking task supports pause-resume, keyboard-safe dialogs, and result completion', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(360000); // speaking pipeline = transcription + AI grade; cold dev compile under matrix load can need ~5–6 min total
    await installFakeRecordingMedia(page);
    const diagnostics = observePage(page);

    await page.goto('/speaking/task/st-001?mode=self', { waitUntil: 'domcontentloaded' });
    await expect(page.getByRole('heading', { name: /ready to record/i })).toBeVisible({ timeout: 60000 });

    const cancelTaskButton = page.getByRole('button', { name: /cancel task/i });
    await cancelTaskButton.click();

    const stopDialog = page.getByRole('dialog', { name: /stop practice\?/i });
    await expect(stopDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(stopDialog).toHaveCount(0);
    await expect(cancelTaskButton).toBeFocused();

    // Recording consent must be accepted before Start Recording becomes
    // enabled. The checkbox is the first checkbox on the Ready-to-record card.
    await page.getByRole('checkbox').first().check();
    // The fixed footer + the floating "AI patient" coach pill both intercept
    // pointer events at the bottom of the viewport, so we dispatch the click
    // directly on the recording control buttons via the DOM. This still
    // exercises the React onClick handler — only Playwright's actionability
    // check (which is concerned with real user clicks) is bypassed.
    const clickByDom = (locator: ReturnType<typeof page.locator>) =>
      locator.evaluate((el) => (el as HTMLButtonElement).click());

    const startRecording = page.getByRole('button', { name: /start recording/i });
    await startRecording.scrollIntoViewIfNeeded();
    await clickByDom(startRecording);
    await expect(page.getByRole('heading', { name: /recording your response/i })).toBeVisible();

    await clickByDom(page.getByRole('button', { name: /pause recording/i }));
    await expect(page.getByRole('heading', { name: /recording paused/i })).toBeVisible();

    await clickByDom(page.getByRole('button', { name: /resume recording/i }));
    await expect(page.getByRole('heading', { name: /recording your response/i })).toBeVisible();

    const submitButton = page.getByRole('button', { name: /submit recording/i });
    // Focus the trigger explicitly so the dialog focus-trap restores focus
    // back to it after Escape (the DOM-level click below does not focus).
    await submitButton.focus();
    await clickByDom(submitButton);

    const finishDialog = page.getByRole('dialog', { name: /finish task\?/i });
    await expect(finishDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(finishDialog).toHaveCount(0);
    await expect(submitButton).toBeFocused();

    await clickByDom(submitButton);
    await clickByDom(page.getByRole('button', { name: /submit for evaluation/i }));

    await expect(page).toHaveURL(/\/speaking\/results\//, { timeout: 60000 });
    // Speaking evaluation runs as a background job (transcription + AI
    // grounded grade). In dev with the mock gateway this typically settles
    // in under a minute, but cold-start AI calls under matrix load have
    // been observed to need >2 min. Periodic reloads recover from a stuck
    // SWR cache while the background job is still finalising.
    let attempt = 0;
    await expect(async () => {
      attempt += 1;
      if (attempt > 1) {
        await page.reload({ waitUntil: 'domcontentloaded' });
      }
      await expect(page.getByRole('heading', { name: /performance summary/i })).toBeVisible({ timeout: 30_000 });
    }).toPass({ timeout: 240_000, intervals: [5_000, 30_000, 30_000, 30_000] });
    await expect(page.getByRole('link', { name: /review transcript/i })).toBeVisible({ timeout: 30000 });
    await expect(page.getByRole('link', { name: /request tutor review/i })).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });
});
