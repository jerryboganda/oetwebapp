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
 *   3. NO AUTOMATED QA IN CI (owner directive 2026-10-06, permanent): no workflow may run a
 *      test / QA runner (vitest, dotnet test, Playwright, pytest, cargo test, node --test, k6 ...)
 *      except build-images.yml, which carries the deployment-only Writing content gates and the
 *      release-manifest self-test; qa-smoke.yml must stay deleted. The owner QAs manually and
 *      reports bugs; the agent fixes them on demand;
 *   4. build-images.yml stays path-filtered to build inputs (a push touching
 *      none of them must start no build and no rollout);
 *   5. production-deploy.yml keeps its identity, its serialized concurrency
 *      group and its immutable provenance/supersede gates, never runs on a
 *      PR, and never builds on the VPS;
 *   6. the rollout script stays pull-only (--no-build, no docker build);
 *   7. the measured accelerated build/reuse/cache/runtime contract survives;
 *   8. agent entrypoints inherit it and shipping cannot skip verified completion;
 *   9. only the audited workflows in PROD_SSH_WORKFLOWS may hold an SSH/VPS
 *      credential, so a new SSH workflow is always a visible edit of this file;
 *  10. Owner Fleet (owner directive 2026-10-05), every rule existence-conditional
 *      so nothing is required until the file/directory exists: a fleet.yml keeps
 *      its identity, serialization, own guards and a pull-only SSH block between
 *      BEGIN/END REMOTE FLEET ROLLOUT markers, stays BUILD-ONLY (rule 3 plus no
 *      benchmark/parity/conformance runner); platform/** never carries private
 *      keys or node/fleet token literals and fleet code never weakens SSH host-key
 *      trust; the release stays exactly four components.
 *
 * Compute policy: static file reads only.
 */
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const workflowDir = join(root, '.github', 'workflows');

export const PLAYWRIGHT_COMMAND =
  /(pnpm exec playwright|npx playwright|playwright install|playwright test|merge-reports|playwright@)/;

/**
 * The ONLY workflows that may hold a production/VPS SSH credential (owner directive
 * 2026-10-05, list re-audited 2026-10-06). The pull-only rollout rules are judged per
 * workflow, so a new SSH workflow must appear as a visible edit of this list in the same
 * commit instead of slipping in beside the audited ones. Adding to it also requires an
 * owner-written AGENTS.md exception. fleet.yml is the eighth (Owner Fleet exception (f)): only
 * its dispatch-only, main-only, pull-only `sync` job holds PROD_SSH_KEY and the pinned host key.
 */
export const PROD_SSH_WORKFLOWS = [
  'agent-console.yml',
  'fleet.yml',
  'mobile-release.yml',
  'production-deploy.yml',
  'publish-existing-desktop-to-vps.yml',
  'publish-existing-mobile-to-vps.yml',
  'tauri-desktop-release.yml',
  'writing-ai.yml',
];
export const SSH_ACCESS =
  /(secrets\.(?:PROD|VPS|FLEET|HELPER)_[A-Z0-9_]+|secrets\.[A-Z0-9_]*SSH[A-Z0-9_]*|webfactory\/ssh-agent|appleboy\/(?:ssh|scp)-action|\bssh-keyscan\b|\bssh-add\b|StrictHostKeyChecking)/;

