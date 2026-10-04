#!/usr/bin/env node
/**
 * Static pipeline contract (owner directive 2026-10-03) — the non-bypassable
 * half of the CI/CD rules. Runs in the always-executing `guards` job of
 * build-images.yml on every build, so drift fails the run BEFORE production
 * images exist and long before a rollout could touch the VPS.
 *
 * What it enforces:
 *   1. exactly ONE workflow performs the production rollout, and it is
 *      production-deploy.yml (the legacy deploy-production.yml path must stay
 *      empty — a stale GitHub registration shadows a same-named file);
 *   2. no workflow that runs Playwright may have a push / pull_request /
 *      schedule trigger (the "no automated e2e" hard rule);
 *   3. qa-smoke.yml stays unit + backend only;
 *   4. build-images.yml stays path-filtered to build inputs (a push touching
 *      none of them must start no build and no rollout);
 *   5. production-deploy.yml keeps its identity, its serialized concurrency
 *      group and its immutable provenance/supersede gates, never runs on a
 *      PR, and never builds on the VPS;
 *   6. the rollout script stays pull-only (--no-build, no docker build);
 *   7. the sanctioned ship wrapper exists.
 *
 * Compute policy: static file reads only.
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const workflowDir = join(root, '.github', 'workflows');

export const PLAYWRIGHT_COMMAND =
  /(pnpm exec playwright|npx playwright|playwright install|playwright test|merge-reports|playwright@)/;

/** Comment-free source: a rule must be judged on what the file DOES. */
export function activeLines(source) {
  return String(source ?? '')
    .split(/\r?\n/)
    .filter((line) => !/^\s*#/.test(line))
    .join('\n');
}

/** The `on:` block: from the top-level `on:` line to the next top-level key. */
export function onBlock(source) {
  const lines = String(source ?? '').split(/\r?\n/);
  const start = lines.findIndex((line) => /^on:/.test(line));
  if (start === -1) return '';
  const collected = [lines[start]];
  for (let i = start + 1; i < lines.length; i += 1) {
    if (/^\S/.test(lines[i]) && !/^#/.test(lines[i])) break;
    collected.push(lines[i]);
  }
  return collected.join('\n');
}

export function checkContract({ readWorkflow, listWorkflows, readFile }) {
  const failures = [];
  const files = listWorkflows();

  // 1. Every Playwright lane is dispatch-only.
  for (const file of files) {
    const source = activeLines(readWorkflow(file));
    if (!PLAYWRIGHT_COMMAND.test(source)) continue;
    const on = onBlock(source);
    for (const trigger of ['push', 'pull_request', 'schedule']) {
      if (new RegExp(`^\\s+${trigger}:`, 'm').test(on)) {
        failures.push(
          `${file}: runs Playwright but has an '${trigger}' trigger - every browser lane must be workflow_dispatch only`,
        );
      }
    }
  }

  // 2. Exactly one rollout workflow, at the intended path.
  const rolloutRefs = files.filter((file) => activeLines(readWorkflow(file))
    .replace(/^[ \t]*bash -n scripts\/deploy\/auto-deploy-ghcr\.sh[ \t]*$/gm, '')
    .includes('auto-deploy-ghcr.sh'));
  if (rolloutRefs.length !== 1 || rolloutRefs[0] !== 'production-deploy.yml') {
    failures.push(
      `the production rollout must live in exactly production-deploy.yml (found: ${rolloutRefs.join(', ') || 'none'})`,
    );
  }
  if (files.includes('deploy-production.yml')) {
    failures.push(
      'deploy-production.yml must stay absent: a stale GitHub workflow registration at that path shadows the file',
    );
  }

  // 3. QA Smoke is unit + backend only.
  const smoke = activeLines(readWorkflow('qa-smoke.yml'));
  if (/(e2e-smoke|e2e-prepare|merge-reports|exec playwright|playwright install)/i.test(smoke)) {
    failures.push('qa-smoke.yml must stay unit + backend only (no e2e jobs, no Playwright)');
  }

  // 4. build-images is path-filtered to build inputs.
  const build = readWorkflow('build-images.yml');
  const buildOn = onBlock(build);
  if (!/^\s+paths:/m.test(buildOn)) {
    failures.push('build-images.yml must keep a `paths:` filter on push (runtime inputs only)');
  }
  if (/paths-ignore:/.test(buildOn)) {
    failures.push('build-images.yml must use `paths:` (not `paths-ignore:`) so the trigger list is provable');
  }
  for (const canary of ["'app/**'", "'backend/**'", "'rulebooks/**'", "'Dockerfile'"]) {
    if (!build.includes(canary)) failures.push(`build-images.yml must keep ${canary} in its push paths filter`);
  }

  // 5. The rollout workflow keeps its identity, gates and serialization.
  const deploy = readWorkflow('production-deploy.yml');
  const required = [
    ['name: Deploy production', 'workflow name (the watcher, triage and docs match it)'],
    ['run-name:', 'run-name that carries the deployed SHA'],
    ['group: production-deploy', 'serialized rollout concurrency group'],
    ['cancel-in-progress: false', 'rollouts must never cancel each other mid-flip'],
    ['needs: [resolve, apply-migrations]', 'the migration gate before the rollout'],
    ['release-manifest.mjs resolve', 'successful exact-SHA image and migration provenance'],
    ['release-manifest.mjs verify-api', 'source-run SQL checksum verification'],
    ['release-manifest.mjs superseded', 'immediate and safe pre-cutover descendant checks'],
    ['deploy_phase prepare', 'the health-gated preparation boundary'],
    ['deploy_phase promote', 'promotion only after CI rechecks eligibility'],
    ['name: promotion-proof', 'actual promotion evidence, not merely a successful stand-down'],
  ];
  for (const [needle, why] of required) {
    if (!deploy.includes(needle)) failures.push(`production-deploy.yml is missing ${needle} (${why})`);
  }
  if (/^\s+pull_request:/m.test(onBlock(deploy))) {
    failures.push('production-deploy.yml must never run on pull_request');
  }
  if (/(^|\s)--build(\s|$)/m.test(deploy)) {
    failures.push('production-deploy.yml must never pass --build (the VPS does not build)');
  }
  if (/sleep 30|setup-dotnet|dotnet (?:build|publish|restore|tool)/.test(activeLines(deploy))) {
    failures.push('production-deploy.yml must not recompile the API or keep a fixed settle delay');
  }
  for (const needle of ['migrations script --idempotent --no-build', 'release-manifest.mjs create',
    '--prefer-index=false', 'needs: [syntax-gate, guards, changes]', 'name: release-manifest']) {
    if (!activeLines(build).includes(needle)) failures.push(`build-images.yml must keep ${needle}`);
  }
  if (/:(?:latest)\s*$|\s(?:SRC|SOURCE)=.*:latest/m.test(activeLines(build))) {
    failures.push('build-images.yml must not publish/consume mutable latest as release provenance');
  }

  // 6. The rollout script stays pull-only.
  const rolloutScript = readFile('scripts/deploy/auto-deploy-ghcr.sh');
  const active = rolloutScript
    .split(/\r?\n/)
    .filter((line) => !/^\s*#/.test(line))
    .join('\n');
  if (!active.includes('--no-build')) failures.push('auto-deploy-ghcr.sh must keep --no-build on every start');
  if (/(docker\s+build|docker\s+compose[^\n]*\sbuild(\s|$))/m.test(active)) {
    failures.push('auto-deploy-ghcr.sh must never build on the VPS');
  }
  for (const needle of ['--pull never', 'nginx -t', 'nginx -s reload', 'rollback_routers',
    'DEPLOY_LIVE', 'prepared-', 'live-release.env']) {
    if (!active.includes(needle)) failures.push(`auto-deploy-ghcr.sh must keep ${needle}`);
  }

  // 7. The sanctioned ship wrapper exists.
  if (!readFile('scripts/ship/ship.mjs').length) {
    failures.push('scripts/ship/ship.mjs (the `pnpm run ship` entry point) must exist');
  }

  return failures;
}

/** Scan a checkout. Shared by the CLI and the pre-push gate. */
export function scanRepo(rootDir = root) {
  const workflows = join(rootDir, '.github', 'workflows');
  const readFile = (relative) => {
    const path = join(rootDir, relative);
    return existsSync(path) ? readFileSync(path, 'utf8') : '';
  };
  return checkContract({
    listWorkflows: () =>
      readdirSync(workflows)
        .filter((file) => file.endsWith('.yml') || file.endsWith('.yaml'))
        .sort(),
    readWorkflow: (file) => readFile(join('.github', 'workflows', file)),
    readFile,
  });
}

export function main() {
  const failures = scanRepo(root);

  if (failures.length) {
    console.error('[pipeline-contract] FAILED');
    for (const failure of failures) console.error(`  - ${failure}`);
    return 1;
  }
  console.log(
    '[pipeline-contract] OK: single pull-only rollout, no automated browser lanes, path-filtered builds, ship wrapper present.',
  );
  return 0;
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) process.exitCode = main();
