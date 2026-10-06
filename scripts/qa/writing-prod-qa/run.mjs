// Writing AI production QA harness (WAI-10). Run ONLY by .github/workflows/writing-prod-qa.yml, never locally.
//   node run.mjs check-inputs   validate the dispatch inputs + the 36 scripts (no network)
//   node run.mjs run            the suites (discover is strictly read-only)
//   node run.mjs safety-net     always(): deactivate QA fault flags, clean up learners per policy
// Inputs arrive as env vars (SUITE, PROFESSIONS, ...; see parseInputs). Admin access: OET_ADMIN_EMAIL /
// OET_ADMIN_PASSWORD. Output: $QA_OUT_DIR/evidence (tables, JSON facts; no letter/model-answer/case-note text)
// and $QA_OUT_DIR/media (clipped screenshots). Every verdict is a pure function of lib.mjs / geometry.mjs.
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as api from './api.mjs';
import { CREDITS_PER_LETTER, ENDPOINTS, PROVIDERS, ROUTES, TEST_IDS } from './contract.mjs';
import {
  buildTable, categoryForLetterType, createLane, creditPreflight, creditVerdict, deriveDeviceId, faultSideEffects,
  freeSampleProblems, gradingStepsProblems, guardDecision, myWorkProblems, overallVerdict, pacingDelayMs,
  paidSpendProblems, parseInputs, planDiscovery, planRows, preflightDecision, providerEvidence, scriptFor,
  matchLetters, splitText, suiteRuns, syntheticEmail, timerVerdict, validateLetters, validateScripts, verdictOf, writingHealth,
} from './lib.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const OUT = process.env.QA_OUT_DIR || 'writing-prod-qa-out';
const EVIDENCE = path.join(OUT, 'evidence');
const MEDIA = path.join(OUT, 'media');
const STATE_FILE = path.join(OUT, 'run-state.json');
const RUN_ID = process.env.GITHUB_RUN_ID || 'adhoc';
const RUN_KEY = `${RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT || '1'}`;
const CREDITS_PER_PLANNED_LETTER = 4; // 2 per letter + headroom for one VOID_DEPLOY re-run
const log = (...args) => console.log(new Date().toISOString().slice(11, 19), ...args);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const tid = (id) => `[data-testid="${id}"]`;
const slug = (s) => String(s).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

class Blocked extends Error {}

