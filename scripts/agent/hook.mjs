#!/usr/bin/env node
/**
 * Claude Code hook adapter for the agent ledger (SESSION_STATE.md, TASKS.json,
 * VERIFICATION.md). Wired through hook-shim.mjs as SessionStart and Stop hooks.
 *
 *   session-start  print the ledger status of THIS branch's run (plain stdout)
 *   stop           block, once per issue, a completion claim the ledger contradicts
 *
 * Contract
 * - Fail open. Every code path ends with exit code 0 (exit code 2 would BLOCK a
 *   Stop hook) and stdout is either empty or exactly what the event expects.
 * - Scoped. The ledger files are tracked and shared, so a ledger only counts as
 *   this session's when it differs from the branch's fork point on origin/main
 *   (committed on this branch, or uncommitted). Anyone else's ledger stays silent.
 * - Bounded. A Stop block is written to a marker first, repeats of the same issue
 *   never block again, and a session is blocked at most twice.
 * - Compute policy (AGENTS.md): reads files and runs read-only git. It never
 *   builds, tests, installs, or calls the network.
 *
 * Usage:
 *   node scripts/agent/hook.mjs session-start [--index]   (hook JSON on stdin)
 *   node scripts/agent/hook.mjs stop                      (hook JSON on stdin)
 *   node scripts/agent/hook.mjs --self-test               (CI only)
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { checkState, isPassResult, parseGateRows, parseHeader, readyTasks, sectionBody } from './state.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const LEDGER = ['SESSION_STATE.md', 'TASKS.json', 'VERIFICATION.md'];
const MAX_CONTEXT = 6000;
const MAX_LINE = 400;
const MAX_BLOCKS_PER_SESSION = 2;
const NOT_APPLICABLE = new Set(['n/a', 'na']);
// Strong, verification-flavoured completion claims only. "Implemented" or "done"
// alone must never trigger a block.
const CLAIM = /\b(production[- ]ready|fully (?:verified|tested)|all (?:tests|gates|checks) (?:pass|passed|are green)|everything (?:passes|is green)|verified end[- ]to[- ]end|no remaining (?:issues|risks))\b/i;
const BOM = String.fromCharCode(0xfeff);

function stripBom(text) {
  return text.startsWith(BOM) ? text.slice(1) : text;
}

// ------------------------------------------------------------------- git

function git(dir, args) {
  try {
    return execFileSync(
      'git',
      ['-c', 'core.fsmonitor=false', '-c', 'gc.auto=0', '-c', 'maintenance.auto=false', ...args],
      {
        cwd: dir,
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
        timeout: 5000,
        windowsHide: true,
        env: { ...process.env, GIT_OPTIONAL_LOCKS: '0' },
      },
    ).trim();
  } catch {
    return '';
  }
}

function gitOk(dir, args) {
  try {
    execFileSync('git', args, { cwd: dir, stdio: 'ignore', timeout: 5000, windowsHide: true });
    return true;
  } catch {
    return false;
  }
}

function toplevel(dir) {
  return git(dir, ['rev-parse', '--show-toplevel']);
}

function read(dir, name) {
  try {
    return stripBom(readFileSync(join(dir, name), 'utf8'));
  } catch {
    return null;
  }
}

/**
 * True when this checkout's ledger belongs to the current branch's run:
 * uncommitted ledger edits, or ledger changes committed since the fork point
 * from origin/main. An untouched ledger belongs to whoever last merged it.
 */
export function ledgerIsMine(dir) {
  if (!existsSync(join(dir, 'SESSION_STATE.md'))) return false;
  if (git(dir, ['status', '--porcelain', '--', ...LEDGER])) return true;
  if (!git(dir, ['rev-parse', '--verify', '-q', 'origin/main'])) return false;
  return Boolean(git(dir, ['diff', '--name-only', 'origin/main...HEAD', '--', ...LEDGER]));
}

// ---------------------------------------------------------------- ledger

function ledgerCheck(dir) {
  const stateSource = read(dir, 'SESSION_STATE.md');
  const tasksSource = read(dir, 'TASKS.json');
  const head = git(dir, ['rev-parse', '--short', 'HEAD']);
  const declared = parseHeader(stateSource ?? '').HEAD ?? '';
  const descends = declared && head ? gitOk(dir, ['merge-base', '--is-ancestor', declared, head]) : false;
  return checkState({
    stateSource,
    tasksSource,
    git: head ? { head, descendsFrom: descends } : null,
  });
}

