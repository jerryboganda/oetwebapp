// Browser glue of the Writing production QA: a real learner journey in Playwright against the live app.
// Thin by design: every verdict is a pure function of lib.mjs / geometry.mjs. Single-session rule: a learner is
// signed in by the BROWSER only; node never signs a learner in. Learner API reads go through the signed-in page
// itself (same origin, its cookies, the bearer and device id the page last sent), as speaking-live-voice does.
import { chromium, devices, webkit } from 'playwright';
import {
  APP_URL, BROWSER_API_PREFIX, CONTRACT_GROUPS, DESKTOP_WIDTHS, ENDPOINTS, MOBILE_VIEWPORTS, ROUTES, SELECTORS, TEST_IDS,
} from './contract.mjs';
import {
  clearanceProblems, collectContainment, collectOverlap, containmentProblems, mobileVerdict, overlapProblems,
  positiveControl, probeTarget, targetProblems,
} from './geometry.mjs';
import { contractGaps, correctionsProblems, reportTextProblems, scoreLabelProblems, sectionOrderProblems } from './lib.mjs';

export class ContractMissing extends Error {
  constructor(group, ids) {
    super(`contract ids missing on the live ${group} page: ${ids.join(', ')}`);
    this.ids = ids;
  }
}

const tid = (id) => `[data-testid="${id}"]`;
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// The harness's own navigations abort the AI Assistant hub's SignalR connect; that is noise, not the product
// (copied from scripts/qa/live-voice-served-provider.mjs, which this harness must not import or edit).
const NAVIGATION_ABORT = /(Connection disconnected with error|Failed to start the (transport|connection)|Failed to complete negotiation with the server|\[AI Assistant\] Connection failed)[\s\S]*Failed to fetch/;
export const isNavigationAbortNoise = (text) => NAVIGATION_ABORT.test(String(text));

// The learner user id in a bearer (its "sub" claim), copied from live-voice-served-provider.mjs.
export function learnerIdFromBearer(bearer) {
  try {
    const token = String(bearer ?? '').replace(/^Bearer\s+/i, '');
    const payload = JSON.parse(Buffer.from(token.split('.')[1] ?? '', 'base64url').toString('utf8'));
    return typeof payload.sub === 'string' && payload.sub ? payload.sub : null;
  } catch {
    return null;
  }
}

// Native-shell emulation (init script): the app bootstrap stamps runtimeKind 'web' and may re-stamp it, so an
// observer keeps 'capacitor-native'. window.Capacitor is NOT faked (auth keeps its web code path). Whether the
// app really rendered as native is proven separately (positive control), never assumed.
function stampNativeShell() {
  const root = document.documentElement;
  const stamp = () => {
    if (root.dataset.runtimeKind !== 'capacitor-native') root.dataset.runtimeKind = 'capacitor-native';
    if (root.dataset.capacitorNative !== 'true') root.dataset.capacitorNative = 'true';
  };
  stamp();
  new MutationObserver(stamp).observe(root, { attributes: true, attributeFilter: ['data-runtime-kind', 'data-capacitor-native'] });
}

const browsers = {};
export async function launch(name = 'chromium') {
  browsers[name] ??= await (name === 'webkit' ? webkit : chromium).launch();
  return browsers[name];
}
export async function closeBrowsers() {
  for (const b of Object.values(browsers)) await b.close().catch(() => undefined);
}

/**
 * A learner's browser session. device = a Playwright device name (mobile UI suite) or null (desktop 1366x900).
 * seedClock installs the page clock so a seeded reading window can be fast-forwarded.
 * Returns { context, page, api(path, method), bearer(), userId(), errors, close() }.
 */
