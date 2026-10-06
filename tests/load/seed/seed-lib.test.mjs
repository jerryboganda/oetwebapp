import assert from 'node:assert/strict';
import test from 'node:test';
import {
  DEFAULT_CREDITS, buildCreditBody, buildUserBody, createAdminClient, findUserId, listAccounts, mapUserIds, parseArgs,
  purgeAccounts, runPool, seedAccounts, targetExamDate,
} from './seed-lib.mjs';
import { main } from './seed-accounts.mjs';

const NOW = new Date('2026-10-06T00:00:00Z');

test('parseArgs reads flags and the environment, and refuses production', () => {
  const a = parseArgs(['--api', 'https://api.staging.example', '--learners', '10', '--experts', '2', '--purge'], {});
  assert.equal(a.learners, 10);
  assert.equal(a.experts, 2);
  assert.equal(a.purge, true);
  assert.equal(a.profession, 'medicine');
  assert.equal(parseArgs([], { K6_API_URL: 'https://api.staging.example' }).api, 'https://api.staging.example');
  assert.throws(() => parseArgs(['--api', 'https://api.oetwithdrhesham.co.uk'], {}), /production host/);
  assert.throws(() => parseArgs([], {}), /--api/);
  assert.throws(() => parseArgs(['--api', 'https://x.test', '--learners', '-1'], {}), /learners/);
  assert.throws(() => parseArgs(['--api', 'https://x.test', '--concurrency', '0'], {}), /concurrency/);
  assert.throws(() => parseArgs(['--api', 'https://x.test', '--nope'], {}), /unknown argument/);
  assert.throws(() => parseArgs(['--api'], {}), /needs a value/);
});

test('listAccounts is probe + learners + experts with distinct emails', () => {
  const accounts = listAccounts({ learners: 3, experts: 2, prefix: 'lt', domain: 'x.test' });
  assert.deepEqual(accounts.map((a) => a.email), [
    'lt-probe-0000@x.test', 'lt-learner-0000@x.test', 'lt-learner-0001@x.test', 'lt-learner-0002@x.test',
    'lt-expert-000@x.test', 'lt-expert-001@x.test',
  ]);
});

test('buildUserBody matches the admin create contract', () => {
  const learner = buildUserBody(listAccounts({ learners: 1, experts: 0, prefix: 'lt', domain: 'x.test' })[1], {
    professionId: 'medicine', password: 'Passw0rd!x', now: NOW,
  });
  assert.deepEqual(learner, {
    name: 'Load learner 0000', email: 'lt-learner-0000@x.test', role: 'learner', professionId: 'medicine',
    mobileNumber: null, targetExamDate: '2026-12-05', password: 'Passw0rd!x', sendInvite: false,
  });
  const expert = buildUserBody(listAccounts({ learners: 0, experts: 1, prefix: 'lt', domain: 'x.test' })[1], {
    professionId: 'medicine', password: 'Passw0rd!x', now: NOW,
  });
  assert.equal(expert.role, 'expert');
  assert.equal(expert.targetExamDate, null);
  assert.equal(targetExamDate(NOW), '2026-12-05');
});

test('buildCreditBody sets every pool explicitly and changes nothing by delta', () => {
  const body = buildCreditBody({ expiresAt: '2027-01-04T00:00:00.000Z', reason: 'r' });
  assert.equal(body.sharedCreditsSet, DEFAULT_CREDITS.shared);
  assert.equal(body.speakingOnlyCreditsSet, 200);
  assert.equal(body.sharedCreditsDelta + body.flexibleCreditsDelta + body.writingOnlyCreditsDelta + body.speakingOnlyCreditsDelta
    + body.listeningTestsDelta + body.readingTestsDelta + body.mockExamsDelta, 0);
  assert.equal(Object.keys(body).length, 16);
});

test('runPool bounds concurrency and keeps input order', async () => {
  let inFlight = 0;
  let peak = 0;
  const results = await runPool([1, 2, 3, 4, 5, 6, 7], 3, async (n) => {
    inFlight += 1;
    peak = Math.max(peak, inFlight);
    await new Promise((r) => setTimeout(r, 5));
    inFlight -= 1;
    return n * 2;
  });
  assert.deepEqual(results, [2, 4, 6, 8, 10, 12, 14]);
  assert.ok(peak <= 3);
  assert.deepEqual(await runPool([], 3, async () => 1), []);
});

function fakeClient({ existing = new Set(), failCredits = new Set() } = {}) {
  const calls = [];
  const users = new Map();
  return {
    calls,
    users,
    async post(path, body) {
      calls.push(['POST', path]);
      if (path === '/v1/admin/users') {
        if (existing.has(body.email)) throw Object.assign(new Error('exists'), { status: 409, code: 'email_already_exists' });
        const id = `usr_${users.size + 1}`;
        users.set(body.email, id);
        return { id };
      }
      const credit = path.match(/^\/v1\/admin\/ai-package-credits\/([^/]+)\/adjust$/);
      if (credit) {
        if (failCredits.has(credit[1])) throw Object.assign(new Error('boom'), { status: 500 });
        return {};
      }
      const del = path.match(/^\/v1\/admin\/users\/([^/]+)\/delete$/);
      if (del) return {};
      throw new Error(`unexpected ${path}`);
    },
    async get(path) {
      calls.push(['GET', path]);
      const url = new URL(`https://x${path}`);
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const rows = [...users.entries()].concat([...existing].map((e, i) => [e, `old_${i}`]))
        .filter(([email]) => email.toLowerCase().includes(search))
        .map(([email, id]) => ({ id, email }));
      return { items: rows };
    },
  };
}

