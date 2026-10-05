// Behavioural tests of oet-fleet-ctl, the privileged helper-side control script (RW-141, RW-142, RW-150). The real script runs
// under bash with a stub `docker` first in PATH that records every call; paths are redirected into a temp directory with the
// test-only overrides the script documents. Runs on GitHub Actions only (node --test).
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const ctl = resolve(here, '../../ansible/roles/fleet_user/files/oet-fleet-ctl');
const REPO = 'ghcr.io/jerryboganda/oetwebapp-fleet-agent';
const digest = `sha256:${'a'.repeat(64)}`;
const imageId = `sha256:${'c'.repeat(64)}`;
const apiBase = 'https://api.example.test';
const nodeId = `rw_${'0'.repeat(26)}`;
// Assembled at run time: no token-shaped literal exists in the repository.
const nodeToken = `orw1_${'a'.repeat(16)}_${'B'.repeat(43)}`;

const dockerStub = `#!/bin/sh
printf '%s\\n' "$*" >> "$STUB_LOG"
case "$1" in
  login) cat > "$STUB_STDIN"; exit 0 ;;
  logout) exit 0 ;;
  pull) printf '%s\\n' '${imageId}'; exit 0 ;;
  run) printf '%s\\n' '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'; exit 0 ;;
  inspect) exit 1 ;;
  stop|restart|rm|rmi) exit 0 ;;
  logs) printf 'agent log line\\n'; exit 0 ;;
  image)
    case "$2" in
      inspect)
        case "$*" in
          *RepoDigests*) printf '%s\\n' '${REPO}@${digest}'; exit 0 ;;
          *'{{.Id}}'*) printf '%s\\n' '${imageId}'; exit 0 ;;
          *) exit 0 ;;
        esac ;;
      ls) printf '%s\\n' '${digest}' 'sha256:${'1'.repeat(64)}' 'sha256:${'2'.repeat(64)}' 'sha256:${'3'.repeat(64)}' 'sha256:${'4'.repeat(64)}'; exit 0 ;;
    esac ;;
esac
exit 0
`;

function sandbox() {
  const dir = mkdtempSync(join(tmpdir(), 'oet-ctl-'));
  const bin = join(dir, 'bin');
  mkdirSync(bin);
  mkdirSync(join(dir, 'etc'));
  mkdirSync(join(dir, 'run'));
  mkdirSync(join(dir, 'units'));
  writeFileSync(join(bin, 'docker'), dockerStub);
  chmodSync(join(bin, 'docker'), 0o755);
  return {
    dir,
    log: join(dir, 'docker.log'),
    stdin: join(dir, 'docker.stdin'),
    etc: join(dir, 'etc'),
    run: join(dir, 'run'),
    units: join(dir, 'units'),
    bin,
  };
}

function runCtl(box, args, input = '') {
  const result = spawnSync('bash', [ctl, ...args], {
    env: {
      ...process.env,
      OET_FLEET_TEST_PATH: box.bin,
      OET_FLEET_ETC: box.etc,
      OET_FLEET_RUN: box.run,
      OET_FLEET_UNIT_DIR: box.units,
      OET_FLEET_SKIP_SYSTEMCTL: '1',
      OET_FLEET_ALLOW_NON_ROOT: '1',
      STUB_LOG: box.log,
      STUB_STDIN: box.stdin,
    },
    input,
    encoding: 'utf8',
  });
  const calls = existsSync(box.log) ? readFileSync(box.log, 'utf8').trim().split('\n').filter(Boolean) : [];
  return { status: result.status, stdout: result.stdout.trim(), stderr: result.stderr, calls };
}

function envText(extra = {}) {
  const base = {
    OET_API_BASE: apiBase,
    OET_NODE_ID: nodeId,
    OET_NODE_TOKEN: nodeToken,
    OET_AGENT_IMAGE_DIGEST: digest,
    OET_BUDGET_CPU_MILLI: '1000',
    OET_BUDGET_MEM_MIB: '1024',
    OET_BUDGET_TMP_MIB: '512',
    ...extra,
  };
  return `${Object.entries(base).map(([key, value]) => `${key}=${value}`).join('\n')}\n`;
}

