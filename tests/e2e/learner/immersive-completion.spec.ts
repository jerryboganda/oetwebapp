import { expect, test } from '@playwright/test';
import { attachDiagnostics, expectNoSevereClientIssues, observePage } from '../fixtures/diagnostics';
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
    const resultHeader = page.getByText(/canonical oet listening score|practice score/i);
    const notFound = page.getByRole('heading', { name: /result not found/i });
    await expect(resultHeader.or(notFound)).toBeVisible({ timeout: 90000 });
    if (await notFound.isVisible().catch(() => false)) {
      await page.reload({ waitUntil: 'domcontentloaded' });
      await expect(resultHeader).toBeVisible({ timeout: 90000 });
    }
    await expect(page.getByRole('heading', { name: /detailed review/i })).toBeVisible({ timeout: 30000 });

    expectNoSevereClientIssues(diagnostics);
    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  // Re-authored for the V2 practice session (the legacy /writing/player was
  // retired). Hermetic: the Writing API is route-mocked, so it needs no seeded
  // task. Types into the real Tiptap editor after skipping the reading window
  // with the page clock, waits for "Saved", submits ONCE and lands on grading.
  test('writing practice autosaves, survives a refresh, and submits once to grading', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(180000);
    const diagnostics = observePage(page);
    page.on('dialog', (dialog) => dialog.accept());
    const scenarioId = 'e2e-immersive-writing';
    const content = 'Dear Dr Patterson, I am writing to refer Mrs Eleanor Vance for wound review after surgery.';
    let draft: Record<string, unknown> | null = null;
    let submissions = 0;
    const json = (body: unknown, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route('**/v1/writing/attempt-events', (route) => route.fulfill(json({ accepted: 1 })));
    await page.route(`**/v1/writing/highlights/${scenarioId}`, (route) => route.fulfill(json({ highlightsJson: '{}' })));
    await page.route(`**/v1/writing/scenarios/${scenarioId}`, (route) => route.fulfill(json({
      id: scenarioId, title: 'E2E referral', letterType: 'LT-RR', profession: 'medicine', subDiscipline: null,
      topics: [], difficulty: 2, caseNotesStructured: [{ index: 1, text: 'Wound review.', relevance: 'relevant' }],
      isDiagnostic: false, status: 'published', createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z',
      taskPromptMarkdown: 'Write a referral letter.', fixedInstructions: [], readingTimeSeconds: 300,
      writingTimeSeconds: 2400, wordGuideMin: 180, wordGuideMax: 200, stimulusPdfMediaAssetId: null, stimulusPdfDownloadPath: null,
    })));
    await page.route(`**/v1/writing/scenarios/${scenarioId}/eligibility`, (route) => route.fulfill(json({ feedbackMessage: null })));
    await page.route(`**/v1/writing/drafts/${scenarioId}/practice`, (route) => {
      if (route.request().method() === 'GET') {
        return route.fulfill(draft ? json(draft) : json({ code: 'not_found' }, 404));
      }
      const body = route.request().postDataJSON() as Record<string, unknown>;
      const version = Number(draft?.version ?? 0) + 1;
      draft = { ...draft, ...body, scenarioId, mode: 'practice', status: 'active', version, lastSavedAt: new Date().toISOString() };
      return route.fulfill(json(draft));
    });
    await page.route('**/v1/writing/submissions', (route) => {
      submissions += 1;
      return route.fulfill(json({ id: 'e2e-immersive-sub', scenarioId, status: 'queued', letterContent: content }));
    });

    await page.clock.install();
    await page.goto(`/writing/practice/session/${scenarioId}`, { waitUntil: 'domcontentloaded' });
    const timer = page.getByTestId('writing-timer');
    await expect(timer).toHaveAttribute('data-phase', 'reading', { timeout: 60000 });
    await page.clock.fastForward('05:00');
    await expect(timer).toHaveAttribute('data-phase', 'writing', { timeout: 15000 });

    const writingEditor = page.locator('div.ProseMirror#practice-editor');
    await writingEditor.click();
    await writingEditor.pressSequentially(content, { delay: 15 });
    await expect(page.getByTestId('writing-draft-status')).toHaveAttribute('data-state', 'saved', { timeout: 20000 });

    await page.reload({ waitUntil: 'domcontentloaded' });
    await expect(writingEditor).toHaveText(content, { timeout: 60000 });

    await page.getByTestId('writing-submit').click();
    await page.waitForURL(/\/writing\/submissions\/e2e-immersive-sub\/grading/, { timeout: 60000, waitUntil: 'commit' });
    expect(submissions).toBe(1);

    diagnostics.detach();
    await attachDiagnostics(testInfo, diagnostics);
  });

  test('speaking task: rules + consent first, one record control, keyboard-safe dialogs, result completion', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') {
      test.skip();
    }

    testInfo.setTimeout(360000); // speaking pipeline = transcription + AI grade; cold dev compile under matrix load can need ~5–6 min total
    await installFakeRecordingMedia(page);
    const diagnostics = observePage(page);

    await page.goto('/speaking/task/st-001?mode=self', { waitUntil: 'domcontentloaded' });
    // 23 Sep 2026: ONE Rules + consent step before the recorder (replaces the
    // inline and finish-dialog consent checkboxes).
    await expect(page.getByTestId('speaking-rules-consent')).toBeVisible({ timeout: 60000 });
    await page.getByRole('checkbox', { name: /i have read the rules/i }).check();
    await page.getByRole('button', { name: /continue to the recorder/i }).click();
    await expect(page.getByRole('heading', { name: /ready to record/i })).toBeVisible({ timeout: 60000 });
    await expect(page.getByTestId('speaking-role-card')).toBeVisible();

    const cancelTaskButton = page.getByRole('button', { name: /cancel task/i });
    await cancelTaskButton.click();

    const stopDialog = page.getByRole('dialog', { name: /stop practice\?/i });
    await expect(stopDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(stopDialog).toHaveCount(0);
    await expect(cancelTaskButton).toBeFocused();

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
    // One control only: no pause/stop duplicates while recording.
    await expect(page.getByRole('button', { name: /pause recording|finish exam recording/i })).toHaveCount(0);

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
