// The ONE place that names what the fleet harness talks to: paths, HTTP methods, request classes
// (which thresholds apply) and which responses are expected. When the API renames a route, change it
// here (and docs/ops/LOAD-TESTING.md); nothing else in the harness hard-codes a path.
//
// Every entry was read from the backend source (backend/src/OetLearner.Api/Endpoints/*.cs); the
// smoke profile's status matrix then proves each one against a live stack before a long run.
//
// `class` decides the threshold group:
//   critical-read  bootstrap / dashboard / entitlement / ...          p95 <= 500 ms, p99 <= 1.5 s
//   exam-save      per-answer / per-draft autosave                      p95 <= 500 ms
//   submission     exam submit / Writing submit                         p95 <= 500 ms
//   live-setup     speaking session + realtime + room setup             p95 <= 1 s
//   exam-start, live-turn, ai-roundtrip, search, auth, hub, other       tracked, not latency-gated
//
// `ok` statuses are success; `domain` statuses (optionally limited to `domainCodes`) are documented
// business refusals, e.g. the Reading Part B/C window not being open yet. Anything else is an
// unexpected failure (see classify.mjs).

const act = (id, method, path, klass, more = {}) => Object.freeze({
  id,
  method,
  path,
  name: `${method} ${path}`,
  class: klass,
  ok: [200],
  domain: [],
  domainCodes: [],
  ...more,
});

export const READING_DOMAIN_CODES = Object.freeze([
  'part_a_locked', 'part_bc_not_open', 'part_bc_break_not_resumed', 'answer_window_closed',
  'attempt_deadline_passed', 'attempt_not_in_progress', 'question_out_of_scope',
]);