test('seedAccounts creates accounts, credits learners but not experts, and is idempotent for existing ones', async () => {
  const existing = new Set(['lt-learner-0000@x.test']);
  const client = fakeClient({ existing });
  const summary = await seedAccounts(client, {
    learners: 2, experts: 1, prefix: 'lt', domain: 'x.test', password: 'Passw0rd!x', professionId: 'medicine', now: NOW,
  });
  assert.equal(summary.total, 4);
  assert.equal(summary.created, 3); // probe, learner 1, expert 0
  assert.equal(summary.existing, 1);
  assert.equal(summary.credited, 3); // probe + 2 learners; the expert gets no credits
  assert.deepEqual(summary.failed, []);
  const creditCalls = client.calls.filter(([, path]) => path.endsWith('/adjust'));
  assert.equal(creditCalls.length, 3);
});

test('seedAccounts reports failures per account and never throws', async () => {
  const client = fakeClient({ failCredits: new Set(['usr_2']) });
  const summary = await seedAccounts(client, {
    learners: 1, experts: 0, prefix: 'lt', domain: 'x.test', password: 'Passw0rd!x', professionId: 'medicine', now: NOW,
  });
  assert.equal(summary.failed.length, 1);
  assert.match(summary.failed[0].email, /learner-0000/);
  assert.ok(!JSON.stringify(summary).includes('Passw0rd'));
});

test('seedAccounts requires a usable password', async () => {
  await assert.rejects(seedAccounts(fakeClient(), { learners: 1, experts: 0, prefix: 'lt', domain: 'x.test', password: 'short', professionId: 'm' }), /at least 8/);
});

test('purgeAccounts lists once by prefix and deletes only what exists', async () => {
  const client = fakeClient();
  await seedAccounts(client, { learners: 2, experts: 0, prefix: 'lt', domain: 'x.test', password: 'Passw0rd!x', professionId: 'm', now: NOW });
  client.calls.length = 0;
  const summary = await purgeAccounts(client, { learners: 3, experts: 1, prefix: 'lt', domain: 'x.test' });
  assert.equal(summary.total, 5);
  assert.equal(summary.deleted, 3); // probe + 2 learners exist
  assert.equal(summary.missing, 2);
  assert.equal(client.calls.filter(([m, p]) => m === 'GET' && p.startsWith('/v1/admin/users?')).length, 1);
});

test('mapUserIds pages until a short page and findUserId matches the exact email', async () => {
  const pages = [
    { items: Array.from({ length: 2 }, (_, i) => ({ id: `u${i}`, email: `a${i}@x.test` })) },
    { items: [{ id: 'u2', email: 'a2@x.test' }] },
  ];
  let call = 0;
  const client = { get: async () => pages[call++] };
  const map = await mapUserIds(client, 'a', 2);
  assert.equal(map.size, 3);
  assert.equal(map.get('a2@x.test'), 'u2');
  const one = { get: async () => [{ id: 'x', email: 'Other@x.test' }, { id: 'y', email: 'me@x.test' }] };
  assert.equal(await findUserId(one, 'ME@x.test'), 'y');
  assert.equal(await findUserId({ get: async () => [] }, 'none@x.test'), null);
});

test('the admin client signs in once, retries 429 / 5xx and re-signs-in after a 401', async () => {
  const seen = [];
  let attempts = 0;
  const fetchImpl = async (url, init) => {
    seen.push(`${init.method} ${url.replace('https://api.test', '')}`);
    const respond = (status, json) => ({ status, ok: status >= 200 && status < 300, json: async () => json });
    if (url.endsWith('/v1/auth/sign-in')) return respond(200, { accessToken: `t${seen.length}` });
    attempts += 1;
    if (attempts === 1) return respond(429, {});
    if (attempts === 2) return respond(401, {});
    return respond(200, { ok: true });
  };
  const client = createAdminClient({ api: 'https://api.test', email: 'a@x.test', password: 'p', fetchImpl, sleep: async () => {} });
  assert.deepEqual(await client.get('/v1/admin/users'), { ok: true });
  assert.equal(seen.filter((s) => s.endsWith('sign-in')).length, 2);
});

test('the admin client surfaces API errors without a body', async () => {
  const fetchImpl = async (url) => (url.endsWith('/v1/auth/sign-in')
    ? { status: 200, ok: true, json: async () => ({ accessToken: 't' }) }
    : { status: 403, ok: false, json: async () => ({ code: 'forbidden' }) });
  const client = createAdminClient({ api: 'https://api.test', email: 'a', password: 'p', fetchImpl, sleep: async () => {} });
  await assert.rejects(client.post('/v1/admin/users', {}), (error) => error.status === 403 && /forbidden/.test(error.message));
});

test('the CLI dry run sends nothing and the production guard fires before anything else', async () => {
  const dry = await main(['--api', 'https://api.staging.example', '--learners', '5', '--experts', '1', '--dry-run'], {});
  assert.equal(dry.dryRun, true);
  assert.equal(dry.plan.accounts, 7);
  await assert.rejects(main(['--api', 'https://app.oetwithdrhesham.co.uk', '--dry-run'], {}), /production host/);
  await assert.rejects(main(['--api', 'https://api.staging.example'], {}), /OET_LOAD_ADMIN_EMAIL/);
  await assert.rejects(
    main(['--api', 'https://api.staging.example'], { OET_LOAD_ADMIN_EMAIL: 'a', OET_LOAD_ADMIN_PASSWORD: 'b' }),
    /OET_LOAD_PASSWORD/,
  );
});