function parse(stdout) {
  return JSON.parse(stdout.split('\n').pop());
}

test('put-env writes exactly the validated allow-listed keys atomically with mode 0600', () => {
  const box = sandbox();
  const result = runCtl(box, ['put-env'], envText());

  assert.equal(result.status, 0, result.stdout);
  assert.deepEqual(parse(result.stdout), { ok: true });
  const written = readFileSync(join(box.etc, 'agent.env'), 'utf8');
  assert.equal(written, envText());
  if (process.platform !== 'win32') assert.equal(statSync(join(box.etc, 'agent.env')).mode & 0o777, 0o600);
  assert.ok(!result.stdout.includes(nodeToken), 'the token must never be echoed');
});

for (const [name, text] of [
  ['an unknown key', envText({ OET_SECRET_PROVIDER_KEY: 'x' })],
  ['an http API base', envText({ OET_API_BASE: 'http://api.example.test' })],
  ['an API base with a path', envText({ OET_API_BASE: 'https://api.example.test/x' })],
  ['a malformed node id', envText({ OET_NODE_ID: 'rw_short' })],
  ['a malformed token', envText({ OET_NODE_TOKEN: 'orw1_nope' })],
  ['a malformed digest', envText({ OET_AGENT_IMAGE_DIGEST: 'sha256:abc' })],
  ['a non-numeric budget', envText({ OET_BUDGET_MEM_MIB: 'lots' })],
  ['an unknown log level', envText({ OET_LOG_LEVEL: 'shouty' })],
  ['a duplicate key', `${envText()}OET_NODE_ID=${nodeId}\n`],
  ['a line without an equals sign', `${envText()}garbage\n`],
  ['a missing required key', envText().split('\n').filter((line) => !line.startsWith('OET_NODE_TOKEN=')).join('\n')],
  ['an injected extra line', `${envText()}PATH=/tmp\n`],
]) {
  test(`put-env rejects ${name} and leaves no file behind`, () => {
    const box = sandbox();
    const result = runCtl(box, ['put-env'], text);

    assert.notEqual(result.status, 0);
    assert.equal(parse(result.stdout).ok, false);
    assert.equal(existsSync(join(box.etc, 'agent.env')), false);
    assert.ok(!result.stdout.includes(nodeToken));
  });
}

test('put-env refuses input larger than 4 KiB', () => {
  const box = sandbox();
  const result = runCtl(box, ['put-env'], `${envText()}${'#'.repeat(4200)}\n`);

  assert.notEqual(result.status, 0);
  assert.equal(existsSync(join(box.etc, 'agent.env')), false);
});

test('login takes the credential on stdin only; it never reaches the argument vector or the output', () => {
  const box = sandbox();
  const token = `ghs_${'x'.repeat(36)}`;
  const result = runCtl(box, ['login', '--registry', 'ghcr.io'], `github-actions\n${token}\n`);

  assert.equal(result.status, 0, result.stdout);
  assert.deepEqual(parse(result.stdout), { ok: true });
  assert.ok(result.calls.some((call) => call.startsWith('login ghcr.io -u github-actions --password-stdin')));
  assert.ok(result.calls.every((call) => !call.includes(token)), 'the token must not appear in any argument');
  assert.equal(readFileSync(box.stdin, 'utf8'), token);
  assert.ok(!result.stdout.includes(token));
});

for (const [name, input] of [
  ['an invalid user name', `bad user!\n${'t'.repeat(30)}\n`],
  ['a token with illegal characters', 'github-actions\ntoken with spaces\n'],
  ['a missing token line', 'github-actions\n'],
]) {
  test(`login rejects ${name}`, () => {
    const box = sandbox();
    const result = runCtl(box, ['login', '--registry', 'ghcr.io'], input);
    assert.notEqual(result.status, 0);
    assert.equal(result.calls.length, 0, 'docker must not have been called');
  });
}

