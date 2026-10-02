import { createAdminClient, discover, enableFaultFlag, resetCircuit } from './api.mjs';
import { ENDPOINTS, PROVIDERS } from './contract.mjs';

type Call = { url: string; method: string; body?: string };

// A fake API host: sign-in answers a token, every other path answers `answers[path]` (or {}).
function fakeFetch(answers: Record<string, unknown> = {}, statusFor: (call: Call, n: number) => number = () => 200) {
  const calls: Call[] = [];
  const impl = async (url: string, init: { method: string; body?: string }) => {
    const call = { url, method: init.method, body: init.body };
    calls.push(call);
    const path = new URL(url).pathname;
    const status = statusFor(call, calls.length);
    const json = path === ENDPOINTS.signIn ? { accessToken: `token-${calls.length}` } : (answers[path] ?? {});
    return { status, ok: status < 400, json: async () => json };
  };
  return { calls, impl };
}

describe('admin client', () => {
  it('keeps the discover suite strictly read-only (GET only, plus the admin sign-in)', async () => {
    const { calls, impl } = fakeFetch({ [ENDPOINTS.flags]: [{ id: 'f1', key: 'free_samples_enabled', enabled: true }, { id: 'f2', key: 'other', enabled: true }] });
    const admin = createAdminClient({ email: 'a@x', password: 'p', readOnly: true, fetchImpl: impl as never });
    const found = await discover(admin);
    expect(found.flags).toEqual([{ id: 'f1', key: 'free_samples_enabled', enabled: true, rolloutPercentage: undefined }]);
    const writes = calls.filter((c) => c.method !== 'GET').map((c) => new URL(c.url).pathname);
    expect(writes).toEqual([ENDPOINTS.signIn]);
    expect(calls.filter((c) => c.method === 'GET')).toHaveLength(8);
    await expect(admin.post(ENDPOINTS.flags, {})).rejects.toThrow(/read-only/);
    await expect(enableFaultFlag(admin, { kind: 'all', userId: 'usr_1', runKey: '1' })).rejects.toThrow(/read-only/);
    expect(calls.filter((c) => c.method !== 'GET')).toHaveLength(1);
  });

  it('signs in once and again only after a 401, never leaking the token in errors', async () => {
    let failed = false;
    const { calls, impl } = fakeFetch({}, (call) => {
      if (!failed && call.method === 'GET') { failed = true; return 401; }
      return 200;
    });
    const admin = createAdminClient({ email: 'a@x', password: 'p', fetchImpl: impl as never });
    await admin.get(ENDPOINTS.writingProvider);
    await admin.get(ENDPOINTS.circuits);
    expect(calls.map((c) => `${c.method} ${new URL(c.url).pathname}`)).toEqual([
      `POST ${ENDPOINTS.signIn}`, `GET ${ENDPOINTS.writingProvider}`, `POST ${ENDPOINTS.signIn}`, `GET ${ENDPOINTS.writingProvider}`, `GET ${ENDPOINTS.circuits}`,
    ]);
    const broken = createAdminClient({ email: 'a@x', password: 'p', fetchImpl: fakeFetch({}, (c) => (c.method === 'GET' ? 500 : 200)).impl as never });
    const error = await broken.get(ENDPOINTS.circuits).catch((e: Error) => e);
    expect(String(error)).toBe(`Error: GET ${ENDPOINTS.circuits}: HTTP 500 (request_failed)`);
    expect(String(error)).not.toMatch(/token/);
  });

  it('refuses to run without admin credentials and never resets the Max circuit', async () => {
    await expect(createAdminClient({ email: '', password: '', fetchImpl: fakeFetch().impl as never }).get(ENDPOINTS.circuits)).rejects.toThrow(/OET_ADMIN_EMAIL/);
    const admin = createAdminClient({ email: 'a@x', password: 'p', fetchImpl: fakeFetch().impl as never });
    await expect(resetCircuit(admin, PROVIDERS.claude)).rejects.toThrow(/never reset/);
  });
});