export async function openSession({ browserName = 'chromium', device = null, deviceId, nativeShell = false, storageState, seedClock = false, log }) {
  const browser = await launch(browserName);
  const context = await browser.newContext({
    ...(device ? devices[device] : { viewport: { width: 1366, height: 900 } }),
    locale: 'en-GB',
    ...(storageState ? { storageState } : {}),
  });
  await context.addInitScript((id) => { try { localStorage.setItem('oet_device_id', id); } catch { /* cookie fallback */ } }, deviceId);
  if (nativeShell) await context.addInitScript(stampNativeShell);
  const state = { bearer: null, deviceId: null, lastNavAt: 0 };
  const errors = [];
  context.on('request', (r) => {
    if (!r.url().includes(`${BROWSER_API_PREFIX}/v1/`)) return;
    r.headerValue('authorization').then((v) => { if (v) state.bearer = v; }, () => undefined);
    r.headerValue('x-oet-device-id').then((v) => { if (v) state.deviceId = v; }, () => undefined);
  });
  const page = await context.newPage();
  page.setDefaultTimeout(30_000);
  if (seedClock) await page.clock.install();
  page.on('console', (m) => {
    if (m.type() !== 'error') return;
    if (isNavigationAbortNoise(m.text()) && Date.now() - state.lastNavAt < 5_000) return;
    errors.push(m.text().slice(0, 300));
  });
  page.on('framenavigated', () => { state.lastNavAt = Date.now(); });

  async function api(path, method = 'GET', body) {
    for (let i = 0; i < 40 && !state.bearer; i += 1) await sleep(500);
    for (let attempt = 1; ; attempt += 1) {
      try {
        return await page.evaluate(async (call) => {
          const csrf = document.cookie.match(/(?:^|; ?)oet_csrf=([^;]+)/)?.[1];
          const headers = { 'content-type': 'application/json' };
          if (call.bearer) headers.authorization = call.bearer;
          if (call.deviceId) headers['x-oet-device-id'] = call.deviceId;
          if (csrf) headers['x-csrf-token'] = decodeURIComponent(csrf);
          try {
            const res = await fetch(call.url, { method: call.method, credentials: 'include', headers, body: call.body, signal: AbortSignal.timeout(60_000) });
            return { status: res.status, body: await res.json().catch(() => null) };
          } catch (error) {
            return { status: 0, body: null, error: String(error).slice(0, 200) };
          }
        }, { url: `${BROWSER_API_PREFIX}${path}`, method, body: body === undefined ? undefined : JSON.stringify(body), bearer: state.bearer, deviceId: state.deviceId });
      } catch (error) {
        if (attempt >= 3) throw error;
        await sleep(1_000); // a navigation destroyed the page context mid-call
      }
    }
  }
  log?.(['browser session opened:', browserName, device ?? 'desktop', nativeShell ? 'native shell emulated' : ''].join(' ').trim());
  return {
    context, page, api, errors,
    bearer: () => state.bearer,
    userId: () => learnerIdFromBearer(state.bearer),
    close: () => context.close().catch(() => undefined),
  };
}

export const appUrl = (route) => `${APP_URL}${route}`;

/** Browser sign-in (the only way a QA learner is ever signed in). Never screenshots the form. */
export async function signIn(session, { email, password }) {
  const { page } = session;
  await page.goto(appUrl(ROUTES.signIn), { waitUntil: 'domcontentloaded' });
  await page.waitForFunction((selector) => {
    const form = document.querySelector(selector);
    return form && Object.keys(form).some((k) => k.startsWith('__reactProps$') && typeof form[k]?.onSubmit === 'function');
  }, SELECTORS.signInForm, { timeout: 60_000 });
  await page.locator(SELECTORS.signInEmail).first().fill(email);
  await page.locator(SELECTORS.signInPassword).first().fill(password);
  const answer = page.waitForResponse((r) => new URL(r.url()).pathname.endsWith('/sign-in') && r.request().method() === 'POST', { timeout: 60_000 });
  await page.locator(SELECTORS.signInSubmit).first().click();
  const res = await answer;
  if (res.status() !== 200) {
    const json = await res.json().catch(() => null);
    throw new Error(`learner sign-in failed: HTTP ${res.status()} (${json?.errorCode ?? json?.code ?? 'no_code'})`);
  }
  await page.waitForURL((u) => !u.pathname.startsWith(ROUTES.signIn), { timeout: 60_000 });
  for (let i = 0; i < 40 && !session.bearer(); i += 1) await sleep(500);
  if (!session.bearer()) throw new Error('the signed-in page never sent a bearer to the API');
}

