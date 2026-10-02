#!/usr/bin/env node
// Read-only production diagnostics for the Writing AI grade chain (WAI-00).
//
// Answers "why did the latest Writing grades fail?" from the live control plane
// without needing a submission id: latest writing.grade operations, non-success
// usage rows, provider rows, the Max/Claude marker, circuits, budgets, the
// global policy and the credit ledger of the learners whose grades failed.
// Prints a ranked list of root-cause signals (the hypotheses A1..B7 of the
// Writing AI launch plan, WAI-00).
//
// Nothing here writes, with ONE owner-authorised exception: VERIFY_API_MODEL=true
// makes a single POST /v1/admin/ai/providers/anthropic/test-model (one tiny real
// completion, about $0.01) to prove the Level 2 model id answers on the API row.
// It is never retried, and it stops with a loud line when the API has no credit.
//
// Output hygiene (this repo's Actions logs are public while it is public): user
// ids are shortened to 8 characters, e-mail-like text is redacted, error text is
// truncated, and the bearer is never printed.
//
// Env: ADMIN_EMAIL, ADMIN_PASSWORD (required), DIAGNOSE_LATEST ('true'),
// VERIFY_API_MODEL ('true'), PROBE_SUBSCRIPTION_PROVIDERS ('true': a 1-token test
// of the Claude Max and Codex SUBSCRIPTION sidecars, $0, never the paid API),
// OET_API_BASE (optional).

const base = process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk';
const diagnoseLatest = process.env.DIAGNOSE_LATEST === 'true';
const verifyApiModel = process.env.VERIFY_API_MODEL === 'true';
const L1 = 'writing-claude-sub';
const L2 = 'anthropic';
const L3 = 'writing-codex-sub';
const L2_MODEL = 'claude-opus-5-5';

const short = (id) => String(id ?? '').slice(0, 8);
const clean = (text, max = 200) =>
  String(text ?? '')
    .replace(/[\w.+-]+@[\w-]+\.[\w.-]+/g, '<email>')
    .replace(/\s+/g, ' ')
    .slice(0, max);
const minutesAgo = (iso) => (iso ? (Date.now() - Date.parse(iso)) / 60000 : Number.NaN);
const log = (label, value) => console.log(label, typeof value === 'string' ? value : JSON.stringify(value));

if (!process.env.ADMIN_EMAIL || !process.env.ADMIN_PASSWORD) throw new Error('CI admin credentials unavailable');

