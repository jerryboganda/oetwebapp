import { expect, test, type ConsoleMessage, type Response } from '@playwright/test';

/**
 * Production drill-down — open practice items in each major module.
 *
 * Goes one level deeper than prod-interaction.spec.ts: clicks the FIRST item
 * in each module's list (not just the primary CTA), so we exercise:
 *   - reading paper player route with a real paperId
 *   - listening drill player
 *   - writing editor canvas
 *   - vocabulary flashcard / quiz session
 *   - mocks setup → confirm gate
 *
 * Does NOT submit any data (no essay submit, no quiz submit) — read-only deep
 * traversal to surface 5xx that only fire on inner pages.
 *
 * Run: $env:PROD_LEARNER_EMAIL=...; $env:PROD_LEARNER_PASSWORD=...;
 *      npx playwright test tests/e2e/prod-drilldown.spec.ts --project=chromium-unauth --workers=1
 */

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const EMAIL = process.env.PROD_LEARNER_EMAIL;
const PASSWORD = process.env.PROD_LEARNER_PASSWORD;

test.describe.configure({ mode: 'serial' });
test.skip(!EMAIL || !PASSWORD, 'Set PROD_LEARNER_EMAIL and PROD_LEARNER_PASSWORD env vars.');

test('prod — module drill-down: open first practice item in each module', async ({ page }) => {
  test.setTimeout(360_000);
  const consoleErrors: string[] = [];
  const apiFailures: string[] = [];
  let surface = 'sign-in';

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() !== 'error') return;
    const text = msg.text();
    if (/favicon|beacon|cancel|Failed to load resource/i.test(text)) return;
    consoleErrors.push(`[${surface}] ${text}`);
  });
  page.on('response', (res: Response) => {
    const url = res.url();
    if (!url.includes('/v1/')) return;
    if (res.status() >= 500) apiFailures.push(`[${surface}] ${res.status()} ${url}`);
  });

  // Sign in
  await page.goto(`${PROD_URL}/sign-in`, { waitUntil: 'domcontentloaded' });
  await page.locator('#email').waitFor({ state: 'visible', timeout: 15_000 });
  await page.locator('#email').fill(EMAIL!);
  await page.locator('#password').fill(PASSWORD!);
  await Promise.all([
    page.waitForResponse(
      (r) => /\/v1\/auth\/(sign[-_]?in|login)/i.test(r.url()) && r.request().method() === 'POST',
      { timeout: 20_000 },
    ).catch(() => null),
    page.getByRole('button', { name: /^sign in$/i }).click(),
  ]);
  await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 30_000 });

  // Helper — click first link/card matching href prefix in current page
  const drillInto = async (route: string, hrefPrefix: RegExp, label: string) => {
    surface = `${label}-list`;
    await page.goto(`${PROD_URL}${route}`, { waitUntil: 'domcontentloaded', timeout: 30_000 });
    await page.waitForLoadState('networkidle', { timeout: 12_000 }).catch(() => {});

    const firstLink = page
      .locator(`a[href*="${hrefPrefix.source.replace(/[\\^$.*+?()[\]{}|]/g, '')}"]`)
      .first();

    // Fallback: any link in the main content with the prefix
    const matches = await page.evaluate((prefixSource) => {
      const re = new RegExp(prefixSource);
      const out: string[] = [];
      document.querySelectorAll('a[href]').forEach((a) => {
        const href = (a as HTMLAnchorElement).getAttribute('href') ?? '';
        if (re.test(href)) out.push(href);
      });
      return out.slice(0, 1);
    }, hrefPrefix.source);

    if (matches.length === 0) {
      test.info().annotations.push({ type: `${label}:item`, description: 'no items found' });
      return;
    }
    surface = `${label}-item`;
    const target = matches[0]!.startsWith('http') ? matches[0]! : `${PROD_URL}${matches[0]}`;
    const resp = await page.goto(target, { waitUntil: 'domcontentloaded', timeout: 30_000 })
      .catch(() => null);
    expect(resp?.status() ?? 0, `${label} item ${matches[0]} status`).toBeLessThan(500);
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    test.info().annotations.push({
      type: `${label}:item`,
      description: `opened ${matches[0]} (${resp?.status() ?? '?'})`,
    });
  };

  // Reading: any /reading/paper/<id> or /reading/player/<id> link
  await drillInto('/reading', /\/reading\/(paper|player)\//, 'reading');

  // Listening: /listening/(drills|player|review)/<id>
  await drillInto('/listening', /\/listening\/(drills|player|review|results)\//, 'listening');

  // Writing: /writing/(player|library|model)
  surface = 'writing-list';
  await page.goto(`${PROD_URL}/writing`, { waitUntil: 'domcontentloaded', timeout: 30_000 });
  await page.waitForLoadState('networkidle', { timeout: 12_000 }).catch(() => {});
  // The editor lives at /writing/player. Verify it loads without 5xx.
  surface = 'writing-player';
  const wResp = await page.goto(`${PROD_URL}/writing/player`, {
    waitUntil: 'domcontentloaded',
    timeout: 30_000,
  });
  expect(wResp?.status() ?? 0, 'writing player status').toBeLessThan(500);
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
  // Spot-check editor presence (textarea OR contenteditable surface)
  const editorVisible =
    (await page.locator('textarea, [contenteditable="true"]').first().isVisible().catch(() => false));
  test.info().annotations.push({
    type: 'writing:editor',
    description: editorVisible ? 'editor rendered' : 'editor not detected',
  });

  // Speaking: /speaking/(roleplay|task|phrasing|transcript)/<id>
  await drillInto('/speaking', /\/speaking\/(roleplay|task|phrasing|transcript|results)\//, 'speaking');

  // Mocks: /mocks/<id> or /mocks/player/<id>
  await drillInto('/mocks', /\/mocks\/(player|report)?\/?[^/]+$/, 'mocks');

  // Vocabulary: flashcards screen — verify card UI loads even without items
  surface = 'vocab-flashcards';
  const vfResp = await page.goto(`${PROD_URL}/vocabulary/flashcards`, {
    waitUntil: 'domcontentloaded',
    timeout: 30_000,
  });
  expect(vfResp?.status() ?? 0, 'vocabulary flashcards status').toBeLessThan(500);
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  surface = 'vocab-quiz';
  const vqResp = await page.goto(`${PROD_URL}/vocabulary/quiz`, {
    waitUntil: 'domcontentloaded',
    timeout: 30_000,
  });
  expect(vqResp?.status() ?? 0, 'vocabulary quiz status').toBeLessThan(500);
  await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

  // Lessons: /lessons/<id>
  await drillInto('/lessons', /\/lessons\/[^/]+$/, 'lessons');

  // Grammar: /grammar/<lessonId>
  await drillInto('/grammar', /\/grammar\/[^/]+$/, 'grammar');

  // Pronunciation: /pronunciation/<drillId>
  await drillInto('/pronunciation', /\/pronunciation\/[^/]+$/, 'pronunciation');

  // Conversation: /conversation/<sessionId>
  await drillInto('/conversation', /\/conversation\/[^/]+$/, 'conversation');

  // Final assertions
  expect(consoleErrors, 'no console errors during drill-down').toEqual([]);
  expect(apiFailures, 'no API 5xx during drill-down').toEqual([]);
});