// Owner Fleet: required identity and gates of .github/workflows/fleet.yml, once it exists.
const FLEET_REQUIRED = [
  ['name: Fleet (build + rollout)', 'distinct workflow name (watchers, triage and docs match it; never Build images or Deploy production)'],
  ['group: fleet', 'serialized fleet concurrency group'],
  ['cancel-in-progress: false', 'fleet rollouts must never cancel each other mid-rollout'],
  ["github.ref == 'refs/heads/main'", 'the SSH rollout runs from main only'],
  ['environment: production', 'the protected environment that holds the SSH secret'],
  ['node scripts/deploy/verify-pipeline-contract.mjs', 'its own pipeline-contract guard (build-images guards do not run for a platform-only push)'],
  ['bash scripts/deploy/verify-compute-offload.sh', 'its own pull-only rollout guard (neither ship:gate nor build-images runs it for a platform-only push)'],
];
// The VPS rollout script of fleet.yml: command-position tools only, so words like "node" in a
// log message are not mistaken for a source build.
const FLEET_REMOTE_COMMAND =
  /(?:^|[;&|(]|\$\()\s*(?:sudo\s+)?(?:[A-Za-z_][A-Za-z0-9_]*=\S*\s+)*(?:npm|npx|pnpm|yarn|node|dotnet|tsc|make|pip3?|cargo|ansible(?:-[a-z]+)?)(?:\s|$)/m;
const FLEET_REMOTE_BUILD =
  /docker\s+(?:build|buildx|builder)(?:\s|$)|docker\s+image\s+build|compose[^#\n]*\sbuild(?:\s|$)|git\s+(?:clone|fetch|pull|checkout|reset|submodule)(?:\s|$)/im;
const FLEET_REMOTE_DESTRUCTIVE =
  /docker\s+volume\s+(?:rm|prune)|docker\s+system\s+prune|compose[^#\n]*\sdown\s[^#\n]*(?:-v|--volumes)(?:\s|$)/m;
// fleet.yml is BUILD-ONLY (owner directive 2026-10-06). QA_COMMAND (rule 3) already bars every test
// runner; this adds the benchmark / parity / conformance runners it does not name.
const FLEET_QA_EXTRA = /(\bbenchmark\w*|\bparity\b|\bconformance\b|\bhyperfine\b|\bBenchmarkDotNet\b)/i;

// platform/** is public while work ships: never private keys or live credentials.
const PRIVATE_KEY_BLOCK = /-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----/;
const WIREGUARD_PRIVATE_KEY = /^\s*PrivateKey\s*=\s*[A-Za-z0-9+\/]{43}=\s*$/;
const FLEET_TOKEN_LITERAL = /(?:orw1|ofs1)_[0-9a-f]{16}_[A-Za-z0-9_-]{43}/;
const WEAK_HOST_KEY_TRUST =
  /StrictHostKeyChecking[=\s"']+(?:accept-new|no|off)\b|UserKnownHostsFile[=\s"']+\/dev\/null|host_key_checking[=:\s"']+(?:false|no|0)\b/i;
const SCAN_SKIP_DIRS = new Set(['node_modules', '.git', 'bin', 'obj', 'dist', '.next', 'TestResults']);
const SCAN_BINARY = /\.(?:png|jpe?g|gif|ico|webp|woff2?|ttf|otf|eot|dll|exe|so|dylib|zip|gz|tgz|tar|7z|pdf|db|sqlite3?|nupkg|snk)$/i;
const SCAN_MAX_BYTES = 2_000_000;

/**
 * Test / QA runners. Owner directive 2026-10-06 (permanent): CI never runs automated QA - the owner
 * tests the product by hand and reports bugs, and the agent fixes them on demand.
 */
export const QA_COMMAND =
  /(\bvitest\b|\bjest\b|\bpytest\b|playwright|dotnet\s+test\b|cargo\s+(?:test|clippy)\b|\b(?:npm|pnpm|yarn)\s+(?:run\s+|exec\s+)?test\b|node\s+--test\b|\bk6\s+run\b|xunit)/i;

/** The only workflow that may run a test: the deployment-only Writing gates and the release-manifest self-test. */
export const QA_ALLOWED_WORKFLOWS = new Set(['build-images.yml']);

/** Comment-free source: a rule must be judged on what the file DOES. */
export function activeLines(source) {
  return String(source ?? '')
    .split(/\r?\n/)
    .filter((line) => !/^\s*#/.test(line))
    .map((line) => {
      let quote = '';
      for (let i = 0; i < line.length; i += 1) {
        const char = line[i];
        if (char === '\\' && quote !== "'") { i += 1; continue; }
        if (char === quote) quote = '';
        else if (!quote && (char === "'" || char === '"')) quote = char;
        else if (!quote && char === '#' && (i === 0 || /\s/.test(line[i - 1]))) {
          return line.slice(0, i).trimEnd();
        }
      }
      return line;
    })
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

/** This repository's job keys use two-space indentation. Reject missing jobs. */
export function jobBlock(source, name) {
  const lines = activeLines(source).split('\n');
  const start = lines.findIndex((line) => line.trimEnd() === `  ${name}:`);
  if (start === -1) return '';
  const collected = [lines[start]];
  for (let i = start + 1; i < lines.length; i += 1) {
    if (/^(?:\S|  \S)/.test(lines[i])) break;
    collected.push(lines[i]);
  }
  return collected.join('\n');
}

/**
 * The SSH rollout script of an out-of-band workflow: the text between its
 * `# BEGIN REMOTE <NAME> ROLLOUT` and `# END REMOTE <NAME> ROLLOUT` marker lines,
 * comment-free. Markers are comments, so this reads the RAW workflow source.
 * Returns null when either marker is missing or they are out of order.
 */
export function remoteRolloutBlock(source, name) {
  const begin = `# BEGIN REMOTE ${name} ROLLOUT`;
  const end = `# END REMOTE ${name} ROLLOUT`;
  const raw = String(source ?? '');
  if (!raw.includes(begin) || !raw.includes(end) || raw.indexOf(end) < raw.indexOf(begin)) return null;
  const collected = [];
  let inside = false;
  for (const line of raw.split(/\r?\n/)) {
    if (line.includes(begin)) inside = true;
    else if (line.includes(end)) inside = false;
    else if (inside) collected.push(line);
  }
  return activeLines(collected.join('\n'));
}

/** Rules for .github/workflows/fleet.yml. Called only when that file exists. */
export function fleetWorkflowFailures(source) {
  const failures = [];
  const fleet = activeLines(source);
  for (const [needle, why] of FLEET_REQUIRED) {
    if (!fleet.includes(needle)) failures.push(`fleet.yml is missing ${needle} (${why})`);
  }
  const on = onBlock(fleet);
  if (!on) failures.push('fleet.yml must declare its triggers in a top-level on: block');
  for (const trigger of ['pull_request_target', 'schedule']) {
    if (new RegExp(`^\\s+${trigger}:`, 'm').test(on)) {
      failures.push(`fleet.yml must not run on ${trigger} (push to main and workflow_dispatch only)`);
    }
  }
  if (/:latest(?![\w.-])/.test(fleet)) {
    failures.push('fleet.yml must not publish or consume mutable :latest (helpers pull by immutable digest only)');
  }
  const qaExtra = FLEET_QA_EXTRA.exec(fleet);
  if (qaExtra) {
    failures.push(`fleet.yml is BUILD-ONLY: it must not run a benchmark, parity or conformance job (${qaExtra[0]}); the owner QAs the fleet manually (owner directive 2026-10-06)`);
  }
  const remote = remoteRolloutBlock(source, 'FLEET');
  if (remote === null) {
    failures.push('fleet.yml must delimit its SSH rollout script with "# BEGIN REMOTE FLEET ROLLOUT" / "# END REMOTE FLEET ROLLOUT"');
    return failures;
  }
  if (!/compose[^#\n]*\spull(\s|$)/m.test(remote)) {
    failures.push('the fleet VPS rollout must pull prebuilt GHCR images (compose pull)');
  }
  if (!/(^|\s)up\s[^#\n]*--no-build/m.test(remote)) {
    failures.push('the fleet VPS rollout must start containers with up --no-build');
  }
  if (remote.split('\n').some((line) => /(^|\s)up\s+-/.test(line) && !line.includes('--no-build'))) {
    failures.push('every compose up in the fleet VPS rollout must pass --no-build');
  }
  if (FLEET_REMOTE_COMMAND.test(remote) || FLEET_REMOTE_BUILD.test(remote)) {
    failures.push('the fleet VPS rollout contains a build/test/install or source-sync command');
  }
  if (FLEET_REMOTE_DESTRUCTIVE.test(remote)) {
    failures.push('the fleet VPS rollout must never remove or prune volumes, or compose down -v');
  }
  return failures;
}

/**
 * platform/** is public while work ships (owner directive 2026-10-05): no private keys, no
 * node/fleet token literals, and fleet code never weakens SSH host-key trust. A deliberate
 * test fixture opts out per line with a `secret-scan:allow` marker.
 */
export function platformSourceFailures(files, readFile) {
  const failures = [];
  for (const file of files) {
    const parts = file.split('/');
    const isDoc = /\.md$/i.test(file);
    const isTest = parts.some((part) => /^tests?$/i.test(part) || /\.tests?$/i.test(part));
    const isFleetCode = parts[1] === 'fleet' && !isDoc && !isTest;
    readFile(file).split(/\r?\n/).forEach((line, index) => {
      if (/secret-scan:allow/.test(line)) return;
      const where = `${file}:${index + 1}`;
      if (PRIVATE_KEY_BLOCK.test(line) || WIREGUARD_PRIVATE_KEY.test(line)) {
        failures.push(`${where}: private key material must never be committed (the repository is public while work ships)`);
      } else if (FLEET_TOKEN_LITERAL.test(line)) {
        failures.push(`${where}: a node/fleet token literal must never be committed`);
      } else if (isFleetCode && WEAK_HOST_KEY_TRUST.test(line)) {
        failures.push(`${where}: fleet code must pin host keys (StrictHostKeyChecking=yes with a pinned file); accept-new, no and a /dev/null known_hosts are forbidden`);
      }
    });
  }
  return failures;
}

export function checkContract({ readWorkflow, listWorkflows, readFile, listFiles = () => [] }) {
  const failures = [];
  const files = listWorkflows();
  const requireTokens = (label, source, tokens) => {
    for (const token of tokens) {
      if (!source.includes(token)) failures.push(`${label} must keep ${token}`);
    }
  };
  const requireNeeds = (source, name, expected) => {
    const job = jobBlock(source, name);
    const match = /^    needs:\s*\[([^\]]*)\]/m.exec(job);
    if (/^    needs:/m.test(job) && !match) {
      failures.push(`${name} must use the explicit inline needs list`);
      return;
    }
    const actual = (match?.[1] ?? '')
      .split(',').map((value) => value.trim()).filter(Boolean).sort();
    if (!job || JSON.stringify(actual) !== JSON.stringify([...expected].sort())) {
      failures.push(`${name} must keep exactly needs: [${expected.join(', ')}] (no extra critical-path jobs)`);
    }
  };

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

  // 1b. SSH/VPS credentials are an allow-list: a new SSH workflow is a visible edit here.
  for (const file of files) {
    if (PROD_SSH_WORKFLOWS.includes(file)) continue;
    if (SSH_ACCESS.test(activeLines(readWorkflow(file)))) {
      failures.push(
        `${file}: holds an SSH/VPS credential but is not in PROD_SSH_WORKFLOWS - a new SSH workflow needs a visible edit of scripts/deploy/verify-pipeline-contract.mjs and an owner-written AGENTS.md exception`,
      );
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

  // 3. No automated QA in CI (owner directive 2026-10-06, permanent).
  if (files.includes('qa-smoke.yml')) {
    failures.push('qa-smoke.yml must stay deleted: CI never runs automated QA (owner directive 2026-10-06)');
  }
  for (const file of files) {
    if (QA_ALLOWED_WORKFLOWS.has(file)) continue;
    const hit = QA_COMMAND.exec(activeLines(readWorkflow(file)));
    if (hit) {
      failures.push(
        `${file}: runs a test/QA runner (${hit[0]}) - CI never runs automated QA (owner directive 2026-10-06); `
        + 'the owner tests manually and reports bugs, the agent fixes them on demand',
      );
    }
  }

  // 4. build-images is path-filtered to build inputs.
  const build = activeLines(readWorkflow('build-images.yml'));
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
  const deploy = activeLines(readWorkflow('production-deploy.yml'));
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

  // Job-scoped checks prevent a token in a comment or unrelated job masking drift.
  for (const name of ['changes', 'syntax-gate', 'guards']) {
    requireNeeds(build, name, []);
    if (/^    if:/m.test(jobBlock(build, name))) failures.push(`${name} must remain unconditional`);
  }
  for (const [name, output] of [['build-web', 'web'], ['build-api', 'api'],
    ['build-backup', 'backup'], ['build-agent-gateway', 'gateway']]) {
    requireNeeds(build, name, ['syntax-gate', 'guards', 'changes']);
    requireTokens(name, jobBlock(build, name), [
      `if: needs.changes.outputs.${output} == 'true'`,
      "push: ${{ github.event_name != 'pull_request' && !inputs.benchmark }}",
    ]);
  }
  requireNeeds(build, 'retag', ['syntax-gate', 'guards', 'changes']);
  requireNeeds(build, 'writing-model-answer-gate', ['changes', 'build-api']);
  requireNeeds(build, 'writing-regression-gate', ['changes']);
  requireNeeds(build, 'release-manifest', ['changes', 'syntax-gate', 'guards', 'retag',
    'build-web', 'build-api', 'build-backup', 'build-agent-gateway',
    'writing-model-answer-gate', 'writing-regression-gate']);
  requireNeeds(deploy, 'apply-migrations', ['resolve']);
  requireNeeds(deploy, 'deploy', ['resolve', 'apply-migrations']);
  if (/qa-smoke|qa-gate|QA Smoke/.test(build + deploy)) {
    failures.push('QA Smoke must remain outside the production critical path');
  }
  requireTokens('build-images.yml', build, ['group: build-${{ github.sha }}', 'cancel-in-progress: false']);
  requireTokens('changes', jobBlock(build, 'changes'), [
    'release-manifest.mjs detect', 'reuse_matrix: ${{ steps.detect.outputs.reuse_matrix }}',
    'REBUILD_ALL: ${{ inputs.rebuild_all || inputs.benchmark || false }}',
  ]);
  if (!/^\s*run: node scripts\/deploy\/release-manifest\.mjs detect\s*$/m.test(jobBlock(build, 'changes'))) {
    failures.push('changes must execute successful-ancestor detection, not merely mention it');
  }
  requireTokens('syntax-gate', jobBlock(build, 'syntax-gate'), [
    'fetch-depth: 2', 'node scripts/ship/pre-push-gate.mjs --ci',
  ]);
  requireTokens('guards', jobBlock(build, 'guards'), [
    'node scripts/deploy/verify-pipeline-contract.mjs',
    'node --test scripts/deploy/release-manifest.test.mjs',
  ]);
  const retag = jobBlock(build, 'retag');
  requireTokens('retag', retag, [
    "needs.changes.outputs.reuse_count != '0'", "!inputs.benchmark",
    'matrix: ${{ fromJSON(needs.changes.outputs.reuse_matrix) }}',
    'imagetools create --prefer-index=false', 'test "$actual" = "$EXPECTED_DIGEST"',
  ]);
  if (/docker (?:pull|build\b|buildx build)/.test(retag)) {
    failures.push('retag must remain registry-only (no image download or rebuild)');
  }
  requireTokens('build-web', jobBlock(build, 'build-web'), [
    "if: ${{ github.event_name != 'pull_request' && !inputs.benchmark }}",
    'cache-source: .build-cache/pnpm', 'cache-target: /root/.local/share/pnpm/store',
    'cache-source: .build-cache/next', 'cache-target: /app/.next/cache',
    'skip-extraction: true', 'type=gha,scope=web', 'type=gha,mode=max,scope=web',
    'cleanup: ${{ runner.environment != \'github-hosted\' }}',
    '--output type=local,dest=.build-cache/next,platform-split=false',
  ]);
  const api = jobBlock(build, 'build-api');
  requireTokens('build-api', api, [
    'dotnet publish backend/src/OetLearner.Api/OetLearner.Api.csproj',
    '/p:UseAppHost=false', '/p:ProduceReferenceAssembly=true',
    'migrations script --idempotent --no-build --configuration Release',
    'referencesChecksum:$referencesChecksum', 'name: api-release-${{ github.sha }}',
    'file: ./backend/Dockerfile.runtime', 'type=gha,scope=api', 'type=gha,mode=max,scope=api',
  ]);
  if (/dotnet build backend\/src\/OetLearner\.Api|setup-node|pnpm .*build/.test(api)) {
    failures.push('build-api must publish once, not rebuild the API or frontend separately');
  }
  const writing = jobBlock(build, 'writing-model-answer-gate');
  requireTokens('writing-model-answer-gate', writing, [
    "needs.changes.outputs.writing == 'true'", 'dotnet restore "$test_project"',
    'name: api-release-${{ github.sha }}', '.referencesChecksum', '.sdk',
    'reference_args=(-p:BuildProjectReferences=false -p:UseAppHost=false)',
    'assert gate == expected and expected < default',
    'dotnet test "$test_project"', '-p:DeploymentWritingGateOnly=true',
    'FullyQualifiedName~WritingRev8ModelAnswerGateTests',
    'int(c.attrib["executed"]) > 0 and int(c.attrib["failed"]) == 0',
  ]);
  if (!/^\s*dotnet test "\$test_project"[^\n]*-p:DeploymentWritingGateOnly=true/m.test(writing)) {
    failures.push('writing-model-answer-gate must actually execute its deployment-only source set');
  }
  requireTokens('writing-regression-gate', jobBlock(build, 'writing-regression-gate'), [
    "if: needs.changes.outputs.writing == 'true'", 'python verify_manifest.py',
  ]);
  requireTokens('release-manifest', jobBlock(build, 'release-manifest'), [
    "github.event_name != 'pull_request' && !inputs.benchmark",
    "!contains(needs.*.result, 'failure')", "!contains(needs.*.result, 'cancelled')",
    'release-manifest.mjs create', 'name: release-manifest',
  ]);

  requireTokens('Dockerfile', activeLines(readFile('Dockerfile')), [
    'RUN --mount=type=cache,target=/root/.local/share/pnpm/store',
    'RUN --mount=type=cache,target=/app/.next/cache pnpm run build',
    'COPY --from=builder --exclude=server.js --exclude=.next /app/.next/standalone ./',
    'COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone/.next ./.next',
    'sha256sum -cs /tmp/standalone.sha256', 'WEB_RUNTIME_BYTES_AND_OWNERS_OK',
  ]);
  requireTokens('backend/Dockerfile.runtime', activeLines(readFile('backend/Dockerfile.runtime')), [
    'COPY --exclude=OetLearner.Api.* backend/publish ./',
    'COPY backend/publish/OetLearner.Api.* ./',
    'RUN --mount=type=bind,source=backend/publish,target=/published',
    'sha256sum --check --quiet /tmp/published.sha256',
  ]);
  requireTokens('auto-deploy-ghcr.sh', active, [
    'service_matches()', 'config --hash "$service"',
    '[ "$desired_hash" = "$actual_hash" ]', '[ "$actual_image" = "$desired_image" ]',
    'if service_matches "$service" "$container" "$image"; then', 'DEPLOY_REUSE service=',
    'compose "$target_slot" config | sha256sum',
    'head -n1 "$pending")" = "$identity"',
  ]);

  const ship = readFile('scripts/ship/ship.mjs').replace(/^\s*\/\/.*$/gm, '');
  requireTokens('ship.mjs', ship, [
    'validateReleaseOptions(flags);', 'if (lockIsActive(existing))',
    'requireKnownVisibility(repoVisibility())',
    'requireActiveRunList(ghJson(', 'let watchStatus = runWatcher(pushed.sha,',
    'const recorded = recordEvidence();', 'if (!recorded)',
    'await maybeFlipPrivate(paths);', 'SHIP_PUSH_STARTED_AT',
    "writeJson(paths.lock, lock, { exclusive: true })", 'renameSync(temporary, file)',
    'refusing to treat unreadable state as absent', 'SHIP_REMOTE_HOLDER_REGISTERED',
    'if (readRemoteHolders().length)', 'releaseRemoteHolder(lease.remoteHolder)',
  ]);
  const watcher = activeLines(readFile('scripts/ship/watch-deploy.ps1'));
  requireTokens('watch-deploy.ps1', watcher, [
    "throw 'SkipVpsSsh is forbidden:", "Only Deploy production can verify",
    'SHIP-WATCH_NOTHING_TO_DEPLOY', 'SHIP-WATCH_PUSH_TO_VERIFIED_LIVE',
    'SERVING_IMAGE_OK=', 'wrapper must own the visibility lease and evidence.',
  ]);
  for (const entrypoint of ['CLAUDE.md', 'GEMINI.md']) {
    if (!/^@(?:\.\/)?AGENTS\.md\s*$/m.test(readFile(entrypoint))) {
      failures.push(`${entrypoint} must import the canonical AGENTS.md contract`);
    }
  }
  for (const entrypoint of ['.github/copilot-instructions.md', 'CONTRIBUTING.md', 'agent-console/etc/MANUAL.md']) {
    if (!readFile(entrypoint).includes('AGENTS.md')) failures.push(`${entrypoint} must reference AGENTS.md`);
  }
  requireTokens('agent-console/src/config.ts', readFile('agent-console/src/config.ts'), [
    "deployWorkflowFile !== 'production-deploy.yml'", 'alternate rollout paths are forbidden',
  ]);
  requireTokens('agent-console/src/ship.ts', readFile('agent-console/src/ship.ts'), [
    'if (holders.length > 0 || await this.activeRunCount(env) > 0)',
    'Cannot verify ${status} Actions; refusing a private flip.',
    'Cannot verify repository visibility.', 'Cannot verify lease holders;',
  ]);
  requireTokens('AGENTS.md', readFile('AGENTS.md'), [
    'Mandatory accelerated baseline', '510.240', 'pnpm run ship',
  ]);

  // 10. Owner Fleet (owner directive 2026-10-05). The release stays exactly four components: a
  // fifth would break every historical manifest (validateManifest requires all four) and the
  // measured 510.240 s graph. Changing that is a visible edit of this line, with the owner.
  requireTokens('scripts/deploy/release-manifest.mjs', readFile('scripts/deploy/release-manifest.mjs'), [
    "export const COMPONENTS = ['web', 'api', 'db-backup', 'agent-gateway'];",
  ]);
  // The rules below are existence-conditional: nothing is required until fleet.yml or
  // platform/** exists, so this checker passes before the fleet does.
  const platformFiles = listFiles('platform');
  if (files.includes('fleet.yml')) failures.push(...fleetWorkflowFailures(readWorkflow('fleet.yml')));
  if (files.includes('fleet.yml') || platformFiles.length > 0) {
    requireTokens('AGENTS.md', readFile('AGENTS.md'), ['Owner Fleet exception']);
  }
  failures.push(...platformSourceFailures(platformFiles, readFile));

  return failures;
}

/** Text files under a repo-relative directory, forward-slash paths. Empty when it does not exist. */
export function listTextFiles(rootDir, relativeDir) {
  const found = [];
  const walk = (relative) => {
    const absolute = join(rootDir, relative);
    if (!existsSync(absolute) || !statSync(absolute).isDirectory()) return;
    for (const entry of readdirSync(absolute, { withFileTypes: true })) {
      const child = `${relative}/${entry.name}`;
      if (entry.isDirectory()) {
        if (!SCAN_SKIP_DIRS.has(entry.name)) walk(child);
      } else if (entry.isFile() && !SCAN_BINARY.test(entry.name) && statSync(join(rootDir, child)).size <= SCAN_MAX_BYTES) {
        found.push(child);
      }
    }
  };
  walk(relativeDir);
  return found.sort();
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
    listFiles: (relativeDir) => listTextFiles(rootDir, relativeDir),
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
    '[pipeline-contract] OK: mandatory accelerated reuse/cache/runtime path, verified ship completion, inherited agent contract, single pull-only rollout.',
  );
  return 0;
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) process.exitCode = main();