let token = '';
async function signIn() {
  const response = await fetch(`${base}/v1/auth/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-OET-Client-Platform': 'web' },
    body: JSON.stringify({ email: process.env.ADMIN_EMAIL, password: process.env.ADMIN_PASSWORD, rememberMe: false }),
    signal: AbortSignal.timeout(30000),
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok || !body.accessToken) {
    throw new Error(`Admin sign-in failed: HTTP ${response.status} (${body.errorCode ?? body.code ?? 'no_session'})`);
  }
  token = body.accessToken;
}

// One failed read must not hide the others: every read reports its own status.
async function read(path) {
  try {
    const response = await fetch(`${base}${path}`, {
      headers: { Authorization: `Bearer ${token}` },
      signal: AbortSignal.timeout(30000),
    });
    if (!response.ok) return { ok: false, status: response.status, path };
    return { ok: true, status: response.status, data: await response.json() };
  } catch (error) {
    return { ok: false, status: 0, path, error: clean(error?.message) };
  }
}

await signIn();
console.log('Signed in as the CI admin (read-only run).');

// RULE MAX-ALWAYS-ON (owner, 2 Oct 2026): restore the Claude Max route NOW by clearing the legacy
// 7-day "Max is exhausted" marker that the old pipeline wrote. The one write this script can make
// besides the authorised model check; explicit opt-in only.
if (process.env.CLEAR_MAX_MARKER === 'true') {
  const before = await read('/v1/admin/ai/writing-provider');
  log('MAX_MARKER_BEFORE', { quotaExceededUntil: before.data?.quotaExceededUntil, failoverActive: before.data?.failoverActive, mode: before.data?.mode });
  const response = await fetch(`${base}/v1/admin/ai/writing-provider`, {
    method: 'PUT',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ clearQuotaMarker: true }),
    signal: AbortSignal.timeout(30000),
  });
  const after = await read('/v1/admin/ai/writing-provider');
  log('MAX_MARKER_CLEARED', {
    http: response.status, quotaExceededUntil: after.data?.quotaExceededUntil, failoverActive: after.data?.failoverActive, mode: after.data?.mode,
  });
  if (!response.ok || after.data?.quotaExceededUntil) process.exitCode = 1;
}

const [ops, usage, providers, writingProvider, circuits, budgets, policy, usageSummary, flags] = await Promise.all([
  read('/v1/admin/ai/operations?featureCode=writing.grade&pageSize=200'),
  read('/v1/admin/ai/usage?featureCode=writing.grade&pageSize=200'),
  read('/v1/admin/ai/providers'),
  read('/v1/admin/ai/writing-provider'),
  read('/v1/admin/ai/circuits'),
  read('/v1/admin/ai/budgets'),
  read('/v1/admin/ai/global-policy'),
  read('/v1/admin/ai/usage/summary'),
  read('/v1/admin/flags'),
]);
log('READ_STATUS', Object.fromEntries(Object.entries({
  operations: ops, usage, providers, writingProvider, circuits, budgets, policy, usageSummary, flags,
}).map(([name, r]) => [name, r.ok ? r.status : `FAILED ${r.status}`])));

const opRows = ops.data?.rows ?? [];
const usageRows = usage.data?.rows ?? [];
const providerRows = Array.isArray(providers.data) ? providers.data : [];
const circuitRows = circuits.data?.rows ?? [];
const budgetRows = budgets.data?.rows ?? [];

// ── Providers, marker, policy, budgets, circuits ────────────────────────────
log('PROVIDERS', providerRows
  .filter((p) => [L1, L2, L3].includes(p.code))
  .map((p) => ({
    code: p.code, active: p.isActive, model: p.defaultModel, effort: p.reasoningEffort,
    baseUrlSet: Boolean(p.baseUrl), keyHint: p.apiKeyHint ? 'present' : 'missing',
    lastTestedAt: p.lastTestedAt, lastTestStatus: p.lastTestStatus, lastTestError: clean(p.lastTestError, 160),
  })));
const wp = writingProvider.data ?? {};
log('WRITING_PROVIDER', {
  mode: wp.mode, quotaExceededUntil: wp.quotaExceededUntil, failoverActive: wp.failoverActive,
  utilizationPct: wp.quota?.utilizationPct, quotaSource: wp.quota?.source,
  gradedToday: wp.gradedToday, gradedWeek: wp.gradedWeek, fallbackCountWeek: wp.fallbackCountWeek,
  claudeWeek: wp.claude, claudeApiWeek: wp.claudeApi, codexWeek: wp.codex,
});
const pol = policy.data ?? {};
log('GLOBAL_POLICY', {
  killSwitch: pol.killSwitchEnabled, killScope: pol.killSwitchScope, disabledFeatures: pol.disabledFeaturesCsv,
  monthlyBudgetUsd: pol.monthlyBudgetUsd, hardKillPct: pol.hardKillPct, enforceSpendCaps: pol.enforceSpendCaps,
  disabledPlatformProvider: pol.defaultPlatformProviderId,
});
log('BUDGETS', budgetRows.map((b) => ({
  scope: b.scope, period: b.periodKey, limit: b.limit, reserved: b.reserved, committed: b.committed,
  exhausted: b.limit > 0 && b.reserved + b.committed >= b.limit,
})));
log('CIRCUITS', circuitRows
  .filter((c) => c.kind === 'provider' || String(c.key ?? '').startsWith('writing'))
  .map((c) => ({ key: c.key, kind: c.kind, state: c.state, failures: c.failureCount, lastCode: c.lastFailureCode, openUntil: c.openUntil })));
const flagList = Array.isArray(flags.data) ? flags.data : flags.data?.rows ?? flags.data?.items ?? flags.data?.flags ?? [];
log('FLAGS_OF_INTEREST', flagList
  .filter((f) => /free_samples_enabled|writing_grade_fault|writing/i.test(JSON.stringify(f)))
  .map((f) => ({ key: f.key ?? f.name, enabled: f.enabled ?? f.isEnabled, rollout: f.rolloutPercentage, type: f.type, updatedAt: f.updatedAt })));
log('USAGE_SUMMARY_MONTH', (usageSummary.data?.rows ?? []).slice(0, 25));

// ── Latest operations and usage ─────────────────────────────────────────────
const bySubmission = new Map();
for (const op of opRows) {
  const list = bySubmission.get(op.resourceId) ?? [];
  list.push(op);
  bySubmission.set(op.resourceId, list);
}
const countStates = {};
for (const op of opRows) countStates[op.state] = (countStates[op.state] ?? 0) + 1;
log('WRITING_GRADE_OPERATION_STATES_LATEST_200', countStates);
const notCompleted = opRows.filter((op) => op.state !== 'Completed').slice(0, 25);
log('LATEST_NON_COMPLETED_OPERATIONS', notCompleted.map((op) => ({
  submission: short(op.resourceId), user: short(op.userId), state: op.state, createdAt: op.createdAt,
  updatedAt: op.updatedAt, ageMin: Math.round(minutesAgo(op.createdAt)), hasResult: Boolean(op.resultRef),
})));
const nonSuccess = usageRows.filter((r) => r.outcome !== 'Success');
log('NON_SUCCESS_USAGE_COUNT', { nonSuccess: nonSuccess.length, of: usageRows.length });
log('LATEST_NON_SUCCESS_USAGE', nonSuccess.slice(0, 30).map((r) => ({
  at: r.createdAt, user: short(r.userId), provider: r.providerId, model: r.model, outcome: r.outcome,
  code: r.errorCode, latencyMs: r.latencyMs, retries: r.retryCount, failover: clean(r.failoverTrace, 120),
  policy: clean(r.policyTrace, 120), message: clean(r.errorMessage, 200),
})));
const byProvider = {};
for (const r of usageRows) {
  const key = `${r.providerId ?? 'none'}:${r.outcome}`;
  byProvider[key] = (byProvider[key] ?? 0) + 1;
}
log('USAGE_BY_PROVIDER_OUTCOME_LATEST_200', byProvider);

// ── Error breakdown (which refusals/failures, and when) ─────────────────────
const breakdown = new Map();
for (const r of nonSuccess) {
  const key = [r.providerId ?? 'none', r.outcome, r.errorCode ?? '', clean(r.policyTrace, 80)].join(' | ');
  const entry = breakdown.get(key) ?? { n: 0, first: r.createdAt, last: r.createdAt, maxLatency: 0 };
  entry.n += 1;
  if (Date.parse(r.createdAt) < Date.parse(entry.first)) entry.first = r.createdAt;
  if (Date.parse(r.createdAt) > Date.parse(entry.last)) entry.last = r.createdAt;
  entry.maxLatency = Math.max(entry.maxLatency, r.latencyMs ?? 0);
  breakdown.set(key, entry);
}
log('NON_SUCCESS_BREAKDOWN', [...breakdown.entries()]
  .sort((a, b) => b[1].n - a[1].n)
  .map(([key, v]) => ({ key, ...v })));

// ── Level 1 (Claude Max) profile: does it usually work, and how long does it take? ──
const l1Usage = await read(`/v1/admin/ai/usage?providerId=${L1}&pageSize=200`);
const l1All = l1Usage.data?.rows ?? [];
const l1Ok = l1All.filter((r) => r.outcome === 'Success');
const percentile = (values, p) => {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
};
log('LEVEL1_CLAUDE_PROFILE', {
  rows: l1All.length, successes: l1Ok.length, firstAt: l1All.at(-1)?.createdAt, lastAt: l1All[0]?.createdAt,
  successLatencyMs: { p50: percentile(l1Ok.map((r) => r.latencyMs), 50), p90: percentile(l1Ok.map((r) => r.latencyMs), 90), max: percentile(l1Ok.map((r) => r.latencyMs), 100) },
  promptTokensMedian: percentile(l1Ok.map((r) => r.promptTokens), 50),
});
log('LEVEL1_CLAUDE_FAILURES', l1All.filter((r) => r.outcome !== 'Success').slice(0, 20).map((r) => ({
  at: r.createdAt, outcome: r.outcome, code: r.errorCode, latencyMs: r.latencyMs, message: clean(r.errorMessage, 220),
})));
const codexUsage = await read(`/v1/admin/ai/usage?providerId=${L3}&pageSize=200`);
const codexAll = codexUsage.data?.rows ?? [];
log('LEVEL3_CODEX_PROFILE', {
  rows: codexAll.length, successes: codexAll.filter((r) => r.outcome === 'Success').length,
  firstAt: codexAll.at(-1)?.createdAt, lastAt: codexAll[0]?.createdAt,
  lastSuccessAt: codexAll.find((r) => r.outcome === 'Success')?.createdAt,
  lastSuccessModel: codexAll.find((r) => r.outcome === 'Success')?.model,
});

// ── Profession catalogue (QA pre-flight: which professions are active) ──────
const catalog = await read('/v1/professions/catalog');
log('PROFESSION_CATALOG', (catalog.data?.professions ?? []).map((p) => ({ id: p.id, active: p.isActive })));

// ── Optional no-cost probe of the two SUBSCRIPTION sidecars (never the paid API) ──
if (process.env.PROBE_SUBSCRIPTION_PROVIDERS === 'true') {
  for (const code of [L1, L3]) {
    try {
      const response = await fetch(`${base}/v1/admin/ai/providers/${code}/test`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        signal: AbortSignal.timeout(150000),
      });
      const body = await response.json().catch(() => ({}));
      log(`PROBE_${code}`, { http: response.status, status: body.status, latencyMs: body.latencyMs, error: clean(body.errorMessage, 240) });
    } catch (error) {
      log(`PROBE_${code}`, { error: clean(error?.message) });
    }
  }
}

// ── Credit ledger of the failing learners ───────────────────────────────────
const failingUsers = [];
const seen = new Set();
const failureTimeline = [
  ...nonSuccess.map((r) => ({ at: r.createdAt, userId: r.userId })),
  ...opRows.filter((op) => ['FailedTerminal', 'Indeterminate', 'BlockedBudget'].includes(op.state))
    .map((op) => ({ at: op.createdAt, userId: op.userId })),
].sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
for (const entry of failureTimeline) {
  if (entry.userId && !seen.has(entry.userId)) { seen.add(entry.userId); failingUsers.push(entry.userId); }
  if (failingUsers.length >= 3) break;
}
const ledgerFacts = [];
for (const userId of failingUsers) {
  const credits = await read(`/v1/admin/ai-package-credits/${encodeURIComponent(userId)}?pageSize=40`);
  if (!credits.ok) { ledgerFacts.push({ user: short(userId), error: `HTTP ${credits.status}` }); continue; }
  const c = credits.data;
  const tx = (c.transactions ?? [])
    .filter((t) => /writing/i.test(`${t.referenceId ?? ''} ${t.reason ?? ''}`))
    .slice(0, 12)
    .map((t) => ({
      at: t.createdAt, reason: t.reason,
      ref: String(t.referenceId ?? '').replace(userId, short(userId)).slice(0, 90),
      shared: t.sharedCreditsDelta, flexible: t.flexibleCreditsDelta, writingOnly: t.writingOnlyCreditsDelta,
    }));
  ledgerFacts.push({
    user: short(userId), remaining: c.creditsRemaining, granted: c.creditsGranted, used: c.creditsUsed,
    shared: c.sharedCredits, flexible: c.flexibleCredits, writingOnly: c.writingOnlyCredits,
    writingUnlimited: c.writingUnlimited, expiresAt: c.expiresAt, writingLedger: tx,
  });
}
log('FAILED_LEARNER_CREDIT_LEDGER', ledgerFacts);

// ── Ranked root-cause signals ───────────────────────────────────────────────
const signals = [];
const signal = (id, title, hits, detail) => { if (hits > 0) signals.push({ id, title, hits, detail }); };
const subsWithIndeterminate = [...bySubmission.values()].filter((list) =>
  list.some((op) => op.state === 'Indeterminate') && !list.some((op) => op.state === 'Completed'));
signal('A1', 'Indeterminate duplicate poisons Retry and skips Levels 2/3', subsWithIndeterminate.length,
  'a submission whose only grade operations are Indeterminate: the retry replays the dead slot');
signal('A1c', 'Replay-walk exhaustion (many terminal operations per submission)',
  [...bySubmission.values()].filter((list) => list.filter((op) => ['FailedTerminal', 'Indeterminate'].includes(op.state)).length >= 4).length,
  'four or more terminal operations on one submission');
signal('A3', 'Queued/leased ghost operations (older than 30 min)',
  opRows.filter((op) => ['Queued', 'Leased', 'RetryScheduled'].includes(op.state) && minutesAgo(op.updatedAt) > 30).length,
  'work claimed by a grader that never finished (deploy/restart orphan)');
signal('A4', 'Budget or quota hard-kill refused the call',
  opRows.filter((op) => op.state === 'BlockedBudget').length
  + nonSuccess.filter((r) => /budget|quota_denied|hard.?kill/i.test(`${r.errorCode} ${r.errorMessage} ${r.policyTrace}`)).length,
  'BlockedBudget operations or budget/hard-kill refusals in usage');
signal('A5b', 'Plan / credit / premium gate refused a credit-funded grade',
  nonSuccess.filter((r) => /feature_not_in_plan|plan|credit|premium|insufficient/i.test(`${r.errorCode} ${r.errorMessage}`)).length,
  'GatewayRefused rows citing plan, credits or premium');
signal('A5', 'Credit ledger: debited at task open and now empty (one-letter learner)',
  ledgerFacts.filter((f) => f.remaining === 0 && (f.writingLedger ?? []).some((t) => /writing-v2:/.test(t.ref))).length,
  'a failing learner has a writing-v2 debit and zero credits left');
const inactive = [L1, L2, L3].filter((code) => {
  const row = providerRows.find((p) => p.code === code);
  return !row || !row.isActive || (row.lastTestStatus && !/^ok$/i.test(row.lastTestStatus));
});
signal('A7', 'Provider row missing, inactive or failing its last test', inactive.length, inactive.join(', '));
const l1Rows = nonSuccess.filter((r) => r.providerId === L1);
const text = (r) => `${r.errorCode} ${r.errorMessage}`;
signal('B1', 'Claude CLI login expired or contended', l1Rows.filter((r) => /auth|login|oauth|401|token/i.test(text(r))).length, 'writing-claude-sub auth-looking failures');
signal('B2', 'Max allowance quota / rate limit', l1Rows.filter((r) => /quota|429|usage limit|rate/i.test(text(r))).length
  + (wp.quotaExceededUntil && Date.parse(wp.quotaExceededUntil) > Date.now() ? 1 : 0), 'quota text on writing-claude-sub or the sticky marker is set');
signal('B3', 'Single serial lane / timeouts on Claude',
  l1Rows.filter((r) => r.outcome === 'Timeout' || r.latencyMs >= 285000 || /timed out|timeout|504|502/i.test(text(r))).length,
  'timeouts or ~300 s CLI-limit latencies on writing-claude-sub');
signal('B4', 'Truncated / unparseable provider output', nonSuccess.filter((r) => /unreadable|unparseable|could not parse|truncat|not valid json/i.test(text(r))).length, 'unreadable rubric output');
signal('B5', 'Codex false-positive quota detection',
  nonSuccess.filter((r) => r.providerId === L3 && /quota|429|reached/i.test(text(r)) && r.latencyMs >= 20000).length,
  'writing-codex-sub failed with quota text after a long (real) run');
signal('B6', 'Codex login / model rejected', nonSuccess.filter((r) => r.providerId === L3 && /login|auth|model|not found|unsupported/i.test(text(r))).length, 'writing-codex-sub auth/model failures');
signal('B7', 'Anthropic API hop failing', nonSuccess.filter((r) => r.providerId === L2).length,
  nonSuccess.filter((r) => r.providerId === L2).slice(0, 3).map((r) => clean(r.errorMessage, 100)).join(' | '));
signal('MARK', 'Sticky quota marker is set (Max route skipped)',
  wp.quotaExceededUntil && Date.parse(wp.quotaExceededUntil) > Date.now() ? 1 : 0, String(wp.quotaExceededUntil ?? ''));
signal('CIRC', 'A provider circuit is open', circuitRows.filter((c) => c.kind === 'provider' && c.state !== 'closed' && c.state !== 'Closed').length,
  circuitRows.filter((c) => c.kind === 'provider' && c.state !== 'closed' && c.state !== 'Closed').map((c) => c.key).join(', '));
signal('KILL', 'Kill switch or a writing feature is disabled',
  (pol.killSwitchEnabled ? 1 : 0) + (/writing/i.test(String(pol.disabledFeaturesCsv ?? '')) ? 1 : 0),
  `killSwitch=${pol.killSwitchEnabled} disabled=${pol.disabledFeaturesCsv ?? ''}`);
signals.sort((a, b) => b.hits - a.hits);
console.log('\n=== RANKED_ROOT_CAUSE_SIGNALS (by evidence count; ids are the plan hypotheses) ===');
for (const s of signals) console.log(`${s.id.padEnd(5)} hits=${String(s.hits).padEnd(4)} ${s.title}  [${clean(s.detail, 160)}]`);
if (signals.length === 0) console.log('No signal matched in the latest 200 grade operations / usage rows.');
log('HYPOTHESES_WITHOUT_SIGNAL', ['A1', 'A1c', 'A3', 'A4', 'A5', 'A5b', 'A7', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'B7']
  .filter((id) => !signals.some((s) => s.id === id)));

// ── The one owner-authorised paid check ─────────────────────────────────────
let apiModelFailed = false;
if (verifyApiModel) {
  const row = providerRows.find((p) => p.code === L2);
  if (!row) {
    console.log('VERIFY_API_MODEL skipped: no anthropic provider row exists.');
    apiModelFailed = true;
  } else {
    console.log(`VERIFY_API_MODEL: one real completion on '${L2}' with model '${L2_MODEL}' (about $0.01, audited).`);
    const response = await fetch(`${base}/v1/admin/ai/providers/${L2}/test-model`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ model: L2_MODEL }),
      signal: AbortSignal.timeout(120000),
    });
    const body = await response.json().catch(() => ({}));
    log('VERIFY_API_MODEL_RESULT', {
      http: response.status, status: body.status, model: body.model, latencyMs: body.latencyMs,
      errorMessage: clean(body.errorMessage, 240), steps: body.steps,
    });
    apiModelFailed = !response.ok || String(body.status ?? '').toLowerCase() !== 'ok';
    if (/credit balance|billing|insufficient/i.test(`${body.errorMessage}`)) {
      console.log('API_HAS_NO_CREDIT: the Anthropic API row answered "no credit". Funding it is the OWNER\'s decision; no other API call will be made.');
    }
  }
}

const summary = [
  '## Writing grading diagnostics (read-only)',
  '',
  `Latest ${opRows.length} writing.grade operations: ${JSON.stringify(countStates)}`,
  `Non-success usage rows: ${nonSuccess.length} of ${usageRows.length}`,
  '',
  '| Signal | Hits | Meaning |', '|---|---|---|',
  ...signals.map((s) => `| ${s.id} | ${s.hits} | ${s.title} |`),
  verifyApiModel ? `\nAPI model check (${L2_MODEL}): ${apiModelFailed ? 'FAILED' : 'ok'}` : '',
].join('\n');
if (process.env.GITHUB_STEP_SUMMARY) {
  const { appendFileSync } = await import('node:fs');
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
}
if (!diagnoseLatest && !verifyApiModel) console.log('Neither DIAGNOSE_LATEST nor VERIFY_API_MODEL was set; nothing more to do.');
if (apiModelFailed) process.exitCode = 1;
