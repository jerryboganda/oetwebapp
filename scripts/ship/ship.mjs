#!/usr/bin/env node
/**
 * One-command ship, safe for several AI agents shipping in parallel.
 *
 *   pnpm run ship                       full flow (lock -> rebase -> gate -> lease
 *                                       -> public -> push -> watch -> record)
 *   pnpm run ship -- --no-push          lock + rebase + gate, without release
 *   pnpm run ship -- --dry-run          gate + report, no git/visibility mutation
 *   pnpm run ship -- --sha <sha>        watch an existing SHA (skip rebase/push)
 *   pnpm run ship -- --status           show lock / lease / visibility state
 *   pnpm run ship -- --release-lease    release this process's visibility lease
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
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { hostname } from 'node:os';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const LOCK_TTL_MS = 30 * 60 * 1000;
const LEASE_TTL_MS = 60 * 60 * 1000;
const HEARTBEAT_MS = 60 * 1000;
const BASE_BRANCH = 'main';
const HOLDERS_VARIABLE = 'PUBLIC_WINDOW_HOLDERS';
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

function gh(args, { allowFail = true, allowNotFound = false } = {}) {
  const result = spawnSync('gh', args, {
    cwd: root,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 64 * 1024 * 1024,
  });
  if (result.status !== 0) {
    if (allowNotFound && /\bHTTP 404\b/.test(String(result.stderr))) return null;
    if (allowFail) return null;
    throw new Error(`gh ${args.join(' ')} failed: ${String(result.stderr ?? '').trim()}`);
  }
  return String(result.stdout ?? '').trim();
}

function ghJson(args, { allowNotFound = false } = {}) {
  const raw = gh(args, { allowFail: false, allowNotFound });
  if (raw === null && allowNotFound) return undefined;
  try {
    return JSON.parse(raw);
  } catch {
    throw new Error(`gh ${args.join(' ')} returned invalid JSON; refusing an unverified visibility decision`);
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

export function readJson(file) {
  try {
    const value = JSON.parse(readFileSync(file, 'utf8'));
    if (!value || typeof value !== 'object' || Array.isArray(value)
      || !['session', 'host'].every((key) => typeof value[key] === 'string' && value[key].length > 0)
      || !Number.isInteger(value.pid) || value.pid <= 0
      || typeof value.expiresAt !== 'string'
      || !Number.isFinite(Date.parse(value.expiresAt))
      || (value.remoteHolder !== undefined && (typeof value.remoteHolder !== 'string' || !value.remoteHolder))) {
      throw new Error('Invalid ship-state fields');
    }
    return value;
  } catch (error) {
    if (error?.code === 'ENOENT') return null;
    throw new Error(`Cannot verify ship state ${file}; refusing to treat unreadable state as absent.`);
  }
}

export function writeJson(file, value, { exclusive = false } = {}) {
  mkdirSync(dirname(file), { recursive: true });
  const contents = `${JSON.stringify(value, null, 2)}\n`;
  if (exclusive) {
    writeFileSync(file, contents, { flag: 'wx' });
    return;
  }
  const temporary = `${file}.${process.pid}.tmp`;
  writeFileSync(temporary, contents, { flag: 'wx' });
  try {
    renameSync(temporary, file);
  } finally {
    if (existsSync(temporary)) rmSync(temporary, { force: true });
  }
}

// ------------------------------------------------------------------- lock

/** Is a process with this pid alive on THIS host? EPERM means "alive, not ours". */
function processIsAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error?.code === 'EPERM';
  }
}

export function lockIsActive(lock, { host = hostname(), alive = processIsAlive, now = Date.now() } = {}) {
  if (!lock || typeof lock !== 'object') return false;
  return lock.host === host ? alive(Number(lock.pid)) : leaseIsActive(lock, now);
}

export function acquireLock(paths) {
  const existing = readJson(paths.lock);
  // Synchronous git/watch commands can delay the heartbeat; a live local owner
  // retains its lock even after the TTL. Never unlink an earlier snapshot:
  // another acquirer could have replaced it with a live owner's lock.
  if (existing) {
    if (lockIsActive(existing)) {
      throw new Error(
        `another ship is running: ${existing.session} (pid ${existing.pid}, expires ${existing.expiresAt}).\n` +
          'Wait for it; a running ship cannot be force-released.',
      );
    }
    throw new Error(`Inactive ship lock at ${paths.lock}; verify its owner and recover this exact file before retrying.`);
  }
  const now = Date.now();
  const lock = {
    session: SESSION,
    pid: process.pid,
    host: hostname(),
    startedAt: new Date(now).toISOString(),
    expiresAt: new Date(now + LOCK_TTL_MS).toISOString(),
  };
  try {
    writeJson(paths.lock, lock, { exclusive: true });
  } catch (error) {
    if (error?.code === 'EEXIST') throw new Error('Another ship acquired the lock; wait and retry.');
    throw error;
  }
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
  return lockIsActive(lease) ? lease : null;
}