test('logout removes the temporary credential directory', () => {
  const box = sandbox();
  mkdirSync(join(box.run, 'dockercfg'));
  writeFileSync(join(box.run, 'dockercfg', 'config.json'), '{}');

  const result = runCtl(box, ['logout']);

  assert.equal(result.status, 0);
  assert.equal(existsSync(join(box.run, 'dockercfg')), false);
});

test('pull accepts only the fleet agent repository by digest and reports the image id and repo digests', () => {
  const box = sandbox();
  const result = runCtl(box, ['pull', `${REPO}@${digest}`]);

  assert.equal(result.status, 0, result.stdout);
  const body = parse(result.stdout);
  assert.equal(body.ok, true);
  assert.equal(body.imageId, imageId);
  assert.deepEqual(body.repoDigests, [`${REPO}@${digest}`]);
});

for (const ref of [
  `ghcr.io/someone/else@${digest}`,
  `${REPO}:latest`,
  `${REPO}@sha256:short`,
  `${REPO}@latest`,
  `docker.io/library/alpine@${digest}`,
]) {
  test(`pull refuses ${ref}`, () => {
    const box = sandbox();
    const result = runCtl(box, ['pull', ref]);
    assert.notEqual(result.status, 0);
    assert.equal(result.calls.length, 0);
  });
}

test('verify succeeds when the digest is present and the image id matches, and fails on a mismatch', () => {
  const box = sandbox();
  assert.equal(runCtl(box, ['verify', digest]).status, 0);
  assert.equal(runCtl(box, ['verify', digest, imageId]).status, 0);
  const mismatch = runCtl(box, ['verify', digest, `sha256:${'d'.repeat(64)}`]);
  assert.notEqual(mismatch.status, 0);
  assert.equal(parse(mismatch.stdout).error, 'image_id_mismatch');
});

function flagsOf(call) {
  return call.split(' ');
}

test('run starts the container with exactly the fixed hardened flags and nothing caller-controlled', () => {
  const box = sandbox();
  assert.equal(runCtl(box, ['put-env'], envText()).status, 0);

  const result = runCtl(box, ['run', digest]);

  assert.equal(result.status, 0, result.stdout);
  assert.equal(parse(result.stdout).ok, true);
  const call = result.calls.find((entry) => entry.startsWith('run -d'));
  assert.ok(call, 'docker run was not called');
  const text = ` ${call} `;
  for (const required of [
    '--name oet-fleet-agent', '--pull never', '--init', '--read-only', '--cap-drop ALL', '--security-opt no-new-privileges',
    '--pids-limit 512', '--memory 1024m', '--memory-swap 1024m', '--cpus 1.00', '--user 10001:10001', '--ulimit core=0',
    '--tmpfs /scratch:rw,noexec,nosuid,nodev,size=512m', '--tmpfs /tmp:rw,noexec,nosuid,nodev,size=256m',
    '--log-driver local', '--log-opt max-size=5m', '--log-opt max-file=2', '--restart unless-stopped',
    `--env-file ${join(box.etc, 'agent.env')}`, '--health-interval 30s',
  ]) {
    assert.ok(text.includes(` ${required} `) || text.includes(` ${required}`), `missing flag: ${required}`);
  }
  // The image is by digest, last.
  assert.ok(call.endsWith(`${REPO}@${digest}`));
  // Nothing that widens the container.
  for (const forbidden of ['--privileged', '--network host', '--net host', ' -v ', '--volume', '--mount', 'docker.sock', '--device', '--cap-add', '--pid host', '--userns host']) {
    assert.ok(!call.includes(forbidden), `forbidden flag present: ${forbidden}`);
  }
  assert.equal(flagsOf(call).filter((flag) => flag === '--user').length, 1);
});

test('run refuses a digest that differs from the env file, a missing env file, or a missing image', () => {
  const box = sandbox();
  assert.equal(parse(runCtl(box, ['run', digest]).stdout).error, 'env_file_missing');

  assert.equal(runCtl(box, ['put-env'], envText()).status, 0);
  const other = `sha256:${'e'.repeat(64)}`;
  assert.equal(parse(runCtl(box, ['run', other]).stdout).error, 'digest_differs_from_env');
  assert.equal(runCtl(box, ['run', 'latest']).status, 1);
});

