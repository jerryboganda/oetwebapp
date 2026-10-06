// Disposable-account seeding for the fleet load harness (pure logic; the CLI is seed-accounts.mjs).
//
// Every account is created through the SAME admin endpoint the Writing production QA uses
// (POST /v1/admin/users with a password and sendInvite:false: pre-verified, no email), with a
// deterministic email and device id (tests/load/fleet/accounts.mjs), so the k6 legs derive their
// accounts without any shared file and a re-run is idempotent (an existing account is a no-op).
//
// SAFETY: refuses any production host before the first request. SingleActiveSessionEnabled and
// TrustedDeviceRequired are NOT touched: a deterministic device id per account is auto-trusted on its
// first sign-in, so the harness runs with the production security posture.

import { accountFor, accountPlan, assertNonProduction } from '../fleet/accounts.mjs';

export const DEFAULT_CREDITS = Object.freeze({
  shared: 400, flexible: 0, writingOnly: 100, speakingOnly: 200, listeningTests: 100, readingTests: 100, mockExams: 10,
});

export function parseArgs(argv, env = {}) {
  const args = {
    api: env.K6_API_URL ?? '',
    learners: 1500,
    experts: 75,
    profession: env.OET_LOAD_PROFESSION ?? 'medicine',
    concurrency: 6,
    purge: false,
    dryRun: false,
    report: '',
    prefix: env.OET_LOAD_ACCOUNT_PREFIX ?? 'loadtest',
    domain: env.OET_LOAD_EMAIL_DOMAIN ?? 'load.oet.test',
  };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const value = () => {
      i += 1;
      if (argv[i] === undefined) throw new Error(`${arg} needs a value`);
      return argv[i];
    };
    if (arg === '--api') args.api = value();
    else if (arg === '--learners') args.learners = Number(value());
    else if (arg === '--experts') args.experts = Number(value());
    else if (arg === '--profession') args.profession = value();
    else if (arg === '--concurrency') args.concurrency = Number(value());
    else if (arg === '--prefix') args.prefix = value();
    else if (arg === '--domain') args.domain = value();
    else if (arg === '--report') args.report = value();
    else if (arg === '--purge') args.purge = true;
    else if (arg === '--dry-run') args.dryRun = true;
    else throw new Error(`unknown argument ${arg}`);
  }
  if (!args.api) throw new Error('--api <https origin of the NON-production API> (or K6_API_URL) is required');
  assertNonProduction('--api', args.api);
  for (const key of ['learners', 'experts', 'concurrency']) {
    if (!Number.isInteger(args[key]) || args[key] < 0) throw new Error(`--${key} must be a non-negative integer`);
  }
  if (args.concurrency < 1) throw new Error('--concurrency must be >= 1');
  return args;
}

/** Every account a run of this size uses, learners first (the probe is index 0 of its own kind). */
export function listAccounts({ learners, experts, prefix, domain }) {
  const plan = accountPlan({ learners, experts });
  const accounts = [accountFor('probe', 0, { prefix, domain })];
  for (let i = 0; i < plan.learners; i += 1) accounts.push(accountFor('learner', i, { prefix, domain }));
  for (let i = 0; i < plan.experts; i += 1) accounts.push(accountFor('expert', i, { prefix, domain }));
  return accounts;
}

/** Target exam date 60 days out (required for learners; must be today or later). */
export function targetExamDate(now = new Date()) {
  return new Date(now.getTime() + 60 * 86400000).toISOString().slice(0, 10);
}

export function buildUserBody(account, { professionId, password, now = new Date() }) {
  return {
    name: account.name,
    email: account.email,
    role: account.kind === 'expert' ? 'expert' : 'learner',
    professionId,
    mobileNumber: null,
    targetExamDate: account.kind === 'expert' ? null : targetExamDate(now),
    password,
    sendInvite: false,
  };
}

/** Every credit field explicit, exactly like the Writing QA harness: set the pools, change nothing else. */
export function buildCreditBody({ credits = DEFAULT_CREDITS, expiresAt, reason }) {
  return {
    sharedCreditsDelta: 0,
    flexibleCreditsDelta: 0,
    writingOnlyCreditsDelta: 0,
    speakingOnlyCreditsDelta: 0,
    listeningTestsDelta: 0,
    readingTestsDelta: 0,
    mockExamsDelta: 0,
    sharedCreditsSet: credits.shared,
    flexibleCreditsSet: credits.flexible,
    writingOnlyCreditsSet: credits.writingOnly,
    speakingOnlyCreditsSet: credits.speakingOnly,
    listeningTestsSet: credits.listeningTests,
    readingTestsSet: credits.readingTests,
    mockExamsSet: credits.mockExams,
    expiresAt,
    reason,
  };
}

