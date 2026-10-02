#!/usr/bin/env node
/**
 * Thin launcher for the agent-ledger hooks. Install ONE copy outside the repo
 * (see scripts/agent/README.md, "Claude Code hooks") and point the user-level
 * SessionStart and Stop hooks at it. On Windows, Claude Code reads project
 * settings from the folder it was launched in only, so a user-level hook is the
 * single wiring that covers the workspace root, the main checkout and every
 * worktree.
 *
 * It holds NO ledger logic. It finds the checkout the session is working in,
 * runs that checkout's own scripts/agent/hook.mjs (versioned and CI-tested with
 * the repo), and relays its stdout. Silent when there is nothing to do: not a
 * repo, no hook.mjs in that checkout, or any error. It always exits 0.
 *
 *   node hook-shim.mjs session-start    (hook JSON on stdin)
 *   node hook-shim.mjs stop             (hook JSON on stdin)
 */
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const HOOK = join('scripts', 'agent', 'hook.mjs');
const BOM = String.fromCharCode(0xfeff);

function stripBom(text) {
  return text.startsWith(BOM) ? text.slice(1) : text;
}

const event = process.argv[2] ?? '';

function git(cwd, args) {
  try {
    const run = spawnSync('git', ['-c', 'core.fsmonitor=false', ...args], {
      cwd,
      encoding: 'utf8',
      timeout: 5000,
      windowsHide: true,
      env: { ...process.env, GIT_OPTIONAL_LOCKS: '0' },
    });
    return run.status === 0 ? String(run.stdout).trim() : '';
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

/** First registered worktree, of any repo directly under `dir`, that carries hook.mjs. */
function findCheckoutWithHook(dir) {
  let names = [];
  try {
    names = readdirSync(dir, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);
  } catch {
    return '';
  }
  for (const name of names) {
    const repo = join(dir, name);
    let isRepo = false;
    try {
      isRepo = statSync(join(repo, '.git')).isDirectory();
    } catch {
      isRepo = false;
    }
    if (!isRepo) continue;
    for (const line of git(repo, ['worktree', 'list', '--porcelain']).split(/\r?\n/)) {
      if (!line.startsWith('worktree ')) continue;
      const path = line.slice('worktree '.length).trim();
      if (existsSync(join(path, HOOK))) return path;
    }
  }
  return '';
}

async function main() {
  if (event !== 'session-start' && event !== 'stop') return;
  const raw = await readStdin(1500);
  let cwd = process.cwd();
  try {
    const parsed = JSON.parse(stripBom(raw));
    if (parsed && typeof parsed.cwd === 'string' && parsed.cwd) cwd = parsed.cwd;
  } catch {
    cwd = process.cwd();
  }
  const top = git(cwd, ['rev-parse', '--show-toplevel']);
  let target = '';
  let extra = [];
  if (top) {
    if (existsSync(join(top, HOOK))) target = top;
  } else if (event === 'session-start') {
    target = findCheckoutWithHook(cwd);
    extra = ['--index'];
  }
  if (!target) return;
  const run = spawnSync(process.execPath, [join(target, HOOK), event, ...extra], {
    input: raw,
    cwd: target,
    encoding: 'utf8',
    timeout: 8000,
    windowsHide: true,
  });
  if (run.status === 0 && run.stdout) process.stdout.write(run.stdout);
}

main().catch(() => {});