test('run clamps the budgets to the host and never exceeds the hard maxima', () => {
  const box = sandbox();
  assert.equal(runCtl(box, ['put-env'], envText({ OET_BUDGET_CPU_MILLI: '64000', OET_BUDGET_MEM_MIB: '262144', OET_BUDGET_TMP_MIB: '65536' })).status, 0);

  const result = runCtl(box, ['run', digest]);

  assert.equal(result.status, 0, result.stdout);
  const call = result.calls.find((entry) => entry.startsWith('run -d'));
  const cpus = Number(/--cpus ([0-9.]+)/.exec(call)[1]);
  const memory = Number(/--memory ([0-9]+)m/.exec(call)[1]);
  const tmpfs = Number(/--tmpfs \/scratch:[^ ]*size=([0-9]+)m/.exec(call)[1]);
  assert.ok(cpus <= 64);
  assert.ok(memory <= 262144);
  assert.ok(tmpfs <= memory, 'tmpfs is RAM and must fit inside the memory limit');
});

test('stop uses the default 90 s grace and accepts only 1..120', () => {
  const box = sandbox();
  assert.equal(runCtl(box, ['stop']).status, 0);
  assert.equal(runCtl(box, ['stop', '--grace', '30']).status, 0);
  assert.notEqual(runCtl(box, ['stop', '--grace', '0']).status, 0);
  assert.notEqual(runCtl(box, ['stop', '--grace', '121']).status, 0);
  assert.notEqual(runCtl(box, ['stop', '--grace', 'abc']).status, 0);
});

test('logs accepts only a bounded tail', () => {
  const box = sandbox();
  assert.equal(runCtl(box, ['logs', '--tail', '50']).status, 0);
  assert.notEqual(runCtl(box, ['logs', '--tail', '0']).status, 0);
  assert.notEqual(runCtl(box, ['logs', '--tail', '201']).status, 0);
  assert.notEqual(runCtl(box, ['logs']).status, 0);
});

test('prune removes only fleet agent images beyond the newest three', () => {
  const box = sandbox();
  const result = runCtl(box, ['prune']);

  assert.equal(result.status, 0, result.stdout);
  const removed = result.calls.filter((call) => call.startsWith('rmi '));
  assert.ok(removed.every((call) => call.startsWith(`rmi ${REPO}@sha256:`)));
  assert.equal(removed.length, 2);
  assert.deepEqual(parse(result.stdout), { ok: true, removed: 2 });
});

test('unit-sync writes a boot unit that only starts and stops the fleet agent container', () => {
  const box = sandbox();
  const result = runCtl(box, ['unit-sync']);

  assert.equal(result.status, 0, result.stdout);
  const unit = readFileSync(join(box.units, 'oet-fleet-agent.service'), 'utf8');
  assert.match(unit, /ExecStart=-\/usr\/bin\/docker start oet-fleet-agent/);
  assert.match(unit, /ExecStop=-\/usr\/bin\/docker stop -t 90 oet-fleet-agent/);
  assert.doesNotMatch(unit, /docker (rm|rmi|system|volume|network)|prune/);
});

test('an unknown or missing verb is refused with a JSON error', () => {
  const box = sandbox();
  assert.equal(parse(runCtl(box, ['exec']).stdout).error, 'unknown_verb');
  assert.equal(parse(runCtl(box, []).stdout).error, 'missing_verb');
});

test('the script never references a volume, a prune of anything but its own images, a network or the docker socket', () => {
  const source = readFileSync(ctl, 'utf8').split('\n').filter((line) => !/^\s*#/.test(line)).join('\n');
  assert.doesNotMatch(source, /docker\s+(volume|network|system|compose|build)\b/);
  assert.doesNotMatch(source, /docker\.sock/);
  assert.doesNotMatch(source, /\bdocker\s+rm\s+-f\s+(?!"\$CONTAINER")/);
  assert.doesNotMatch(source, /rm\s+-rf\s+\/(?!run)/);
});