/** Run `fn` over `items` with at most `limit` in flight; results keep input order. */
export async function runPool(items, limit, fn) {
  const results = new Array(items.length);
  let next = 0;
  const worker = async () => {
    for (;;) {
      const index = next;
      next += 1;
      if (index >= items.length) return;
      results[index] = await fn(items[index], index);
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return results;
}

const isExistsError = (error) => error?.status === 409
  || (error?.status === 400 && /exist|already|duplicate/i.test(`${error?.code ?? ''} ${error?.message ?? ''}`));

/** A thin admin HTTP client. `fetchImpl` is injectable for tests. Never logs a token or a password. */
export function createAdminClient({ api, email, password, fetchImpl = globalThis.fetch, sleep = (ms) => new Promise((r) => setTimeout(r, ms)) }) {
  let token = null;
  const send = async (method, path, body, bearer) => {
    const response = await fetchImpl(`${api}${path}`, {
      method,
      headers: {
        'Content-Type': 'application/json',
        'X-OET-Client-Platform': 'web',
        'X-OET-Device-Id': 'load-seed-admin',
        ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    let json = null;
    try { json = await response.json(); } catch (error) { json = null; }
    return { status: response.status, ok: response.ok, json };
  };
  const apiError = (method, path, r) => Object.assign(
    new Error(`${method} ${path.split('?')[0]}: HTTP ${r.status}${r.json?.code ? ` (${r.json.code})` : ''}`),
    { status: r.status, code: r.json?.code ?? r.json?.errorCode ?? null, detail: r.json?.message ?? null },
  );
  const signIn = async () => {
    const r = await send('POST', '/v1/auth/sign-in', { email, password, rememberMe: false });
    if (!r.ok || !r.json?.accessToken) throw apiError('POST', '/v1/auth/sign-in', r);
    token = r.json.accessToken;
  };
  const request = async (method, path, body) => {
    if (!token) await signIn();
    for (let attempt = 1; ; attempt += 1) {
      let r = await send(method, path, body, token);
      if (r.status === 401) {
        await signIn();
        r = await send(method, path, body, token);
      }
      if (r.ok) return r.json;
      if ((r.status === 429 || r.status >= 500) && attempt < 5) {
        await sleep(500 * attempt);
        continue;
      }
      throw apiError(method, path, r);
    }
  };
  return { get: (path) => request('GET', path), post: (path, body = {}) => request('POST', path, body) };
}

const userRows = (page) => (Array.isArray(page) ? page : (page?.items ?? page?.users ?? page?.rows ?? []));

/** Find a user id by email through the admin list (search narrows server-side; match is exact). */
export async function findUserId(client, email) {
  const rows = userRows(await client.get(`/v1/admin/users?search=${encodeURIComponent(email)}&page=1&pageSize=20`));
  const row = rows.find((r) => String(r?.email ?? '').toLowerCase() === email.toLowerCase());
  return row?.id ?? row?.userId ?? null;
}

/**
 * Create every account (idempotent) and give learners their credit pools. Returns counts only: no
 * identifier of a real person exists here, but no password or token is ever included either.
 */
export async function seedAccounts(client, options) {
  const { password, professionId, concurrency = 6, now = new Date(), log = () => {}, credits = DEFAULT_CREDITS } = options;
  if (!password || password.length < 8) throw new Error('OET_LOAD_PASSWORD must be at least 8 characters');
  const accounts = listAccounts(options);
  const expiresAt = new Date(now.getTime() + 90 * 86400000).toISOString();
  const summary = { total: accounts.length, created: 0, existing: 0, credited: 0, failed: [] };

  await runPool(accounts, concurrency, async (account, index) => {
    try {
      let userId = null;
      try {
        const created = await client.post('/v1/admin/users', buildUserBody(account, { professionId, password, now }));
        userId = created?.id ?? created?.userId ?? null;
        summary.created += 1;
      } catch (error) {
        if (!isExistsError(error)) throw error;
        summary.existing += 1;
        userId = await findUserId(client, account.email);
      }
      if (account.kind !== 'expert') {
        if (!userId) throw new Error(`no user id for ${account.email}`);
        await client.post(
          `/v1/admin/ai-package-credits/${encodeURIComponent(userId)}/adjust`,
          buildCreditBody({ credits, expiresAt, reason: 'Fleet load test: disposable learner' }),
        );
        summary.credited += 1;
      }
      if ((index + 1) % 100 === 0) log(`seeded ${index + 1}/${accounts.length}`);
    } catch (error) {
      summary.failed.push({ email: account.email, error: String(error.message ?? error).slice(0, 200) });
    }
  });
  return summary;
}

/** email (lower case) -> user id for every user whose name or email matches `search`, paged. */
export async function mapUserIds(client, search, pageSize = 500) {
  const ids = new Map();
  for (let page = 1; page <= 100; page += 1) {
    const rows = userRows(await client.get(`/v1/admin/users?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`));
    for (const row of rows) {
      const id = row?.id ?? row?.userId ?? null;
      if (id && row?.email) ids.set(String(row.email).toLowerCase(), id);
    }
    if (rows.length < pageSize) break;
  }
  return ids;
}

/** Delete every account of the run (admin delete; the same call the QA cleanup uses). */
export async function purgeAccounts(client, options) {
  const { concurrency = 6, log = () => {} } = options;
  const accounts = listAccounts(options);
  const summary = { total: accounts.length, deleted: 0, missing: 0, failed: [] };
  const idByEmail = await mapUserIds(client, `${options.prefix}-`);
  await runPool(accounts, concurrency, async (account, index) => {
    try {
      const userId = idByEmail.get(account.email.toLowerCase()) ?? null;
      if (!userId) {
        summary.missing += 1;
        return;
      }
      await client.post(`/v1/admin/users/${encodeURIComponent(userId)}/delete`, { reason: 'Fleet load test cleanup' });
      summary.deleted += 1;
      if ((index + 1) % 100 === 0) log(`purged ${index + 1}/${accounts.length}`);
    } catch (error) {
      summary.failed.push({ email: account.email, error: String(error.message ?? error).slice(0, 200) });
    }
  });
  return summary;
}
