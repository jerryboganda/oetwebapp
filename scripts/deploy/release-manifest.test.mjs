import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { stripVTControlCharacters } from 'node:util';
import test from 'node:test';
import { COMPONENTS, apiNeedsMigrations, buildInputsChanged, changedPaths, classifyInputs, comparisonContainsSha,
  eligibleBuild, validateManifest, verifySqlArtifact } from './release-manifest.mjs';
import { activeLines, checkContract, scanRepo } from './verify-pipeline-contract.mjs';
import { reportPipelineContract } from '../ship/pre-push-gate.mjs';
import { acquireLock, main as shipMain, parseArgs, readJson as readShipState, requireHolderValue,
  selfTest as shipSelfTest, validateReleaseOptions, writeJson as writeShipState } from '../ship/ship.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const sha = 'a'.repeat(40);
const repo = 'jerryboganda/oetwebapp';
const digest = `sha256:${'b'.repeat(64)}`;
const sql = Buffer.from('SELECT 1;\n');
const checksum = createHash('sha256').update(sql).digest('hex');
const none = { web: false, api: false, 'db-backup': false, 'agent-gateway': false, writing: false };

function manifest() {
  return { schema: 1, repo, sha, buildRunId: 100,
    components: Object.fromEntries(COMPONENTS.map((name) => [name, {
      image: `ghcr.io/${repo}-${name}:${sha}`, digest, sourceSha: sha, sourceRunId: 100,
    }])),
    migrations: { sha, runId: 100, artifact: `api-release-${sha}`, checksum, efVersion: '10.0.5' } };
}

test('runtime inputs are classified independently; deployment, tests and ledgers do not compile images', () => {
  for (const path of ['app/page.tsx', 'pages/example.tsx', 'Dockerfile', 'Dockerfile.dockerignore', 'pnpm-lock.yaml']) {
    assert.deepEqual(classifyInputs([path]), { ...none, web: true }, path);
  }
  for (const path of ['backend/src/OetLearner.Api/Program.cs', 'global.json', 'Directory.Build.targets']) {
    assert.deepEqual(classifyInputs([path]), { ...none, api: true }, path);
  }
  for (const path of ['TASKS.json', 'backend/tests/PlatformTests.cs', 'docs/example.md',
    'docker-compose.production.yml', 'scripts/deploy/auto-deploy-ghcr.sh', 'vitest.config.ts']) {
    assert.deepEqual(classifyInputs([path]), none, path);
  }
  assert.deepEqual(classifyInputs(['data/seed.json']), { ...none, web: true, api: true });
  assert.deepEqual(classifyInputs(['scripts/backup/Dockerfile']), { ...none, 'db-backup': true });
  assert.deepEqual(classifyInputs(['agent-gateway/Dockerfile']), { ...none, 'agent-gateway': true });
  assert.deepEqual(classifyInputs(['rulebooks/writing/rules.json']), { ...none, web: true, api: true, writing: true });
  assert.deepEqual(classifyInputs(['backend/src/OetLearner.Api/Services/Writing/Grader.cs']), { ...none, api: true, writing: true });
  assert.deepEqual(classifyInputs(null), Object.fromEntries(Object.keys(none).map((name) => [name, true])));
});

test('truncated comparisons rebuild conservatively and renamed inputs preserve both paths', () => {
  assert.equal(changedPaths({ files: Array.from({ length: 300 }, () => ({ filename: 'docs/a.md' })) }), null);
  assert.deepEqual(changedPaths({ files: [{ filename: 'docs/a.md', previous_filename: 'app/a.ts' }] }), ['docs/a.md', 'app/a.ts']);
  assert.throws(() => changedPaths({}), /changed files/);
});

test('no-op proof matches the actual ordered workflow push paths, never just missing runs', () => {
  const workflow = readFileSync(join(root, '.github', 'workflows', 'build-images.yml'), 'utf8');
  assert.equal(buildInputsChanged(['docs/example.md', 'TASKS.json', 'backend/tests/A.cs', 'backend/README.md'], workflow), false);
  for (const file of ['Dockerfile', 'app/page.tsx', 'global.json', 'scripts/deploy/auto-deploy-ghcr.sh',
    '.github/workflows/production-deploy.yml', '.env.production.example', 'tests/writing-regression/manifest.json']) {
    assert.equal(buildInputsChanged([file], workflow), true, file);
  }
  assert.equal(buildInputsChanged(null, workflow), true);
  assert.throws(() => buildInputsChanged(['docs/a.md'], 'on:\n  push:\n'), /paths/);
});

test('only successful main releases and true ancestors qualify for reuse or superseding', () => {
  const run = { status: 'completed', conclusion: 'success', headBranch: 'main', headSha: sha,
    event: 'push', displayTitle: `Build images ${sha}` };
  assert.equal(eligibleBuild(run), true);
  for (const replacement of [{ status: 'in_progress' }, { conclusion: 'failure' }, { conclusion: 'cancelled' },
    { event: 'pull_request' }, { headBranch: 'feature' }, { displayTitle: `Benchmark build ${sha}` }]) {
    assert.equal(eligibleBuild({ ...run, ...replacement }), false);
  }
  assert.equal(comparisonContainsSha({ status: 'ahead', merge_base_commit: { sha } }, sha), true);
  assert.equal(comparisonContainsSha({ status: 'diverged', merge_base_commit: { sha } }, sha), false);
  assert.equal(comparisonContainsSha({ status: 'ahead', merge_base_commit: { sha: 'c'.repeat(40) } }, sha), false);
});

test('release provenance rejects mutable refs, wrong identities and mismatched SQL origins', () => {
  assert.equal(validateManifest(manifest(), repo, sha).sha, sha);
  for (const mutate of [
    (value) => { value.repo = 'another/repo'; },
    (value) => { value.buildRunId = 0; },
    (value) => { value.components.web.image = `ghcr.io/${repo}-web:latest`; },
    (value) => { value.components.api.digest = 'unknown'; },
    (value) => { value.migrations.runId = 101; },
    (value) => { value.migrations.sha = 'c'.repeat(40); },
  ]) {
    const value = manifest(); mutate(value);
    assert.throws(() => validateManifest(value, repo, sha));
  }
});