export async function presentTestIds(page, ids) {
  const present = [];
  for (const id of ids) if (await page.locator(tid(id)).count()) present.push(id);
  return present;
}

/** Waits (shared deadline) for every id of the group to render; a page that renders late is not "missing". */
export async function requireContract(page, group, ms = 30_000) {
  const deadline = Date.now() + ms;
  const present = [];
  for (const id of CONTRACT_GROUPS[group]) {
    const timeout = Math.max(1_000, deadline - Date.now());
    if (await page.locator(tid(id)).first().waitFor({ state: 'attached', timeout }).then(() => true, () => false)) present.push(id);
  }
  const gaps = contractGaps(group, present);
  if (gaps.length) throw new ContractMissing(group, gaps);
}

/**
 * Failure diagnostics: the page path, title and the first 300 characters of visible text, plus a screenshot of
 * the top of the viewport. On pages that show letter, case-note or model-answer text only headings and
 * alerts are kept.
 */
export async function pageDiagnostics(page, shotPath) {
  if (!page || page.isClosed()) return { page: 'closed' };
  const url = new URL(page.url());
  const sensitive = /\/writing\/(practice\/session|paper\/session|submissions\/[^/]+\/(results|revise))/.test(url.pathname);
  const text = await page.evaluate((onlyHeadings) => {
    const raw = onlyHeadings
      ? [...document.querySelectorAll('h1, h2, [role="alert"], [role="status"], [role="dialog"] h2')].map((e) => e.innerText).join(' | ')
      : document.body.innerText;
    return raw.replace(/\s+/g, ' ').trim().slice(0, 300);
  }, sensitive).catch(() => null);
  const width = page.viewportSize()?.width ?? 1366;
  if (shotPath) await page.screenshot({ path: shotPath, clip: { x: 0, y: 0, width, height: 320 } }).catch(() => undefined);
  return { path: url.pathname + url.search, title: await page.title().catch(() => null), text, textScope: sensitive ? 'headings and alerts only' : 'body' };
}

/** Library -> click the task's own link (as a candidate does) -> the eligibility answer (task-open debit). */
export async function openTaskFromLibrary(session, task) {
  const { page } = session;
  await page.goto(appUrl(ROUTES.library), { waitUntil: 'domcontentloaded' });
  const link = page.locator(`a[href="${ROUTES.practice(task.scenarioId)}"]`).first();
  if (!(await link.waitFor({ state: 'visible', timeout: 30_000 }).then(() => true, () => false))) {
    await page.locator('input[type="search"]').first().fill(task.title);
    if (!(await link.waitFor({ state: 'visible', timeout: 30_000 }).then(() => true, () => false))) {
      throw new Error(`the learner library does not list task ${task.scenarioId} ("${task.title}") for this profession`);
    }
  }
  const eligibility = page.waitForResponse((r) => r.url().includes(ENDPOINTS.eligibility(task.scenarioId)), { timeout: 90_000 });
  await link.click();
  const res = await eligibility;
  if (res.status() !== 200) {
    const json = await res.json().catch(() => null);
    throw new Error(`task open refused: HTTP ${res.status()} (${json?.errorCode ?? json?.code ?? 'no_code'})`);
  }
  await page.waitForURL((u) => u.pathname === ROUTES.practice(task.scenarioId), { timeout: 60_000 });
}

// The timer renders 0 while the task is still loading ("Loading scenario..."): wait until it carries a real
// reading (up to 60 s) before reading it, so a slow load is never judged as a lost timer.
export async function readTimer(page) {
  const timer = page.locator(tid(TEST_IDS.timer)).first();
  await timer.waitFor({ state: 'attached', timeout: 60_000 });
  await page.waitForFunction((sel) => Number(document.querySelector(sel)?.getAttribute('data-seconds-remaining')) > 0, tid(TEST_IDS.timer), { timeout: 60_000 }).catch(() => undefined);
  return { phase: await timer.getAttribute('data-phase'), seconds: Number(await timer.getAttribute('data-seconds-remaining')) };
}