function acquireLease(paths) {
  const held = readLease(paths);
  if (held && held.session !== SESSION) return { ok: false, holder: held };
  const previous = readJson(paths.lease);
  if (previous && !held && previous.remoteHolder) {
    if (previous.host !== hostname()) throw new Error('An expired foreign-host lease needs verified owner recovery.');
    releaseRemoteHolder(previous.remoteHolder);
  }
  const now = Date.now();
  writeJson(paths.lease, {
    session: SESSION,
    pid: process.pid,
    host: hostname(),
    acquiredAt: held?.acquiredAt ?? new Date(now).toISOString(),
    heartbeatAt: new Date(now).toISOString(),
    expiresAt: new Date(now + LEASE_TTL_MS).toISOString(),
    purpose: 'ship',
    remoteHolder: SESSION,
  });
  const holders = readRemoteHolders();
  if (!holders.includes(SESSION)) writeRemoteHolders([...holders, SESSION]);
  console.log('SHIP_REMOTE_HOLDER_REGISTERED');
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
  if (lease && lease.session === SESSION) {
    if (lease.remoteHolder) releaseRemoteHolder(lease.remoteHolder);
    rmSync(paths.lease, { force: true });
  }
}

export function requireHolderValue(value) {
  if (typeof value !== 'string') throw new Error('Native visibility holders are unverified.');
  const text = value.trim();
  if (!text) return [];
  let holders;
  if (/^[\[{"']/.test(text) || /^(?:null|true|false)$/.test(text)) {
    try { holders = JSON.parse(text); } catch { throw new Error('Native visibility holders have invalid JSON.'); }
  } else {
    holders = text.split(',');
  }
  if (!Array.isArray(holders) || holders.some((holder) => typeof holder !== 'string' || !holder.trim())) {
    throw new Error('Native visibility holders must be a verified string list.');
  }
  return [...new Set(holders.map((holder) => holder.trim()))];
}

function readRemoteHolders() {
  const variable = ghJson(['api', `repos/{owner}/{repo}/actions/variables/${HOLDERS_VARIABLE}`], { allowNotFound: true });
  if (variable === undefined) {
    const repo = ghJson(['api', 'repos/{owner}/{repo}']);
    if (repo?.permissions?.admin !== true) throw new Error('Native holder absence is unverified without repository administration access.');
    return [];
  }
  return requireHolderValue(variable?.value);
}

function writeRemoteHolders(holders) {
  // ponytail: GitHub variables have no CAS, so this is holder coordination, not
  // a distributed commit mutex. Keep local locking/queue checks; use a CAS-backed
  // lease if multi-host contention grows.
  gh(['variable', 'set', HOLDERS_VARIABLE, '--body', JSON.stringify(holders)], { allowFail: false });
  const verified = readRemoteHolders();
  if (holders.some((holder) => !verified.includes(holder))) throw new Error('Native visibility-holder update is unverified.');
}

function releaseRemoteHolder(holder) {
  const holders = readRemoteHolders();
  if (holders.includes(holder)) writeRemoteHolders(holders.filter((value) => value !== holder));
  if (readRemoteHolders().includes(holder)) throw new Error('Native visibility holder was not released; retain recovery state.');
}

function repoVisibility() {
  const raw = gh(['repo', 'view', '--json', 'visibility', '--jq', '.visibility']);
  return raw ? raw.toUpperCase() : null;
}

export function requireKnownVisibility(value) {
  if (value !== 'PUBLIC' && value !== 'PRIVATE') {
    throw new Error('Repository visibility is unverified; refusing to push or change visibility.');
  }
  return value;
}

export function requireActiveRunList(value) {
  if (!Array.isArray(value) || value.some((run) => !Number.isSafeInteger(run?.databaseId) || run.databaseId <= 0)) {
    throw new Error('GitHub Actions queue is unverified; refusing a private flip.');
  }
  return value;
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
  const queued = requireActiveRunList(ghJson(['run', 'list', '--status', 'queued', '--limit', '20', '--json', 'databaseId']));
  const running = requireActiveRunList(ghJson(['run', 'list', '--status', 'in_progress', '--limit', '20', '--json', 'databaseId']));
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
  const pushStartedAt = new Date().toISOString();
  console.log(`SHIP_PUSH_STARTED_AT ${pushStartedAt}`);
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    const beforePush = git(['ls-remote', 'origin', `refs/heads/${BASE_BRANCH}`], { allowFail: true });
    const pushBaseSha = beforePush ? beforePush.split(/\s+/)[0] : '';
    console.log(`SHIP_PUSH_ATTEMPT_STARTED_AT ${new Date().toISOString()}`);
    const result = spawnSync('git', ['push', 'origin', `HEAD:${BASE_BRANCH}`], { cwd: root, stdio: 'inherit' });
    if (result.status === 0) return { ok: true, sha: git(['rev-parse', 'HEAD']), pushStartedAt, pushBaseSha };
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

function runWatcher(sha, { workflow, pushStartedAt, pushBaseSha } = {}) {
  const script = join(root, 'scripts', 'ship', 'watch-deploy.ps1');
  const candidates = process.platform === 'win32' ? ['powershell'] : ['pwsh', 'powershell'];
  const args = ['-ExecutionPolicy', 'Bypass', '-File', script, '-Sha', sha, '-SkipPublic', '-SkipPrivateFlip'];
  if (workflow) args.push('-Workflow', workflow);
  if (pushStartedAt) args.push('-PushStartedAt', pushStartedAt);
  if (pushBaseSha) args.push('-PushBaseSha', pushBaseSha);
  for (const bin of candidates) {
    const result = spawnSync(bin, args, { cwd: root, stdio: 'inherit' });
    if (result.error && result.error.code === 'ENOENT') continue;
    return result.status ?? 1;
  }
  console.error('ship: PowerShell is required; install/restore it before verified wrapper recovery.');
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
  if (readRemoteHolders().length) {
    console.log('SHIP_VISIBILITY_KEPT_PUBLIC another workstation/console native holder exists');
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
  if (readRemoteHolders().length) {
    console.log('SHIP_VISIBILITY_KEPT_PUBLIC a native holder appeared during the settle window');
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
    // `pnpm run ship -- --sha <sha>` passes the `--` separator through as an
    // argument; ignoring it keeps the flags after it working.
    if (token === '--') continue;
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
    if (!['sha', 'workflow'].includes(name)) throw new Error(`unknown ship option --${name}`);
    if (value === undefined || value.startsWith('--')) throw new Error(`missing value for --${name}`);
    flags[name] = value;
    i += 1;
  }
  return flags;
}

export function validateReleaseOptions(flags) {
  for (const name of ['no-watch', 'no-visibility', 'no-record', 'force-release']) {
    if (flags[name]) throw new Error(`--${name} is forbidden: the accelerated release contract cannot be bypassed.`);
  }
  if (flags._.length) throw new Error(`unexpected ship arguments: ${flags._.join(' ')}`);
  if (flags.workflow && flags.workflow !== 'Deploy production') {
    throw new Error('Only Deploy production can verify a production release.');
  }
  if (flags.sha && (flags['no-push'] || flags['dry-run'])) {
    throw new Error('--sha is a verified recovery watch, not a no-push/dry-run diagnostic.');
  }
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
  validateReleaseOptions(flags);

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
    if (readRemoteHolders().length) {
      console.log('blocked: native workstation/console visibility holders exist');
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

  acquireLock(paths);
  const stopHeartbeat = startHeartbeat(paths);
  console.log(`SHIP_START session=${SESSION} branch=${branch} head=${git(['rev-parse', '--short', 'HEAD'])}`);

  try {
    if (flags['dry-run']) {
      const needsRebase = !(() => {
        const upstream = git(['rev-parse', `origin/${BASE_BRANCH}`], { allowFail: true });
        if (!upstream) return false;
        const result = spawnSync('git', ['merge-base', '--is-ancestor', upstream, 'HEAD'], { cwd: root, stdio: 'ignore' });
        return result.status === 0;
      })();
      const gate = runGate();
      console.log(`SHIP_DRY_RUN rebase_needed=${needsRebase} visibility=${repoVisibility() ?? 'unknown'} gate=${gate ? 'PASS' : 'FAIL'}`);
      return gate ? 0 : 1;
    }

    let recoverySha;
    if (flags.sha) {
      recoverySha = git(['rev-parse', '--verify', `${flags.sha}^{commit}`], { allowFail: true });
      if (!recoverySha || !/^[0-9a-f]{40}$/.test(recoverySha)) {
        console.error(`ship: cannot resolve ${flags.sha}`);
        return 1;
      }
    } else {
      const rebase = rebaseOntoMain();
      if (!rebase.ok) return 1;
    }
    if (!runGate()) {
      console.error('ship: ship:gate failed - fix the findings above before releasing');
      return 1;
    }

    if (flags['no-push']) {
      console.log('SHIP_NO_PUSH stopping before visibility, push or deployment');
      return 0;
    }

    const lease = acquireLease(paths);
    if (!lease.ok) {
      throw new Error(`Ship visibility lease is held by ${lease.holder.session}; wait for its release.`);
    }
    const visibility = requireKnownVisibility(repoVisibility());
    if (visibility === 'PRIVATE') {
      // Public BEFORE the push: a queued run that starts while the repo is
      // private is refused by hosted runners.
      console.log('SHIP_VISIBILITY_PUBLIC');
      if (!setVisibility('public')) {
        console.error('ship: could not make the repo public - hosted Actions runs will be refused');
        return 1;
      }
      if (requireKnownVisibility(repoVisibility()) !== 'PUBLIC') {
        throw new Error('Public-before-push verification failed; refusing the release.');
      }
    }

    const pushed = recoverySha ? { ok: true, sha: recoverySha } : pushWithRetry();
    if (!pushed.ok) return 1;
    if (recoverySha) {
      console.log(`SHIP_WATCH_ONLY ${recoverySha}`);
    } else {
      const remote = git(['ls-remote', 'origin', `refs/heads/${BASE_BRANCH}`], { allowFail: true });
      const remoteSha = remote ? remote.split(/\s+/)[0] : null;
      console.log(`SHIP_PUSHED ${pushed.sha}`);
      if (remoteSha && remoteSha !== pushed.sha) console.log(`SHIP_SUPERSEDED_IN_FLIGHT origin/main now ${remoteSha}`);
    }

    let watchStatus = runWatcher(pushed.sha, { workflow: flags.workflow, pushStartedAt: pushed.pushStartedAt, pushBaseSha: pushed.pushBaseSha });
    if (watchStatus === 0) {
      const recorded = recordEvidence();
      if (!recorded) {
        console.error('ship: ax:record failed; this release is not complete.');
        watchStatus = 1;
      }
      if (flags.verify) {
        const verified = spawnSync(process.execPath, [join(root, 'scripts', 'agent', 'state.mjs'), 'verify'], { cwd: root, stdio: 'inherit' });
        if (verified.status !== 0) watchStatus = 1;
      }
    }

    // Release the lease, then flip private only if nothing else is in flight.
    // The check runs even when the repo was ALREADY public at the start: with
    // several sessions sharing a public window the last ship out must close it,
    // and maybeFlipPrivate() refuses while another lease or any run is live.
    releaseLease(paths);
    await maybeFlipPrivate(paths);
    releaseLock(paths);
    releaseLease(paths);
    return watchStatus;
  } finally {
    stopHeartbeat();
    // The lease must never outlive this process on a failure path; releasing it
    // early is safe because maybeFlipPrivate() runs before this on success.
    try {
      releaseLease(paths);
    } finally {
      releaseLock(paths);
    }
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

  const parsed = parseArgs(['--sha', 'abc', '--verify']);
  expect('flags parse', parsed.sha === 'abc' && parsed.verify === true);

  // `pnpm run ship -- --sha <sha>` forwards the separator; it must not swallow
  // the flag that follows it (that once turned a watch-only run into a ship).
  const withSeparator = parseArgs(['--', '--sha', 'abc', '--verify']);
  expect(
    'a bare -- separator is ignored',
    withSeparator.sha === 'abc' && withSeparator.verify === true && withSeparator._.length === 0,
    JSON.stringify(withSeparator),
  );

  const rejects = (action) => {
    try { action(); return false; } catch { return true; }
  };
  for (const flag of ['no-watch', 'no-visibility', 'no-record', 'force-release']) {
    expect(`${flag} cannot bypass shipping`, rejects(() => validateReleaseOptions(parseArgs([`--${flag}`]))));
  }
  expect('alternate workflow rejected', rejects(() => validateReleaseOptions(parseArgs(['--workflow', 'QA Smoke']))));
  expect('unknown argument rejected', rejects(() => parseArgs(['--unrecognised', 'true'])));
  expect('missing value rejected', rejects(() => parseArgs(['--sha', '--status'])));
  expect('recovery cannot be a dry run', rejects(() => validateReleaseOptions(parseArgs(['--sha', 'abc', '--dry-run']))));
  expect('unknown visibility refused', rejects(() => requireKnownVisibility(null)));
  expect('private/public visibility accepted', requireKnownVisibility('PRIVATE') === 'PRIVATE' && requireKnownVisibility('PUBLIC') === 'PUBLIC');
  expect('unknown queue refused', rejects(() => requireActiveRunList(null)));
  expect('invalid queue refused', rejects(() => requireActiveRunList([{}])));
  expect('known queue accepted', requireActiveRunList([{ databaseId: 123 }]).length === 1);
  expect('known empty queue accepted', requireActiveRunList([]).length === 0);
  const localLock = { host: 'fixture', pid: 123, expiresAt: new Date(now - 60000).toISOString() };
  expect('live local lock survives delayed heartbeat', lockIsActive(localLock, { host: 'fixture', alive: () => true, now }));
  expect('dead local lock reclaimed', !lockIsActive(localLock, { host: 'fixture', alive: () => false, now }));
  expect('expired remote lock reclaimed', !lockIsActive(localLock, { host: 'another-host', now }));

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
