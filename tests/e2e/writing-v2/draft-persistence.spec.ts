import { expect, test, type BrowserContext, type Page, type Route } from '@playwright/test';

/**
 * Writing V2 — zero-loss drafts (WAI-07). Hermetic: the Writing API is
 * route-mocked by an in-memory draft server that follows the shared draft
 * contract (create-only expectedVersion 0, compare-and-set, timers take the
 * minimum, phase never regresses). Only the learner auth state comes from
 * setup. Scope: chromium-learner.
 */

const SCENARIO_ID = 'e2e-draft-persistence';
const SESSION_PATH = `/writing/practice/session/${SCENARIO_ID}`;
const LETTER = 'Dear Dr Green, I am writing to refer Mr Adam Lee for review of a persistent cough.';

const SCENARIO = {
  id: SCENARIO_ID,
  title: 'E2E routine referral',
  letterType: 'LT-RR',
  profession: 'medicine',
  subDiscipline: null,
  topics: [],
  difficulty: 2,
  caseNotesStructured: [{ index: 1, text: 'Persistent cough for three weeks.', relevance: 'relevant' }],
  isDiagnostic: false,
  status: 'published',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  taskPromptMarkdown: 'Write a routine referral letter.',
  fixedInstructions: [],
  readingTimeSeconds: 300,
  writingTimeSeconds: 2400,
  wordGuideMin: 180,
  wordGuideMax: 200,
  stimulusPdfMediaAssetId: null,
  stimulusPdfDownloadPath: null,
};

type Draft = Record<string, unknown> & { content: string; version: number };

/** One in-memory server shared by every browser context of a test (a new context = another device). */
class DraftServer {
  draft: Draft | null = null;
  puts = 0;
  failPuts: 'none' | '503' | 'abort' = 'none';

