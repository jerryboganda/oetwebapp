// Regression cases for the fleet workflow contract checker (verify-fleet-workflow.sh) and the shape of fleet.yml itself.
// Each case mutates a COPY of the workflow and asserts the checker rejects it, so a weakened rule cannot go unnoticed.
// Runs on GitHub Actions only (node --test).
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '../../../..');
const verifier = resolve(repo, 'platform/fleet/scripts/verify-fleet-workflow.sh');
const workflowPath = resolve(repo, '.github/workflows/fleet.yml');
const original = readFileSync(workflowPath, 'utf8');

function check(mutate, { expectChange = true } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'oet-fleet-wf-'));
  const file = join(dir, 'fleet.yml');
  const mutated = mutate(original);
  // A mutation whose search text drifted away would silently test nothing.
  if (expectChange) assert.notEqual(mutated, original, 'the mutation did not change the workflow');
  writeFileSync(file, mutated);
  const scanRoot = join(dir, 'scan');
  mkdirSync(scanRoot);
  const result = spawnSync('bash', [verifier], {
    env: { ...process.env, FLEET_WORKFLOW_FILE: file, FLEET_SCAN_ROOT: scanRoot },
    encoding: 'utf8',
  });
  return { status: result.status, stderr: result.stderr, stdout: result.stdout };
}

test('the committed workflow passes the contract', () => {
  const result = check((text) => text, { expectChange: false });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /OK/);
});

const mutations = [
  ['a docker build on the VPS', (text) => text.replace('compose up -d --no-build --wait --wait-timeout 300', 'docker build -t x .\n          compose up -d --no-build --wait --wait-timeout 300'), /build\/test\/install|source-sync/],
  ['a compose build', (text) => text.replace('compose pull --quiet; then', 'compose build; then compose pull --quiet; then'), /build\/test\/install|source-sync/],
  ['an up without --no-build', (text) => text.replace('compose up -d --no-build --wait --wait-timeout 300', 'compose up -d --wait --wait-timeout 300'), /--no-build/],
  ['an extra up without --no-build', (text) => text.replace('          compose up -d --no-build --wait --wait-timeout 300', '          compose up -d --no-build --wait --wait-timeout 300\n          compose up -d --force-recreate'), /--no-build/],
  ['a missing compose pull', (text) => text.replaceAll('compose pull --quiet', 'compose images'), /compose pull/],
  ['an npm install on the VPS', (text) => text.replace('log() {', 'npm install\n          log() {'), /build\/test\/install|source-sync/],
  ['a dotnet command on the VPS', (text) => text.replace('log() {', 'dotnet --info\n          log() {'), /build\/test\/install|source-sync/],
  ['a source sync on the VPS', (text) => text.replace('log() {', 'git pull\n          log() {'), /build\/test\/install|source-sync/],
  ['a mutable latest tag in the rollout', (text) => text.replace('printf \'FLEET_AGENT_IMAGE=%s@%s\\n\'', 'printf \'FLEET_AGENT_IMAGE=%s:latest\\n\''), /mutable :latest tag/],
  ['accept-new host key handling', (text) => text.replaceAll('StrictHostKeyChecking=yes', 'StrictHostKeyChecking=accept-new'), /accept-new/],
  ['no pinned known_hosts file', (text) => text.replaceAll('UserKnownHostsFile', 'UserSomethingElse'), /known_hosts|UserKnownHostsFile/],
  ['a registry token in argv instead of stdin', (text) => text.replace('--password-stdin', '-p "$GHCR_TOKEN"'), /stdin/],
  ['no registry logout trap', (text) => text.replaceAll('docker logout', 'docker version'), /logout/],
  ['the production rollout script', (text) => `${text}\n# x\n      - run: bash scripts/deploy/auto-deploy-ghcr.sh\n`, /rollout script/],
  ['a pull_request trigger', (text) => text.replace('workflow_dispatch:\n    inputs:', 'pull_request:\n  workflow_dispatch:\n    inputs:'), /pull_request/],
  ['a schedule trigger', (text) => text.replace('workflow_dispatch:\n    inputs:', 'schedule:\n    - cron: "0 3 * * *"\n  workflow_dispatch:\n    inputs:'), /schedule/],
  ['a lost shared-source path filter', (text) => text.replace("      - 'backend/src/OetLearner.Api/Services/Content/PdfPigPdfTextExtractor.cs'\n", ''), /PdfPigPdfTextExtractor/],
  ['a lost csproj path filter', (text) => text.replace("      - 'backend/src/OetLearner.Api/OetLearner.Api.csproj'\n", ''), /OetLearner\.Api\.csproj/],
  ['a lost global.json path filter', (text) => text.replace("      - 'global.json'\n", ''), /global\.json/],
  ['a lost Directory.Build.props path filter', (text) => text.replace("      - 'backend/Directory.Build.props'\n", ''), /Directory\.Build\.props/],
  ['a Playwright lane', (text) => `${text}\n      - run: pnpm exec playwright test\n`, /Playwright/],
  ['the sync job reachable without dispatch', (text) => text.replace("github.event_name == 'workflow_dispatch' && inputs.sync", 'true'), /workflow_dispatch|sync input/],
  ['production SSH credentials outside the sync job', (text) => text.replace('  guards:\n', '  guards:\n    env:\n      K: ${{ secrets.PROD_SSH_KEY }}\n'), /sync job may use/],
  ['a stray ssh command outside the checked options', (text) => `${text}\n          ssh root@185.252.233.186 id\n`, /ssh_opts/],
  ['a missing marker', (text) => text.replace('# END REMOTE FLEET ROLLOUT', '# END'), /exactly one END marker/],
  ['a duplicated marker', (text) => text.replace('# END REMOTE FLEET ROLLOUT', '# END REMOTE FLEET ROLLOUT\n          # END REMOTE FLEET ROLLOUT'), /exactly one END marker/],
];