export const ACTIONS = Object.freeze({
  // ---- auth --------------------------------------------------------------------------------------
  signIn: act('sign-in', 'POST', '/v1/auth/sign-in', 'auth', { auth: true }),
  refresh: act('refresh', 'POST', '/v1/auth/refresh', 'auth', { auth: true }),

  // ---- the shared learner mix ---------------------------------------------------------------------
  bootstrap: act('bootstrap', 'GET', '/v1/me/bootstrap', 'critical-read'),
  dashboard: act('dashboard', 'GET', '/v1/learner/dashboard', 'critical-read'),
  entitlement: act('entitlement', 'GET', '/v1/me/entitlement-snapshot', 'critical-read'),
  subscription: act('subscription', 'GET', '/v1/subscriptions/me', 'critical-read'),
  readiness: act('readiness', 'GET', '/v1/readiness', 'critical-read'),
  studyPlan: act('study-plan', 'GET', '/v1/study-plan', 'critical-read'),
  engagement: act('engagement', 'GET', '/v1/learner/engagement', 'critical-read'),
  progress: act('progress', 'GET', '/v1/progress', 'critical-read'),
  notifications: act('notifications', 'GET', '/v1/notifications', 'other'),
  credits: act('credits', 'GET', '/v1/me/ai-package-credits', 'other'),
  search: act('search', 'GET', '/v1/search', 'search'),
  papers: act('papers', 'GET', '/v1/papers', 'other'),

  // ---- Reading (server-authoritative attempt; Exam mode: Part A first, B/C after its window) ------
  readingHome: act('reading-home', 'GET', '/v1/reading-papers/home', 'other'),
  readingStructure: act('reading-structure', 'GET', '/v1/reading-papers/papers/:id/structure', 'other'),
  readingStart: act('reading-start', 'POST', '/v1/reading-papers/papers/:id/attempts', 'exam-start', { domain: [400, 402, 403, 404, 409] }),
  readingSave: act('reading-save-answer', 'PUT', '/v1/reading-papers/attempts/:id/answers/:qid', 'exam-save', {
    ok: [204], domain: [400], domainCodes: READING_DOMAIN_CODES,
  }),
  readingGetAttempt: act('reading-get-attempt', 'GET', '/v1/reading-papers/attempts/:id', 'other'),
  readingSubmit: act('reading-submit', 'POST', '/v1/reading-papers/attempts/:id/submit', 'submission', {
    domain: [400], domainCodes: READING_DOMAIN_CODES,
  }),

  // ---- Listening ----------------------------------------------------------------------------------
  listeningSession: act('listening-session', 'GET', '/v1/listening-papers/papers/:id/session', 'other', { domain: [400, 402, 403, 404, 409] }),
  listeningStart: act('listening-start', 'POST', '/v1/listening-papers/papers/:id/attempts', 'exam-start', { domain: [400, 402, 403, 404, 409] }),
  listeningSave: act('listening-save-answer', 'PUT', '/v1/listening-papers/attempts/:id/answers/:qid', 'exam-save', {
    ok: [204], domain: [400, 403, 409],
  }),

  // ---- Writing (draft CAS, then submit with an idempotency key) ------------------------------------
  writingScenarios: act('writing-scenarios', 'GET', '/v1/writing/scenarios', 'other'),
  writingEligibility: act('writing-eligibility', 'GET', '/v1/writing/scenarios/:id/eligibility', 'exam-start', { domain: [402, 403, 404, 409] }),
  writingDraftSave: act('writing-draft-save', 'PUT', '/v1/writing/drafts/:id/:mode', 'exam-save'),
  writingDraftGet: act('writing-draft-get', 'GET', '/v1/writing/drafts/:id/:mode', 'other'),
  writingSubmit: act('writing-submit', 'POST', '/v1/writing/submissions', 'submission', { ok: [200, 201] }),
  writingSubmissionGet: act('writing-submission-get', 'GET', '/v1/writing/submissions/:id', 'other'),

  // ---- AI speaking (control plane only: provider media never passes through the API) ---------------
  speakingCards: act('speaking-cards', 'GET', '/v1/speaking/role-play-cards', 'other'),
  speakingConsentRecord: act('speaking-consent-record', 'POST', '/v1/speaking/consents', 'other', { domain: [400, 409] }),
  speakingCreate: act('speaking-create', 'POST', '/v1/speaking/sessions', 'live-setup', { domain: [402, 403, 404, 409] }),
  speakingConsent: act('speaking-consent', 'POST', '/v1/speaking/sessions/:id/consent', 'live-setup'),
  speakingStartWarmup: act('speaking-start-warmup', 'POST', '/v1/speaking/sessions/:id/start-warmup', 'live-setup', { domain: [409] }),
  speakingFinishWarmup: act('speaking-finish-warmup', 'POST', '/v1/speaking/sessions/:id/finish-warmup', 'live-setup', {
    ok: [200, 202], shedOk: true, domain: [402, 409],
  }),
  speakingStartRoleplay: act('speaking-start-roleplay', 'POST', '/v1/speaking/sessions/:id/start-roleplay', 'live-setup', {
    shedOk: true, domain: [409],
  }),
  speakingGet: act('speaking-get', 'GET', '/v1/speaking/sessions/:id', 'other'),
  livePreflight: act('live-preflight', 'GET', '/v1/speaking/realtime/sessions/:id/preflight', 'live-setup', { shedOk: true, domain: [409, 503] }),
  liveOpenAiOffer: act('live-openai-offer', 'POST', '/v1/speaking/realtime/sessions/:id/openai/offer', 'live-setup', { ok: [200, 202], shedOk: true, domain: [409] }),
  liveGeminiToken: act('live-gemini-token', 'POST', '/v1/speaking/realtime/sessions/:id/gemini/token', 'live-setup', { ok: [200, 202], shedOk: true, domain: [409] }),
  liveTurn: act('live-turn', 'POST', '/v1/speaking/realtime/sessions/:id/turns', 'live-turn', { domain: [409] }),
  liveTranscript: act('live-transcript', 'POST', '/v1/speaking/realtime/sessions/:id/transcript', 'live-turn', { domain: [409] }),
  liveAudioTurn: act('live-audio-turn', 'POST', '/v1/speaking/realtime/sessions/:id/audio-turns', 'live-turn', { domain: [409] }),
  speakingEnd: act('speaking-end', 'POST', '/v1/speaking/sessions/:id/end', 'live-turn', { domain: [409] }),
  speakingAssess: act('speaking-ai-assess', 'POST', '/v1/speaking/sessions/:id/ai-assess', 'ai-roundtrip', { ok: [200, 202], domain: [409] }),
  speakingResults: act('speaking-results', 'GET', '/v1/speaking/sessions/:id/results', 'other'),

  // ---- tutor rooms ----------------------------------------------------------------------------------
  roomCreate: act('room-create', 'POST', '/v1/speaking/live-rooms', 'live-setup', { ok: [200, 201], domain: [403, 404, 409] }),
  roomToken: act('room-token', 'GET', '/v1/speaking/live-rooms/:id/token', 'live-setup', { domain: [403, 404] }),
  roomEnd: act('room-end', 'POST', '/v1/speaking/live-rooms/:id/end', 'live-setup', { ok: [200, 204], domain: [403, 404] }),

  // ---- SignalR over long-polling (the transport browsers actually use through the web origin) ------
  hubNegotiate: act('hub-negotiate', 'POST', '/hub/negotiate', 'hub', { hub: true }),
  hubPoll: act('hub-poll', 'GET', '/hub', 'hub', { hub: true, domain: [204] }),
  hubSend: act('hub-send', 'POST', '/hub', 'hub', { hub: true }),
  hubClose: act('hub-close', 'DELETE', '/hub', 'hub', { hub: true, ok: [200, 202, 204], domain: [404] }),
});

export const HUBS = Object.freeze({
  notifications: '/v1/notifications/hub',
  speakingLiveRoom: '/v1/speaking/live-rooms/hub',
});

/** Endpoint ids for the smoke status matrix (every action the harness can call). */
export const ENDPOINT_IDS = Object.freeze(Object.values(ACTIONS).map((spec) => spec.id));

/** Substitute `:name` placeholders in a path template (values are URL-encoded). */
export function fill(template, params = {}) {
  return template.replace(/:([a-zA-Z]+)/g, (match, name) => {
    if (!(name in params)) throw new Error(`missing path parameter '${name}' for ${template}`);
    return encodeURIComponent(String(params[name]));
  });
}
