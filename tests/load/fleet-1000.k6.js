// OET fleet load test: 1,000 distinct learners, 100 AI speaking sessions, 50 tutor rooms, for a
// 60 minute steady state, plus a 1,500-learner overload profile. Dispatch-only (see
// .github/workflows/load-fleet.yml); never aimed at production (the host guard in config.js aborts
// the run in init if it is).
//
//   k6 run -e K6_API_URL=https://api.staging.example -e K6_WEB_URL=https://app.staging.example \
//          -e OET_LOAD_PASSWORD=... -e K6_PROFILE=smoke tests/load/fleet-1000.k6.js
//
// One k6 process is one LEG. A GitHub-hosted runner cannot drive 1,000 learners alone, so the
// workflow runs several legs in parallel (K6_LEG_COUNT / K6_LEG_INDEX), each owning a disjoint,
// interleaved slice of the accounts and its own source IP. See docs/ops/LOAD-TESTING.md.
//
// Thresholds (tests/load/fleet/thresholds.mjs) are the owner's targets and GATE: a breach makes k6
// exit 99 and the workflow fails. Nothing is swallowed with `|| true` or continue-on-error.

import { CFG, PARAMS, PLAN, TIMELINE, ROOMS } from './fleet/config.js';
import { ACTIONS, ENDPOINT_IDS, fill } from './fleet/contract.js';
import { runExpert, runLearner } from './fleet/flows.js';
import { call, newSession, signInSession } from './fleet/http.js';
import {
  cardIds, extractGuidIds, paperIds, readingPaperIds, readingPartAQuestionIds,
} from './fleet/extract.mjs';
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
 * Discover what the stack can serve, as one real learner (the probe account): Reading papers the
 * learner may open and their Part A question ids, Writing scenarios, Listening papers and the
 * role-play cards. Missing content is not an error here; the flows that need it skip and the report
 * lists them as coverage gaps.
 */
export function setup() {
  const sess = newSession('probe', 0, -1);
  if (!signInSession(sess)) {
    throw new Error('the probe account could not sign in: seed the accounts first (tests/load/seed/seed-accounts.mjs) and check OET_LOAD_PASSWORD');
  }
  const content = { reading: [], listening: [], writing: [], cards: [] };

  const home = call(sess, ACTIONS.readingHome);
  if (home.ok) {
    for (const id of readingPaperIds(home.json()).slice(0, 3)) {
      const structure = call(sess, ACTIONS.readingStructure, { path: fill(ACTIONS.readingStructure.path, { id }) });
      if (!structure.ok) continue;
      const partA = readingPartAQuestionIds(structure.json());
      if (partA.length > 0) content.reading.push({ paperId: id, partA });
    }
  }
  const scenariosList = call(sess, ACTIONS.writingScenarios, { path: `${ACTIONS.writingScenarios.path}?page=1&pageSize=20` });
  if (scenariosList.ok) content.writing = extractGuidIds(scenariosList.json()).slice(0, 5);
  const listening = call(sess, ACTIONS.papers, { path: `${ACTIONS.papers.path}?subtest=listening&pageSize=5` });
  if (listening.ok) content.listening = paperIds(listening.json());
  const cards = call(sess, ACTIONS.speakingCards);
  if (cards.ok) content.cards = cardIds(cards.json()).slice(0, 10);

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