for (const [name, mutate, expected] of mutations) {
  test(`the checker rejects ${name}`, () => {
    const result = check(mutate);
    assert.equal(result.status, 1, `expected a rejection but the checker passed\n${result.stdout}`);
    assert.match(result.stderr, expected);
  });
}

test('the checker rejects a secret-shaped string under the scan root', () => {
  const dir = mkdtempSync(join(tmpdir(), 'oet-fleet-scan-'));
  const scanRoot = join(dir, 'scan');
  mkdirSync(scanRoot);
  // Assembled at run time so no token-shaped literal exists in this file.
  writeFileSync(join(scanRoot, 'leak.txt'), `token=orw1_${'a'.repeat(16)}_${'B'.repeat(43)}\n`);
  const result = spawnSync('bash', [verifier], {
    env: { ...process.env, FLEET_WORKFLOW_FILE: workflowPath, FLEET_SCAN_ROOT: scanRoot },
    encoding: 'utf8',
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /secret-shaped strings/);
  assert.doesNotMatch(result.stderr, /orw1_a{16}_/, 'the value itself must never be printed');
});

test('fleet.yml is a separate pipeline: its own concurrency group, dispatch-only VPS access, builds by digest, no deploy script', () => {
  const active = original.split('\n').filter((line) => !/^\s*#/.test(line)).join('\n');
  assert.match(original, /^concurrency:\n  group: fleet\n  cancel-in-progress: false/m);
  assert.match(original, /^name: Fleet \(build \+ rollout\)$/m);
  assert.doesNotMatch(active, /auto-deploy-ghcr\.sh/);
  assert.doesNotMatch(active, /production-deploy|build-images/, 'no coupling to the production release graph');
  assert.match(original, /tags: ghcr\.io\/\$\{\{ github\.repository \}\}-fleet-agent:\$\{\{ github\.sha \}\}/);
  assert.match(original, /tags: ghcr\.io\/\$\{\{ github\.repository \}\}-fleet-manager:\$\{\{ github\.sha \}\}/);
  assert.match(original, /provenance: false/);
  assert.match(original, /agentDigest/);
  assert.match(original, /parity:/);
  assert.match(original, /RW100_agent_output_is_byte_equal/);
});
