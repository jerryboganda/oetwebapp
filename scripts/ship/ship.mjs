#!/usr/bin/env node
/**
 * One-command ship, safe for several AI agents shipping in parallel.
 *
 *   pnpm run ship                       full flow (lock -> rebase -> gate -> lease
 *                                       -> public -> push -> watch -> record)
 *   pnpm run ship -- --no-watch         stop after the push
 *   pnpm run ship -- --no-push          lock + rebase + gate + lease, then stop
 *                                       (two-shell lease testing)
 *   pnpm run ship -- --dry-run          fetch + gate + report, no mutation
 *   pnpm run ship -- --sha <sha>        watch an existing SHA (skip rebase/push)
 *   pnpm run ship -- --status           show lock / lease / visibility state
 *   pnpm run ship -- --release-lease    force-release a stuck visibility lease
 *
 * Concurrency model (owner directive 2026-10-03):
 * - One ship LOCK per repository (shared by every linked worktree through
 *   <git-common-dir>/ax-ship/) so two local sessions never rebase/push at once.
 * - One VISIBILITY LEASE drives public/private: the repo only needs to be public
 *   while hosted Actions runs exist. The last lease holder flips it private,
 *   and only when no Actions run is queued or in progress.
 * - GitHub itself serializes production rollouts (workflow concurrency);
 *   builds of different SHAs run in parallel by design. A push may therefore be
 *   SUPERSEDED by a newer push before its deploy runs - the newer commit contains
 *   it, and watch-deploy.ps1 follows the newer run instead of failing.
 *
 * Compute policy (AGENTS.md): this script performs git/gh/host commands and the
 * seconds-long static ship gate. It never builds, tests, lints or installs.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { hostname } from 'node:os';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const LOCK_TTL_MS = 30 * 60 * 1000;
const LEASE_TTL_MS = 60 * 60 * 1000;
const HEARTBEAT_MS = 60 * 1000;
const BASE_BRANCH = 'main';
const SESSION = process.env.AX_SHIP_SESSION || `${hostname()}#${process.pid}`;

// ------------------------------------------------------------------ helpers

export function parseRepoSlug(remoteUrl) {
  const value = String(remoteUrl ?? '').trim();
  const match = /github\.com[:/]([^/]+\/[^/]+?)(?:\.git)?$/i.exec(value);
  return match ? match[1] : null;
}

export function parseUpdatedAt(source) {
  const match = /^Updated:\s*(\S+)/m.exec(String(source ?? ''));
  if (!match) return null;
  const parsed = new Date(match[1]);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/**
 * The documented ledger conflict rule: keep the side with the newer
 * `Updated:` block wholesale (scripts/agent/README.md). Returns the winning
 * source plus which side won, or null when neither side is parseable.
 */
export function pickNewerLedger(a, b) {
  const aDate = parseUpdatedAt(a);
  const bDate = parseUpdatedAt(b);
  if (!aDate && !bDate) return null;
  if (aDate && (!bDate || aDate >= bDate)) return { source: a, side: 'a' };
  return { source: b, side: 'b' };
}

export function leaseIsActive(lease, now = Date.now()) {
  if (!lease || typeof lease !== 'object') return false;
  const expires = Date.parse(String(lease.expiresAt ?? ''));
  return Number.isFinite(expires) && expires > now;
}

// --------------------------------------------------------------- git / gh

function git(args, { allowFail = false, cwd = root } = {}) {
  try {
    return execFileSync('git', args, {
      cwd,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      maxBuffer: 64 * 1024 * 1024,
    }).trim();
  } catch (error) {
    if (allowFail) return null;
    throw new Error(`git ${args.join(' ')} failed: ${String(error.stderr ?? error.message).trim()}`);
  }
}

function gh(args, { allowFail = true } = {}) {
  const result = spawnSync('gh', args, {
    cwd: root,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 64 * 1024 * 1024,
  });
  if (result.status !== 0) {
    if (allowFail) return null;
    throw new Error(`gh ${args.join(' ')} failed: ${String(result.stderr ?? '').trim()}`);
  }
  return String(result.stdout ?? '').trim();
}

