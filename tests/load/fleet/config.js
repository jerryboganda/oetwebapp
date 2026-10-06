// k6 init-context configuration for the fleet load harness. Everything comes from environment
// variables so the same script serves the smoke run, one leg of the 1,000-learner run, and the
// overload run. See docs/ops/LOAD-TESTING.md for the full variable reference.
//
// Safety: nothing here ever resolves to a production host. A production URL aborts the run during
// init, before a single request is sent.

import { assertNonProduction, accountFor } from './accounts.mjs';
import { PROFILE_NAMES, buildTimeline, normalizeParams, planLeg } from './profiles.mjs';

const read = (name, fallback = '') => {
  const value = __ENV[name];
  return value === undefined || value === null || String(value).trim() === '' ? fallback : String(value).trim();
};
const number = (name, fallback) => {
  const raw = read(name, '');
  if (raw === '') return fallback;
  const value = Number(raw);
  if (!Number.isFinite(value)) throw new Error(`${name} must be a number (got '${raw}')`);
  return value;
};
const stripSlash = (value) => value.replace(/\/+$/, '');

const apiUrl = stripSlash(read('K6_API_URL'));
const webUrl = stripSlash(read('K6_WEB_URL'));
if (apiUrl === '') throw new Error('K6_API_URL is required (the HTTPS origin of the NON-production API under test)');
assertNonProduction('K6_API_URL', apiUrl);
if (webUrl !== '') assertNonProduction('K6_WEB_URL', webUrl);

const profile = read('K6_PROFILE', 'smoke');
if (!PROFILE_NAMES.includes(profile)) {
  throw new Error(`K6_PROFILE must be one of ${PROFILE_NAMES.join(', ')} (got '${profile}')`);
}

const viaWeb = webUrl !== '' && read('K6_VIA_WEB', '1') !== '0';

const hubMode = read('K6_HUB_MODE', 'longpoll');
if (!['longpoll', 'off'].includes(hubMode)) {
  throw new Error("K6_HUB_MODE must be 'longpoll' or 'off' (WebSocket hubs are not modelled: browsers long-poll through the web origin)");
}

const stageFractions = read('K6_STAGE_FRACTIONS', '');

export const CFG = Object.freeze({
  apiUrl,
  webUrl,
  viaWeb,
  /** Where learner traffic goes: the web origin's /api/backend proxy (what browsers use) or the API. */
  prefix: viaWeb ? `${webUrl}/api/backend` : apiUrl,
  profile,
  password: read('OET_LOAD_PASSWORD'),
  accountPrefix: read('OET_LOAD_ACCOUNT_PREFIX', 'loadtest'),
  emailDomain: read('OET_LOAD_EMAIL_DOMAIN', 'load.oet.test'),
  hubMode,
  // Smoke compresses think time to 15 % so a ~140 s learner reaches the exam flows (the old workflow set this).
  thinkScale: number('K6_THINK_SCALE', profile === 'smoke' ? 0.15 : 1),
  speakingTurns: Math.max(1, Math.floor(number('K6_SPEAKING_TURNS', 12))),
  speakingMaxWaitS: number('K6_SPEAKING_MAX_WAIT_S', 600),
  speakingAssessEvery: Math.max(0, Math.floor(number('K6_SPEAKING_ASSESS_EVERY', 5))),
  roomSeconds: number('K6_ROOM_SECONDS', 300),
  readingSaves: Math.max(1, Math.floor(number('K6_READING_SAVES', 20))),
  writingSaves: Math.max(1, Math.floor(number('K6_WRITING_SAVES', 8))),
  listeningSaves: Math.max(1, Math.floor(number('K6_LISTENING_SAVES', 10))),
  roomsFile: read('K6_ROOMS_FILE'),
  requiredFlows: read('K6_REQUIRED_FLOWS', profile === 'smoke' ? 'browse' : 'browse,reading,writing,speaking,room')
    .split(',').map((s) => s.trim()).filter(Boolean),
  statusMatrixEndpoints: read('K6_STATUS_MATRIX', profile === 'smoke' ? '1' : '0') === '1',
  summaryPath: read('K6_SUMMARY_PATH', 'k6-summary.json'),
  runId: read('K6_RUN_ID', 'local'),
  sha: read('K6_SHA', ''),
  targetLabel: read('K6_TARGET_LABEL', ''),
  simulators: read('K6_SIMULATORS_NOTE', ''),
});

export const PARAMS = normalizeParams(profile, {
  learners: read('K6_LEARNERS'),
  legCount: read('K6_LEG_COUNT'),
  legIndex: read('K6_LEG_INDEX'),
  signinPerMinPerLeg: read('K6_SIGNIN_PER_MIN'),
  cooldownSeconds: read('K6_COOLDOWN_SECONDS'),
  holdSeconds: read('K6_HOLD_SECONDS'),
  steadyMinutes: read('K6_STEADY_MINUTES'),
  settleSeconds: read('K6_SETTLE_SECONDS'),
  stageHoldMinutes: read('K6_STAGE_HOLD_MINUTES'),
  stageFractions: stageFractions === '' ? undefined : stageFractions.split(',').map((s) => Number(s.trim())),
  surgeLearners: read('K6_SURGE_LEARNERS'),
  surgeHoldMinutes: read('K6_SURGE_HOLD_MINUTES'),
  recoveryMinutes: read('K6_RECOVERY_MINUTES'),
  subsideSeconds: read('K6_SUBSIDE_SECONDS'),
});

export const TIMELINE = buildTimeline(PARAMS);

/** Pre-provisioned rooms (each with an assigned expert), by room ordinal. Absolute path required. */
export const ROOMS = (() => {
  if (CFG.roomsFile === '') return [];
  const parsed = JSON.parse(open(CFG.roomsFile));
  const rooms = Array.isArray(parsed) ? parsed : parsed.rooms;
  if (!Array.isArray(rooms)) throw new Error('K6_ROOMS_FILE must hold an array (or { "rooms": [...] }) of { ordinal, liveRoomId }');
  return rooms.map((room, index) => {
    if (typeof room.liveRoomId !== 'string' || room.liveRoomId === '') throw new Error(`rooms[${index}].liveRoomId is required`);
    return { ordinal: Number.isInteger(room.ordinal) ? room.ordinal : index, liveRoomId: room.liveRoomId };
  });
})();

export const PLAN = planLeg(TIMELINE, PARAMS, Number.POSITIVE_INFINITY, ROOMS.length);

export const roomByOrdinal = (ordinal) => ROOMS.find((room) => room.ordinal === ordinal) ?? null;

export const accountOf = (kind, ordinal) => accountFor(kind, ordinal, { prefix: CFG.accountPrefix, domain: CFG.emailDomain });