test('a built-but-never-deployed API still requires migration application', () => {
  const release = manifest();
  assert.equal(apiNeedsMigrations(release, null), true);
  assert.equal(apiNeedsMigrations(release, manifest()), false);
  const deployed = manifest(); deployed.components.api.digest = `sha256:${'c'.repeat(64)}`;
  assert.equal(apiNeedsMigrations(release, deployed), true);
});

test('required SQL fails closed for empty bytes, wrong SHA/run/tool/checksum or altered contents', () => {
  const metadata = { sha, runId: 100, checksum, efVersion: '10.0.5' };
  const expected = { sha, runId: 100, checksum };
  verifySqlArtifact(metadata, sql, expected);
  for (const replacement of [{ sha: 'c'.repeat(40) }, { runId: 101 }, { efVersion: '10.0.4' }, { checksum: '0'.repeat(64) }]) {
    assert.throws(() => verifySqlArtifact({ ...metadata, ...replacement }, sql, expected));
  }
  assert.throws(() => verifySqlArtifact(metadata, Buffer.alloc(0), expected), /empty/);
  assert.throws(() => verifySqlArtifact(metadata, Buffer.from('SELECT 2;'), expected), /checksum/);
});

test('the complete checkout preserves its mechanical pipeline contract', () => {
  assert.deepEqual(scanRepo(root), []);
  const failures = checkContract({
    listWorkflows: () => ['build-images.yml', 'production-deploy.yml'],
    readWorkflow: (file) => readFileSync(join(root, '.github', 'workflows', file), 'utf8')
      + (file === 'build-images.yml' ? '\n      - run: bash scripts/deploy/auto-deploy-ghcr.sh\n' : ''),
    readFile: (file) => readFileSync(join(root, file), 'utf8'),
  });
  assert.ok(failures.some((failure) => failure.includes('production rollout must live in exactly')));
});

test('CI never runs automated QA: a test runner in any workflow but the build gates fails the contract', () => {
  const contractWith = (extraName, extraSource) => checkContract({
    listWorkflows: () => ['build-images.yml', 'production-deploy.yml', extraName],
    readWorkflow: (file) => (file === extraName ? extraSource : readFileSync(join(root, '.github', 'workflows', file), 'utf8')),
    readFile: (file) => readFileSync(join(root, file), 'utf8'),
  });
  const workflowRunning = (command) =>
    `name: x\non:\n  push:\n    branches: [main]\njobs:\n  t:\n    steps:\n      - run: ${command}\n`;
  for (const command of ['pnpm exec vitest run', 'dotnet test backend/x.csproj', 'pnpm exec playwright test',
    'pytest tests', 'node --test x.test.mjs', 'cargo test', 'pnpm test', 'k6 run load.js']) {
    assert.ok(contractWith('extra.yml', workflowRunning(command)).some((failure) => failure.includes('automated QA')), command);
  }
  // A comment cannot trip it, and a plain build or deploy workflow is fine.
  assert.ok(!contractWith('extra.yml', '# vitest is not run here\nname: x\non:\n  push:\njobs:\n  b:\n    steps:\n      - run: echo build\n')
    .some((failure) => failure.includes('automated QA')));
  // The deleted QA Smoke workflow must not come back, whatever it contains.
  assert.ok(contractWith('qa-smoke.yml', 'name: QA Smoke\non:\n  workflow_dispatch:\njobs:\n  a:\n    steps:\n      - run: echo hi\n')
    .some((failure) => failure.includes('qa-smoke.yml must stay deleted')));
});

test('pipeline comments cannot satisfy a contract; quoted hashes remain literal', () => {
  assert.equal(activeLines('run: echo disabled # release-manifest.mjs detect\n# ignored\nvalue: "keep # literal"'),
    'run: echo disabled\nvalue: "keep # literal"');
});

function mutatedContract(relative, mutate) {
  const read = (file) => {
    const source = readFileSync(join(root, file), 'utf8');
    if (file !== relative) return source;
    const changed = mutate(source);
    assert.notEqual(changed, source, `${relative}: regression mutation did not apply`);
    return changed;
  };
  return checkContract({
    listWorkflows: () => ['build-images.yml', 'production-deploy.yml'],
    readWorkflow: (file) => read(`.github/workflows/${file}`),
    readFile: read,
  });
}

