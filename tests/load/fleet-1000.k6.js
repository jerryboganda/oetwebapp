// MANUAL TOOL, INERT: run manually by the owner from a self-provisioned load generator; no CI runs
// this; agents never run it (AGENTS.md "NO AUTOMATED QA ANYWHERE"). Runbook: docs/ops/LOAD-TESTING.md.
//
// OET fleet load test: 1,000 distinct learners, 100 AI speaking sessions, 50 tutor rooms, for a
// 60 minute steady state, plus a 1,500-learner overload profile. Never aimed at production (the host
// guard in config.js aborts the run in init if it is).
//
//   k6 run -e K6_API_URL=https://api.staging.example -e K6_WEB_URL=https://app.staging.example \
//          -e OET_LOAD_PASSWORD=... -e K6_PROFILE=smoke tests/load/fleet-1000.k6.js
//
// One k6 process is one LEG. One generator machine cannot drive 1,000 learners alone, so the owner
// runs several legs in parallel on separate generators (K6_LEG_COUNT / K6_LEG_INDEX), each owning a
// disjoint, interleaved slice of the accounts and its own source IP.
//
// Thresholds (tests/load/fleet/thresholds.mjs) are the owner's targets and GATE: a breach makes k6
// exit 99 for that leg. Nothing here swallows the k6 exit code.

import { CFG, PARAMS, PLAN, TIMELINE, ROOMS } from './fleet/config.js';
import { ACTIONS, ENDPOINT_IDS, HUBS, fill, isCsrfExemptHub } from './fleet/contract.js';
import { runExpert, runLearner } from './fleet/flows.js';
import { call, newSession, signInSession } from './fleet/http.js';
import {
  cardIds, extractGuidIds, paperIds, readingPaperIds, readingPartAQuestionIds,
} from './fleet/extract.mjs';
import { hubCadenceRows } from './fleet/cadence.mjs';
import { describeTimeline } from './fleet/profiles.mjs';
import { buildSummary } from './fleet/summary-model.mjs';
import { buildThresholds, mergeThresholds, statusMatrixThresholds } from './fleet/thresholds.mjs';

const scenarioSeconds = Math.ceil(TIMELINE.durationS + 300);

const scenarios = {};
if (PLAN.learners > 0) {
  scenarios.learners = {
    executor: 'shared-iterations',
    exec: 'learners',
    vus: PLAN.learners,
    iterations: PLAN.learners,
    maxDuration: `${scenarioSeconds}s`,
    gracefulStop: '120s',
  };
}
if (PLAN.experts > 0) {
  scenarios.experts = {
    executor: 'shared-iterations',
    exec: 'experts',
    vus: PLAN.experts,
    iterations: PLAN.experts,
    maxDuration: `${scenarioSeconds}s`,
    gracefulStop: '120s',
  };
}
if (Object.keys(scenarios).length === 0) {
  throw new Error('this leg has no learners to run: check K6_LEARNERS, K6_LEG_COUNT and K6_LEG_INDEX');
}

const expect = {
  browse: PLAN.counts.learner + PLAN.counts.speaker + PLAN.counts.room,
  reading: PLAN.personas.reader,
  listening: PLAN.personas.listener,
  writing: PLAN.personas.writer,
  speaking: PLAN.counts.speaker,
  room: PLAN.counts.room,
};

export const options = {
  scenarios,
  thresholds: mergeThresholds(
    buildThresholds(CFG.profile, {
      stages: TIMELINE.stages.map((s) => s.target),
      expect,
      requiredFlows: CFG.requiredFlows,
    }),
    statusMatrixThresholds({ endpoints: CFG.statusMatrixEndpoints ? ENDPOINT_IDS : [] }),
  ),
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  // No per-URL tag: hub polls carry a unique `&_=` timestamp and every id is in the path, so `url` would
  // be a high-cardinality tag on every sample. Requests are grouped by the `name` / `ep` / `class` tags.
  systemTags: ['status', 'method', 'scenario', 'error', 'error_code', 'check', 'expected_response'],
  setupTimeout: '10m',
  userAgent: 'oet-fleet-loadtest/1',
};

/**
 * Discover what the stack can serve, as one real learner: Reading papers the learner may open and
 * their Part A question ids, Writing scenarios, Listening papers and the role-play cards. Missing
 * content is not an error here; the flows that need it skip and the report lists them as coverage
 * gaps. A discovery that could not reach the API at all IS an error: it would otherwise look like an
 * empty stack and cost the run an hour before the required-flow thresholds said so.
 *
 * Every leg runs its own setup() at about the same time, and a fresh sign-in revokes every other
 * session of that account (SingleActiveSessionEnabled / TrustedDeviceRequired stay on), so the legs
 * must NOT share a login. Each one discovers as its own first learner (global learner g = the leg
 * index, which is also the account iteration 0 of this leg's learners scenario signs in as; that
 * later sign-in revokes this session, after setup() is done with it).
 */
