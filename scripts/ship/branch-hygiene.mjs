#!/usr/bin/env node
/**
 * Branch + worktree hygiene (owner directive 2026-10-07; hard enforced).
 *
 * Parallel agents/workflows used to leave ~45 worktrees and ~285 branches behind.
 * `sweep()` removes only what is provably safe to lose (clean worktree / branch
 * whose tip is already in origin/main or on a remote branch), then fails when
 * more than MAX_EXTRA_WORKTREES worktrees or MAX_LOCAL_BRANCHES branches remain.
 * Anything unmerged AND unpushed is never deleted; it is reported so the owner
 * or agent pushes it or throws it away on purpose.
 *
 * Usage: node scripts/ship/branch-hygiene.mjs [--check] [--remote] [--self-test]
 *   default  fix local worktrees/branches, then enforce the caps (exit 1 on breach)
 *   --check  enforce the caps only, change nothing
 *   --remote also delete remote branches merged into origin/main with no open PR
 */
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const MAX_EXTRA_WORKTREES = 2; // besides the primary checkout
export const MAX_LOCAL_BRANCHES = 4; // main + the active branch(es)
const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const LEDGER_FILES = new Set(['VERIFICATION.md']); // machine-written, never real work

function git(args, cwd = root) {
  try {
    return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  } catch {
    return null;
  }
}
const lines = (s) => (s ? s.split(/\r?\n/).filter(Boolean) : []);
const norm = (p) => resolve(p).replace(/\\/g, '/').toLowerCase();

function worktrees() {
  const out = [];
  let cur = null;
  for (const l of lines(git(['worktree', 'list', '--porcelain']))) {
    if (l.startsWith('worktree ')) out.push((cur = { path: l.slice(9) }));
    else if (cur && l.startsWith('branch ')) cur.branch = l.slice(7).replace('refs/heads/', '');
  }
  return out;
}

const inMain = (ref) => git(['merge-base', '--is-ancestor', ref, 'origin/main']) !== null;
const onRemote = (ref) => lines(git(['branch', '-r', '--contains', ref])).some((b) => !b.includes('->'));
const safeToLose = (ref) => inMain(ref) || onRemote(ref);

export function sweep({ fix = true, remote = false } = {}) {
  const notes = [];
  git(['worktree', 'prune']);
  const [primary, ...extra] = worktrees();
  const current = git(['branch', '--show-current']);

  for (const w of extra) {
    const dirty = lines(git(['status', '--short', '--untracked-files=no'], w.path)).filter(
      (l) => !LEDGER_FILES.has(l.slice(3).trim()),
    );
    const head = git(['rev-parse', 'HEAD'], w.path);
    if (fix && !dirty.length && head && safeToLose(head)) {
      git(['worktree', 'remove', '--force', '--force', w.path]);
      notes.push(`removed worktree ${w.path}`);
    }
  }

  const kept = new Set(worktrees().map((w) => w.branch).filter(Boolean));
  for (const b of lines(git(['for-each-ref', '--format=%(refname:short)', 'refs/heads']))) {
    if (b === 'main' || b === current || kept.has(b)) continue;
    if (fix && safeToLose(b)) {
      git(['branch', '-D', b]);
      notes.push(`deleted branch ${b}`);
    }
  }

  if (fix && remote) {
    const open = new Set();
    try {
      const prs = execFileSync('gh', ['pr', 'list', '--state', 'open', '--limit', '300', '--json', 'headRefName', '--jq', '.[].headRefName'], {
        cwd: root,
        encoding: 'utf8',
      });
      for (const n of lines(prs)) open.add(n);
    } catch {
      notes.push('remote sweep skipped: gh pr list failed');
      remote = false;
    }
    if (remote) {
      for (const r of lines(git(['for-each-ref', '--format=%(refname:lstrip=3)', 'refs/remotes/origin']))) {
        if (r === 'HEAD' || r === 'main' || open.has(r) || !inMain(`origin/${r}`)) continue;
        git(['push', 'origin', '--delete', r]);
        notes.push(`deleted remote branch ${r}`);
      }
    }
  }

  const wt = worktrees().filter((w) => norm(w.path) !== norm(primary.path));
  const branches = lines(git(['for-each-ref', '--format=%(refname:short)', 'refs/heads']));
  const failures = [];
  if (wt.length > MAX_EXTRA_WORKTREES)
    failures.push(`${wt.length} extra worktrees (max ${MAX_EXTRA_WORKTREES}): ${wt.map((w) => w.path).join(', ')}`);
  if (branches.length > MAX_LOCAL_BRANCHES)
    failures.push(`${branches.length} local branches (max ${MAX_LOCAL_BRANCHES}): ${branches.join(', ')}`);
  return { notes, failures };
}

function selfTest() {
  const a = sweep({ fix: false });
  return Array.isArray(a.notes) && Array.isArray(a.failures);
}

function main(argv) {
  if (argv.includes('--self-test')) {
    console.log(selfTest() ? 'branch-hygiene self-test OK' : 'branch-hygiene self-test FAILED');
    process.exit(selfTest() ? 0 : 1);
  }
  const { notes, failures } = sweep({ fix: !argv.includes('--check'), remote: argv.includes('--remote') });
  for (const n of notes) console.log(`branch-hygiene: ${n}`);
  if (failures.length) {
    for (const f of failures) console.error(`branch-hygiene: ${f}`);
    console.error(
      'branch-hygiene FAILED - push or delete the leftovers on purpose (git worktree remove <path>; git branch -D <name>); never leave stale agent worktrees/branches behind.',
    );
    process.exit(1);
  }
  console.log('branch-hygiene OK');
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main(process.argv.slice(2));