function describe(dir) {
  const stateSource = read(dir, 'SESSION_STATE.md') ?? '';
  const header = parseHeader(stateSource);
  const gates = parseGateRows(stateSource);
  const open = gates.filter(
    (gate) => !isPassResult(gate.result) && !NOT_APPLICABLE.has(String(gate.result).trim().toLowerCase()),
  );
  let tasks = [];
  try {
    const parsed = JSON.parse(read(dir, 'TASKS.json') ?? '{}');
    tasks = Array.isArray(parsed.tasks) ? parsed.tasks : [];
  } catch {
    tasks = [];
  }
  const next =
    sectionBody(stateSource, 'Next action')
      .split(/\r?\n/)
      .map((line) => line.trim())
      .find(Boolean) ?? '';
  return {
    session: header.Session ?? '?',
    mode: header.Mode ?? '?',
    goal: header.Goal ?? '?',
    updated: header.Updated ?? '?',
    branch: git(dir, ['rev-parse', '--abbrev-ref', 'HEAD']) || header.Branch || '?',
    head: git(dir, ['rev-parse', '--short', 'HEAD']) || header.HEAD || '?',
    pass: gates.filter((gate) => isPassResult(gate.result)).length,
    open,
    total: tasks.length,
    done: tasks.filter((task) => task.status === 'done').length,
    ready: readyTasks({ tasks }).map((task) => task.id),
    next,
  };
}

function clip(text) {
  const lines = String(text)
    .split('\n')
    .map((line) => (line.length > MAX_LINE ? line.slice(0, MAX_LINE - 3) + '...' : line));
  const out = lines.join('\n');
  return out.length > MAX_CONTEXT ? out.slice(0, MAX_CONTEXT - 3) + '...' : out;
}

// ----------------------------------------------------------- session-start

export function startContext(dir) {
  if (!ledgerIsMine(dir)) return '';
  const info = describe(dir);
  const result = ledgerCheck(dir);
  const lines = [];
  lines.push('AX run state: this branch owns an agent ledger (SESSION_STATE.md, TASKS.json, VERIFICATION.md).');
  lines.push(
    'session ' + info.session + ' | mode ' + info.mode + ' | ' + info.branch + '@' + info.head + ' | updated ' + info.updated,
  );
  lines.push('goal: ' + info.goal);
  const gateText = info.open.length ? '; open: ' + info.open.map((gate) => gate.gate + '=' + gate.result).join(', ') : '';
  lines.push('gates: ' + info.pass + ' PASS' + gateText);
  const readyText = info.ready.length ? '; ready: ' + info.ready.join(', ') : '';
  lines.push('tasks: ' + info.done + '/' + info.total + ' done' + readyText);
  if (info.next) lines.push('next action: ' + info.next);
  if (result.errors.length) {
    lines.push('ledger check: ' + result.errors.length + ' error(s):');
    for (const error of result.errors.slice(0, 5)) lines.push('  - ' + error);
  } else {
    const warned = result.warnings.length ? ' (' + result.warnings.length + ' warning(s))' : '';
    lines.push('ledger check: OK' + warned);
  }
  lines.push(
    'Rules: a gate is PASS only with an Actions run id or local:ship:gate (builds and tests need a run id). Run "pnpm run ax:check" before claiming a task done. Continue this run only if it matches the newest request; protocol: AGENTS.md "Continuity Protocol".',
  );
  return clip(lines.join('\n'));
}

function childRepos(dir) {
  try {
    return readdirSync(dir, { withFileTypes: true })
      .filter((entry) => {
        if (!entry.isDirectory()) return false;
        try {
          return statSync(join(dir, entry.name, '.git')).isDirectory();
        } catch {
          return false;
        }
      })
      .map((entry) => join(dir, entry.name));
  } catch {
    return [];
  }
}

/** One line per worktree whose ledger belongs to an unfinished run. */
export function indexContext(repoDirs) {
  const seen = new Set();
  const rows = [];
  for (const repo of repoDirs) {
    for (const line of git(repo, ['worktree', 'list', '--porcelain']).split(/\r?\n/)) {
      if (!line.startsWith('worktree ')) continue;
      const path = line.slice('worktree '.length).trim();
      const key = resolve(path).toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      if (!existsSync(path) || !ledgerIsMine(path)) continue;
      const info = describe(path);
      if (info.mode === 'done') continue;
      rows.push(
        '- ' + path + ' [' + info.branch + '] mode ' + info.mode + ' | ' + info.goal + ' | next: ' + (info.next || '(none)'),
      );
    }
  }
  if (!rows.length) return '';
  const lines = [
    'AX: ' + rows.length + ' unfinished run ledger(s) in this workspace. Open the matching checkout\'s SESSION_STATE.md before acting; protocol: AGENTS.md "Continuity Protocol".',
    ...rows.slice(0, 8),
  ];
  return clip(lines.join('\n'));
}