export function setup() {
  const content = { reading: [], listening: [], writing: [], cards: [] };
  if (PLAN.learners === 0) {
    // an experts-only leg: no learner flow here needs content
    console.log(JSON.stringify({ setup: 'discovery', skipped: 'no learners on this leg', leg: `${PARAMS.legIndex + 1}/${PARAMS.legCount}` }));
    return content;
  }
  const sess = newSession('learner', PARAMS.legIndex, PARAMS.legIndex);
  if (!signInSession(sess)) {
    throw new Error(`the discovery account (learner ${PARAMS.legIndex}) could not sign in: seed the accounts first (tests/load/seed/seed-accounts.mjs) and check OET_LOAD_PASSWORD`);
  }

  let requests = 0;
  let succeeded = 0;
  let lastStatus = 0;
  const discover = (spec, requestOptions) => {
    const r = call(sess, spec, requestOptions);
    requests += 1;
    lastStatus = r.status;
    if (r.ok) succeeded += 1;
    return r;
  };

  const home = discover(ACTIONS.readingHome);
  if (home.ok) {
    for (const id of readingPaperIds(home.json()).slice(0, 3)) {
      const structure = discover(ACTIONS.readingStructure, { path: fill(ACTIONS.readingStructure.path, { id }) });
      if (!structure.ok) continue;
      const partA = readingPartAQuestionIds(structure.json());
      if (partA.length > 0) content.reading.push({ paperId: id, partA });
    }
  }
  const scenariosList = discover(ACTIONS.writingScenarios, { path: `${ACTIONS.writingScenarios.path}?page=1&pageSize=20` });
  if (scenariosList.ok) content.writing = extractGuidIds(scenariosList.json()).slice(0, 5);
  const listening = discover(ACTIONS.papers, { path: `${ACTIONS.papers.path}?subtest=listening&pageSize=5` });
  if (listening.ok) content.listening = paperIds(listening.json());
  const cards = discover(ACTIONS.speakingCards);
  if (cards.ok) content.cards = cardIds(cards.json()).slice(0, 10);

  if (succeeded === 0) {
    throw new Error(`content discovery failed: none of ${requests} requests succeeded (last status ${lastStatus}); check the API and web URLs, the session policy and the seeded account`);
  }
  for (const [name, hubPath] of Object.entries(HUBS)) {
    if (!isCsrfExemptHub(hubPath)) {
      console.warn(`harness drift: contract.js does not list ${hubPath} (the ${name} hub) as exempt from the web proxy's CSRF check `
        + '(SIGNALR_HUB_PATH_PATTERN in lib/backend-proxy.ts); browsers send no x-csrf-token on SignalR requests, '
        + 'so the harness sends the header and the numbers for that hub are not what a browser would see. See docs/ops/LOAD-TESTING.md section 2.');
    }
  }

  console.log(JSON.stringify({
    setup: 'discovery',
    reading: content.reading.length,
    writing: content.writing.length,
    listening: content.listening.length,
    cards: content.cards.length,
    leg: `${PARAMS.legIndex + 1}/${PARAMS.legCount}`,
    plan: PLAN,
    pairedRooms: ROOMS.length,
    timeline: describeTimeline(TIMELINE),
  }));
  return content;
}

export function learners(content) {
  runLearner(content);
}

export function experts() {
  runExpert();
}

const fmt = (value, digits = 0) => (typeof value === 'number' ? value.toFixed(digits) : 'n/a');

function textReport(summary) {
  const lines = [];
  lines.push('');
  lines.push(`OET fleet load test: profile=${CFG.profile} leg=${PARAMS.legIndex + 1}/${PARAMS.legCount} learners(planned)=${TIMELINE.totalLearners}`);
  lines.push(`thresholds: ${summary.passed ? 'ALL PASSED' : `${summary.thresholdsFailed.length} FAILED`}`);
  for (const failure of summary.thresholdsFailed) lines.push(`  FAILED ${failure.metric}  ${failure.expression}`);
  const metric = (name) => summary.metrics[name];
  const read = metric('http_req_duration{class:critical-read,phase:steady}');
  if (read) lines.push(`critical reads (steady): p95=${fmt(read.values['p(95)'])} ms  p99=${fmt(read.values['p(99)'])} ms`);
  const failures = metric('oet_unexpected_failure{phase:steady}');
  if (failures) lines.push(`unexpected failures (steady): ${fmt(failures.values.rate * 100, 3)} %`);
  lines.push('');
  return lines.join('\n');
}

export function handleSummary(data) {
  const meta = {
    profile: CFG.profile,
    leg: PARAMS.legIndex,
    legCount: PARAMS.legCount,
    totalLearners: TIMELINE.totalLearners,
    plan: PLAN,
    pairedRooms: ROOMS.length,
    hubMode: CFG.hubMode,
    viaWeb: CFG.viaWeb,
    thinkScale: CFG.thinkScale,
    // effective pause lengths while a hub is held (the report prints them next to the nominal ones)
    hubCadence: CFG.hubMode !== 'off' && CFG.thinkScale > 0 ? hubCadenceRows(CFG.thinkScale) : [],
    k6Version: __ENV.K6_VERSION_STRING || '',
    runId: CFG.runId,
    sha: CFG.sha,
    targetLabel: CFG.targetLabel,
    simulators: CFG.simulators,
    timeline: describeTimeline(TIMELINE),
  };
  const summary = buildSummary(data, meta);
  return {
    [CFG.summaryPath]: JSON.stringify(summary, null, 2),
    stdout: textReport(summary),
  };
}