/** Waits out the reading window: real = the live 5-minute clock; seed = the page clock fast-forwarded. */
export async function waitForWritingPhase(session, readingWindow) {
  const { page } = session;
  const timer = await readTimer(page);
  if (readingWindow === 'seed' && timer.phase === 'reading') await page.clock.fastForward(((timer.seconds || 300) + 2) * 1000);
  await page.locator(`${tid(TEST_IDS.timer)}[data-phase="writing"]`).first().waitFor({ state: 'attached', timeout: 9 * 60_000 });
}

/** Types like a candidate: pressSequentially word by word, 45-95 ms per key, caret at the end. Never fill/paste. */
export async function typeText(page, text) {
  const editor = page.locator(SELECTORS.editorInput);
  await editor.click();
  await page.keyboard.press('Control+End');
  for (const chunk of text.match(/\S+\s*/g) ?? []) {
    await editor.pressSequentially(chunk, { delay: 45 + Math.floor(Math.random() * 51) });
  }
}

export const editorText = (page) => page.locator(SELECTORS.editorInput).innerText().then((t) => t.replace(/ /g, ' ').trim());
export const draftState = (page) => page.locator(tid(TEST_IDS.draftStatus)).first().getAttribute('data-state').catch(() => null);

/** Polls the server draft until its content equals `text` (null when it never did within `ms`). */
export async function waitDraftEquals(session, scenarioId, text, ms = 15_000) {
  const deadline = Date.now() + ms;
  let last = null;
  while (Date.now() < deadline) {
    const res = await session.api(ENDPOINTS.draft(scenarioId));
    last = res.body?.content ?? null;
    if (last === text) return { ok: true, content: last };
    await sleep(1_000);
  }
  return { ok: false, content: last };
}

export async function waitDraftState(page, states, ms) {
  const deadline = Date.now() + ms;
  for (;;) {
    const state = await draftState(page);
    if (states.includes(state) || Date.now() > deadline) return state;
    await sleep(500);
  }
}

/** Clicks Submit and returns the new submission id (the page's own POST /v1/writing/submissions). */
export async function submit(session) {
  const { page } = session;
  const created = page.waitForResponse((r) => new URL(r.url()).pathname.endsWith(ENDPOINTS.submissions) && r.request().method() === 'POST', { timeout: 120_000 });
  await page.locator(tid(TEST_IDS.submit)).first().click();
  const res = await created;
  const json = await res.json().catch(() => null);
  if (res.status() >= 300 || !json?.id) throw new Error(`submit failed: HTTP ${res.status()} (${json?.errorCode ?? json?.code ?? 'no_code'})`);
  await page.waitForURL((u) => u.pathname.includes(`/writing/submissions/${json.id}`), { timeout: 60_000 });
  return json.id;
}

export async function gradingSteps(page) {
  const steps = page.locator(tid(TEST_IDS.gradingSteps)).first();
  if (!(await steps.waitFor({ state: 'visible', timeout: 30_000 }).then(() => true, () => false))) return null;
  return steps.locator('li').allInnerTexts();
}

// The harness's own server-truth reads carry this marker so a fault_mode=client page route never rewrites them.
const RAW = '?qa=raw';

/** Polls the submission until graded / failed (or the deadline). */
// afterRetry: a Retry was just pressed; the row still reads `failed` until the server re-queues it, so a
// `failed` read only counts once the row has left `failed` (or after 90 s, when the Retry evidently did nothing).
export async function waitGradeOutcome(session, submissionId, ms = 15 * 60_000, { afterRetry = false } = {}) {
  const started = Date.now();
  const deadline = started + ms;
  let last = null;
  let leftFailed = !afterRetry;
  while (Date.now() < deadline) {
    const res = await session.api(ENDPOINTS.submission(submissionId) + RAW);
    last = res.body;
    if (last?.status && last.status !== 'failed') leftFailed = true;
    if (last?.status === 'graded') return last;
    if (last?.status === 'failed' && (leftFailed || Date.now() - started > 90_000)) return last;
    await sleep(5_000);
  }
  return last ?? { status: 'timeout' };
}

