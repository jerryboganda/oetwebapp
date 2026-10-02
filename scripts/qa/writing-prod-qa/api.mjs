// Admin-side HTTP glue of the Writing production QA (node fetch against the API host). One admin client per run:
// an admin sign-in signs that admin out of every other session, so the client signs in lazily, once, and again
// only after a 401. Bearer tokens are never logged; errors carry method, path, status and the API error code only.
import { API_URL, ENDPOINTS, FAULT_FLAGS, FREE_SAMPLES_FLAG, PROVIDERS } from './contract.mjs';

export class ApiError extends Error {
  constructor(method, path, status, code) {
    super(`${method} ${path.split('?')[0]}: HTTP ${status} (${code})`);
    this.status = status;
    this.code = code;
  }
}
const errorCode = (json) => json?.errorCode ?? json?.code ?? json?.error?.code ?? json?.error ?? 'request_failed';

/**
 * readOnly = the discover suite: any method other than GET throws before it reaches the network (the admin
 * sign-in POST is the only exception). `calls` records every request made, for the read-only unit test.
 */
export function createAdminClient({ email, password, readOnly = false, fetchImpl = globalThis.fetch, baseUrl = API_URL }) {
  let token = null;
  const calls = [];
  async function send(method, path, body, bearer) {
    calls.push({ method, path: path.split('?')[0] });
    const res = await fetchImpl(`${baseUrl}${path}`, {
      method,
      headers: {
        'Content-Type': 'application/json',
        'X-OET-Client-Platform': 'web',
        ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      signal: AbortSignal.timeout(60_000),
    });
    return { status: res.status, ok: res.ok, json: await res.json().catch(() => null) };
  }
  // Concurrent callers share ONE sign-in (a second admin sign-in would revoke the first session).
  let signingIn = null;
  function signIn(staleToken) {
    if (token && token !== staleToken) return Promise.resolve();
    signingIn ??= (async () => {
      if (!email || !password) throw new Error('OET_ADMIN_EMAIL / OET_ADMIN_PASSWORD are not available to this job');
      const r = await send('POST', ENDPOINTS.signIn, { email, password, rememberMe: false });
      if (!r.ok || !r.json?.accessToken) throw new ApiError('POST', ENDPOINTS.signIn, r.status, errorCode(r.json));
      token = r.json.accessToken;
    })().finally(() => { signingIn = null; });
    return signingIn;
  }
  async function request(method, path, body) {
    if (readOnly && method !== 'GET') throw new Error(`the read-only discover client refused ${method} ${path}`);
    if (!token) await signIn(null);
    const used = token;
    let r = await send(method, path, body, used);
    if (r.status === 401) {
      await signIn(used);
      r = await send(method, path, body, token);
    }
    if (!r.ok) throw new ApiError(method, path, r.status, errorCode(r.json));
    return r.json;
  }
  return {
    get: (path) => request('GET', path),
    post: (path, body = {}) => request('POST', path, body),
    put: (path, body) => request('PUT', path, body),
    calls,
  };
}

// ---- Discovery (GET only) -----------------------------------------------------------------------------------------

/** Everything discovery and preflight read, reduced to the fields the harness uses (no task content). */
export async function discover(admin) {
  const [catalog, compatibility, integrity, flags, options, providers, writingProvider, circuits] = await Promise.all([
    admin.get(ENDPOINTS.professionCatalog),
    admin.get(ENDPOINTS.catalogueCompatibility),
    admin.get(ENDPOINTS.loadIntegrity),
    admin.get(ENDPOINTS.flags),
    admin.get(ENDPOINTS.writingOptions),
    admin.get(ENDPOINTS.providers),
    admin.get(ENDPOINTS.writingProvider),
    admin.get(ENDPOINTS.circuits),
  ]);
  const qaFlag = (key) => key === FREE_SAMPLES_FLAG || Object.values(FAULT_FLAGS).some((prefix) => String(key).startsWith(prefix));
  return {
    catalog: { professions: (catalog?.professions ?? []).map(({ id, label, isActive }) => ({ id, label, isActive })) },
    compatibility: {
      publishedScenarios: compatibility?.publishedScenarios ?? null,
      rows: (compatibility?.rows ?? []).map((r) => ({
        scenarioId: r.scenarioId, taskTitle: r.taskTitle, profession: r.profession, letterType: r.letterType,
        publicationStatus: r.publicationStatus, candidateVisible: r.candidateVisible, publishReady: r.publishReady,
      })),
    },
    integrity: { loadFailed: integrity?.loadFailed ?? null, rows: (integrity?.rows ?? []).map((r) => ({ scenarioId: r.scenarioId, loadOk: r.loadOk, failures: r.failures })) },
    flags: (Array.isArray(flags) ? flags : []).filter((f) => qaFlag(f.key)).map(({ id, key, enabled, rolloutPercentage }) => ({ id, key, enabled, rolloutPercentage })),
    options: options ? { aiGradingEnabled: options.aiGradingEnabled, freeTierEnabled: options.freeTierEnabled, killSwitchReason: options.killSwitchReason ?? null } : null,
    providers: (Array.isArray(providers) ? providers : [])
      .filter((p) => Object.values(PROVIDERS).includes(String(p.code ?? '').toLowerCase()))
      .map((p) => ({ code: p.code, isActive: p.isActive, defaultModel: p.defaultModel, lastTestStatus: p.lastTestStatus })),
    writingProvider,
    circuits,
  };
}

export const freeSamplesEnabled = (flags) => flags.some((f) => f.key === FREE_SAMPLES_FLAG && f.enabled === true);

export async function readHealthInputs(admin) {
  const [writingProvider, circuits] = await Promise.all([admin.get(ENDPOINTS.writingProvider), admin.get(ENDPOINTS.circuits)]);
  return { writingProvider, circuits };
}

/** Audited admin reset of one provider circuit (preflight_repair; never used on the Max circuit). */
export async function resetCircuit(admin, key) {
  if (key === PROVIDERS.claude) throw new Error('the Max subscription circuit is never reset by QA: an open Max circuit is a FAIL');
  return admin.post(ENDPOINTS.circuitReset(key), {});
}

// ---- Provisioning -------------------------------------------------------------------------------------------------

/** A pre-verified disposable learner (password set => no invite email, email marked verified). */
export async function createLearner(admin, { email, name, professionId, password }) {
  const targetExamDate = new Date(Date.now() + 60 * 86_400_000).toISOString().slice(0, 10);
  const created = await admin.post(ENDPOINTS.users, {
    name, email, role: 'learner', professionId, mobileNumber: null, targetExamDate, password, sendInvite: false,
  });
  if (!created?.id) throw new Error(`POST ${ENDPOINTS.users} returned no user id`);
  return { userId: created.id, email };
}

/** Sets the learner's credit pools exactly (every field explicit): `writing` Writing-only credits, all else 0. */
export function setWritingCredits(admin, userId, writing, expiresAt) {
  return admin.post(ENDPOINTS.adminCreditsAdjust(userId), {
    sharedCreditsDelta: 0, flexibleCreditsDelta: 0, writingOnlyCreditsDelta: 0, speakingOnlyCreditsDelta: 0,
    listeningTestsDelta: 0, readingTestsDelta: 0, mockExamsDelta: 0,
    sharedCreditsSet: 0, flexibleCreditsSet: 0, writingOnlyCreditsSet: writing, speakingOnlyCreditsSet: 0,
    listeningTestsSet: null, readingTestsSet: null, mockExamsSet: 0,
    expiresAt, reason: 'Writing production QA: disposable learner',
  });
}

export const creditSnapshot = (admin, userId) => admin.get(ENDPOINTS.adminCredits(userId));

export async function usageRows(admin, query) {
  const page = await admin.get(ENDPOINTS.usage(query));
  return (page?.rows ?? []).map((r) => ({
    id: r.id, userId: r.userId, providerId: r.providerId, model: r.model, outcome: r.outcome, errorCode: r.errorCode,
    failoverTrace: r.failoverTrace ?? null, latencyMs: r.latencyMs, retryCount: r.retryCount, createdAt: r.createdAt,
  }));
}

/** WAI-05 fault flag for ONE learner: fails the first `runs` grading runs of each of their submissions. */
export async function enableFaultFlag(admin, { kind, userId, runs = 1, runKey }) {
  const key = `${FAULT_FLAGS[kind]}${userId}`;
  try {
    const created = await admin.post(ENDPOINTS.flags, {
      name: `QA ${kind} grading fault ${userId}`, key, flagType: null, enabled: true, rolloutPercentage: runs,
      description: `Writing production QA run ${runKey}; disposable learner only`, owner: 'writing-prod-qa',
    });
    return { id: created.id, key };
  } catch (error) {
    if (error.status !== 409) throw error;
    const existing = (await admin.get(ENDPOINTS.flags)).find((f) => f.key === key);
    await admin.put(ENDPOINTS.flag(existing.id), { enabled: true, rolloutPercentage: runs });
    return { id: existing.id, key };
  }
}

export const deactivateFlag = (admin, id) => admin.post(ENDPOINTS.flagDeactivate(id), {});

/** Permanent purge: call only after the learner's evidence (usage rows, ledger) has been written. */
export const deleteLearner = (admin, userId, runKey) => admin.post(ENDPOINTS.userDelete(userId), { reason: `Writing production QA run ${runKey} cleanup` });