function ghJson(args) {
  const raw = gh(args);
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

export function statePaths() {
  const common = git(['rev-parse', '--git-common-dir']);
  const base = isAbsolute(common) ? common : resolve(root, common);
  const stateDir = join(base, 'ax-ship');
  return {
    dir: stateDir,
    lock: join(stateDir, 'lock.json'),
    lease: join(stateDir, 'visibility.json'),
  };
}

function readJson(file) {
  try {
    return JSON.parse(readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

function writeJson(file, value) {
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

// ------------------------------------------------------------------- lock

function acquireLock(paths, { force = false } = {}) {
  const existing = readJson(paths.lock);
  if (existing && leaseIsActive({ expiresAt: existing.expiresAt }) && !force) {
    throw new Error(
      `another ship is running: ${existing.session} (pid ${existing.pid}, expires ${existing.expiresAt}).\n` +
        'Wait for it, or release with: pnpm run ship -- --force-release',
    );
  }
  const now = Date.now();
  const lock = {
    session: SESSION,
    pid: process.pid,
    host: hostname(),
    startedAt: new Date(now).toISOString(),
    expiresAt: new Date(now + LOCK_TTL_MS).toISOString(),
  };
  writeJson(paths.lock, lock);
  return lock;
}

function refreshLock(paths) {
  const lock = readJson(paths.lock);
  if (!lock || lock.session !== SESSION) return;
  lock.expiresAt = new Date(Date.now() + LOCK_TTL_MS).toISOString();
  writeJson(paths.lock, lock);
}

function releaseLock(paths) {
  const lock = readJson(paths.lock);
  if (lock && lock.session === SESSION) rmSync(paths.lock, { force: true });
}

// ------------------------------------------------------------------ lease

function readLease(paths) {
  const lease = readJson(paths.lease);
  return lease && leaseIsActive(lease) ? lease : null;
}

function acquireLease(paths) {
  const held = readLease(paths);
  if (held && held.session !== SESSION) return { ok: false, holder: held };
  const now = Date.now();
  writeJson(paths.lease, {
    session: SESSION,
    pid: process.pid,
    host: hostname(),
    acquiredAt: held?.acquiredAt ?? new Date(now).toISOString(),
    heartbeatAt: new Date(now).toISOString(),
    expiresAt: new Date(now + LEASE_TTL_MS).toISOString(),
    purpose: 'ship',
  });
  return { ok: true, holder: null };
}

function refreshLease(paths) {
  const lease = readJson(paths.lease);
  if (!lease || lease.session !== SESSION) return;
  const now = Date.now();
  lease.heartbeatAt = new Date(now).toISOString();
  lease.expiresAt = new Date(now + LEASE_TTL_MS).toISOString();
  writeJson(paths.lease, lease);
}

function releaseLease(paths) {
  const lease = readJson(paths.lease);
  if (lease && lease.session === SESSION) rmSync(paths.lease, { force: true });
}

function repoVisibility() {
  const raw = gh(['repo', 'view', '--json', 'visibility', '--jq', '.visibility']);
  return raw ? raw.toUpperCase() : null;
}

function setVisibility(visibility) {
  const result = spawnSync(
    'gh',
    ['repo', 'edit', '--visibility', visibility, '--accept-visibility-change-consequences'],
    { cwd: root, stdio: 'inherit' },
  );
  return result.status === 0;
}

function activeRuns() {
  const queued = ghJson(['run', 'list', '--status', 'queued', '--limit', '20', '--json', 'databaseId']) ?? [];
  const running = ghJson(['run', 'list', '--status', 'in_progress', '--limit', '20', '--json', 'databaseId']) ?? [];
  return [...queued, ...running];
}

// ------------------------------------------------------------------- ship

function runGate() {
  const script = join(root, 'scripts', 'ship', 'pre-push-gate.mjs');
  const result = spawnSync(process.execPath, [script], { cwd: root, stdio: 'inherit' });
  return result.status === 0;
}

function resolveLedgerConflicts() {
  const unmerged = git(['diff', '--name-only', '--diff-filter=U'], { allowFail: true });
  if (!unmerged) return true;
  const files = unmerged.split(/\r?\n/).filter(Boolean);
  if (!files.length) return true;

  const ledger = new Set(['SESSION_STATE.md', 'TASKS.json']);
  for (const file of files) {
    if (!ledger.has(file)) {
      console.error(`ship: unresolved conflict in ${file} - resolve by hand (never -X ours/theirs blindly)`);
      return false;
    }
    const ours = git(['show', `:2:${file}`], { allowFail: true });
    const theirs = git(['show', `:3:${file}`], { allowFail: true });
    const winner = pickNewerLedger(ours, theirs);
    if (!winner) {
      console.error(`ship: cannot order both sides of ${file} by "Updated:" - resolve by hand`);
      return false;
    }
    writeFileSync(join(root, file), winner.source);
    git(['add', '--', file]);
    console.log(`ship: auto-resolved ${file} (kept the newer Updated: block)`);
  }
  return true;
}

function rebaseOntoMain() {
  console.log('SHIP_FETCH origin main');
  git(['fetch', 'origin', BASE_BRANCH]);
  const before = git(['rev-parse', 'HEAD']);
  const upstream = git(['rev-parse', `origin/${BASE_BRANCH}`]);
  const alreadyContains = (() => {
    const result = spawnSync('git', ['merge-base', '--is-ancestor', upstream, before], { cwd: root, stdio: 'ignore' });
    return result.status === 0;
  })();
  if (alreadyContains) {
    console.log(`SHIP_REBASE up-to-date (${before.slice(0, 9)})`);
    return { ok: true, head: before };
  }

  console.log(`SHIP_REBASE onto origin/${BASE_BRANCH}`);
  let result = spawnSync('git', ['rebase', '--autostash', `origin/${BASE_BRANCH}`], { cwd: root, stdio: 'inherit' });
  if (result.status !== 0) {
    if (!resolveLedgerConflicts()) {
      spawnSync('git', ['rebase', '--abort'], { cwd: root, stdio: 'inherit' });
      return { ok: false };
    }
    result = spawnSync('git', ['rebase', '--continue'], { cwd: root, stdio: 'inherit' });
    if (result.status !== 0) {
      spawnSync('git', ['rebase', '--abort'], { cwd: root, stdio: 'inherit' });
      console.error('ship: rebase could not be completed automatically - rebase by hand');
      return { ok: false };
    }
  }
  return { ok: true, head: git(['rev-parse', 'HEAD']) };
}

function pushWithRetry() {
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    const result = spawnSync('git', ['push', 'origin', `HEAD:${BASE_BRANCH}`], { cwd: root, stdio: 'inherit' });
    if (result.status === 0) return { ok: true, sha: git(['rev-parse', 'HEAD']) };
    console.warn(`ship: push rejected (attempt ${attempt}/3) - another agent pushed; fetching + rebasing`);
    if (attempt === 3) break;
    const rebase = rebaseOntoMain();
    if (!rebase.ok) return { ok: false };
    if (!runGate()) {
      console.error('ship: ship:gate failed after rebase');
      return { ok: false };
    }
  }
  return { ok: false };
}

function startHeartbeat(paths) {
  const timer = setInterval(() => {
    refreshLock(paths);
    refreshLease(paths);
  }, HEARTBEAT_MS);
  timer.unref();
  return () => clearInterval(timer);
}

function runWatcher(sha, { workflow } = {}) {
  const script = join(root, 'scripts', 'ship', 'watch-deploy.ps1');
  const candidates = process.platform === 'win32' ? ['powershell'] : ['pwsh', 'powershell'];
  const args = ['-ExecutionPolicy', 'Bypass', '-File', script, '-Sha', sha, '-SkipPublic', '-SkipPrivateFlip'];
  if (workflow) args.push('-Workflow', workflow);
  for (const bin of candidates) {
    const result = spawnSync(bin, args, { cwd: root, stdio: 'inherit' });
    if (result.error && result.error.code === 'ENOENT') continue;
    return result.status ?? 1;
  }
  console.error('ship: no PowerShell available - run scripts/ship/watch-deploy.ps1 manually');
  return 1;
}

function recordEvidence() {
  const result = spawnSync(process.execPath, [join(root, 'scripts', 'agent', 'state.mjs'), 'record'], {
    cwd: root,
    stdio: 'inherit',
  });
  return result.status === 0;
}

async function sleep(ms) {
  await new Promise((resolvePromise) => setTimeout(resolvePromise, ms));
}

/** Flip private only when nobody else can be affected (see module header). */
async function maybeFlipPrivate(paths) {
  const otherLease = readLease(paths);
  if (otherLease && otherLease.session !== SESSION) {
    console.log(`SHIP_VISIBILITY_KEPT_PUBLIC other lease: ${otherLease.session} (expires ${otherLease.expiresAt})`);
    return false;
  }
  const runs = activeRuns();
  if (runs.length) {
    console.log(`SHIP_VISIBILITY_KEPT_PUBLIC ${runs.length} Actions run(s) queued/in progress`);
    return false;
  }
  // Re-check after a short settle: another session may have acquired the lease
  // between the two calls (its ship flips public again before pushing, but a
  // private window while its runs are queued would still fail them).
  await sleep(5000);
  const late = readLease(paths);
  if (late && late.session !== SESSION) {
    console.log(`SHIP_VISIBILITY_KEPT_PUBLIC lease appeared: ${late.session}`);
    return false;
  }
  if (activeRuns().length) {
    console.log('SHIP_VISIBILITY_KEPT_PUBLIC a run appeared during the settle window');
    return false;
  }
  console.log('SHIP_VISIBILITY_PRIVATE');
  return setVisibility('private');
}

// ------------------------------------------------------------------- cli

export function parseArgs(argv) {
  const flags = { _: [] };
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (!token.startsWith('--')) {
      flags._.push(token);
      continue;
    }
    const name = token.slice(2);
    if (['dry-run', 'no-push', 'no-watch', 'no-visibility', 'no-record', 'status', 'release-lease', 'force-release', 'may-flip-private', 'self-test', 'verify'].includes(name)) {
      flags[name] = true;
      continue;
    }
    const value = argv[i + 1];
    if (value === undefined) throw new Error(`missing value for --${name}`);
    flags[name] = value;
    i += 1;
  }
  return flags;
}

function printStatus(paths) {
  const lock = readJson(paths.lock);
  const lease = readLease(paths);
  console.log(`session    ${SESSION}`);
  console.log(`lock       ${lock ? `${lock.session} pid=${lock.pid} expires=${lock.expiresAt}` : 'free'}`);
  console.log(`lease      ${lease ? `${lease.session} expires=${lease.expiresAt}` : 'free'}`);
  console.log(`visibility ${repoVisibility() ?? 'unknown'}`);
  const runs = activeRuns();
  console.log(`runs       ${runs.length ? runs.map((run) => run.databaseId).join(', ') : 'none queued/in progress'}`);
  return 0;
}

export async function main(argv) {
  const flags = parseArgs(argv);

  if (flags['self-test']) {
    const result = selfTest();
    if (!result.ok) {
      console.error('ship self-test FAILED');
      for (const failure of result.failures) console.error(`  ${failure}`);
      return 1;
    }
    console.log('ship self-test OK');
    return 0;
  }

  const paths = statePaths();

  if (flags.status) return printStatus(paths);
  if (flags['release-lease']) {
    releaseLease(paths);
    console.log('SHIP_LEASE_RELEASED');
    return 0;
  }
  if (flags['may-flip-private']) {
    const lease = readLease(paths);
    if (lease && lease.session !== SESSION) {
      console.log(`blocked: lease held by ${lease.session}`);
      return 1;
    }
    const runs = activeRuns();
    if (runs.length) {
      console.log(`blocked: ${runs.length} run(s) queued/in progress`);
      return 1;
    }
    return 0;
  }

  const branch = git(['rev-parse', '--abbrev-ref', 'HEAD'], { allowFail: true });
  if (!branch || branch === 'HEAD') {
    console.error('ship: refusing to ship from a detached HEAD');
    return 1;
  }

  const lock = acquireLock(paths, { force: Boolean(flags['force-release']) });
  const stopHeartbeat = startHeartbeat(paths);
  console.log(`SHIP_START session=${SESSION} branch=${branch} head=${git(['rev-parse', '--short', 'HEAD'])}`);
  if (lock.pid !== process.pid) console.log(`SHIP_LOCK_TAKEN_OVER from pid=${lock.pid}`);

  try {
    // Watch-only mode: somebody else already pushed; just follow the SHA.
    if (flags.sha) {
      const sha = git(['rev-parse', flags.sha], { allowFail: true });
      if (!sha) {
        console.error(`ship: cannot resolve ${flags.sha}`);
        return 1;
      }
      console.log(`SHIP_WATCH_ONLY ${sha}`);
      return flags['no-watch'] ? 0 : runWatcher(sha, { workflow: flags.workflow });
    }

    if (flags['dry-run']) {
      const needsRebase = !(() => {
        const upstream = git(['rev-parse', `origin/${BASE_BRANCH}`], { allowFail: true });
        if (!upstream) return false;
        const result = spawnSync('git', ['merge-base', '--is-ancestor', upstream, 'HEAD'], { cwd: root, stdio: 'ignore' });
        return result.status === 0;
      })();
      console.log(`SHIP_DRY_RUN rebase_needed=${needsRebase} visibility=${repoVisibility() ?? 'unknown'} gate=${runGate() ? 'PASS' : 'FAIL'}`);
      return 0;
    }

    const rebase = rebaseOntoMain();
    if (!rebase.ok) return 1;
    if (!runGate()) {
      console.error('ship: ship:gate failed - fix the findings above, or use --no-push to inspect');
      return 1;
    }

    const lease = acquireLease(paths);
    if (!lease.ok) {
      console.log(`SHIP_LEASE_BUSY held by ${lease.holder.session} (expires ${lease.holder.expiresAt}) - keeping visibility as-is`);
    }
    const visibility = repoVisibility();
    if (!visibility) {
      console.log('SHIP_VISIBILITY unknown - continuing (gh repo view failed)');
    } else if (visibility === 'PRIVATE' && lease.ok) {
      // Public BEFORE the push: a queued run that starts while the repo is
      // private is refused by hosted runners.
      console.log('SHIP_VISIBILITY_PUBLIC');
      if (!setVisibility('public')) {
        console.error('ship: could not make the repo public - hosted Actions runs will be refused');
        return 1;
      }
    }

    if (flags['no-push']) {
      releaseLease(paths);
      console.log('SHIP_NO_PUSH stopping before push (lock + lease released)');
      return 0;
    }

    const pushed = pushWithRetry();
    if (!pushed.ok) return 1;
    const remote = git(['ls-remote', 'origin', `refs/heads/${BASE_BRANCH}`], { allowFail: true });
    const remoteSha = remote ? remote.split(/\s+/)[0] : null;
    console.log(`SHIP_PUSHED ${pushed.sha}`);
    if (remoteSha && remoteSha !== pushed.sha) console.log(`SHIP_SUPERSEDED_IN_FLIGHT origin/main now ${remoteSha}`);

    let watchStatus = 0;
    if (!flags['no-watch']) {
      watchStatus = runWatcher(pushed.sha, { workflow: flags.workflow });
    }

    if (watchStatus === 0 && !flags['no-watch']) {
      const recorded = recordEvidence();
      if (flags.verify) {
        spawnSync(process.execPath, [join(root, 'scripts', 'agent', 'state.mjs'), 'verify'], { cwd: root, stdio: 'inherit' });
      }
      if (!recorded) console.warn('ship: ax:record reported a problem (evidence not written)');
    }

    // Release the lease, then flip private only if nothing else is in flight.
    // The check runs even when the repo was ALREADY public at the start: with
    // several sessions sharing a public window the last ship out must close it,
    // and maybeFlipPrivate() refuses while another lease or any run is live.
    releaseLease(paths);
    if (lease.ok && !flags['no-visibility']) {
      await maybeFlipPrivate(paths);
    }
    releaseLock(paths);
    releaseLease(paths);
    return watchStatus;
  } finally {
    stopHeartbeat();
    releaseLock(paths);
    // The lease must never outlive this process on a failure path; releasing it
    // early is safe because maybeFlipPrivate() runs before this on success.
    releaseLease(paths);
  }
}

// --------------------------------------------------------------- self-test

export function selfTest() {
  const failures = [];
  const expect = (name, condition, detail = '') => {
    if (!condition) failures.push(`${name}${detail ? `: ${detail}` : ''}`);
  };

  expect('https slug', parseRepoSlug('https://github.com/jerryboganda/oetwebapp.git') === 'jerryboganda/oetwebapp');
  expect('ssh slug', parseRepoSlug('git@github.com:jerryboganda/oetwebapp.git') === 'jerryboganda/oetwebapp');
  expect('no slug', parseRepoSlug('not-a-remote') === null);

  const oldState = '# SESSION STATE\n\nUpdated: 2026-10-01T00:00:00Z\n';
  const newState = '# SESSION STATE\n\nUpdated: 2026-10-03T00:00:00Z\n';
  expect('newer ledger wins', pickNewerLedger(oldState, newState)?.side === 'b');
  expect('newer ledger wins reversed', pickNewerLedger(newState, oldState)?.side === 'a');
  expect('unparseable ledger refused', pickNewerLedger('no header', 'also none') === null);
  expect('equal timestamps keep ours', pickNewerLedger(newState, newState)?.side === 'a');

  const now = Date.now();
  expect('active lease', leaseIsActive({ expiresAt: new Date(now + 60000).toISOString() }, now));
  expect('expired lease', !leaseIsActive({ expiresAt: new Date(now - 60000).toISOString() }, now));
  expect('empty lease', !leaseIsActive(null, now));

  const parsed = parseArgs(['--sha', 'abc', '--no-watch', '--dry-run']);
  expect('flags parse', parsed.sha === 'abc' && parsed['no-watch'] === true && parsed['dry-run'] === true);

  return { ok: failures.length === 0, failures };
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) {
  main(process.argv.slice(2))
    .then((code) => {
      process.exitCode = code ?? 0;
    })
    .catch((error) => {
      console.error(error instanceof Error ? error.stack : error);
      process.exitCode = 1;
    });
}