const buildPath = '.github/workflows/build-images.yml';
for (const [name, file, mutate, message] of [
  ['unconditional guards', buildPath, (s) => s.replace('  guards:\n', '  guards:\n    if: false\n'), /guards must remain unconditional/],
  ['no extra build prerequisites', buildPath,
    (s) => s.replace('  build-web:\n    needs: [syntax-gate, guards, changes]',
      '  build-web:\n    needs: [syntax-gate, guards, changes, writing-model-answer-gate]'), /build-web must keep exactly needs/],
  ['job-scoped changed-input gating', buildPath,
    (s) => s.replace("    if: needs.changes.outputs.web == 'true'", "    if: true\n    # if: needs.changes.outputs.web == 'true'"),
    /build-web must keep if:/],
  ['real ancestor detection', buildPath,
    (s) => s.replace('run: node scripts/deploy/release-manifest.mjs detect',
      'run: echo disabled # release-manifest.mjs detect'), /changes must execute/],
  ['registry-only reuse', buildPath,
    (s) => s.replace('test "$actual" = "$EXPECTED_DIGEST"', 'docker pull "$SOURCE"\n          test "$actual" = "$EXPECTED_DIGEST"'),
    /retag must remain registry-only/],
  ['immutable reused digest verification', buildPath,
    (s) => s.replace('test "$actual" = "$EXPECTED_DIGEST"', 'echo "digest unchecked"'), /retag must keep test/],
  ['consumed Next cache', buildPath,
    (s) => s.replace('cache-target: /app/.next/cache', 'cache-target: /app/.next/unused'), /build-web must keep cache-target/],
  ['native Next cache export', buildPath,
    (s) => s.replace('--output type=local,dest=.build-cache/next,platform-split=false', '--load'),
    /build-web must keep --output/],
  ['same-publish SQL', buildPath,
    (s) => s.replace('migrations script --idempotent --no-build --configuration Release',
      'migrations script --idempotent --configuration Release'), /build-api must keep migrations/],
  ['executable-consistent Writing references', buildPath,
    (s) => s.replace('reference_args=(-p:BuildProjectReferences=false -p:UseAppHost=false)',
      'reference_args=(-p:BuildProjectReferences=false)'), /writing-model-answer-gate must keep reference_args/],
  ['actual scoped Writing test compilation', buildPath,
    (s) => s.replace('dotnet test "$test_project" -c Release --no-restore --nologo -p:RunAnalyzers=false -p:DeploymentWritingGateOnly=true',
      'dotnet test "$test_project" -c Release --no-restore --nologo -p:RunAnalyzers=false'),
    /writing-model-answer-gate must actually execute/],
  ['nonzero actual Writing execution', buildPath,
    (s) => s.replace('int(c.attrib["executed"]) > 0', 'int(c.attrib["executed"]) >= 0'),
    /writing-model-answer-gate must keep int/],
  ['required Writing release gates', buildPath,
    (s) => s.replace('build-agent-gateway, writing-model-answer-gate, writing-regression-gate]',
      'build-agent-gateway]'), /release-manifest must keep exactly needs/],
  ['parallel per-SHA builds', buildPath,
    (s) => s.replace('group: build-${{ github.sha }}', 'group: build-main'), /build-images.yml must keep group/],
  ['standalone web byte verification', 'Dockerfile',
    (s) => s.replace('sha256sum -cs /tmp/standalone.sha256', 'echo "unchecked bytes"'),
    /Dockerfile must keep sha256sum/],
  ['stable API dependency partition', 'backend/Dockerfile.runtime',
    (s) => s.replace('COPY --exclude=OetLearner.Api.* backend/publish ./', 'COPY backend/publish ./'),
    /backend\/Dockerfile.runtime must keep COPY --exclude/],
  ['configuration-aware service reuse', 'scripts/deploy/auto-deploy-ghcr.sh',
    (s) => s.replace('[ "$desired_hash" = "$actual_hash" ]', '[ "unchecked" = "unchecked" ]'),
    /auto-deploy-ghcr.sh must keep \[ "\$desired_hash"/],
  ['physical-image watcher obligation', 'scripts/ship/watch-deploy.ps1',
    (s) => s.replace("throw 'SkipVpsSsh is forbidden:", "Write-Output 'SkipVpsSsh is forbidden:"),
    /watch-deploy.ps1 must keep throw/],
  ['native Claude authority import', 'CLAUDE.md', (s) => s.replace('@AGENTS.md', 'AGENTS.md'),
    /CLAUDE.md must import/],
  ['native Gemini authority import', 'GEMINI.md', (s) => s.replace('@AGENTS.md', 'AGENTS.md'),
    /GEMINI.md must import/],
  ['console workflow pinning', 'agent-console/src/config.ts',
    (s) => s.replace("deployWorkflowFile !== 'production-deploy.yml'", "deployWorkflowFile !== deployWorkflowFile"),
    /agent-console\/src\/config.ts must keep/],
  ['console lease-safe watchdog', 'agent-console/src/ship.ts',
    (s) => s.replace('if (holders.length > 0 || await this.activeRunCount(env) > 0)', 'if (false)'),
    /agent-console\/src\/ship.ts must keep if/],
  ['exclusive lock acquisition', 'scripts/ship/ship.mjs',
    (s) => s.replace('writeJson(paths.lock, lock, { exclusive: true })', 'writeJson(paths.lock, lock)'),
    /ship.mjs must keep writeJson/],
  ['atomic mutable state', 'scripts/ship/ship.mjs',
    (s) => s.replace('renameSync(temporary, file)', 'writeFileSync(file, contents)'),
    /ship.mjs must keep renameSync/],
  ['native console/workstation holder coverage', 'scripts/ship/ship.mjs',
    (s) => s.replaceAll('if (readRemoteHolders().length)', 'if (false)'),
    /ship.mjs must keep if \(readRemoteHolders/],
]) {
  test(`mandatory accelerated baseline rejects loss of ${name}`, () => {
    assert.ok(mutatedContract(file, mutate).some((failure) => message.test(failure)));
  });
}

test('missing pipeline checker fails the real local gate closed', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'oet-missing-contract-'));
  try {
    assert.equal(await reportPipelineContract(dir), false);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('ship controls fail before git, lease, visibility or watcher side effects', async () => {
  assert.deepEqual(shipSelfTest(), { ok: true, failures: [] });
  for (const flag of ['no-watch', 'no-record', 'no-visibility', 'force-release']) {
    await assert.rejects(shipMain([`--${flag}`]), new RegExp(`--${flag} is forbidden`));
  }
  await assert.rejects(shipMain(['--workflow', 'QA Smoke']), /Only Deploy production/);
  for (const args of [[], ['--dry-run'], ['--no-push'], ['--status'], ['--sha', sha, '--verify']]) {
    assert.doesNotThrow(() => validateReleaseOptions(parseArgs(args)));
  }
});

test('ship state is atomically replaced; missing alone is absent and unreadable never frees a lock', () => {
  const dir = mkdtempSync(join(tmpdir(), 'oet-ship-state-'));
  const file = join(dir, 'lock.json');
  const state = { session: 'test', host: 'test-host', pid: process.pid, expiresAt: new Date().toISOString() };
  try {
    assert.equal(readShipState(file), null);
    writeShipState(file, state, { exclusive: true });
    assert.throws(() => writeShipState(file, { ...state, session: 'other' }, { exclusive: true }), { code: 'EEXIST' });
    assert.deepEqual(readShipState(file), state);
    writeShipState(file, { ...state, session: 'updated' });
    assert.equal(readShipState(file).session, 'updated');
    assert.equal(existsSync(`${file}.${process.pid}.tmp`), false);
    for (const contents of ['{broken', '{}', 'null', '[]', JSON.stringify({ ...state, pid: 0 })]) {
      writeFileSync(file, contents);
      assert.throws(() => readShipState(file), /Cannot verify ship state/);
      assert.equal(readFileSync(file, 'utf8'), contents);
    }
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('real lock acquisition refuses live owners and never unlinks an inactive snapshot', () => {
  const dir = mkdtempSync(join(tmpdir(), 'oet-ship-lock-'));
  const paths = { lock: join(dir, 'lock.json') };
  try {
    const lock = acquireLock(paths);
    assert.throws(() => acquireLock(paths), /another ship is running/);
    assert.deepEqual(readShipState(paths.lock), lock);
    const stale = { ...lock, pid: 2_147_483_647, expiresAt: '2000-01-01T00:00:00Z' };
    writeShipState(paths.lock, stale);
    assert.throws(() => acquireLock(paths), /Inactive ship lock/);
    assert.deepEqual(readShipState(paths.lock), stale);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('shared visibility holders accept native formats and reject missing/malformed values', () => {
  assert.deepEqual(requireHolderValue('["owner-pc","agent-console:1","owner-pc"]'), ['owner-pc', 'agent-console:1']);
  assert.deepEqual(requireHolderValue('owner-pc, agent-console:1'), ['owner-pc', 'agent-console:1']);
  for (const value of ['', '[]']) assert.deepEqual(requireHolderValue(value), []);
  for (const value of [undefined, null, 0, '{}', 'null', '[broken', '[1]', '[""]']) {
    assert.throws(() => requireHolderValue(value), /visibility holders/);
  }
});

test('native PowerShell watcher rejects completion bypasses before any GitHub or VPS call', () => {
  const bin = process.platform === 'win32' ? 'powershell' : 'pwsh';
  for (const [args, message] of [
    [['-SkipVpsSsh'], /SkipVpsSsh is forbidden/],
    [['-Workflow', 'QA Smoke'], /Only Deploy production/],
    [['-WorkflowFile', 'qa-smoke.yml'], /Only Deploy production/],
    [[], /wrapper must own the visibility lease/],
  ]) {
    const result = spawnSync(bin, ['-NoProfile', '-File', join(root, 'scripts', 'ship', 'watch-deploy.ps1'), ...args],
      { encoding: 'utf8', timeout: 10_000 });
    assert.ifError(result.error);
    assert.notEqual(result.status, 0);
    const output = stripVTControlCharacters(result.stdout + result.stderr)
      .replace(/^\s*\|\s*/gm, '').replace(/\s+/g, ' ');
    assert.match(output, message);
  }
});

test('Writing reference reuse preserves the executable settings of the API publish', () => {
  const workflow = readFileSync(join(root, '.github', 'workflows', 'build-images.yml'), 'utf8');
  assert.match(workflow, /reference_args=\(-p:BuildProjectReferences=false -p:UseAppHost=false\)/);
  assert.match(workflow, /\/p:UseAppHost=false/);
});

test('deployment Writing compilation is opt-in and Actions checks the default and required source sets', () => {
  const project = readFileSync(join(root, 'backend', 'tests', 'OetLearner.Api.Tests', 'OetLearner.Api.Tests.csproj'), 'utf8');
  const workflow = readFileSync(join(root, '.github', 'workflows', 'build-images.yml'), 'utf8');
  assert.match(project, /<ItemGroup Condition="'\$\(DeploymentWritingGateOnly\)' == 'true'">/);
  assert.match(project, /<Compile Remove="@\(Compile\)" \/>/);
  assert.match(project, /<Compile Include="AssemblyInfo\.cs;Writing\\WritingRev8ModelAnswerGateTests\.cs;Writing\\WritingModelAnswerBatchTests\.cs" \/>/);
  assert.match(workflow, /-getItem:Compile > output\/writing-default-compile\.json/);
  assert.match(workflow, /-getItem:Compile -p:DeploymentWritingGateOnly=true > output\/writing-gate-compile\.json/);
  assert.match(workflow, /assert gate == expected and expected < default/);
  assert.match(workflow, /dotnet test "\$test_project"[^\n]*-p:DeploymentWritingGateOnly=true/);
  assert.match(workflow, /FullyQualifiedName~WritingRev8ModelAnswerGateTests/);
  assert.match(workflow, /int\(c\.attrib\["executed"\]\) > 0 and int\(c\.attrib\["failed"\]\) == 0/);
});

test('API runtime separates stable dependencies without omitting published bytes', () => {
  const dockerfile = readFileSync(join(root, 'backend', 'Dockerfile.runtime'), 'utf8');
  assert.match(dockerfile, /^# syntax=docker\/dockerfile:1\.19\r?$/m);
  assert.match(dockerfile, /COPY --exclude=OetLearner\.Api\.\* backend\/publish \.\/\r?\nRUN test ! -e \/app\/OetLearner\.Api\.dll\r?\nCOPY backend\/publish\/OetLearner\.Api\.\* \.\//);
  assert.match(dockerfile, /RUN --mount=type=bind,source=backend\/publish,target=\/published/);
  assert.match(dockerfile, /find \. -type f -exec sha256sum \{\} \+ > \/tmp\/published\.sha256/);
  assert.match(dockerfile, /sha256sum --check --quiet \/tmp\/published\.sha256/);
  assert.match(dockerfile, /USER appuser/);
  assert.match(dockerfile, /ENTRYPOINT \["dotnet", "OetLearner\.Api\.dll"\]/);
});

test('web runtime preserves standalone bytes and owners without recursive ownership copy-up', () => {
  const dockerfile = readFileSync(join(root, 'Dockerfile'), 'utf8');
  const runtime = dockerfile.split('FROM node:22-alpine AS runner')[1];
  assert.ok(runtime);
  assert.match(dockerfile, /^# syntax=docker\/dockerfile:1\.19\r?$/m);
  assert.match(runtime, /COPY --from=builder --exclude=server\.js --exclude=\.next \/app\/\.next\/standalone \.\//);
  assert.match(runtime, /COPY --from=builder \/app\/\.next\/standalone\/server\.js \.\/server\.js/);
  assert.match(runtime, /COPY --from=builder --chown=nextjs:nodejs \/app\/\.next\/standalone\/\.next \.\/\.next/);
  assert.match(runtime, /COPY --from=builder --chown=nextjs:nodejs \/app\/\.next\/static \.\/\.next\/static/);
  assert.match(runtime, /COPY --from=builder --chown=nextjs:nodejs \/app\/public \.\/public/);
  assert.match(runtime, /RUN --mount=type=bind,from=builder,source=\/app\/\.next\/standalone,target=\/standalone/);
  assert.match(runtime, /sha256sum -cs \/tmp\/standalone\.sha256/);
  assert.doesNotMatch(runtime, /sha256sum --check|--quiet/);
  assert.match(runtime, /WEB_RUNTIME_BYTES_AND_OWNERS_OK/);
  assert.match(runtime, /chown nextjs:nodejs \/app\/\.next \/app\/\.next\/cache \/app\/public/);
  for (const path of ['.next', 'public', 'node_modules', 'server.js']) {
    const owner = path.startsWith('.') || path === 'public' ? '10001:10001' : '0:0';
    assert.ok(runtime.includes(`test "$(stat -c '%u:%g' ${path})" = "${owner}"`));
  }
  assert.doesNotMatch(runtime, /chown -R/);
  assert.match(runtime, /USER nextjs/);
  assert.match(runtime, /CMD \["node", "server\.js"\]/);
});

test('web cache exports fresh files directly and only skips cleanup on ephemeral hosted runners', () => {
  const workflow = readFileSync(join(root, '.github', 'workflows', 'build-images.yml'), 'utf8');
  const web = workflow.match(/^  build-web:\n([\s\S]*?)(?=^  build-api:)/m)?.[1];
  assert.ok(web);
  assert.match(web, /setup-buildx-action@v3\s+with:\s+cleanup: \$\{\{ runner\.environment != 'github-hosted' \}\}/);
  const nextInjection = web.match(/cache-source: \.build-cache\/next([\s\S]*?)(?=\n      - uses:)/)?.[0];
  assert.ok(nextInjection);
  assert.match(nextInjection, /cache-target: \/app\/\.next\/cache/);
  assert.match(nextInjection, /scratch-dir: \.buildkit-next/);
  assert.match(nextInjection, /skip-extraction: true/);
  const exportStep = web.match(/      - name: Export Next cache directly\n([\s\S]*)/)?.[1];
  assert.ok(exportStep);
  assert.match(exportStep, /if: \$\{\{ github\.event_name != 'pull_request' && !inputs\.benchmark && steps\.mount-cache\.outputs\.cache-hit != 'true' \}\}/);
  assert.match(exportStep, /date --iso=ns > \.buildkit-next\/buildstamp/);
  assert.match(exportStep, /COPY buildstamp buildstamp\n\s*RUN --mount=type=cache,target=\/app\/\.next\/cache/);
  assert.match(exportStep, /FROM scratch\n\s*COPY --from=cache-export \/cache\/ \//);
  assert.match(exportStep, /--file \.buildkit-next\/Dancefile.export/);
  assert.match(exportStep, /--output type=local,dest=\.build-cache\/next,platform-split=false/);
  assert.match(exportStep, /\n\s+\.buildkit-next\s*$/);
  assert.doesNotMatch(exportStep, /--load|--push|docker (?:create|cp)|\|\| true/);
  assert.match(web, /push: \$\{\{ github\.event_name != 'pull_request' && !inputs\.benchmark \}\}/);
});

// Explicit offline Docker/HTTP fixtures exercise driver control flow only.
// Real image builds, nginx validation and serving proof remain Actions/live gates.
function rolloutFixture(mode) {
  const dir = mkdtempSync(join(tmpdir(), 'oet-rollout-test-'));
  mkdirSync(join(dir, 'bin'));
  mkdirSync(join(dir, 'scripts', 'deploy', 'nginx'), { recursive: true });
  mkdirSync(join(dir, '.deploy', 'nginx', 'web'), { recursive: true });
  mkdirSync(join(dir, '.deploy', 'nginx', 'api'), { recursive: true });
  writeFileSync(join(dir, '.env.production'), 'RELEASES_HOST_PATH=/tmp/test-releases\n');
  writeFileSync(join(dir, 'docker-compose.production.yml'), 'name: fixture\nservices: {}\n');
  writeFileSync(join(dir, 'validate.sh'), '#!/usr/bin/env bash\nexit 0\n');
  for (const kind of ['web', 'api']) {
    const template = readFileSync(join(root, 'scripts', 'deploy', 'nginx', `${kind}-bluegreen.conf.template`), 'utf8');
    writeFileSync(join(dir, 'scripts', 'deploy', 'nginx', `${kind}-bluegreen.conf.template`), template);
    if (mode !== 'first') {
      writeFileSync(join(dir, '.deploy', 'nginx', kind, 'default.conf'), template.replaceAll('${ACTIVE_SLOT}', 'blue'));
    }
  }
  writeFileSync(join(dir, 'bin', 'docker'), `#!/usr/bin/env bash
set -eu
printf '%s\\n' "$*" >> "$VPS_APP_DIR/calls"
if [ "$1" = compose ]; then
  if [[ " $* " == *" --hash "* ]]; then
    service="\${!#}"; echo "$service hash"; exit 0
  fi
  if [[ " $* " == *" --images "* ]]; then echo nginx:fixture; exit 0; fi
  if [[ " $* " == *" config "* ]]; then echo stable-effective-config; exit 0; fi
  if [[ " $* " == *" --force-recreate "* ]]; then touch "$VPS_APP_DIR/repaired"; fi
  if [[ " $* " == *" web learner-api " ]]; then
    if [ "$MODE" = router-start-failure ] && [ ! -f "$VPS_APP_DIR/router-failed" ]; then
      touch "$VPS_APP_DIR/router-failed"; exit 1
    fi
    touch "$VPS_APP_DIR/router-running"
  fi
  exit 0
fi
if [ "$1" = image ] && [ "\${2:-}" = inspect ] && [ "$MODE" = pull-failure ] \
  && [[ "$*" == *-web@* ]]; then exit 1; fi
if [ "$1" = pull ] && [ "$MODE" = pull-failure ] && [[ "$*" == *-web@* ]]; then exit 1; fi
if [ "$1" = container ] && [ "\${2:-}" = inspect ] && [ "$MODE" = first ]; then
  case "$3" in
    oet-web|oet-api) [ -f "$VPS_APP_DIR/router-running" ] || exit 1 ;;
    *) [ -f "$VPS_APP_DIR/repaired" ] || exit 1 ;;
  esac
fi
if [ "$1" = inspect ] || { [ "$1" = image ] && [ "\${2:-}" = inspect ]; }; then
  case "$*" in
    *State.Running*)
      if [ "$MODE" = first ] && [[ "$*" == *" oet-web" ]] && [ ! -f "$VPS_APP_DIR/router-running" ]; then echo false; else echo true; fi ;;
    *config-hash*)
      if [ "$MODE" = config ] && [[ "$*" == *oet-ai-worker* ]] && [ ! -f "$VPS_APP_DIR/repaired" ]; then echo old-hash; else echo hash; fi ;;
    *State.Health*)
      if [ "$MODE" = unhealthy ] && [[ "$*" == *oet-api-green* ]] && [ ! -f "$VPS_APP_DIR/repaired" ]; then echo unhealthy; else echo healthy; fi ;;
    *Internal*) echo true ;;
    *)
      if [[ "$*" == *oet-api-green* ]] && [ "$MODE" = stale ] && [ ! -f "$VPS_APP_DIR/repaired" ]; then
        echo old-image
      elif [[ "$*" == *oet-api-green* ]] && [ "$MODE" = wrong-serving-image ] \
        && grep -q learner-api-green "$VPS_APP_DIR/.deploy/nginx/api/default.conf"; then
        echo wrong-image
      else echo image-id; fi ;;
  esac
  exit 0
fi
if [ "$1" = exec ]; then
  container="$2"; shift 2
  if [ "$MODE" = readiness-failure ] && [ "$container" = oet-api-green ] \
    && [[ "$*" == *health/ready* ]]; then exit 1; fi
  case "$container" in oet-web|oet-api) kind="\${container#oet-}" ;; *) exit 0 ;; esac
  config="$VPS_APP_DIR/.deploy/nginx/$kind/default.conf"
  if [ "$1" = cat ] || { [ "$1" = nginx ] && [ "\${2:-}" = -T ]; }; then cat "$config"; exit 0; fi
  if [ "$MODE" = bad-config ] && [[ "$*" == *oet-candidate-main.conf* ]]; then exit 1; fi
  if [[ " $* " == *" reload "* ]] && [ "$MODE" = partial ] && [ "$kind" = api ] \
    && grep -q learner-api-green "$config" && [ ! -f "$VPS_APP_DIR/reload-failed" ]; then
    touch "$VPS_APP_DIR/reload-failed"; exit 1
  fi
fi
exit 0
`);
  writeFileSync(join(dir, 'bin', 'curl'), `#!/usr/bin/env bash
set -eu
[ "$MODE" != public-failure ] || exit 1
headers=""
while [ "$#" -gt 0 ]; do
  if [ "$1" = -D ]; then shift; headers="$1"; fi
  shift
done
if [ -n "$headers" ]; then printf 'X-Oet-Release: %s\\r\\nX-Oet-Slot: %s\\r\\n' "$RELEASE_SHA" "$TARGET_SLOT" > "$headers"; fi
if [ "$MODE" = redirect ]; then printf '307'; else printf '200'; fi
`);
  writeFileSync(join(dir, 'bin', 'sleep'), '#!/usr/bin/env bash\nexit 0\n');
  writeFileSync(join(dir, 'bin', 'mkdir'), '#!/usr/bin/env bash\nif [ "$*" = "-p /var/opt/oet-learner/releases" ]; then exit 0; fi\nexec /usr/bin/mkdir "$@"\n');
  for (const name of ['docker', 'curl', 'sleep', 'mkdir']) chmodSync(join(dir, 'bin', name), 0o755);
  const env = { ...process.env, PATH: `${join(dir, 'bin')}:${process.env.PATH}`, MODE: mode,
    TARGET_SLOT: mode === 'first' ? 'blue' : 'green',
    VPS_APP_DIR: dir, VPS_VALIDATE_ENV_SCRIPT: join(dir, 'validate.sh'), RELEASE_SHA: sha,
    WEB_IMAGE: `ghcr.io/${repo}-web@${digest}`, API_IMAGE: `ghcr.io/${repo}-api@${digest}`,
    DB_BACKUP_IMAGE: `ghcr.io/${repo}-db-backup@${digest}`, AGENT_GATEWAY_IMAGE: `ghcr.io/${repo}-agent-gateway@${digest}` };
  return { dir, run: (phase) => spawnSync('bash', [join(root, 'scripts', 'deploy', 'auto-deploy-ghcr.sh')],
    { env: { ...env, DEPLOY_PHASE: phase }, encoding: 'utf8', timeout: 20_000 }) };
}

for (const mode of ['reuse', 'stale', 'config', 'unhealthy', 'first', 'partial', 'public-failure',
  'bad-config', 'router-start-failure', 'wrong-serving-image', 'redirect', 'bad-preparation', 'pull-failure', 'readiness-failure']) {
  test(`offline rollout: ${mode} preserves scoped recreation, persistence and paired rollback`, () => {
    const fixture = rolloutFixture(mode);
    try {
      const prepared = fixture.run('prepare');
      if (mode === 'pull-failure' || mode === 'readiness-failure') {
        assert.notEqual(prepared.status, 0, prepared.stdout + prepared.stderr);
        assert.doesNotMatch(readFileSync(join(fixture.dir, 'calls'), 'utf8'), /nginx -s reload| web learner-api$/m);
        return;
      }
      assert.equal(prepared.status, 0, prepared.stdout + prepared.stderr);
      if (mode === 'bad-preparation') {
        writeFileSync(join(fixture.dir, '.deploy', `prepared-${sha}`), 'incorrect-identity\n0\n');
      }
      const promoted = fixture.run('promote');
      const calls = readFileSync(join(fixture.dir, 'calls'), 'utf8');
      if (mode === 'bad-preparation') {
        assert.notEqual(promoted.status, 0, promoted.stdout + promoted.stderr);
        assert.match(promoted.stderr, /Prepared release identity mismatch/);
        assert.doesNotMatch(calls, /nginx -s reload| web learner-api$/m);
      } else if (['partial', 'public-failure', 'bad-config', 'router-start-failure', 'wrong-serving-image', 'redirect'].includes(mode)) {
        assert.notEqual(promoted.status, 0, promoted.stdout + promoted.stderr);
        assert.match(promoted.stderr, /DEPLOY_ROLLBACK slot=blue/);
        for (const kind of ['web', 'api']) {
          const config = readFileSync(join(fixture.dir, '.deploy', 'nginx', kind, 'default.conf'), 'utf8');
          assert.doesNotMatch(config, /(?:web|learner-api)-green/);
        }
        if (mode === 'router-start-failure') {
          assert.equal(existsSync(join(fixture.dir, 'router-failed')), true);
          assert.match(calls, /router-rollback\.yml/);
        }
      } else {
        assert.equal(promoted.status, 0, promoted.stdout + promoted.stderr);
        assert.match(promoted.stdout, /DEPLOY_LIVE sha=/);
        const slot = mode === 'first' ? 'blue' : 'green';
        assert.match(readFileSync(join(fixture.dir, '.deploy', 'live-release.env'), 'utf8'), new RegExp(`ACTIVE_SLOT=${slot}`));
        for (const kind of ['web', 'api']) {
          assert.match(readFileSync(join(fixture.dir, '.deploy', 'nginx', kind, 'default.conf'), 'utf8'), new RegExp(`-${slot}`));
        }
        if (mode === 'reuse') assert.doesNotMatch(calls, /--force-recreate/);
        if (['stale', 'unhealthy', 'config'].includes(mode)) {
          const updates = calls.split('\n').filter((line) => line.includes('--force-recreate'));
          assert.equal(updates.length, 1);
          assert.match(updates[0], mode === 'config' ? /ai-worker$/ : /learner-api-green$/);
          assert.doesNotMatch(updates[0], /db-backup|agent-gateway/);
        }
      }
      assert.doesNotMatch(calls, /down.*(?:-v|--volumes)/);
    } finally {
      rmSync(fixture.dir, { recursive: true, force: true });
    }
  });
}

for (const mode of ['failed-descendant', 'redirect', 'missing-runtime-build', 'proven-docs-only',
  'benchmark-failed', 'benchmark-only', 'release-failed']) {
  test(`offline watcher: ${mode} requires actual promotion, direct health and proven no-op`, () => {
    const dir = mkdtempSync(join(tmpdir(), 'oet-watch-test-'));
    try {
      const base = 'd'.repeat(40);
      const own = { databaseId: 101, headSha: sha, displayTitle: `Deploy production ${sha}`,
        status: 'completed', conclusion: 'success', url: 'https://example.test/101', workflowName: 'Deploy production' };
      const failed = { ...own, databaseId: 103, headSha: 'c'.repeat(40),
        displayTitle: `Deploy production ${'c'.repeat(40)}`, conclusion: 'failure', url: 'https://example.test/103' };
      const benchmark = { ...own, databaseId: 104, displayTitle: `Benchmark build ${sha}`,
        conclusion: mode === 'benchmark-failed' ? 'failure' : 'success' };
      const build = { ...own, databaseId: 100, displayTitle: `Build images ${sha}`,
        conclusion: mode === 'release-failed' ? 'failure' : 'success' };
      const waiting = ['benchmark-failed', 'release-failed'].includes(mode);
      const missing = ['missing-runtime-build', 'proven-docs-only', 'benchmark-only'].includes(mode);
      const comparison = { status: 'ahead', merge_base_commit: { sha: missing ? base : sha },
        files: [{ filename: mode === 'proven-docs-only' ? 'docs/example.md' : 'app/page.tsx' }] };
      writeFileSync(join(dir, 'git'), `#!/usr/bin/env bash\nprintf '%s\\n' '${sha}'\n`);
      writeFileSync(join(dir, 'gh'), `#!/usr/bin/env bash
set -eu
if [ "$1" = run ] && [ "$2" = list ]; then
  if [[ "$*" == *build-images.yml* ]]; then
    touch '${join(dir, 'build-observed')}'
    printf '%s\\n' '${JSON.stringify(missing ? (mode === 'benchmark-only' ? [benchmark] : []) : [benchmark, build])}'; exit 0
  fi
  if [ '${waiting}' = true ] && [ ! -f '${join(dir, 'build-observed')}' ]; then echo '[]'; exit 0; fi
  printf '%s\\n' '${JSON.stringify(missing ? [] : [failed, own])}'; exit 0
fi
if [ "$1" = api ] && [[ "$2" == *compare/* ]]; then
  printf '%s\\n' '${JSON.stringify(comparison)}'; exit 0
fi
if [ "$1" = api ]; then echo '{"artifacts":[{"name":"promotion-proof","expired":false}]}'; exit 0; fi
if [ "$1" = run ] && [ "$2" = view ]; then
  if [ "$3" = 101 ]; then echo '{"status":"completed","conclusion":"success"}'
  else echo '{"status":"completed","conclusion":"failure"}'; fi
  exit 0
fi
exit 1
`);
      writeFileSync(join(dir, 'curl.exe'), `#!/usr/bin/env bash\nprintf 'HTTP/1.1 ${mode === 'redirect' ? '307 Temporary Redirect' : '200 OK'}\\r\\nX-Oet-Release: ${sha}\\r\\nX-Oet-Slot: blue\\r\\n\\r\\n{}\\n'\n`);
      writeFileSync(join(dir, 'ssh'), `#!/usr/bin/env bash\nprintf 'RELEASE_SHA=${sha}\\nACTIVE_SLOT=blue\\nSERVING_IMAGE_OK=web\\nSERVING_IMAGE_OK=api\\n'\n`);
      for (const name of ['git', 'gh', 'curl.exe', 'ssh']) chmodSync(join(dir, name), 0o755);
      const result = spawnSync('pwsh', ['-NoProfile', '-File', join(root, 'scripts', 'ship', 'watch-deploy.ps1'),
        '-Sha', sha, '-PushBaseSha', base, '-SkipPublic', '-SkipPrivateFlip', '-TimeoutSeconds', '10',
        '-WaitForRunSeconds', '0', '-PollSeconds', '1'],
      { env: { ...process.env, PATH: `${dir}:${process.env.PATH}` }, encoding: 'utf8', timeout: 20_000 });
      if (mode === 'redirect') {
        assert.equal(result.status, 4, result.stdout + result.stderr);
        assert.match(result.stdout, /Public health must return direct HTTP 200/);
      } else if (['missing-runtime-build', 'benchmark-only'].includes(mode)) {
        assert.equal(result.status, 2, result.stdout + result.stderr);
        assert.match(result.stdout, /SHIP-WATCH_MISSING_BUILD/);
        assert.doesNotMatch(result.stdout, /SHIP-WATCH_NOTHING_TO_DEPLOY/);
      } else if (mode === 'release-failed') {
        assert.equal(result.status, 1, result.stdout + result.stderr);
        assert.match(result.stdout, /SHIP-WATCH_BUILD_FAILED/);
      } else {
        assert.equal(result.status, 0, result.stdout + result.stderr);
        if (mode === 'proven-docs-only') assert.match(result.stdout, /SHIP-WATCH_NOTHING_TO_DEPLOY/);
        else {
          assert.match(result.stdout, /SHIP-WATCH_RUN 101/);
          assert.match(result.stdout, /LIVE_SHA_OK/);
          if (mode === 'benchmark-failed') {
            assert.match(result.stdout, /SHIP-WATCH_BUILD_OK/);
            assert.doesNotMatch(result.stdout, /SHIP-WATCH_BUILD_FAILED/);
          }
        }
      }
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
}

for (const mode of ['success', 'driver-failure', 'login-failure', 'cleanup-failure']) {
  test(`offline registry authorization: ${mode} cleans credentials and preserves failures`, () => {
    const dir = mkdtempSync(join(tmpdir(), 'oet-registry-test-'));
    try {
      mkdirSync(join(dir, 'bin'));
      const workflow = readFileSync(join(root, '.github', 'workflows', 'production-deploy.yml'), 'utf8');
      const phase = /^          deploy_phase\(\) \{\r?\n[\s\S]*?^          \}/m.exec(workflow)?.[0];
      assert.ok(phase, 'The actual workflow registry invocation must be exercised.');
      writeFileSync(join(dir, 'driver.sh'), `exit ${mode === 'driver-failure' ? 23 : 0}\n`);
      writeFileSync(join(dir, 'bin', 'docker'), `#!/usr/bin/env bash
set -eu
if [ "$1" = login ]; then
  cat >/dev/null
  printf '{"auths":{}}\\n' > "$DOCKER_CONFIG/config.json"
  [ "$MODE" != cleanup-failure ] || touch "$DOCKER_CONFIG/unexpected-file"
  [ "$MODE" != login-failure ] || exit 29
elif [ "$1" = logout ]; then
  printf 'logout\\n' >> "$REMOTE_DIR/calls"
else
  exit 1
fi
`);
      chmodSync(join(dir, 'bin', 'docker'), 0o755);
      const result = spawnSync('bash', ['-c', `set -euo pipefail
ssh_opts=()
ssh() { bash -c "\${!#}"; }
${phase}
deploy_phase prepare
`], { env: { ...process.env, PATH: `${join(dir, 'bin')}:${process.env.PATH}`, MODE: mode,
        REMOTE_DIR: dir, REMOTE_SCRIPT: join(dir, 'driver.sh'), REMOTE_COMPOSE: 'offline',
        REMOTE_VALIDATE: 'offline', REMOTE_NGINX: 'offline', REMOTE_NGINX_API: 'offline',
        VPS_USER: 'offline', VPS_HOST: 'offline', APP_DIR: dir, SHA: sha,
        GHCR_TOKEN: 'offline-fixture', GHCR_USER: 'offline', WEB_IMAGE: 'offline', API_IMAGE: 'offline',
        DB_BACKUP_IMAGE: 'offline', AGENT_GATEWAY_IMAGE: 'offline' },
      encoding: 'utf8', timeout: 10_000 });
      const expected = { success: 0, 'driver-failure': 23, 'login-failure': 29, 'cleanup-failure': 1 };
      assert.equal(result.status, expected[mode], result.stdout + result.stderr);
      assert.equal(existsSync(join(dir, 'docker-auth-prepare', 'config.json')), false);
      assert.equal(existsSync(join(dir, 'docker-auth-prepare')), mode === 'cleanup-failure');
      assert.equal(readFileSync(join(dir, 'calls'), 'utf8'), 'logout\n');
      if (mode === 'cleanup-failure') assert.match(result.stderr, /registry authorization cleanup failed/i);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
}