/** Grading facts from the learner's own API (counts and flags only: no letter, model answer or case-note text). */
export async function gradeFacts(session, submissionId, typedText) {
  const [submission, grade, report] = await Promise.all([
    session.api(ENDPOINTS.submission(submissionId) + RAW), session.api(ENDPOINTS.grade(submissionId)), session.api(ENDPOINTS.assessment(submissionId)),
  ]);
  const problems = [];
  const s = submission.body;
  const g = grade.body;
  const r = report.body;
  if (s?.letterContent !== typedText) problems.push(`the saved letter differs from the typed text (${String(s?.letterContent ?? '').length} vs ${typedText.length} chars)`);
  if (grade.status !== 200) problems.push(`the grade read answered HTTP ${grade.status}`);
  if (Object.keys(g?.perCriterion ?? {}).length !== 6) problems.push(`${Object.keys(g?.perCriterion ?? {}).length} scored criteria, expected 6`);
  if (r?.status !== 'CandidateReady' || r?.candidateReportVisible !== true) problems.push(`the report is ${r?.status} (candidate visible: ${r?.candidateReportVisible})`);
  if ((r?.criteria ?? []).length !== 6) problems.push(`the report has ${(r?.criteria ?? []).length} criteria, expected 6`);
  return {
    problems,
    facts: {
      submissionStatus: s?.status, gradeId: g?.id ?? null, reportId: r?.id ?? null, modelUsed: g?.modelUsed ?? null,
      estimatedPracticeScore: r?.estimatedPracticeScore ?? null, errorsCount: (r?.errors ?? []).length,
    },
  };
}

/** Post Submissions UI: the row of this submission, exactly once, in the expected state. */
export async function postSubmissionRow(session, submissionId, state, shot) {
  const { page } = session;
  await page.goto(appUrl(ROUTES.postSubmissions), { waitUntil: 'domcontentloaded' });
  await page.locator(tid(TEST_IDS.postSubmissionsList)).first().waitFor({ state: 'visible', timeout: 60_000 });
  const rows = page.locator(`${tid(TEST_IDS.postSubmissionRow)}[data-submission-id="${submissionId}"]`);
  const count = await rows.count();
  const problems = [];
  if (count !== 1) problems.push(`the Post Submissions page shows the submission ${count} times, expected once`);
  else {
    const shown = await rows.first().getAttribute('data-state');
    if (state && shown !== state) problems.push(`the Post Submissions row reads ${shown}, expected ${state}`);
    if (shot) await rows.first().screenshot({ path: shot }).catch(() => undefined);
  }
  return problems;
}

