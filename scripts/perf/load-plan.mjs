#!/usr/bin/env node
// Validates the dispatch inputs of .github/workflows/load-fleet.yml and turns them into the leg matrix
// and the account counts the seed / audit / purge jobs need. Pure logic plus a thin CLI; inputs arrive
// as environment variables (never interpolated into a shell) so a hostile input cannot inject.
//
// Refusals (exit 1, nothing runs): a production host anywhere, a missing explicit non-production
// confirmation, a non-HTTPS target (except loopback for the smoke profile), more learners per leg than
// one GitHub-hosted runner can drive, an impossible leg count.

import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { hostOf, isProductionHost } from '../../tests/load/fleet/accounts.mjs';
import { PROFILE_NAMES, buildTimeline, normalizeParams, planLeg } from '../../tests/load/fleet/profiles.mjs';

/** What one GitHub-hosted runner (4 vCPU / 16 GB) can drive with long-poll hubs; beyond it k6 itself saturates. */
export const MAX_LEARNERS_PER_HOSTED_LEG = 300;
export const MAX_LEGS = 6;

const truthy = (value) => ['true', '1', 'yes', 'on'].includes(String(value ?? '').trim().toLowerCase());
const blank = (value) => value === undefined || value === null || String(value).trim() === '';

function checkTarget(label, url, { allowLoopback }) {
  if (blank(url)) throw new Error(`${label} is required`);
  const text = String(url).trim();
  if (isProductionHost(text)) throw new Error(`${label} points at a production host (${text}); the load harness never runs against production`);
  const host = hostOf(text);
  const loopback = host === 'localhost' || host === '127.0.0.1' || host === '[::1]';
  if (!text.startsWith('https://') && !(allowLoopback && loopback && text.startsWith('http://'))) {
    throw new Error(`${label} must be an https:// URL (cookies are Secure); http is only accepted for loopback with the smoke profile`);
  }
  return text.replace(/\/+$/, '');
}

/**
 * @param {Record<string, string|boolean|undefined>} input  dispatch inputs
 *   profile, legs, apiUrl, webUrl, confirmNonProduction, learners, steadyMinutes, pairedRooms,
 *   allowOversubscribe
 */
export function planRun(input) {
  const profile = String(input.profile ?? 'smoke');
  if (!PROFILE_NAMES.includes(profile)) throw new Error(`profile must be one of ${PROFILE_NAMES.join(', ')} (got '${profile}')`);
  if (!truthy(input.confirmNonProduction)) {
    throw new Error('confirm_non_production must be ticked: this run creates 1,000+ disposable accounts and must never target production');
  }
  const legs = blank(input.legs) ? 1 : Number(input.legs);
  if (!Number.isInteger(legs) || legs < 1 || legs > MAX_LEGS) throw new Error(`legs must be an integer from 1 to ${MAX_LEGS} (got '${input.legs}')`);
  if (profile === 'smoke' && legs !== 1) throw new Error('the smoke profile runs on one leg');

  const apiUrl = checkTarget('api_url', input.apiUrl, { allowLoopback: profile === 'smoke' });
  const webUrl = blank(input.webUrl) ? '' : checkTarget('web_url', input.webUrl, { allowLoopback: profile === 'smoke' });

  const overrides = { legCount: legs, legIndex: 0 };
  if (!blank(input.learners)) overrides.learners = Number(input.learners);
  if (!blank(input.steadyMinutes)) overrides.steadyMinutes = Number(input.steadyMinutes);
  const params = normalizeParams(profile, overrides);
  const timeline = buildTimeline(params);

  const perLeg = Math.ceil(timeline.totalLearners / legs);
  const warnings = [];
  if (perLeg > MAX_LEARNERS_PER_HOSTED_LEG && !truthy(input.allowOversubscribe)) {
    const needed = Math.ceil(timeline.totalLearners / MAX_LEARNERS_PER_HOSTED_LEG);
    throw new Error(
      `${timeline.totalLearners} learners over ${legs} leg(s) is ${perLeg} per leg; a GitHub-hosted runner drives at most `
      + `${MAX_LEARNERS_PER_HOSTED_LEG}. Use at least ${needed} legs${needed > MAX_LEGS ? ' (more than the workflow offers: drive this run from a self-provisioned load generator, see docs/ops/LOAD-TESTING.md)' : ''}.`,
    );
  }
  if (perLeg > MAX_LEARNERS_PER_HOSTED_LEG) warnings.push(`oversubscribed: ${perLeg} learners per leg exceeds ${MAX_LEARNERS_PER_HOSTED_LEG}; generator CPU may distort latency`);
  if (webUrl === '') warnings.push('web_url is empty: learner traffic goes straight to the API, skipping the Next.js BFF hop browsers use');
  if (profile === 'overload' && legs < 4) warnings.push('overload with fewer than 4 legs is generator-bound before it is system-bound');

  const paired = Math.max(0, Math.floor(Number(input.pairedRooms ?? 0)) || 0);
  const probe = planLeg(timeline, normalizeParams(profile, { ...overrides }), Number.POSITIVE_INFINITY, paired);
  const accounts = { learners: timeline.totalLearners, experts: probe.pairedRooms };

  return {
    profile,
    legs,
    matrix: Array.from({ length: legs }, (_, index) => index),
    apiUrl,
    webUrl,
    totalLearners: timeline.totalLearners,
    learnersPerLeg: perLeg,
    durationMinutes: Math.ceil(timeline.durationS / 60),
    accounts,
    warnings,
  };
}

export function fromEnv(env) {
  return {
    profile: env.INPUT_PROFILE,
    legs: env.INPUT_LEGS,
    apiUrl: env.INPUT_API_URL,
    webUrl: env.INPUT_WEB_URL,
    confirmNonProduction: env.INPUT_CONFIRM_NON_PRODUCTION,
    learners: env.INPUT_LEARNERS,
    steadyMinutes: env.INPUT_STEADY_MINUTES,
    pairedRooms: env.INPUT_PAIRED_ROOMS,
    allowOversubscribe: env.INPUT_ALLOW_OVERSUBSCRIBE,
  };
}

/** `key=value` lines for $GITHUB_OUTPUT (single line values only). */
export function toOutputs(plan) {
  return [
    `legs=${JSON.stringify(plan.matrix)}`,
    `leg_count=${plan.legs}`,
    `total_learners=${plan.totalLearners}`,
    `learners_per_leg=${plan.learnersPerLeg}`,
    `duration_minutes=${plan.durationMinutes}`,
    `accounts_learners=${plan.accounts.learners}`,
    `accounts_experts=${plan.accounts.experts}`,
    `api_url=${plan.apiUrl}`,
    `web_url=${plan.webUrl}`,
  ];
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  try {
    const plan = planRun(fromEnv(process.env));
    for (const warning of plan.warnings) process.stdout.write(`::warning::${warning}\n`);
    process.stdout.write(`${JSON.stringify(plan, null, 2)}\n`);
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `${toOutputs(plan).join('\n')}\n`);
  } catch (error) {
    process.stderr.write(`::error::${error.message}\n`);
    process.exitCode = 1;
  }
}