  async install(context: BrowserContext) {
    const json = (route: Route, body: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    await context.route('**/v1/writing/attempt-events', (route) => json(route, { accepted: 1 }));
    await context.route(`**/v1/writing/highlights/${SCENARIO_ID}`, (route) => json(route, { highlightsJson: '{}' }));
    await context.route(`**/v1/writing/scenarios/${SCENARIO_ID}`, (route) => json(route, SCENARIO));
    await context.route(`**/v1/writing/scenarios/${SCENARIO_ID}/eligibility`, (route) => json(route, { feedbackMessage: null }));
    await context.route(`**/v1/writing/drafts/${SCENARIO_ID}/practice`, (route) => this.handle(route, json));
  }

  private async handle(route: Route, json: (route: Route, body: unknown, status?: number) => Promise<void>) {
    const request = route.request();
    if (request.method() === 'GET') {
      if (!this.draft) return json(route, { code: 'not_found', message: 'No draft' }, 404);
      return json(route, this.draft);
    }
    if (request.method() !== 'PUT') return route.continue();
    if (this.failPuts === 'abort') return route.abort('connectionfailed');
    if (this.failPuts === '503') return json(route, { code: 'unavailable', message: 'Down' }, 503);
    const body = request.postDataJSON() as Record<string, unknown>;
    const expected = body.expectedVersion as number | null | undefined;
    const current = this.draft?.version ?? 0;
    if (typeof expected === 'number' && expected !== current) {
      return json(route, { code: 'draft_version_conflict', message: 'Conflict' }, 409);
    }
    const min = (a: unknown, b: unknown) =>
      typeof b !== 'number' ? a : typeof a !== 'number' ? b : Math.min(a, b);
    const previous = this.draft;
    this.puts += 1;
    this.draft = {
      userId: 'learner',
      scenarioId: SCENARIO_ID,
      mode: 'practice',
      content: String(body.content ?? ''),
      wordCount: Number(body.wordCount ?? 0),
      timeSpentSeconds: Number(body.timeSpentSeconds ?? 0),
      lastSavedAt: new Date().toISOString(),
      version: current + 1,
      status: 'active',
      submissionId: null,
      submissionStatus: null,
      phase: previous?.phase === 'writing' ? 'writing' : (body.phase ?? previous?.phase ?? null),
      readingSecondsRemaining: min(previous?.readingSecondsRemaining, body.readingSecondsRemaining),
      writingSecondsRemaining: min(previous?.writingSecondsRemaining, body.writingSecondsRemaining),
    };
    return json(route, this.draft);
  }
}

const editor = (page: Page) => page.locator('div.ProseMirror#practice-editor');
const draftStatus = (page: Page) => page.getByTestId('writing-draft-status');

/** Opens the task and skips the 5-minute reading window with the page clock. */
async function openInWritingPhase(page: Page) {
  await page.clock.install();
  page.on('dialog', (dialog) => dialog.accept());
  await page.goto(SESSION_PATH, { waitUntil: 'domcontentloaded' });
  const timer = page.getByTestId('writing-timer');
  await expect(timer).toBeVisible({ timeout: 60_000 });
  if ((await timer.getAttribute('data-phase')) === 'reading') {
    await page.clock.fastForward('05:00');
  }
  await expect(timer).toHaveAttribute('data-phase', 'writing', { timeout: 15_000 });
  await expect(editor(page)).toBeVisible({ timeout: 30_000 });
}

test.describe('Writing V2 draft persistence @writing-v2 @learner', () => {
  test.beforeEach(({}, testInfo) => {
    if (testInfo.project.name !== 'chromium-learner') test.skip();
  });

  test('refresh while typing restores the exact text and keeps the clock paused, not reset', async ({ page, context }) => {
    const server = new DraftServer();
    await server.install(context);
    await openInWritingPhase(page);

    await editor(page).click();
    await editor(page).pressSequentially(LETTER, { delay: 15 });
    const before = Number(await page.getByTestId('writing-timer').getAttribute('data-seconds-remaining'));
    // Within the debounce window: only this device has the newest characters.
    await page.reload({ waitUntil: 'domcontentloaded' });

    await expect(editor(page)).toHaveText(LETTER, { timeout: 30_000 });
    const timer = page.getByTestId('writing-timer');
    await expect(timer).toHaveAttribute('data-phase', 'writing');
    const after = Number(await timer.getAttribute('data-seconds-remaining'));
    expect(after).toBeLessThanOrEqual(before + 6);
    expect(after).toBeGreaterThan(2400 - 120);
    await expect(page.getByTestId('writing-resume-banner')).toBeVisible();
  });

  test('a new device (empty storage) resumes from the server copy once "Saved" shows', async ({ page, context, browser }, testInfo) => {
    const server = new DraftServer();
    await server.install(context);
    await openInWritingPhase(page);
    await editor(page).click();
    await editor(page).pressSequentially(LETTER, { delay: 15 });
    await expect(draftStatus(page)).toHaveAttribute('data-state', 'saved', { timeout: 20_000 });
    expect(server.draft?.content).toBe(LETTER);
    await context.close();

    const storageState = testInfo.project.use.storageState as string;
    const device = await browser.newContext({ storageState });
    await server.install(device);
    const fresh = await device.newPage();
    await fresh.goto(SESSION_PATH, { waitUntil: 'domcontentloaded' });
    await expect(editor(fresh)).toHaveText(LETTER, { timeout: 60_000 });
    await expect(fresh.getByTestId('writing-timer')).toHaveAttribute('data-phase', 'writing');
    await device.close();
  });

  test('offline: the letter stays on this device, Submit is blocked, and it syncs on reconnect', async ({ page, context }) => {
    const server = new DraftServer();
    await server.install(context);
    await openInWritingPhase(page);

    await context.setOffline(true);
    await editor(page).click();
    await editor(page).pressSequentially(LETTER, { delay: 15 });
    await expect(draftStatus(page)).toHaveAttribute('data-state', 'pending-local', { timeout: 15_000 });
    await expect(page.getByTestId('writing-submit')).toBeDisabled();

    await context.setOffline(false);
    await expect(draftStatus(page)).toHaveAttribute('data-state', 'saved', { timeout: 30_000 });
    expect(server.draft?.content).toBe(LETTER);
    await expect(page.getByTestId('writing-submit')).toBeEnabled();
  });

  test('a failing draft server never loses the text: the device copy survives a restart', async ({ page, context, browser }) => {
    const server = new DraftServer();
    await server.install(context);
    await openInWritingPhase(page);

    server.failPuts = '503';
    await editor(page).click();
    await editor(page).pressSequentially(LETTER, { delay: 15 });
    await expect(draftStatus(page)).toHaveAttribute('data-state', 'pending-local', { timeout: 30_000 });
    server.failPuts = 'abort';

    // "Restart the browser" with this device's storage: the unsynced copy is restored.
    const state = await context.storageState();
    const shadow = state.origins
      .flatMap((origin) => origin.localStorage)
      .find((item) => item.name.startsWith('oet:writing-draft:v1:') && item.name.endsWith(`:${SCENARIO_ID}:practice`));
    expect(shadow && JSON.parse(shadow.value).text).toBe(LETTER);
    await context.close();

    const restarted = await browser.newContext({ storageState: state });
    await server.install(restarted);
    server.failPuts = 'none';
    const again = await restarted.newPage();
    await again.goto(SESSION_PATH, { waitUntil: 'domcontentloaded' });
    await expect(editor(again)).toHaveText(LETTER, { timeout: 60_000 });
    await expect(draftStatus(again)).toHaveAttribute('data-state', 'saved', { timeout: 30_000 });
    expect(server.draft?.content).toBe(LETTER);
    await restarted.close();
  });
});
