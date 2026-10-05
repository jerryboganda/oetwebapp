// Behavioural tests of the SSH forced-command gate (RW-141). The real script runs under bash with a stub `sudo` first in PATH:
// the stub records what the gate would have executed as root. Runs on GitHub Actions only (node --test).
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const gate = resolve(here, '../../ansible/roles/fleet_user/files/oet-fleet-gate');
const REPO = 'ghcr.io/jerryboganda/oetwebapp-fleet-agent';
const digest = `sha256:${'a'.repeat(64)}`;

function makeSandbox() {
  const dir = mkdtempSync(join(tmpdir(), 'oet-gate-'));
  const sudoLog = join(dir, 'sudo.log');
  const sudoStdin = join(dir, 'sudo.stdin');
  const stub = join(dir, 'sudo');
  writeFileSync(stub, `#!/bin/sh\nprintf '%s\\n' "$@" > '${sudoLog}'\ncat > '${sudoStdin}'\n`);
  chmodSync(stub, 0o755);
  return { dir, sudoLog, sudoStdin };
}

function runGate(command, stdin = '') {
  const sandbox = makeSandbox();
  const result = spawnSync('bash', [gate], {
    env: { ...process.env, PATH: `${sandbox.dir}:${process.env.PATH}`, SSH_ORIGINAL_COMMAND: command },
    input: stdin,
    encoding: 'utf8',
  });
  const ran = existsSync(sandbox.sudoLog) ? readFileSync(sandbox.sudoLog, 'utf8').trim().split('\n') : null;
  const piped = existsSync(sandbox.sudoStdin) ? readFileSync(sandbox.sudoStdin, 'utf8') : null;
  return { status: result.status, stderr: result.stderr, ran, piped };
}

const accepted = [
  ['status', 'oet-fleet-ctl status', ['status']],
  ['harden-check', 'oet-fleet-ctl harden-check', ['harden-check']],
  ['login', 'oet-fleet-ctl login --registry ghcr.io', ['login', '--registry', 'ghcr.io']],
  ['logout', 'oet-fleet-ctl logout', ['logout']],
  ['pull', `oet-fleet-ctl pull ${REPO}@${digest}`, ['pull', `${REPO}@${digest}`]],
  ['verify', `oet-fleet-ctl verify ${digest}`, ['verify', digest]],
  ['verify with image id', `oet-fleet-ctl verify ${digest} sha256:${'b'.repeat(64)}`, ['verify', digest, `sha256:${'b'.repeat(64)}`]],
  ['put-env', 'oet-fleet-ctl put-env', ['put-env']],
  ['run', `oet-fleet-ctl run ${digest}`, ['run', digest]],
  ['stop', 'oet-fleet-ctl stop', ['stop']],
  ['stop with grace', 'oet-fleet-ctl stop --grace 90', ['stop', '--grace', '90']],
  ['restart', 'oet-fleet-ctl restart', ['restart']],
  ['logs', 'oet-fleet-ctl logs --tail 100', ['logs', '--tail', '100']],
  ['prune', 'oet-fleet-ctl prune', ['prune']],
  ['wipe-scratch', 'oet-fleet-ctl wipe-scratch', ['wipe-scratch']],
  ['unit-sync', 'oet-fleet-ctl unit-sync', ['unit-sync']],
];

for (const [name, command, expectedArgs] of accepted) {
  test(`accepts ${name} and runs exactly the control script as root`, () => {
    const result = runGate(command);
    assert.equal(result.status, 0, result.stderr);
    assert.deepEqual(result.ran, ['-n', '/usr/local/sbin/oet-fleet-ctl', ...expectedArgs]);
  });
}

const rejected = [
  ['an empty command', ''],
  ['a shell', 'bash'],
  ['a command that is not the control script', 'id'],
  ['a different program', 'cat /etc/shadow'],
  ['command chaining with a semicolon', 'oet-fleet-ctl status; id'],
  ['command chaining with &&', 'oet-fleet-ctl status && id'],
  ['a pipe', 'oet-fleet-ctl status | sh'],
  ['command substitution', 'oet-fleet-ctl logs --tail $(id)'],
  ['backticks', 'oet-fleet-ctl status `id`'],
  ['a redirect', 'oet-fleet-ctl status > /etc/passwd'],
  ['an embedded newline', 'oet-fleet-ctl status\nid'],
  ['quotes', 'oet-fleet-ctl "status"'],
  ['a glob', 'oet-fleet-ctl logs --tail *'],
  ['an unknown verb', 'oet-fleet-ctl exec'],
  ['a shell verb', 'oet-fleet-ctl shell'],
  ['extra arguments to a no-argument verb', 'oet-fleet-ctl status extra'],
  ['another registry for login', 'oet-fleet-ctl login --registry docker.io'],
  ['login without the registry flag', 'oet-fleet-ctl login ghcr.io'],
  ['a pull from another repository', `oet-fleet-ctl pull ghcr.io/someone/else@${digest}`],
  ['a pull by tag', `oet-fleet-ctl pull ${REPO}:latest`],
  ['a pull of a mutable tag even with the right repository', `oet-fleet-ctl pull ${REPO}@latest`],
  ['a pull with a short digest', `oet-fleet-ctl pull ${REPO}@sha256:abc`],
  ['a pull of two images', `oet-fleet-ctl pull ${REPO}@${digest} ${REPO}@${digest}`],
  ['verify with a malformed digest', 'oet-fleet-ctl verify sha256:xyz'],
  ['run with a tag', 'oet-fleet-ctl run latest'],
  ['run with an uppercase digest', `oet-fleet-ctl run sha256:${'A'.repeat(64)}`],
  ['run without a digest', 'oet-fleet-ctl run'],
  ['stop with a grace of zero', 'oet-fleet-ctl stop --grace 0'],
  ['stop with a grace above 120', 'oet-fleet-ctl stop --grace 121'],
  ['stop with a non-numeric grace', 'oet-fleet-ctl stop --grace abc'],
  ['stop with another flag', 'oet-fleet-ctl stop --force'],
  ['logs without a tail', 'oet-fleet-ctl logs'],
  ['logs with a tail of zero', 'oet-fleet-ctl logs --tail 0'],
  ['logs with a tail above 200', 'oet-fleet-ctl logs --tail 201'],
  ['an uppercase program name', 'OET-FLEET-CTL status'],
  ['an absolute path to the script', '/usr/local/sbin/oet-fleet-ctl status'],
  ['an over-long command', `oet-fleet-ctl logs --tail ${'1'.repeat(500)}`],
];

for (const [name, command] of rejected) {
  test(`rejects ${name} with exit 126 and never reaches sudo`, () => {
    const result = runGate(command);
    assert.equal(result.status, 126, result.stderr);
    assert.equal(result.ran, null, 'sudo must not have been called');
    assert.match(result.stderr, /oet-fleet-gate:/);
  });
}

test('passes stdin through untouched (login credentials, put-env text)', () => {
  const result = runGate('oet-fleet-ctl put-env', 'OET_API_BASE=https://api.example.test\n');
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.piped, 'OET_API_BASE=https://api.example.test\n');
});

test('the gate never echoes the rejected command text back as executable output', () => {
  const result = runGate('oet-fleet-ctl status; echo pwned');
  assert.equal(result.status, 126);
  assert.doesNotMatch(result.stderr, /pwned/);
});