/** Desktop + report checks on the results page; mobile overlap checks on a second, native-emulated page. */
export async function resultsUiChecks(session, submissionId, facts, { shotPrefix, mobile = true, desktop = true }) {
  const { page, context } = session;
  const problems = [];
  const partials = [];
  await page.goto(appUrl(ROUTES.results(submissionId)), { waitUntil: 'domcontentloaded' });
  await page.locator(tid(TEST_IDS.scorePanel)).first().waitFor({ state: 'visible', timeout: 60_000 });
  if (desktop) {
    for (const width of DESKTOP_WIDTHS) {
      await page.setViewportSize({ width, height: 900 });
      await page.waitForTimeout(400);
      const sample = await page.evaluate(collectContainment, { root: tid(TEST_IDS.scorePanel), tiles: tid(TEST_IDS.scoreStat) });
      problems.push(...containmentProblems(sample).map((p) => `${width}px: ${p}`));
    }
    await page.setViewportSize({ width: 1366, height: 900 });
  }
  if (shotPrefix) await page.locator(tid(TEST_IDS.scorePanel)).first().screenshot({ path: `${shotPrefix}-score.png` }).catch(() => undefined);
  const sections = await page.locator(tid(TEST_IDS.resultSection)).evaluateAll((els) => els.map((e) => e.getAttribute('data-section')));
  problems.push(...sectionOrderProblems(sections));
  problems.push(...scoreLabelProblems(await page.locator(tid(TEST_IDS.estimatedScore)).first().innerText().catch(() => null)));
  const answer = page.locator(tid(TEST_IDS.modelAnswer)).first();
  if (!(await answer.isVisible().catch(() => false))) problems.push('the grounded model answer is not visible');
  else if ((await answer.evaluate((e) => getComputedStyle(e).whiteSpace)) !== 'pre-wrap') problems.push('the model answer does not keep its line breaks (white-space is not pre-wrap)');
  const main = page.locator(SELECTORS.mainContent).first();
  problems.push(...reportTextProblems(await main.innerText(), await main.locator('a[href]').evaluateAll((els) => els.map((e) => e.getAttribute('href')))));
  const criteria = await page.locator(`${tid(TEST_IDS.criteriaList)} > li`).count();
  if (criteria !== 6) problems.push(`the criteria list shows ${criteria} criteria, expected 6`);
  // > 5 errors: 5-item preview + View all (then the full list replaces the preview); <= 5: full list only.
  const viewAll = page.locator(tid(TEST_IDS.correctionsViewAll)).first();
  const expandable = await viewAll.isVisible().catch(() => false);
  const preview = expandable ? await page.locator(`${tid(TEST_IDS.correctionsPreview)} li`).count() : null;
  if (expandable) await viewAll.click();
  const full = await page.locator(`${tid(TEST_IDS.correctionsFullList)} li`).count();
  problems.push(...correctionsProblems({ preview: preview ?? full, full, api: facts.errorsCount, expandable }));
  // A reload keeps the same saved grade and report (the page's own reads after the reload).
  const reread = Promise.all([
    page.waitForResponse((r) => r.url().endsWith(ENDPOINTS.grade(submissionId)) && r.request().method() === 'GET', { timeout: 60_000 }),
    page.waitForResponse((r) => r.url().endsWith(ENDPOINTS.assessment(submissionId)) && r.request().method() === 'GET', { timeout: 60_000 }),
  ]);
  await page.reload({ waitUntil: 'domcontentloaded' });
  const [gradeRes, reportRes] = await reread.catch(() => [null, null]);
  const ids = [await gradeRes?.json().catch(() => null), await reportRes?.json().catch(() => null)].map((b) => b?.id ?? null);
  if (ids[0] !== facts.gradeId || ids[1] !== facts.reportId) problems.push(`a reload changed the grade/report ids (${ids.join(', ')})`);
  if (mobile) {
    const m = await mobileChecks(session, appUrl(ROUTES.results(submissionId)), shotPrefix);
    problems.push(...m.problems);
    partials.push(...m.partials);
  }
  return { problems, partials };
}

/**
 * Mobile overlap at 360/390/430 with native-shell emulation on a separate page of the SAME context (the same
 * session: no second sign-in). Handle: nothing under it at any scroll offset. Bottom nav: nothing under it and
 * >= 8 px clearance at the end of the page (content may scroll behind a fixed nav mid-page). Targets (section
 * headings, View all corrections, next actions, or `targets`) fully clear and top-most after scrollIntoView.
 */
