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
 *   7. the measured accelerated build/reuse/cache/runtime contract survives;
 *   8. agent entrypoints inherit it and shipping cannot skip verified completion.
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

export function checkContract({ readWorkflow, listWorkflows, readFile }) {
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
    '[pipeline-contract] OK: mandatory accelerated reuse/cache/runtime path, verified ship completion, inherited agent contract, single pull-only rollout.',
  );
  return 0;
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) process.exitCode = main();