function writeJson(name, value) {
  fs.mkdirSync(path.dirname(path.join(EVIDENCE, name)), { recursive: true });
  fs.writeFileSync(path.join(EVIDENCE, name), `${JSON.stringify(value, null, 2)}\n`);
}
const readState = () => (fs.existsSync(STATE_FILE) ? JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')) : null);
const saveState = (state) => fs.writeFileSync(STATE_FILE, `${JSON.stringify(state, null, 2)}\n`);

function loadScripts() {
  const doc = JSON.parse(fs.readFileSync(path.join(here, 'scripts.json'), 'utf8'));
  const check = validateScripts(doc);
  if (!check.ok) throw new Error(`scripts.json is invalid: ${check.problems.join('; ')}`);
  return doc.scripts;
}

// suite=letters: realistic candidate letters (mixed major/minor defects), each pinned to its own production scenario.
function loadLetters() {
  const doc = JSON.parse(fs.readFileSync(path.join(here, 'realistic-letters.json'), 'utf8'));
  const check = validateLetters(doc);
  if (!check.ok) throw new Error(`realistic-letters.json is invalid: ${check.problems.join('; ')}`);
  return doc.letters;
}

// Is a production deploy running or queued? (gh CLI with the job's token; unknown = not busy, logged.)
function deployBusy() {
  try {
    const out = execFileSync('gh', ['run', 'list', '--repo', process.env.GITHUB_REPOSITORY, '--workflow', 'production-deploy.yml', '--limit', '20',
      '--json', 'status', '--jq', '[.[] | select(.status != "completed")] | length'], { encoding: 'utf8' });
    return Number(out.trim()) > 0;
  } catch (error) {
    log('deploy check failed (treated as not busy):', String(error.message).slice(0, 120));
    return false;
  }
}

// ---- Run context ----------------------------------------------------------------------------------------------------

function createContext(inputs, scripts) {
  const password = `Qa-${randomBytes(18).toString('base64url')}`;
  console.log(`::add-mask::${password}`);
  const state = { runKey: RUN_KEY, cleanup: inputs.cleanup, learners: [], flags: [], finished: false, success: false };
  saveState(state);
  return {
    inputs, scripts, password, state,
    admin: api.createAdminClient({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD, readOnly: inputs.suite === 'discover' }),
    labels: Object.fromEntries(scripts.map((s) => [s.profession, s.label])),
    tables: { qa2: [], acceptance: [], ui: [] },
    lane: null,
    halted: null,
    deploy: { busy: false, epoch: 0 },
    paidSpend: new Set(),
    contractMissing: {},
    startedAt: Date.now(),
  };
}

async function provisionLearner(ctx, { key, professionId, letters }) {
  const email = syntheticEmail(RUN_KEY, key);
  const created = await api.createLearner(ctx.admin, { email, name: `WQA ${key} ${RUN_KEY}`, professionId, password: ctx.password });
  ctx.state.learners.push({ key, userId: created.userId, email, purged: false });
  saveState(ctx.state);
  if (letters > 0) {
    // The adjust is an absolute set, so one retry is safe. Two 500s = a server defect: record the body, BLOCKED.
    const set = () => api.setWritingCredits(ctx.admin, created.userId, letters * CREDITS_PER_PLANNED_LETTER, new Date(Date.now() + 3 * 86_400_000).toISOString());
    await set().catch(async (first) => {
      log(`credit adjust for ${key} failed (${first.message}; ${first.detail ?? 'no detail'}); retrying once in 5 s`);
      await sleep(5_000);
      await set().catch((second) => {
        throw new Blocked(`credit adjust failed twice: ${first.message} [${first.detail ?? 'no detail'}] then ${second.message} [${second.detail ?? 'no detail'}]`);
      });
      log(`credit adjust for ${key} succeeded on the retry (first failure: ${first.message})`);
    });
  }
  log(`provisioned ${key} learner ${created.userId}`);
  return { ...created, key, letters, deviceId: deriveDeviceId(`${RUN_KEY}:${key}`), lastScoringAt: 0 };
}

async function startSession(ctx, learner, options = {}) {
  const b = await import('./browser.mjs');
  const session = await b.openSession({
    browserName: options.browserName ?? 'chromium', device: options.device ?? null, deviceId: learner.deviceId,
    nativeShell: Boolean(options.nativeShell), storageState: options.storageState, seedClock: Boolean(options.seedClock), log,
  });
  session.reSignedIn = false;
  if (options.storageState) {
    // A reopened browser keeps cookies + localStorage but not sessionStorage, where a sign-in without
    // "remember me" keeps its session snapshot. If the app sends us to sign-in, sign in again on the SAME device
    // (what a candidate does) and record it.
    await session.page.goto(b.appUrl('/writing'), { waitUntil: 'domcontentloaded' });
    const signedOut = await session.page.waitForURL((u) => u.pathname.startsWith(ROUTES.signIn), { timeout: 15_000 }).then(() => true, () => false);
    for (let i = 0; i < 20 && !signedOut && !session.bearer(); i += 1) await sleep(500);
    if (signedOut || !session.bearer()) {
      session.reSignedIn = true;
      await b.signIn(session, { email: learner.email, password: ctx.password });
    }
  } else await b.signIn(session, { email: learner.email, password: ctx.password });
  for (let i = 0; i < 40 && !session.bearer(); i += 1) await sleep(500);
  if (session.userId() && session.userId() !== learner.userId) throw new Error(`the browser is signed in as ${session.userId()}, not ${learner.userId}`);
  return session;
}

/** Proves the learner is funded the way the credit assertions need (C0 preflight). */
async function verifyFunding(ctx, session, learner, kind) {
  const [entitlement, mine] = await Promise.all([session.api(ENDPOINTS.entitlement), session.api(ENDPOINTS.myCredits)]);
  const reasons = creditPreflight(mine.body, entitlement.body, { letters: learner.letters, kind });
  if (reasons.length) throw new Blocked(`credit preflight: ${reasons.join('; ')}`);
}

async function pace(ctx, learner) {
  const wait = pacingDelayMs(learner.lastScoringAt, Date.now(), ctx.inputs.paceSeconds);
  if (wait) await sleep(wait);
  learner.lastScoringAt = Date.now();
}

// ---- Guard loop -----------------------------------------------------------------------------------------------------

function startGuard(ctx) {
  const tick = async () => {
    try {
      const [inputs, anthropic] = await Promise.all([api.readHealthInputs(ctx.admin), api.usageRows(ctx.admin, { providerId: PROVIDERS.api })]);
      const recent = anthropic.filter((r) => Date.parse(r.createdAt) >= ctx.startedAt);
      for (const r of recent) ctx.paidSpend.add(r.id);
      const decision = guardDecision({ deployBusy: deployBusy(), health: writingHealth(inputs), anthropicRows: recent });
      if (decision.action === 'halt' && !ctx.halted) {
        ctx.halted = decision.reasons.join('; ');
        log(`GUARD HALT: ${ctx.halted}`);
      }
      if (decision.action === 'pause' && !ctx.deploy.busy) ctx.deploy.epoch += 1;
      ctx.deploy.busy = decision.action === 'pause';
    } catch (error) {
      log('guard read failed:', error.message);
    }
  };
  const timer = setInterval(tick, 45_000);
  return { tick, stop: () => clearInterval(timer) };
}

/**
 * Runs one test row. fn(t) fills t.problems (FAIL), t.partials (not proven live), t.blocked, t.notes and the
 * table cells (saved, provider, fallback). Halted runs report NOT_RUN; a deploy during the test = VOID_DEPLOY.
 */
async function runTest(ctx, row, needs, fn) {
  if (ctx.halted) return { ...row, status: 'NOT_RUN', notes: `not started: run halted (${ctx.halted})` };
  const missing = needs.filter((group) => ctx.contractMissing[group]);
  if (missing.length) return { ...row, status: 'BLOCKED', notes: missing.map((g) => ctx.contractMissing[g]).join('; ') };
  // Peers deploy: wait (up to 100 min) for production to settle, then carry on with this test.
  for (let waited = 0; ctx.deploy.busy && waited < 100 * 60_000; waited += 30_000) {
    await sleep(30_000);
    if (waited % (5 * 60_000) === 0) await ctx.guard?.tick();
  }
  if (ctx.deploy.busy) return { ...row, status: 'NOT_RUN', notes: 'not started: production deploys kept running for more than 100 minutes' };
  const epoch = ctx.deploy.epoch;
  // t.track(session) names the page the test is on, for the failure diagnostics below.
  const t = { problems: [], partials: [], blocked: [], notes: [], saved: row.saved ?? '-', provider: row.provider ?? '-', fallback: row.fallback ?? '-', page: null };
  t.track = (session) => { t.page = session?.page ?? null; };
  const started = Date.now();
  const name = slug([row.profession, row.category, row.task].join(' '));
  try {
    await fn(t);
  } catch (error) {
    if (error.ids) { // ContractMissing
      t.blocked.push(error.message);
      for (const group of needs) if (!ctx.contractMissing[group] && error.message.includes(`live ${group} page`)) ctx.contractMissing[group] = error.message;
    } else if (error instanceof Blocked) t.blocked.push(error.message);
    else t.problems.push(`error: ${String(error.message).replace(/\s+/g, ' ').slice(0, 400)}`);
  }
  let status = verdictOf(t);
  if (ctx.deploy.epoch !== epoch || ctx.deploy.busy) status = 'VOID_DEPLOY';
  let diagnostics = null;
  if (status !== 'PASS' && t.page) {
    const b = await import('./browser.mjs');
    diagnostics = await b.pageDiagnostics(t.page, path.join(MEDIA, `failure-${name}.png`)).catch((e) => ({ error: e.message }));
  }
  const notes = [...t.notes, ...t.problems, ...t.partials, ...t.blocked].join('; ') || 'all checks passed';
  const result = { ...row, saved: t.saved, provider: t.provider, fallback: t.fallback, status, notes };
  writeJson(`tests/${name}.json`, { ...result, seconds: Math.round((Date.now() - started) / 1000), diagnostics });
  log(`${status} ${row.profession} / ${row.category}: ${notes.slice(0, 400)}`);
  return result;
}

// ---- One graded letter (shared by matrix, acceptance and ui) ------------------------------------------------------

/**
 * Submit -> grading steps -> graded -> facts -> provider evidence -> results UI -> Post Submissions -> replay
 * retry-grade no-op -> credit rule. The page must be on the practice session with `text` already typed and
 * saved. opts: { kind ('paid'|'free_sample'), expectedFirst, faultFlag, c0, c1, usageBefore, ui: {desktop,mobile},
 * clientFault, retryWhere ('grading'|'post-submissions'), beforeRetry(async) }
 */
async function submitAndVerify(ctx, firstSession, learner, task, text, t, opts) {
  const b = await import('./browser.mjs');
  let session = firstSession; // onFailed may hand back a NEW session (S6: a new device session after 2 min)
  let page = session.page;
  t.track(session);
  const shot = path.join(MEDIA, slug(`${learner.key}-${task.scenarioId}`));
  let failedOnce = false;
  const outcome = await ctx.lane.run(async () => {
    await pace(ctx, learner);
    const submissionId = await b.submit(session);
    const fake = opts.clientFault ? await b.fakeFailedStatus(page, submissionId) : null;
    // The step list renders only while the run is in progress; a run a fault flag fails at once shows the
    // failure card instead, so the steps are judged on runs that are expected to grade.
    if (!opts.expectFailure) {
      if (!ctx.contractChecked?.grading) {
        await b.requireContract(page, 'grading');
        ctx.contractChecked = { ...ctx.contractChecked, grading: true };
      }
      const steps = await b.gradingSteps(page);
      if (steps === null) t.partials.push('grading steps not captured (the page left the grading view first)');
      else t.problems.push(...gradingStepsProblems(steps));
      await page.locator(tid(TEST_IDS.gradingSteps)).first().screenshot({ path: `${shot}-grading.png` }).catch(() => undefined);
    }
    let result = await b.waitGradeOutcome(session, submissionId);
    if (opts.clientFault) result = { ...result, status: 'failed' };
    if (result.status === 'failed') {
      failedOnce = true;
      if (!opts.expectFailure) t.problems.push('grading failed visibly (status failed) on the first run');
      else await b.requireContract(page, 'gradingFailure', 60_000);
      const replaced = opts.onFailed ? await opts.onFailed(submissionId) : null;
      if (replaced) { session = replaced; page = session.page; t.track(session); }
      await pace(ctx, learner);
      const retry = page.locator(tid(TEST_IDS.gradingRetry)).first();
      const retryVisible = await retry.isVisible().catch(() => false);
      await fake?.release().catch(() => undefined);
      if (opts.retryWhere === 'post-submissions') await opts.retryOnRow(session, submissionId);
      else if (retryVisible) await retry.click();
      else {
        t.problems.push('no visible Retry on the grading page');
        await session.api(ENDPOINTS.retryGrade(submissionId), 'POST', {});
      }
      result = await b.waitGradeOutcome(session, submissionId, 15 * 60_000, { afterRetry: true });
    } else if (opts.expectFailure && !opts.clientFault) t.problems.push(`the fault flag did not fail the first run (status ${result.status})`);
    return { submissionId, result };
  });
  const { submissionId, result } = outcome;
  t.notes.push(`submission ${submissionId}`);
  if (result.status !== 'graded') { t.problems.push(`grading did not complete (status ${result.status})`); return { submissionId }; }
  if (failedOnce) t.notes.push('Retry recovered the same submission');
  const opened = await page.waitForURL((u) => u.pathname === ROUTES.results(submissionId), { timeout: 20_000 }).then(() => true, () => false);
  if (!opened && !opts.retryWhere) t.problems.push('the results page did not open within 20 s of graded');

  const { problems, facts } = await b.gradeFacts(session, submissionId, text);
  t.problems.push(...problems);
  const usage = (await api.usageRows(ctx.admin, { userId: learner.userId })).filter((r) => !opts.usageBefore.has(r.id));
  const evidence = providerEvidence({ usage, modelUsed: facts.modelUsed, expectedFirst: opts.expectedFirst ?? PROVIDERS.claude, graded: facts.submissionStatus === 'graded' });
  if (evidence.reused) t.notes.push(evidence.note);
  for (const r of usage) if (r.providerId === PROVIDERS.api) ctx.paidSpend.add(r.id);
  t.problems.push(...evidence.problems);
  t.provider = evidence.reused ? `reused grade (${evidence.finalModel})` : evidence.finalProvider ? `${evidence.finalProvider} / ${evidence.finalModel}` : 'none';
  t.fallback = evidence.fallback ? `yes: ${evidence.fallbackReasons.join(', ')}` : 'no';
  if (opts.faultFlag) t.notes.push(`fault flag ${opts.faultFlag} (first provider row ${evidence.firstProvider})`);

  if (!ctx.contractChecked?.results) {
    await page.goto(b.appUrl(ROUTES.results(submissionId)), { waitUntil: 'domcontentloaded' });
    await page.locator(tid(TEST_IDS.scorePanel)).first().waitFor({ state: 'visible', timeout: 60_000 }).catch(() => undefined);
    await b.requireContract(page, 'results');
    ctx.contractChecked = { ...ctx.contractChecked, results: true };
  }
  const ui = await b.resultsUiChecks(session, submissionId, facts, { shotPrefix: shot, mobile: opts.ui?.mobile !== false, desktop: opts.ui?.desktop !== false });
  t.problems.push(...ui.problems);
  t.partials.push(...ui.partials);

  // Post Submissions: API (exactly once, graded) and UI row.
  const work = await session.api(ENDPOINTS.myWork);
  const apiRow = myWorkProblems({ items: work.body?.items, submissionId, state: 'graded' });
  // A zero-debit letter is only right when it IS the bound free sample; every other letter keeps the 2-credit rule.
  const workItem = (work.body?.items ?? []).find((i) => String(i.submissionId).toLowerCase() === String(submissionId).toLowerCase());
  t.notes.push(`credit rule: ${opts.kind ?? 'paid'} (my-work isFreeSample=${workItem?.isFreeSample ?? 'n/a'})`);
  if ((opts.kind ?? 'paid') === 'free_sample' && workItem && workItem.isFreeSample !== true) t.problems.push('free-sample task, but the submission is not bound to the free sample (isFreeSample false)');
  if ((opts.kind ?? 'paid') === 'paid' && workItem?.isFreeSample === true) t.problems.push('the submission is bound to the free sample although the task was not the offered sample');
  if (!ctx.contractChecked?.postSubmissions) {
    await page.goto(b.appUrl(ROUTES.postSubmissions), { waitUntil: 'domcontentloaded' });
    await page.locator(tid(TEST_IDS.postSubmissionsList)).first().waitFor({ state: 'visible', timeout: 60_000 }).catch(() => undefined);
    await b.requireContract(page, 'postSubmissions');
    ctx.contractChecked = { ...ctx.contractChecked, postSubmissions: true };
  }
  const uiRow = await b.postSubmissionRow(session, submissionId, 'graded', `${shot}-post-submissions.png`);
  t.problems.push(...apiRow, ...uiRow);
  t.saved = !apiRow.length && !uiRow.length ? 'yes (UI + API)' : apiRow.length && uiRow.length ? 'no' : uiRow.length ? 'API only' : 'UI only';

  // Replay: asking again for a graded letter is a no-op (same grade, no new usage, no ledger change).
  const ledgerBefore = ctx.inputs.verifyCredits ? await api.creditSnapshot(ctx.admin, learner.userId) : null;
  const usageCount = (await api.usageRows(ctx.admin, { userId: learner.userId })).length;
  await pace(ctx, learner);
  const replay = await session.api(ENDPOINTS.retryGrade(submissionId), 'POST', {});
  const gradeAfter = await session.api(ENDPOINTS.grade(submissionId));
  if (![200, 202].includes(replay.status) || replay.body?.id !== submissionId) t.problems.push(`replayed retry-grade answered HTTP ${replay.status}`);
  if (gradeAfter.body?.id !== facts.gradeId) t.problems.push('replayed retry-grade changed the grade');
  if ((await api.usageRows(ctx.admin, { userId: learner.userId })).length !== usageCount) t.problems.push('replayed retry-grade made a new grading call');

  if (ctx.inputs.verifyCredits) {
    const after = await api.creditSnapshot(ctx.admin, learner.userId);
    if (ledgerBefore && JSON.stringify(after.transactions) !== JSON.stringify(ledgerBefore.transactions)) t.problems.push('replayed retry-grade changed the credit ledger');
    const verdict = creditVerdict({
      kind: opts.kind ?? 'paid', userId: learner.userId, scenarioId: task.scenarioId, submissionId, before: opts.c0, after,
      steps: opts.c1 ? [{ name: 'after task open', snapshot: opts.c1, delta: opts.kind === 'free_sample' ? 0 : -CREDITS_PER_LETTER }] : [],
    });
    t.problems.push(...verdict.problems.map((p) => `credits: ${p}`));
    writeJson(`credits/${slug(learner.key + " " + submissionId)}.json`, verdict);
  } else t.partials.push('credits not verified (verify_credits=false)');
  writeJson(`facts/${slug(learner.key + " " + submissionId)}.json`, { facts, provider: evidence, usage });
  return { submissionId, facts };
}

/**
 * A fresh learner still holds its profession's free sample: opening THAT task is granted by the sample before any
 * credit is looked at (WritingEntitlementService.AuthorizeStartAsync), so its credit rule is the free-sample one.
 */
async function creditKindFor(session, scenarioId) {
  const offers = (await session.api(ENDPOINTS.freeSamples)).body;
  const offer = (Array.isArray(offers) ? offers : []).find((o) => String(o.contentId).toLowerCase() === String(scenarioId).toLowerCase());
  return offer && ['available', 'retry_available', 'grading_failed', 'in_progress'].includes(offer.state) ? 'free_sample' : 'paid';
}

/** Opens a task like a candidate and types the script (C0/C1 ledger snapshots around the task-open debit). */
async function openAndType(ctx, session, learner, task, text, t, { readingWindow }) {
  const b = await import('./browser.mjs');
  t.track(session);
  const kind = await creditKindFor(session, task.scenarioId);
  if (kind === 'free_sample') t.notes.push("this task is the learner's free sample, so no credit is charged");
  const c0 = ctx.inputs.verifyCredits ? await api.creditSnapshot(ctx.admin, learner.userId) : null;
  const usageBefore = new Set((await api.usageRows(ctx.admin, { userId: learner.userId })).map((r) => r.id));
  await b.openTaskFromLibrary(session, task);
  const c1 = ctx.inputs.verifyCredits ? await api.creditSnapshot(ctx.admin, learner.userId) : null;
  if (!ctx.contractChecked?.editor) {
    await b.requireContract(session.page, 'editor');
    ctx.contractChecked = { ...ctx.contractChecked, editor: true };
  }
  await b.waitForWritingPhase(session, readingWindow);
  if (readingWindow === 'seed') t.notes.push('reading window seeded (page clock fast-forward)');
  if (text) {
    await b.typeText(session.page, text);
    const draft = await b.waitDraftEquals(session, task.scenarioId, text, 15_000);
    if (!draft.ok) t.problems.push('the server draft did not equal the typed text within 15 s');
  }
  return { c0, c1, usageBefore, kind };
}

// ---- Suites ---------------------------------------------------------------------------------------------------------

const qa2Row = (ctx, prof, pick) => {
  const script = scriptFor(ctx.scripts, prof.profession, pick.category);
  return { profession: ctx.labels[prof.profession], task: `${pick.letterType} ${pick.scenarioId}`, category: `${script.title} (${pick.category})`, notes: pick.fallback ?? '' };
};

async function matrixSuite(ctx, plan) {
  const queue = plan.filter((p) => p.enabled && p.picks.length);
  const professionRun = async (prof) => {
    const rows = prof.picks.map((pick) => qa2Row(ctx, prof, pick));
    let learner;
    let session;
    try {
      learner = await provisionLearner(ctx, { key: prof.profession, professionId: prof.catalogId, letters: prof.picks.length });
      session = await startSession(ctx, learner, { seedClock: ctx.inputs.readingWindow === 'seed' });
      if (ctx.inputs.verifyCredits) await verifyFunding(ctx, session, learner, 'paid');
    } catch (error) {
      const status = error instanceof Blocked ? 'BLOCKED' : 'FAIL';
      for (const row of rows) ctx.tables.qa2.push({ ...row, status, notes: `learner setup: ${error.message}` });
      await session?.close();
      return;
    }
    for (const [i, pick] of prof.picks.entries()) {
      const script = scriptFor(ctx.scripts, prof.profession, pick.category);
      const attempt = () => runTest(ctx, rows[i], ['editor', 'grading', 'results', 'postSubmissions'], async (t) => {
        const opened = await openAndType(ctx, session, learner, pick, script.text, t, { readingWindow: ctx.inputs.readingWindow });
        await submitAndVerify(ctx, session, learner, pick, script.text, t, { kind: 'paid', ...opened });
      });
      let result = await attempt();
      if (result.status === 'VOID_DEPLOY' && !ctx.halted) {
        log(`re-queueing ${rows[i].category} once after a deploy`);
        result = await attempt();
      }
      ctx.tables.qa2.push(result);
    }
    await session.close();
  };
  const worker = async () => { while (queue.length) await professionRun(queue.shift()); };
  await Promise.all(Array.from({ length: Math.min(ctx.inputs.concurrency, queue.length) }, worker));
}

/**
 * Owner review 5 Oct 2026: realistic mixed major/minor letters through the real grader. Per letter the report must keep
 * three distinct priorities, short criterion cards, no internal labels and no "Exemplar" (gradeFacts + resultsUiChecks).
 */
async function lettersSuite(ctx, plan) {
  const { runs, blocked } = matchLetters(plan, ctx.letters);
  const letterRow = (letter, extra = {}) => ({
    profession: ctx.labels[letter.profession] ?? letter.profession, task: `${letter.letterType} ${letter.scenarioId}`,
    category: `realistic letter ${letter.id}`, notes: '', ...extra,
  });
  for (const { letter, reason } of blocked) ctx.tables.qa2.push(letterRow(letter, { status: 'BLOCKED', notes: reason }));
  const queue = [...runs];
  const professionRun = async ({ profession, letters }) => {
    const rows = letters.map(({ letter }) => letterRow(letter));
    let learner;
    let session;
    try {
      learner = await provisionLearner(ctx, { key: profession.profession, professionId: profession.catalogId, letters: letters.length });
      session = await startSession(ctx, learner, { seedClock: ctx.inputs.readingWindow === 'seed' });
      if (ctx.inputs.verifyCredits) await verifyFunding(ctx, session, learner, 'paid');
    } catch (error) {
      const status = error instanceof Blocked ? 'BLOCKED' : 'FAIL';
      for (const row of rows) ctx.tables.qa2.push({ ...row, status, notes: `learner setup: ${error.message}` });
      await session?.close();
      return;
    }
    for (const [i, { letter, task }] of letters.entries()) {
      const attempt = () => runTest(ctx, rows[i], ['editor', 'grading', 'results', 'postSubmissions'], async (t) => {
        const opened = await openAndType(ctx, session, learner, task, letter.text, t, { readingWindow: ctx.inputs.readingWindow });
        const done = await submitAndVerify(ctx, session, learner, task, letter.text, t, { kind: 'paid', ...opened, ui: { desktop: false, mobile: false } });
        t.partials.push(...(done.facts?.severityMix ?? []));
      });
      let result = await attempt();
      if (result.status === 'VOID_DEPLOY' && !ctx.halted) {
        log(`re-queueing ${rows[i].category} once after a deploy`);
        result = await attempt();
      }
      ctx.tables.qa2.push(result);
    }
    await session.close();
  };
  const worker = async () => { while (queue.length) await professionRun(queue.shift()); };
  await Promise.all(Array.from({ length: Math.min(ctx.inputs.concurrency, queue.length) }, worker));
}

async function readWindows(session, scenarioId) {
  const res = await session.api(ENDPOINTS.scenario(scenarioId));
  return { reading: Number(res.body?.readingTimeSeconds) || 300, writing: Number(res.body?.writingTimeSeconds) || 2400 };
}

/** P0-3 acceptance on a paid medicine learner (S1-S7, reading-resume, L1+L2 failover) and a free-sample learner (S8). */
async function acceptanceSuite(ctx, plan) {
  const b = await import('./browser.mjs');
  const medicine = plan.find((p) => p.profession === 'medicine');
  const rows = [];
  const add = (row) => { rows.push(row); ctx.tables.acceptance.push(row); };
  const base = (category) => ({ profession: 'Medicine (acceptance)', task: '-', category });
  if (!medicine?.enabled) {
    add({ ...base('P0-3 acceptance'), status: 'BLOCKED', notes: 'Medicine is not enabled, so there is no task to run acceptance on' });
    return;
  }
  const pickOf = (category) => medicine.picks.find((p) => p.category === category) ?? medicine.picks[0];
  // A scenario block starts on a fresh sign-in so a broken page in one block cannot cascade into the next.
  const freshSession = async (old) => {
    await old?.close();
    return startSession(ctx, learner);
  };
  const learner = await provisionLearner(ctx, { key: 'medicine-acc', professionId: medicine.catalogId, letters: 3 });
  let session = await startSession(ctx, learner);
  if (ctx.inputs.verifyCredits) await verifyFunding(ctx, session, learner, 'paid');
  const editorNeeds = ['editor', 'postSubmissions'];

  // ---- S1-S5 on ONE attempt (routine task), typed in five parts; then graded ----
  const task = pickOf('routine');
  const script = scriptFor(ctx.scripts, 'medicine', task.category);
  const parts = splitText(script.text, 5);
  let typed = '';
  let chain = null;
  let windows = null;
  let writingStartedAt = 0;
  const chainStep = async (category, fn) => {
    const row = { ...base(category), task: `${task.letterType} ${task.scenarioId}` };
    if (chain?.broken) { add({ ...row, status: 'NOT_RUN', notes: `not run: ${chain.broken} did not complete` }); return; }
    const result = await runTest(ctx, row, editorNeeds, (t) => { t.track(session); return fn(t); });
    if (!['PASS', 'PARTIAL'].includes(result.status)) chain = { broken: category };
    add(result);
  };
  const typePart = async (t, part) => {
    await b.typeText(session.page, part);
    typed += part;
  };
  const timerNow = async () => (await b.readTimer(session.page)).seconds;
  const expectText = async (t, label) => {
    await session.page.locator('div.ProseMirror#practice-editor').waitFor({ state: 'visible', timeout: 60_000 });
    const shown = await b.editorText(session.page);
    if (shown !== typed.trim()) t.problems.push(`${label}: the editor shows ${shown.length} chars, ${typed.trim().length} were typed`);
  };
  let opened = null;
  await chainStep('P0-3 S1 refresh within 5 s of the last keystroke', async (t) => {
    opened = await openAndType(ctx, session, learner, task, '', t, { readingWindow: 'real' });
    windows = await readWindows(session, task.scenarioId);
    writingStartedAt = Date.now();
    const [first, second = ''] = splitText(parts[0], 2);
    await typePart(t, first);
    await sleep(Math.max(0, writingStartedAt + 45_000 - Date.now())); // >= 45 s of the window used, so a reset is visible
    await typePart(t, second);
    const before = await timerNow(); // then refresh at once: well within 5 s of the last keystroke
    await session.page.reload({ waitUntil: 'domcontentloaded' });
    await expectText(t, 'after the refresh');
    const v = timerVerdict({ before, after: await timerNow(), window: windows.writing });
    t.problems.push(...v.problems);
  });
  await chainStep('P0-3 S2 close the browser, reopen after 30 s', async (t) => {
    await typePart(t, parts[1]);
    if (!(await b.waitDraftEquals(session, task.scenarioId, typed, 15_000)).ok) t.problems.push('not saved before closing');
    const before = await timerNow();
    const storageState = await session.context.storageState();
    await session.close();
    await sleep(30_000);
    session = await startSession(ctx, learner, { storageState });
    t.track(session);
    if (session.reSignedIn) t.notes.push('closing the browser ended the sign-in (no "remember me": the session snapshot lives in sessionStorage); signed in again on the same device');
    await session.page.goto(b.appUrl(ROUTES.practice(task.scenarioId)), { waitUntil: 'domcontentloaded' });
    await expectText(t, 'after reopening');
    t.problems.push(...timerVerdict({ before, after: await timerNow(), window: windows.writing }).problems);
  });
  await chainStep('P0-3 S3 offline mid-typing', async (t) => {
    const half = splitText(parts[2], 2);
    await typePart(t, half[0]);
    const before = await timerNow();
    const offAt = Date.now();
    await session.context.setOffline(true);
    await typePart(t, half[1] ?? '');
    const offline = await b.waitDraftState(session.page, ['pending-local', 'offline'], 15_000);
    if (!['pending-local', 'offline'].includes(offline)) t.problems.push(`offline the draft status reads ${offline}, expected pending-local or offline`);
    await sleep(10_000);
    const after = await timerNow();
    const elapsed = Math.round((Date.now() - offAt) / 1000);
    await session.context.setOffline(false);
    if ((await b.waitDraftState(session.page, ['saved'], 30_000)) !== 'saved') t.problems.push('the draft did not return to saved after reconnecting');
    if (!(await b.waitDraftEquals(session, task.scenarioId, typed, 15_000)).ok) t.problems.push('the reconnected draft differs from the typed text');
    t.problems.push(...timerVerdict({ before, after, window: windows.writing, mode: 'running', elapsed }).problems);
  });
  await chainStep('P0-3 S5 draft saves fail (503 then abort), then recover', async (t) => {
    let n = 0;
    const handler = (route) => (n++ === 0 ? route.fulfill({ status: 503, contentType: 'application/json', body: '{}' }) : route.abort());
    await session.page.route('**/v1/writing/drafts/**', handler);
    const before = await timerNow();
    const at = Date.now();
    await typePart(t, parts[3]);
    const state = await b.waitDraftState(session.page, ['error', 'pending-local', 'offline'], 20_000);
    if (!['error', 'pending-local', 'offline'].includes(state)) t.problems.push(`while saves fail the draft status reads ${state}`);
    const after = await timerNow();
    const elapsed = Math.round((Date.now() - at) / 1000);
    await session.page.unroute('**/v1/writing/drafts/**', handler);
    if ((await b.waitDraftState(session.page, ['saved'], 45_000)) !== 'saved') t.problems.push('the draft did not recover to saved');
    if (!(await b.waitDraftEquals(session, task.scenarioId, typed, 15_000)).ok) t.problems.push('the recovered draft differs from the typed text');
    t.problems.push(...timerVerdict({ before, after, window: windows.writing, mode: 'running', elapsed }).problems);
  });
  await chainStep('P0-3 S4 shutdown, re-sign-in, Resume writing from Post Submissions', async (t) => {
    const before = await timerNow();
    await session.close();
    await sleep(30_000);
    session = await startSession(ctx, learner);
    t.track(session);
    await session.page.goto(b.appUrl(ROUTES.postSubmissions), { waitUntil: 'domcontentloaded' });
    const row = session.page.locator(`${tid(TEST_IDS.postSubmissionRow)}[data-scenario-id="${task.scenarioId}"][data-state="draft"]`).first();
    await row.waitFor({ state: 'visible', timeout: 60_000 });
    await row.locator(tid(TEST_IDS.postSubmissionResume)).first().click();
    await session.page.waitForURL((u) => u.pathname === ROUTES.practice(task.scenarioId), { timeout: 60_000 });
    await expectText(t, 'after Resume writing');
    t.problems.push(...timerVerdict({ before, after: await timerNow(), window: windows.writing }).problems);
  });
  await chainStep('P0-3 S1-S5 letter submitted and graded', async (t) => {
    await typePart(t, parts.slice(4).join(''));
    if (typed !== script.text) t.problems.push('the typed parts do not rebuild the script');
    if (!(await b.waitDraftEquals(session, task.scenarioId, typed, 15_000)).ok) t.problems.push('the final draft differs from the typed text');
    await submitAndVerify(ctx, session, learner, task, typed, t, { kind: 'paid', ...opened });
  });

  // ---- S6/S7: a real failed grade (fault flag, N=1), visible later in Post Submissions, Retry from the row ----
  const urgent = pickOf('urgent');
  const urgentScript = scriptFor(ctx.scripts, 'medicine', urgent.category);
  const s6 = { ...base('P0-3 S6 failed grade visible later in Post Submissions'), task: `${urgent.letterType} ${urgent.scenarioId}` };
  const s7 = { ...base('P0-3 S7 Retry on the Post Submissions row'), task: s6.task };
  session = await freshSession(session);
  if (ctx.inputs.faultMode === 'none') {
    add({ ...s6, status: 'NOT_RUN', notes: 'fault_mode=none' });
    add({ ...s7, status: 'NOT_RUN', notes: 'fault_mode=none' });
  } else {
    const client = ctx.inputs.faultMode === 'client';
    const s6State = { ran: false, problems: [] };
    const result = await runTest(ctx, s7, [...editorNeeds, 'grading', 'gradingFailure'], async (t) => {
      const opened6 = await openAndType(ctx, session, learner, urgent, urgentScript.text, t, { readingWindow: ctx.inputs.readingWindow });
      const health = writingHealth(await api.readHealthInputs(ctx.admin));
      const flag = client ? null : await enableFlag(ctx, 'all', learner.userId);
      try {
        await submitAndVerify(ctx, session, learner, urgent, urgentScript.text, t, {
          kind: 'paid', ...opened6, expectFailure: true, clientFault: client, faultFlag: flag?.key ?? 'client route',
          // client mode only fakes the page's status poll: the server row never fails, so Post Submissions
          // cannot show it and the Retry is pressed on the grading page instead.
          retryWhere: client ? 'grading' : 'post-submissions',
          onFailed: async (submissionId) => {
            s6State.ran = true;
            s6State.problems = await s6Checks(ctx, session, learner, submissionId, { client });
            if (client) return null;
            const failedAt = Date.now();
            await session.close();
            await sleep(Math.max(0, failedAt + 120_000 - Date.now()));
            session = await startSession(ctx, learner);
            return session;
          },
          retryOnRow: async (current, submissionId) => {
            await current.page.goto(b.appUrl(ROUTES.postSubmissions), { waitUntil: 'domcontentloaded' });
            const row = current.page.locator(`${tid(TEST_IDS.postSubmissionRow)}[data-submission-id="${submissionId}"]`).first();
            await row.locator(tid(TEST_IDS.postSubmissionRetry)).first().click();
          },
        });
      } finally {
        if (flag) await disableFlag(ctx, flag);
      }
      t.problems.push(...faultSideEffects(health, writingHealth(await api.readHealthInputs(ctx.admin))));
      if (client) t.partials.push('fault_mode=client: UI proven live with a faked failure; the server failure + retry path is proven by the WAI-03/WAI-05 CI runs');
    });
    const s6Status = s6State.ran
      ? (s6State.problems.length ? 'FAIL' : client ? 'PARTIAL' : 'PASS')
      : (['BLOCKED', 'NOT_RUN', 'VOID_DEPLOY'].includes(result.status) ? result.status : 'FAIL');
    const s6Notes = !s6State.ran ? `no failed grade was observed (${result.notes})`
      : s6State.problems.join('; ') || (client ? 'failure card + Retry shown (faked failure; server path not exercised)' : 'failed row + Retry visible on the grading page and in Post Submissions; a new session after 2 min');
    add({ ...s6, status: s6Status, notes: s6Notes });
    add(result);
  }

  // ---- reading-resume (real 60 s, reload) + F1 L1+L2 failover (L3 serves, zero API spend) ----
  const discharge = pickOf('discharge');
  const dischargeScript = scriptFor(ctx.scripts, 'medicine', discharge.category);
  const rr = { ...base('P0-3 reading window resume (60 s, reload)'), task: `${discharge.letterType} ${discharge.scenarioId}` };
  let rrOpened = null;
  session = await freshSession(session);
  const rrResult = await runTest(ctx, rr, editorNeeds, async (t) => {
    t.track(session);
    rrOpened = {
      kind: await creditKindFor(session, discharge.scenarioId),
      c0: ctx.inputs.verifyCredits ? await api.creditSnapshot(ctx.admin, learner.userId) : null,
      usageBefore: new Set((await api.usageRows(ctx.admin, { userId: learner.userId })).map((r) => r.id)),
    };
    await b.openTaskFromLibrary(session, discharge);
    rrOpened.c1 = ctx.inputs.verifyCredits ? await api.creditSnapshot(ctx.admin, learner.userId) : null;
    const w = await readWindows(session, discharge.scenarioId);
    await sleep(60_000);
    const before = await b.readTimer(session.page);
    await session.page.reload({ waitUntil: 'domcontentloaded' });
    const after = await b.readTimer(session.page);
    if (before.phase !== 'reading' || after.phase !== 'reading') t.problems.push(`phase ${before.phase} -> ${after.phase}, expected reading both times`);
    t.problems.push(...timerVerdict({ before: before.seconds, after: after.seconds, window: w.reading }).problems);
  });
  add(rrResult);
  const f1 = { ...base('L1+L2 synthetic fault: L3 GPT-6.1 Sol serves, zero API spend'), task: rr.task };
  if (!['PASS', 'PARTIAL'].includes(rrResult.status)) add({ ...f1, status: 'NOT_RUN', notes: 'not run: the reading-resume step (which opens this task) did not complete' });
  else add(await runTest(ctx, f1, editorNeeds, async (t) => {
    t.track(session);
    await b.waitForWritingPhase(session, 'real');
    await b.typeText(session.page, dischargeScript.text);
    if (!(await b.waitDraftEquals(session, discharge.scenarioId, dischargeScript.text, 15_000)).ok) t.problems.push('the draft did not equal the typed text within 15 s');
    const health = writingHealth(await api.readHealthInputs(ctx.admin));
    const flag = ctx.inputs.faultMode === 'flag' ? await enableFlag(ctx, 'l1l2', learner.userId) : null;
    try {
      await submitAndVerify(ctx, session, learner, discharge, dischargeScript.text, t, {
        kind: 'paid', ...rrOpened, expectedFirst: flag ? PROVIDERS.codex : PROVIDERS.claude, faultFlag: flag?.key,
      });
    } finally {
      if (flag) await disableFlag(ctx, flag);
    }
    if (!flag) t.partials.push(`fault_mode=${ctx.inputs.faultMode}: the letter was graded normally; L3 failover not proven live`);
    else if (!/gpt-6\.1-sol/i.test(t.provider)) t.partials.push(`L3 served by ${t.provider} (expected GPT-6.1 Sol)`);
    t.problems.push(...faultSideEffects(health, writingHealth(await api.readHealthInputs(ctx.admin))));
  }));
  await session.close();

  // ---- S8: free sample on a zero-credit learner, failed then retried ----
  await freeSampleScenario(ctx, medicine, add, base);
}

async function s6Checks(ctx, session, learner, submissionId, { client }) {
  const b = await import('./browser.mjs');
  const problems = [];
  const failedCard = session.page.locator(tid(TEST_IDS.gradingFailed)).first();
  if (!(await failedCard.waitFor({ state: 'visible', timeout: 30_000 }).then(() => true, () => false))) problems.push('the grading page shows no failure card');
  if (!(await session.page.locator(tid(TEST_IDS.gradingRetry)).first().isVisible().catch(() => false))) problems.push('the grading page shows no Retry');
  if (client) return problems;
  const work = await session.api(ENDPOINTS.myWork);
  problems.push(...myWorkProblems({ items: work.body?.items, submissionId, state: 'failed' }));
  const item = (work.body?.items ?? []).find((i) => i.submissionId === submissionId);
  if (item && item.canRetry !== true) problems.push('the failed row is not retryable (canRetry false)');
  problems.push(...await b.postSubmissionRow(session, submissionId, 'failed', path.join(MEDIA, `${slug(learner.key)}-failed-row.png`)));
  return problems;
}

async function freeSampleScenario(ctx, medicine, add, base) {
  const b = await import('./browser.mjs');
  const row = { ...base('P0-3 S8 free sample: failed grade burns no use, Retry counts once'), profession: 'Medicine (free sample)' };
  if (!api.freeSamplesEnabled(ctx.found.flags)) { add({ ...row, status: 'BLOCKED', notes: 'free_samples_enabled is OFF (owner decision; the harness never flips it)' }); return; }
  if (ctx.inputs.faultMode === 'none') { add({ ...row, status: 'NOT_RUN', notes: 'fault_mode=none' }); return; }
  let session;
  add(await runTest(ctx, row, ['editor', 'gradingFailure', 'results'], async (t) => {
    const learner = await provisionLearner(ctx, { key: 'medicine-free', professionId: medicine.catalogId, letters: 0 });
    session = await startSession(ctx, learner);
    t.track(session);
    const c0 = await api.creditSnapshot(ctx.admin, learner.userId);
    const refusal = creditPreflight(c0, null, { letters: 1, kind: 'free' });
    if (refusal.length) throw new Blocked(refusal.join('; '));
    const offers = (await session.api(ENDPOINTS.freeSamples)).body;
    const offer = Array.isArray(offers) ? offers[0] : null;
    if (!offer?.contentId || offer.state !== 'available') throw new Blocked(`no available writing free sample (${offer?.state ?? 'none'})`);
    const compat = ctx.found.compatibility.rows.find((r) => String(r.scenarioId).toLowerCase() === String(offer.contentId).toLowerCase());
    const task = { scenarioId: offer.contentId, title: compat?.taskTitle ?? '', letterType: compat?.letterType ?? 'LT-OT' };
    row.task = `${task.letterType} ${task.scenarioId}`;
    const script = scriptFor(ctx.scripts, 'medicine', categoryForLetterType(task.letterType));
    const usageBefore = new Set((await api.usageRows(ctx.admin, { userId: learner.userId })).map((r) => r.id));
    const eligibility = session.page.waitForResponse((r) => r.url().includes(ENDPOINTS.eligibility(task.scenarioId)), { timeout: 90_000 });
    await session.page.goto(b.appUrl(offer.route || ROUTES.practice(task.scenarioId)), { waitUntil: 'domcontentloaded' });
    if ((await eligibility).status() !== 200) throw new Error('the free-sample task did not open');
    await b.waitForWritingPhase(session, 'real');
    await b.typeText(session.page, script.text);
    if (!(await b.waitDraftEquals(session, task.scenarioId, script.text, 15_000)).ok) t.problems.push('the draft did not equal the typed text within 15 s');
    const flag = ctx.inputs.faultMode === 'flag' ? await enableFlag(ctx, 'all', learner.userId) : null;
    let afterFailure = null;
    try {
      await submitAndVerify(ctx, session, learner, task, script.text, t, {
        c0, c1: null, usageBefore, kind: 'free_sample', expectFailure: true, clientFault: ctx.inputs.faultMode === 'client', faultFlag: flag?.key ?? 'client route',
        onFailed: async () => { afterFailure = (await session.api(ENDPOINTS.freeSamples)).body; },
      });
    } finally {
      if (flag) await disableFlag(ctx, flag);
    }
    const afterRetry = (await session.api(ENDPOINTS.freeSamples)).body;
    t.problems.push(...freeSampleProblems({ afterFailure, afterRetry }));
    if (ctx.inputs.faultMode === 'client') t.partials.push('fault_mode=client: server failure path not exercised; see the WAI-05 CI run');
  }));
  await session?.close();
}

async function enableFlag(ctx, kind, userId) {
  const flag = await api.enableFaultFlag(ctx.admin, { kind, userId, runs: 1, runKey: RUN_KEY });
  ctx.state.flags.push({ ...flag, active: true });
  saveState(ctx.state);
  log(`fault flag ${flag.key} enabled`);
  return flag;
}

async function disableFlag(ctx, flag) {
  await api.deactivateFlag(ctx.admin, flag.id);
  const saved = ctx.state.flags.find((f) => f.id === flag.id);
  if (saved) saved.active = false;
  saveState(ctx.state);
  log(`fault flag ${flag.key} deactivated`);
}

/** UI suite: one full letter per requested browser on a phone device with the native shell emulated. */
async function uiSuite(ctx, plan) {
  const b = await import('./browser.mjs');
  const medicine = plan.find((p) => p.profession === 'medicine');
  const devices = ctx.inputs.browsers.map((name) => ({ name, device: name === 'webkit' ? 'iPhone 14' : 'Pixel 7' }));
  if (!medicine?.enabled) {
    for (const d of devices) ctx.tables.ui.push({ profession: 'Medicine (UI)', task: '-', category: `UI ${d.device}`, status: 'BLOCKED', notes: 'Medicine is not enabled' });
    return;
  }
  const learner = await provisionLearner(ctx, { key: 'medicine-ui', professionId: medicine.catalogId, letters: devices.length });
  for (const [i, d] of devices.entries()) {
    const pick = medicine.picks[i % medicine.picks.length];
    const script = scriptFor(ctx.scripts, 'medicine', pick.category);
    const row = { profession: 'Medicine (UI)', task: `${pick.letterType} ${pick.scenarioId}`, category: `UI ${d.device} (${d.name})` };
    ctx.tables.ui.push(await runTest(ctx, row, ['editor', 'grading', 'results', 'postSubmissions'], async (t) => {
      const session = await startSession(ctx, learner, { browserName: d.name, device: d.device, nativeShell: true, seedClock: ctx.inputs.readingWindow === 'seed' });
      t.track(session);
      try {
        if (i === 0 && ctx.inputs.verifyCredits) await verifyFunding(ctx, session, learner, 'paid');
        const opened = await openAndType(ctx, session, learner, pick, '', t, { readingWindow: ctx.inputs.readingWindow });
        const submitClear = await b.mobileChecks(session, b.appUrl(ROUTES.practice(pick.scenarioId)), path.join(MEDIA, slug(`ui-${d.name}-practice`)), [{ selector: tid(TEST_IDS.submit), label: 'practice Submit' }]);
        t.problems.push(...submitClear.problems);
        t.partials.push(...submitClear.partials);
        await b.typeText(session.page, script.text);
        if (!(await b.waitDraftEquals(session, pick.scenarioId, script.text, 15_000)).ok) t.problems.push('the draft did not equal the typed text within 15 s');
        await submitAndVerify(ctx, session, learner, pick, script.text, t, { kind: 'paid', ...opened, ui: { desktop: false, mobile: true } });
      } finally {
        await session.close();
      }
    }));
  }
}

// ---- Preflight, post-run, evidence, cleanup -----------------------------------------------------------------------

/** allowRepair = false in discover (read-only): preflight_repair circuit resets only happen in live suites. */
async function preflight(ctx, plannedLetters, allowRepair) {
  const read = { writingProvider: ctx.found.writingProvider, circuits: ctx.found.circuits };
  const decide = (inputs, repair) => preflightDecision({
    health: writingHealth(inputs), providers: ctx.found.providers, requireL2Disabled: ctx.inputs.requireL2Disabled,
    repair, writingProvider: inputs.writingProvider, plannedLetters,
  });
  let decision = decide(read, allowRepair && ctx.inputs.preflightRepair);
  const repaired = [];
  if (decision.repairs.length) {
    for (const key of decision.repairs) { await api.resetCircuit(ctx.admin, key); repaired.push(key); }
    decision = decide(await api.readHealthInputs(ctx.admin), false);
  }
  if (!api.freeSamplesEnabled(ctx.found.flags)) decision.warnings.push('free_samples_enabled is OFF: S8 will be BLOCKED');
  if (ctx.found.options?.aiGradingEnabled === false) decision.blockers.push(`Writing AI grading is switched off (${ctx.found.options.killSwitchReason ?? 'no reason'})`);
  writeJson('preflight.json', { ...decision, repaired, health: writingHealth(read) });
  const status = decision.failures.length ? 'FAIL' : decision.blockers.length ? 'BLOCKED' : 'PASS';
  ctx.tables.acceptance.push({
    profession: '(system)', task: 'writing chain', category: 'preflight (Max first, no marker, circuits)', status,
    notes: [...decision.failures, ...decision.blockers, ...decision.warnings, ...repaired.map((k) => `circuit ${k} reset (preflight_repair)`)].join('; ') || 'clean',
  });
  return status === 'PASS';
}

async function postRunChecks(ctx) {
  const health = writingHealth(await api.readHealthInputs(ctx.admin));
  const problems = [];
  if (health.marker !== null) problems.push(`quotaExceededUntil is ${health.marker} after the run (must stay null)`);
  if (health.maxCircuit) problems.push(`the ${PROVIDERS.claude} circuit is ${health.maxCircuit.state} after the run`);
  if (health.failoverActive) problems.push('failoverActive is true after the run');
  ctx.tables.acceptance.push({ profession: '(system)', task: 'writing chain', category: 'post-run (marker null, Max circuit closed)', status: problems.length ? 'FAIL' : 'PASS', notes: problems.join('; ') || 'clean' });
  const spend = paidSpendProblems(ctx.paidSpend.size);
  ctx.tables.acceptance.push({ profession: '(system)', task: 'anthropic usage rows', category: `paidApiSpend = ${ctx.paidSpend.size}`, status: spend.length ? 'FAIL' : 'PASS', notes: spend.join('; ') || 'zero paid Anthropic API calls during the run' });
}

function writeTables(ctx) {
  const all = [...ctx.tables.qa2, ...ctx.tables.acceptance, ...ctx.tables.ui];
  const verdict = overallVerdict(all);
  const sections = [['qa2', 'QA-2 live letters per profession'], ['acceptance', 'P0-3 acceptance + system checks'], ['ui', 'UI suite']];
  const runUrl = process.env.GITHUB_SERVER_URL ? `${process.env.GITHUB_SERVER_URL}/${process.env.GITHUB_REPOSITORY}/actions/runs/${RUN_ID}` : RUN_ID;
  let summary = `# Writing production QA (${ctx.inputs.suite})\n\nRun: ${runUrl}\n\n**${verdict}**\n`;
  for (const [name, title] of sections) {
    if (!ctx.tables[name].length) continue;
    const { md, csv } = buildTable(ctx.tables[name], { runId: RUN_ID });
    fs.writeFileSync(path.join(EVIDENCE, `${name}.md`), `${md}\n`);
    fs.writeFileSync(path.join(EVIDENCE, `${name}.csv`), `${csv}\n`);
    summary += `\n## ${title}\n\n${md}\n`;
  }
  fs.writeFileSync(path.join(EVIDENCE, 'verdict.txt'), `${verdict}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
  log(verdict);
  return { verdict, allPass: verdict.startsWith('ALL PASS') };
}

/** Reads each learner's usage/ledger evidence FIRST, then purges per policy (on_success | always | never). */
async function cleanup(admin, state, success) {
  const purge = state.cleanup === 'always' || (state.cleanup === 'on_success' && success);
  for (const f of state.flags.filter((x) => x.active)) {
    await api.deactivateFlag(admin, f.id).then(() => { f.active = false; }, (e) => log(`flag ${f.key} deactivate failed: ${e.message}`));
  }
  for (const learner of state.learners.filter((l) => !l.purged)) {
    const [usage, ledger] = await Promise.all([api.usageRows(admin, { userId: learner.userId }).catch(() => null), api.creditSnapshot(admin, learner.userId).catch(() => null)]);
    writeJson(`learners/${slug(learner.key)}.json`, {
      userId: learner.userId, email: learner.email, usage,
      ledger: ledger?.transactions?.map((t) => ({ reason: t.reason, referenceId: t.referenceId, writingOnlyCreditsDelta: t.writingOnlyCreditsDelta, createdAt: t.createdAt })) ?? null,
    });
    if (purge) await api.deleteLearner(admin, learner.userId, state.runKey).then(() => { learner.purged = true; }, (e) => log(`purge ${learner.userId} failed: ${e.message}`));
  }
  saveState(state);
  const kept = 'learners kept (cleanup=' + state.cleanup + (success ? '' : ', run not all PASS') + ')';
  log(`cleanup: ${purge ? 'learners purged' : kept}`);
}

// ---- Entry points ---------------------------------------------------------------------------------------------------

async function run() {
  const inputs = parseInputs(process.env);
  const scripts = loadScripts();
  fs.mkdirSync(EVIDENCE, { recursive: true });
  fs.mkdirSync(MEDIA, { recursive: true });
  const ctx = createContext(inputs, scripts);
  writeJson('run.json', { runKey: RUN_KEY, inputs, startedAt: new Date(ctx.startedAt).toISOString() });
  ctx.found = await api.discover(ctx.admin);
  const plan = planDiscovery({ ...ctx.found, professions: inputs.professions, categories: inputs.categories });
  writeJson('discover.json', { ...ctx.found, plan });
  if (inputs.suite === 'discover') {
    ctx.tables.qa2.push(...planRows(plan, ctx.labels));
    await preflight(ctx, 0, false).catch((e) => log('preflight read failed:', e.message)); // read-only: no repairs
    writeTables(ctx);
    ctx.state.finished = true;
    saveState(ctx.state);
    return 0;
  }
  ctx.lane = createLane();
  const lettersOnly = inputs.suite === 'letters';
  ctx.letters = lettersOnly ? loadLetters().filter((l) => inputs.professions.includes(l.profession)) : [];
  const planned = lettersOnly ? ctx.letters.length : plan.reduce((n, p) => n + (p.enabled ? p.picks.length : 0), 0);
  const notEnabled = planRows(plan.filter((p) => !p.enabled), ctx.labels);
  let ok = false;
  let guard = null;
  try {
    ok = await preflight(ctx, planned + 6, true);
    if (!ok) {
      if (suiteRuns(inputs.suite, 'matrix')) ctx.tables.qa2.push(...planRows(plan, ctx.labels).map((r) => (r.status === 'NOT_RUN' ? { ...r, notes: 'not started: preflight did not pass' } : r)));
      if (lettersOnly) {
        ctx.tables.qa2.push(...ctx.letters.map((l) => ({
          profession: ctx.labels[l.profession] ?? l.profession, task: `${l.letterType} ${l.scenarioId}`,
          category: `realistic letter ${l.id}`, status: 'NOT_RUN', notes: 'not started: preflight did not pass',
        })));
      }
    } else {
      guard = startGuard(ctx);
      ctx.guard = guard;
      await guard.tick();
      if (suiteRuns(inputs.suite, 'acceptance')) await acceptanceSuite(ctx, plan).catch((e) => ctx.tables.acceptance.push({ profession: 'Medicine (acceptance)', task: '-', category: 'P0-3 acceptance', status: e instanceof Blocked ? 'BLOCKED' : 'FAIL', notes: `aborted: ${e.message}` }));
      if (suiteRuns(inputs.suite, 'ui')) await uiSuite(ctx, plan).catch((e) => ctx.tables.ui.push({ profession: 'Medicine (UI)', task: '-', category: 'UI suite', status: e instanceof Blocked ? 'BLOCKED' : 'FAIL', notes: `aborted: ${e.message}` }));
      if (suiteRuns(inputs.suite, 'matrix')) {
        await matrixSuite(ctx, plan);
        ctx.tables.qa2.push(...notEnabled);
      }
      if (lettersOnly) await lettersSuite(ctx, plan); // not part of `all`: it grades extra letters
      await guard.tick();
    }
  } finally {
    guard?.stop();
    const b = await import('./browser.mjs').catch(() => null);
    await b?.closeBrowsers();
  }
  await postRunChecks(ctx).catch((e) => ctx.tables.acceptance.push({ profession: '(system)', task: 'writing chain', category: 'post-run', status: 'FAIL', notes: e.message }));
  if (ctx.halted) ctx.tables.acceptance.push({ profession: '(system)', task: 'guard loop', category: 'halt', status: 'FAIL', notes: ctx.halted });
  const { allPass } = writeTables(ctx);
  ctx.state.success = allPass;
  ctx.state.finished = true;
  await cleanup(ctx.admin, ctx.state, allPass);
  return allPass ? 0 : 1;
}

async function safetyNet() {
  const state = readState();
  if (!state) { log('safety net: no run state, nothing to do'); return 0; }
  const success = state.finished && state.success;
  const purge = state.cleanup === 'always' || (state.cleanup === 'on_success' && success);
  const pending = state.flags.some((f) => f.active) || (purge && state.learners.some((l) => !l.purged));
  if (!pending) { log('safety net: flags off, learners handled, nothing to do'); return 0; }
  fs.mkdirSync(EVIDENCE, { recursive: true });
  const admin = api.createAdminClient({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD });
  await cleanup(admin, state, success);
  return 0;
}

const mode = process.argv[2] ?? 'run';
try {
  if (mode === 'check-inputs') {
    const inputs = parseInputs(process.env);
    loadScripts();
    loadLetters();
    console.log(JSON.stringify(inputs));
    process.exitCode = 0;
  } else if (mode === 'safety-net') process.exitCode = await safetyNet();
  else if (mode === 'run') process.exitCode = await run();
  else throw new Error(`unknown mode ${mode}`);
} catch (error) {
  console.log(`::error::${String(error.message).replace(/\r?\n/g, ' ')}`);
  process.exitCode = 1;
}
// Evidence, tables and cleanup are finished by here. A lingering handle (browser, keep-alive socket) kept some runs
// "in progress" for hours after the last letter (runs 37261674873, 37274160131, 37279045941): exit explicitly.
process.exit(process.exitCode ?? 0);