// -------------------------------------------------------------------- stop

export function stopDecision(dir, input) {
  if (!ledgerIsMine(dir)) return null;
  const issues = ledgerCheck(dir).errors.map((error) => 'ledger: ' + error);
  const info = describe(dir);
  if (CLAIM.test(String(input.last_assistant_message ?? '')) && info.open.length) {
    issues.push(
      'you claimed completion, but these gates are not PASS: ' +
        info.open.map((gate) => gate.gate + ' (' + gate.result + ')').join(', '),
    );
  }
  if (!issues.length) return null;

  const gitDir = git(dir, ['rev-parse', '--absolute-git-dir']);
  if (!gitDir) return null;
  const sessionId = String(input.session_id ?? 'nosession').replace(/[^A-Za-z0-9_-]/g, '_').slice(0, 64);
  const key = createHash('sha1').update([...issues].sort().join('\n')).digest('hex').slice(0, 16);
  const file = join(gitDir, 'ax', 'stop-' + sessionId + '.json');
  let seen = [];
  try {
    const parsed = JSON.parse(readFileSync(file, 'utf8'));
    seen = Array.isArray(parsed.keys) ? parsed.keys : [];
  } catch {
    seen = [];
  }
  if (input.stop_hook_active === true || seen.includes(key) || seen.length >= MAX_BLOCKS_PER_SESSION) {
    return { systemMessage: 'AX: unresolved ledger issues (not blocking again): ' + issues.slice(0, 3).join(' | ') };
  }
  try {
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, JSON.stringify({ keys: [...seen, key] }));
  } catch {
    return null;
  }
  const reason = [
    'AX ledger check failed in ' + dir + ':',
    ...issues.slice(0, 6).map((issue) => '- ' + issue),
    'Fix the ledger ("pnpm run ax:check" lists every problem) or state plainly which gate is NOT RUN. Do not claim a task complete while a gate is open. This check blocks at most ' +
      MAX_BLOCKS_PER_SESSION +
      ' times per session.',
  ].join('\n');
  return { decision: 'block', reason: clip(reason) };
}

// --------------------------------------------------------------- dispatcher

/** Returns exactly what to write to stdout. Never throws. */
export function handle(event, raw, flags = []) {
  try {
    let input = {};
    try {
      input = raw ? JSON.parse(stripBom(String(raw))) : {};
    } catch {
      input = {};
    }
    if (!input || typeof input !== 'object') input = {};
    const cwd = typeof input.cwd === 'string' && input.cwd ? input.cwd : process.cwd();
    const top = toplevel(cwd);
    if (event === 'session-start') {
      if (top) return startContext(top);
      return flags.includes('--index') ? indexContext(childRepos(cwd)) : '';
    }
    if (event === 'stop') {
      const decision = top ? stopDecision(top, input) : null;
      return decision ? JSON.stringify(decision) : '';
    }
    return '';
  } catch {
    return '';
  }
}

function readStdin(ms) {
  return new Promise((resolveText) => {
    if (process.stdin.isTTY) {
      resolveText('');
      return;
    }
    const chunks = [];
    let finished = false;
    const timer = setTimeout(() => finish(), ms);
    function finish() {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      process.stdin.removeAllListeners('data');
      process.stdin.removeAllListeners('end');
      process.stdin.removeAllListeners('error');
      process.stdin.pause();
      resolveText(Buffer.concat(chunks).toString('utf8'));
    }
    process.stdin.on('data', (chunk) => chunks.push(Buffer.from(chunk)));
    process.stdin.on('end', finish);
    process.stdin.on('error', finish);
  });
}

// ---------------------------------------------------------------- self-test

function sh(cwd, args) {
  return execFileSync('git', args, {
    cwd,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    env: {
      ...process.env,
      GIT_AUTHOR_NAME: 'ax',
      GIT_AUTHOR_EMAIL: 'ax@example.invalid',
      GIT_COMMITTER_NAME: 'ax',
      GIT_COMMITTER_EMAIL: 'ax@example.invalid',
    },
  }).trim();
}