export async function mobileChecks(session, url, shotPrefix, targets = null) {
  // A sign-in without "remember me" keeps its session snapshot in sessionStorage, which is per tab: a new page of
  // the same context starts signed out. Carry the signed-in page's sessionStorage over (as a browser does when it
  // duplicates a tab) so the emulated native-shell page is the SAME session, never a second sign-in.
  const entries = await session.page.evaluate(() => Object.entries(sessionStorage));
  const page = await session.context.newPage();
  await page.addInitScript(({ origin, items }) => {
    if (location.origin !== origin) return;
    for (const [key, value] of items) if (sessionStorage.getItem(key) === null) sessionStorage.setItem(key, value);
  }, { origin: APP_URL, items: entries });
  await page.addInitScript(stampNativeShell);
  const problems = [];
  const partials = [];
  const obstacles = [{ name: 'bottom nav', selector: SELECTORS.bottomNav }, { name: 'handle', selector: SELECTORS.handle }];
  try {
    for (const viewport of MOBILE_VIEWPORTS) {
      const at = `${viewport.width}px`;
      await page.setViewportSize(viewport);
      await page.goto(url, { waitUntil: 'domcontentloaded' });
      if (!(await page.locator(SELECTORS.mainContent).first().waitFor({ state: 'visible', timeout: 60_000 }).then(() => true, () => false))) {
        const diag = await pageDiagnostics(page, shotPrefix ? `${shotPrefix}-mobile-${viewport.width}-failure.png` : null);
        problems.push(`${at}: the native-shell page never showed its content (${JSON.stringify(diag)})`);
        continue;
      }
      await page.waitForTimeout(1_000);
      const handleVisible = await page.locator(SELECTORS.handle).first().isVisible().catch(() => false);
      const found = [];
      let last = null;
      for (const fraction of [0, 0.25, 0.5, 0.75, 1]) {
        last = await page.evaluate(collectOverlap, { content: SELECTORS.mainContent, obstacles, fraction });
        found.push(...overlapProblems(last, ['handle']));
      }
      if (last?.atEnd) found.push(...overlapProblems(last, ['bottom nav']), ...clearanceProblems(last));
      const probeList = targets ?? [
        { selector: `${tid(TEST_IDS.resultSection)} :is(h1, h2, h3, h4)`, label: 'section heading' },
        { selector: tid(TEST_IDS.correctionsViewAll), label: 'View all corrections' },
        { selector: `${tid(TEST_IDS.resultSection)}[data-section="next-actions"] :is(a[href], button)`, label: 'next action' },
      ];
      const probes = [];
      for (const t of probeList) {
        const count = await page.locator(t.selector).count();
        for (let index = 0; index < count; index += 1) probes.push(await page.evaluate(probeTarget, { selector: t.selector, index, label: `${t.label} ${index + 1}` }));
      }
      found.push(...targetProblems(probes, last?.obstacles ?? []));
      if (shotPrefix) {
        await page.evaluate(collectOverlap, { content: SELECTORS.mainContent, obstacles, fraction: 1 });
        await page.screenshot({ path: `${shotPrefix}-mobile-${viewport.width}.png`, clip: { x: 0, y: viewport.height - 220, width: viewport.width, height: 220 } }).catch(() => undefined);
      }
      // Positive control (last, it opens the menu): below lg the native-only mobile-menu entries must render.
      let menuEntries = 0;
      const menuButton = page.locator(SELECTORS.mobileMenuButton).first();
      if (await menuButton.isVisible().catch(() => false)) {
        await menuButton.click();
        for (const id of [TEST_IDS.menuReloadApp, TEST_IDS.menuCheckUpdates]) {
          if (await page.locator(tid(id)).first().waitFor({ state: 'visible', timeout: 5_000 }).then(() => true, () => false)) menuEntries += 1;
        }
        await menuButton.click().catch(() => undefined);
      }
      const verdict = mobileVerdict({ control: positiveControl({ width: viewport.width, handleVisible, menuEntries }), problems: found });
      problems.push(...[...new Set(found)].map((p) => `${at}: ${p}`));
      if (verdict === 'NOT_PROVEN') partials.push(`${at}: mobile overlap NOT PROVEN (native-shell emulation not confirmed: the native mobile-menu entries did not render)`);
    }
  } finally {
    await page.close().catch(() => undefined);
  }
  return { problems, partials };
}

/** fault_mode=client: the page sees its submission as failed (server untouched) until `release()`. */
export async function fakeFailedStatus(page, submissionId) {
  const pattern = new RegExp(`${BROWSER_API_PREFIX.replaceAll('/', '\\/')}\\/v1\\/writing\\/submissions\\/${submissionId}(\\?|$)`);
  const handler = async (route) => {
    if (route.request().method() !== 'GET' || route.request().url().includes(RAW)) return route.fallback();
    const res = await route.fetch();
    const json = await res.json().catch(() => ({}));
    return route.fulfill({ response: res, json: { ...json, status: 'failed', canRetry: true, failureCode: 'grading_delayed', autoRetrying: false } });
  };
  await page.route(pattern, handler);
  return { release: () => page.unroute(pattern, handler) };
}