export function selfTest() {
  const failures = [];
  const expect = (name, condition, detail = '') => {
    if (!condition) failures.push(name + (detail ? ': ' + detail : ''));
  };
  const base = mkdtempSync(join(tmpdir(), 'ax hook '));
  try {
    const repo = join(base, 'repo');
    mkdirSync(repo, { recursive: true });
    sh(repo, ['init', '-q', '-b', 'main']);
    sh(repo, ['config', 'core.autocrlf', 'false']);
    writeFileSync(join(repo, 'README.md'), 'fixture\n');
    sh(repo, ['add', 'README.md']);
    sh(repo, ['commit', '-q', '-m', 'init']);
    const head0 = sh(repo, ['rev-parse', '--short', 'HEAD']);

    const template = readFileSync(join(here, 'session-state.template.md'), 'utf8');
    const stamp = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
    const ledgerText = (goal) =>
      template
        .replace(/^Session:.*$/m, 'Session: hook-fixture')
        .replace(/^Goal:.*$/m, 'Goal: ' + goal)
        .replace(/^Updated:.*$/m, 'Updated: ' + stamp)
        .replace(/^Branch:.*$/m, 'Branch: main')
        .replace(/^HEAD:.*$/m, 'HEAD: ' + head0);
    const tasksText = JSON.stringify({ version: 1, tasks: [{ id: 'T-1', title: 'fixture', status: 'in_progress' }] });
    writeFileSync(join(repo, 'SESSION_STATE.md'), ledgerText('main ledger goal'));
    writeFileSync(join(repo, 'TASKS.json'), tasksText);
    writeFileSync(join(repo, 'VERIFICATION.md'), '# Verification Ledger\n');
    sh(repo, ['add', ...LEDGER]);
    sh(repo, ['commit', '-q', '-m', 'ledger']);
    sh(repo, ['update-ref', 'refs/remotes/origin/main', 'HEAD']);

    const claim = { last_assistant_message: 'Everything is production-ready and fully verified.' };

    expect('an untouched ledger is not mine', !ledgerIsMine(repo));
    expect('session-start is silent for someone else\'s ledger', startContext(repo) === '');
    expect('stop is silent for someone else\'s ledger', stopDecision(repo, { session_id: 's0', ...claim }) === null);

    // Make the ledger this branch's own (uncommitted edit).
    const mineText = ledgerText('hook fixture goal');
    writeFileSync(join(repo, 'SESSION_STATE.md'), mineText);
    expect('an edited ledger is mine', ledgerIsMine(repo));
    const context = startContext(repo);
    expect('session-start shows the goal', context.includes('hook fixture goal'), context);
    expect('session-start shows open gates', context.includes('deploy=NOT RUN'), context);
    expect('session-start shows the next action', context.includes('next action:'), context);
    expect('session-start output is bounded', context.length <= MAX_CONTEXT);
    expect('a valid ledger without a claim does not block', stopDecision(repo, { session_id: 's1' }) === null);

    const first = stopDecision(repo, { session_id: 's2', ...claim });
    expect('claim with an open gate blocks', first?.decision === 'block' && first.reason.includes('deploy'), JSON.stringify(first));
    const second = stopDecision(repo, { session_id: 's2', ...claim });
    expect('the same issue never blocks twice', second?.decision !== 'block' && Boolean(second?.systemMessage), JSON.stringify(second));
    const other = stopDecision(repo, { session_id: 's3', ...claim });
    expect('a new session is checked again', other?.decision === 'block', JSON.stringify(other));
    const active = stopDecision(repo, { session_id: 's4', stop_hook_active: true, ...claim });
    expect('stop_hook_active never blocks', active?.decision !== 'block', JSON.stringify(active));
    expect('a mild message with an open gate does not block', stopDecision(repo, { session_id: 's5', last_assistant_message: 'Implemented the change.' }) === null);

    // A claim is fine once no gate is open.
    // Tick EVERY open gate. Match generically: the template's row names track
    // the deploy pipeline (deploy.yml -> production-deploy.yml, ...), and this
    // test is about the hook, not about which workflow name the row carries.
    writeFileSync(
      join(repo, 'SESSION_STATE.md'),
      mineText.replace(
        /\| ([^|]+) \| ([^|]+) \| NOT RUN \| NOT RUN \|/g,
        '| $1 | $2 | run 36824151971 | PASS |',
      ),
    );
    expect('a claim with every gate PASS does not block', stopDecision(repo, { session_id: 's6', ...claim }) === null);

    // Structural errors block even without a claim, at most twice per session.
    const broken1 = mineText.replace(/## Blockers[\s\S]*?(?=## Next action)/, '');
    writeFileSync(join(repo, 'SESSION_STATE.md'), broken1);
    const b1 = stopDecision(repo, { session_id: 's7' });
    expect('a broken ledger blocks', b1?.decision === 'block' && b1.reason.includes('Blockers'), JSON.stringify(b1));
    writeFileSync(join(repo, 'SESSION_STATE.md'), broken1.replace(/## Next action[\s\S]*$/, '## Next action\n'));
    const b2 = stopDecision(repo, { session_id: 's7' });
    expect('a different issue blocks again', b2?.decision === 'block', JSON.stringify(b2));
    writeFileSync(join(repo, 'SESSION_STATE.md'), broken1.replace('Mode: execute', 'Mode: autopilot'));
    const b3 = stopDecision(repo, { session_id: 's7' });
    expect('the third distinct issue hits the cap', b3?.decision !== 'block' && Boolean(b3?.systemMessage), JSON.stringify(b3));

    // Dispatcher robustness.
    expect('garbage stdin is silent', handle('stop', 'not json') === '');
    expect('an unknown event is silent', handle('mystery', '{}') === '');
    expect('a missing cwd is silent', handle('session-start', JSON.stringify({ cwd: join(base, 'nope') })) === '');
    writeFileSync(join(repo, 'SESSION_STATE.md'), mineText);
    const viaHandle = handle('session-start', JSON.stringify({ cwd: repo }));
    expect('handle() injects the ledger', viaHandle.includes('hook fixture goal'));
    const stopped = handle('stop', JSON.stringify({ cwd: repo, session_id: 's8', ...claim }));
    expect('handle() emits one JSON object', JSON.parse(stopped).decision === 'block', stopped);

    // The shim delegates to the checkout's own hook.mjs.
    const agentDir = join(repo, 'scripts', 'agent');
    mkdirSync(agentDir, { recursive: true });
    for (const name of ['hook.mjs', 'state.mjs', 'session-state.template.md']) cpSync(join(here, name), join(agentDir, name));
    const shim = join(here, 'hook-shim.mjs');
    const viaShim = spawnSync(process.execPath, [shim, 'session-start'], {
      input: JSON.stringify({ cwd: repo }),
      encoding: 'utf8',
      timeout: 20000,
    });
    expect('the shim relays session-start', viaShim.status === 0 && viaShim.stdout.includes('hook fixture goal'), String(viaShim.stdout) + String(viaShim.stderr));
    const shimOutside = spawnSync(process.execPath, [shim, 'stop'], {
      input: JSON.stringify({ cwd: join(base, 'nope') }),
      encoding: 'utf8',
      timeout: 20000,
    });
    expect('the shim is silent outside a repo', shimOutside.status === 0 && shimOutside.stdout === '', String(shimOutside.stdout));

    // Workspace mode: the launch folder is not a repo; worktrees are indexed.
    sh(repo, ['checkout', '-q', '--', 'SESSION_STATE.md']);
    const tree = join(base, 'wt tree');
    sh(repo, ['worktree', 'add', '-q', '-b', 'feat/ax-fixture', tree]);
    writeFileSync(join(tree, 'SESSION_STATE.md'), ledgerText('worktree goal'));
    const indexed = spawnSync(process.execPath, [shim, 'session-start'], {
      input: JSON.stringify({ cwd: base }),
      encoding: 'utf8',
      timeout: 20000,
    });
    expect('workspace mode indexes the worktree run', indexed.status === 0 && indexed.stdout.includes('worktree goal') && indexed.stdout.includes('feat/ax-fixture'), String(indexed.stdout) + String(indexed.stderr));
  } catch (error) {
    failures.push('self-test crashed: ' + (error && error.stack ? error.stack : String(error)));
  } finally {
    try {
      rmSync(base, { recursive: true, force: true, maxRetries: 3 });
    } catch {
      // A leftover temp directory is harmless.
    }
  }
  return { ok: failures.length === 0, failures };
}

// --------------------------------------------------------------------- main

async function main() {
  const [event, ...flags] = process.argv.slice(2);
  if (event === '--self-test') {
    const result = selfTest();
    if (!result.ok) {
      console.error('ax hook self-test FAILED');
      for (const line of result.failures) console.error('  ' + line);
      process.exitCode = 1;
      return;
    }
    console.log('ax hook self-test OK');
    return;
  }
  const raw = await readStdin(1500);
  const out = handle(event, raw, flags);
  if (out) process.stdout.write(out.endsWith('\n') ? out : out + '\n');
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) {
  main().catch(() => {});
}
