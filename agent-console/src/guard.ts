import path from 'node:path';
import type { GuardVerdict, Mode, ToolCallRequest } from './engines/types.js';

// The Guard: a table-driven classifier for every tool call an engine wants to
// run, plus the mode/taint decision (plan Phase 1 "src/guard.ts").
//
//   destructive  → Guarded asks the owner, Autopilot pre-snapshots then allows
//   unparseable  → treated exactly like destructive (we cannot see what runs)
//   forbidden    → denied in every mode (policy says only the Ship executor
//                  merges or changes visibility; heavy compute runs on Actions)
//   taintSource  → the read makes the session tainted (attacker-writable input)
//
// The Guard is a seatbelt, not the boundary: enforcement lives in the uid
// split, Postgres privileges, the GitHub ruleset and the docker/egress
// proxies. It never persists anything.

export type GuardCategory =
  | 'read'
  | 'file_write'
  | 'db_read'
  | 'db_write'
  | 'docker_read'
  | 'docker_write'
  | 'git_read'
  | 'git_write'
  | 'git_push'
  | 'gh_read'
  | 'gh_write'
  | 'network_read'
  | 'network_write'
  | 'process'
  | 'env_edit'
  | 'protected_path'
  | 'compute'
  /** Runs a script from the agent-writable worktree (e.g. the allowed pre-push gate). */
  | 'worktree_code'
  | 'unknown';

export interface Classification extends GuardVerdict {
  /** Mutates something (file, DB, container, remote); Read-only mode denies it. */
  write: boolean;
  /** Denied in every mode. */
  forbidden: boolean;
  categories: GuardCategory[];
  /** A destructive/unparseable call that may touch the database: take a pre-snapshot first. */
  needsDbSnapshot: boolean;
  /** Table-scoped snapshot when exactly one table is affected; empty = full dump. */
  snapshotTables: string[];
  /** Key used for approve_session grants (exact normalized call). */
  grantKey: string;
}

export interface ClassifyContext {
  /** The session worktree (engine cwd). Paths inside it are the agent's own scratch space. */
  worktree?: string;
  /** Working directory of the call when it differs from the worktree. */
  cwd?: string;
}

/** Categories that need an owner click once the session is tainted — even in Autopilot. */
export const TAINT_SENSITIVE_CATEGORIES: ReadonlySet<GuardCategory> = new Set<GuardCategory>([
  'db_write',
  'docker_write',
  'git_push',
  'gh_write',
  'network_write',
  'env_edit',
  // The script lives in the agent-writable worktree: after untrusted input it may
  // have been rewritten to do anything, so it needs the owner like any opaque code.
  'worktree_code',
]);

// ---------------------------------------------------------------- findings

class Findings {
  readonly destructive: string[] = [];
  readonly unparseable: string[] = [];
  readonly forbidden: string[] = [];
  readonly categories = new Set<GuardCategory>();
  write = false;
  taintSource: string | undefined;
  dbDestructive = false;
  dbHint = false;
  readonly tables = new Set<string>();
  tablesUnknown = false;

  destroy(reason: string): void {
    if (!this.destructive.includes(reason)) this.destructive.push(reason);
  }
  unparse(reason: string): void {
    if (!this.unparseable.includes(reason)) this.unparseable.push(reason);
  }
  forbid(reason: string): void {
    if (!this.forbidden.includes(reason)) this.forbidden.push(reason);
  }
  cat(...categories: GuardCategory[]): void {
    for (const c of categories) this.categories.add(c);
    if (categories.some((c) => WRITE_CATEGORIES.has(c))) this.write = true;
  }
  taint(source: string): void {
    this.taintSource ??= source;
  }
}

const WRITE_CATEGORIES: ReadonlySet<GuardCategory> = new Set<GuardCategory>([
  'file_write',
  'db_write',
  'docker_write',
  'git_write',
  'git_push',
  'gh_write',
  'network_write',
  'process',
  'env_edit',
  'protected_path',
  'compute',
  'worktree_code',
  'unknown',
]);

// ------------------------------------------------------------------- lexer

export interface Word {
  value: string;
  hasExpansion: boolean;
  hasGlob: boolean;
  quoted: boolean;
}

export interface Redirect {
  op: string;
  fd: string | null;
  target: string | null;
}

export interface Segment {
  words: Word[];
  redirects: Redirect[];
  pipedFrom: boolean;
  pipedTo: boolean;
}

export type LexFlag =
  | 'heredoc'
  | 'command_substitution'
  | 'process_substitution'
  | 'ansi_c_quoting'
  | 'unterminated_quote'
  | 'background';

export interface LexResult {
  segments: Segment[];
  flags: Set<LexFlag>;
}

const newSegment = (pipedFrom = false): Segment => ({ words: [], redirects: [], pipedFrom, pipedTo: false });
const EXPANSION_START = /[A-Za-z_{0-9@*#?$!-]/;

/** Minimal POSIX-shell lexer: words (with quote removal), operators, redirections. */
export function lexShell(input: string): LexResult {
  const flags = new Set<LexFlag>();
  const segments: Segment[] = [];
  let seg = newSegment();
  let word: Word | null = null;
  let pending: Redirect | null = null;

  const cur = (): Word => {
    word ??= { value: '', hasExpansion: false, hasGlob: false, quoted: false };
    return word;
  };
  const pushWord = (): void => {
    if (word === null) return;
    if (pending) {
      pending.target = word.value;
      seg.redirects.push(pending);
      pending = null;
    } else {
      seg.words.push(word);
    }
    word = null;
  };
  const startRedirect = (op: string, fd: string | null): void => {
    if (pending) seg.redirects.push(pending);
    pending = { op, fd, target: null };
  };
  const endSegment = (piped: boolean): void => {
    pushWord();
    if (pending) {
      seg.redirects.push(pending);
      pending = null;
    }
    if (seg.words.length > 0 || seg.redirects.length > 0) {
      seg.pipedTo = piped;
      segments.push(seg);
    }
    seg = newSegment(piped);
  };

  let i = 0;
  while (i < input.length) {
    const ch = input[i] as string;
    const next = input[i + 1];

    if (ch === '\\') {
      if (next === '\n') {
        i += 2;
        continue;
      }
      if (next !== undefined) {
        const w = cur();
        w.value += next;
        w.quoted = true;
      }
      i += 2;
      continue;
    }
    if (ch === "'") {
      const end = input.indexOf("'", i + 1);
      const w = cur();
      w.quoted = true;
      if (end < 0) {
        flags.add('unterminated_quote');
        w.value += input.slice(i + 1);
        i = input.length;
      } else {
        w.value += input.slice(i + 1, end);
        i = end + 1;
      }
      continue;
    }
    if (ch === '"') {
      const w = cur();
      w.quoted = true;
      let j = i + 1;
      let closed = false;
      while (j < input.length) {
        const c = input[j] as string;
        const n = input[j + 1];
        if (c === '\\' && n !== undefined && '$`"\\\n'.includes(n)) {
          if (n !== '\n') w.value += n;
          j += 2;
          continue;
        }
        if (c === '"') {
          closed = true;
          j += 1;
          break;
        }
        if (c === '`') flags.add('command_substitution');
        if (c === '$' && n !== undefined) {
          if (n === '(') flags.add('command_substitution');
          else if (EXPANSION_START.test(n)) w.hasExpansion = true;
        }
        w.value += c;
        j += 1;
      }
      if (!closed) flags.add('unterminated_quote');
      i = j;
      continue;
    }
    if (ch === '$') {
      if (next === "'") {
        flags.add('ansi_c_quoting');
        const w = cur();
        w.quoted = true;
        let j = i + 2;
        while (j < input.length && input[j] !== "'") j += input[j] === '\\' ? 2 : 1;
        w.value += input.slice(i + 2, Math.min(j, input.length));
        i = j + 1;
        continue;
      }
      if (next === '(') {
        flags.add('command_substitution');
        const w = cur();
        w.hasExpansion = true;
        w.value += '$(';
        i += 2;
        continue;
      }
      if (next !== undefined && EXPANSION_START.test(next)) cur().hasExpansion = true;
      cur().value += ch;
      i += 1;
      continue;
    }
    if (ch === '`') {
      flags.add('command_substitution');
      const w = cur();
      w.hasExpansion = true;
      w.value += ch;
      i += 1;
      continue;
    }
    if (ch === ' ' || ch === '\t' || ch === '\r') {
      pushWord();
      i += 1;
      continue;
    }
    if (ch === '\n') {
      endSegment(false);
      i += 1;
      continue;
    }
    if (ch === '#' && word === null) {
      const end = input.indexOf('\n', i);
      i = end < 0 ? input.length : end;
      continue;
    }
    if (ch === ';') {
      endSegment(false);
      i += next === ';' ? 2 : 1;
      continue;
    }
    if (ch === '&') {
      if (next === '&') {
        endSegment(false);
        i += 2;
        continue;
      }
      if (next === '>') {
        pushWord();
        const op = input[i + 2] === '>' ? '&>>' : '&>';
        startRedirect(op, null);
        i += op.length;
        continue;
      }
      flags.add('background');
      endSegment(false);
      i += 1;
      continue;
    }
    if (ch === '|') {
      if (next === '|') {
        endSegment(false);
        i += 2;
        continue;
      }
      endSegment(true);
      i += next === '&' ? 2 : 1;
      continue;
    }
    if (ch === '(' || ch === ')') {
      endSegment(false);
      i += 1;
      continue;
    }
    if (ch === '<' || ch === '>') {
      let fd: string | null = null;
      const w = word as Word | null;
      if (w !== null && !w.quoted && /^\d+$/.test(w.value)) {
        fd = w.value;
        word = null;
      } else {
        pushWord();
      }
      if (ch === '<') {
        if (next === '<') {
          flags.add('heredoc');
          const op = input[i + 2] === '<' ? '<<<' : input[i + 2] === '-' ? '<<-' : '<<';
          startRedirect(op, fd);
          i += op.length;
          continue;
        }
        if (next === '(') {
          flags.add('process_substitution');
          i += 2;
          continue;
        }
        const op = next === '&' ? '<&' : next === '>' ? '<>' : '<';
        startRedirect(op, fd);
        i += op.length;
        continue;
      }
      if (next === '(') {
        flags.add('process_substitution');
        i += 2;
        continue;
      }
      const op = next === '>' ? '>>' : next === '&' ? '>&' : next === '|' ? '>|' : '>';
      startRedirect(op, fd);
      i += op.length;
      continue;
    }
    if (ch === '*' || ch === '?' || ch === '[') cur().hasGlob = true;
    cur().value += ch;
    i += 1;
  }
  endSegment(false);
  return { segments, flags };
}

// ------------------------------------------------------------- path rules

const VOLUME_PATHS = [
  '/',
  '/var/lib/docker',
  '/var/lib/postgresql',
  '/var/opt/oet-learner',
  '/backups',
  '/workspace',
  '/home/agent',
  '/opt/oetwebapp',
  '/var/lib/oet-agent',
  '/run/secrets',
];

const SYSTEM_PREFIXES = ['/etc', '/usr', '/bin', '/sbin', '/lib', '/lib64', '/opt', '/var/lib', '/var/opt', '/root', '/run', '/boot', '/proc', '/sys', '/dev'];

const PROTECTED_HOME_ENTRIES = ['.ssh', '.gitconfig', '.config', '.bashrc', '.profile', '.bash_profile', '.npmrc', '.netrc'];

interface ShellCtx {
  worktree: string | null;
  cwd: string | null;
  findings: Findings;
  depth: number;
  /** Arguments arrive at runtime (xargs); targets are unknown. */
  dynamicArgs: boolean;
  /** Inner command of docker exec: paths are container paths. */
  inContainer: boolean;
}

interface Io {
  pipedFrom: boolean;
  pipedTo: boolean;
  stdinFromFile: boolean;
  stdoutToFile: boolean;
}

function normalizeDir(dir: string | undefined | null): string | null {
  if (!dir) return null;
  const n = path.posix.normalize(dir.replace(/\\/g, '/'));
  return n.length > 1 ? n.replace(/\/+$/, '') : n;
}

/** Absolute path of `target`, or null when it cannot be known statically. */
function resolvePath(target: string, ctx: ShellCtx): string | null {
  if (target.startsWith('~') || target.includes('$') || target.includes('`')) return null;
  if (target.startsWith('/')) return path.posix.normalize(target);
  if (ctx.cwd === null) return null;
  return path.posix.normalize(path.posix.join(ctx.cwd, target));
}

function isInside(abs: string, dir: string): boolean {
  return abs === dir || abs.startsWith(dir === '/' ? '/' : `${dir}/`);
}

/** True when the path is (or may be) outside the session worktree. */
function outsideWorktree(target: string, ctx: ShellCtx): boolean {
  if (ctx.inContainer) return true;
  const abs = resolvePath(target, ctx);
  if (abs === null) return true;
  if (ctx.worktree === null) {
    // Unknown worktree: relative paths without parent traversal are assumed local.
    return target.startsWith('/') || target.split('/').includes('..');
  }
  return !isInside(abs, ctx.worktree);
}

function isWorktreeRoot(target: string, ctx: ShellCtx): boolean {
  const t = target.replace(/\/+$/, '');
  if (t === '' || t === '.' || t === '*' || t === './*' || t === '.*') return true;
  const abs = resolvePath(target, ctx);
  return abs !== null && ctx.worktree !== null && abs === ctx.worktree;
}

function isEnvFile(p: string): boolean {
  return /^\.env(\..*)?$/i.test(path.posix.basename(p)) || /^\.env[A-Za-z0-9_-]*$/i.test(path.posix.basename(p));
}

/** Plan-listed protected files (writes are destructive). Returns the reason or null. */
export function protectedPathReason(p: string, ctx?: { worktree?: string | null; cwd?: string | null }): string | null {
  const normalized = p.replace(/\\/g, '/');
  const base = path.posix.basename(normalized);
  const parts = normalized.split('/');
  if (isEnvFile(normalized)) return `edit of environment file ${base}`;
  // Hooks and repo config execute code on ordinary git commands (commit, diff, …);
  // the worktree's `.git` file points git at another directory.
  if (parts.includes('.git')) return 'write to git internals (.git/**: hooks, config)';
  if (parts.includes('.claude')) return 'write to .claude/**';
  if (parts.includes('.codex')) return 'write to .codex/**';
  if (base === '.mcp.json') return 'write to .mcp.json';
  if (/(^|\/)\.github\/workflows(\/|$)/.test(normalized)) return 'write to .github/workflows/**';
  if (/^docker-compose.*\.ya?ml$/i.test(base)) return `write to ${base}`;
  if (/\.template$/i.test(base)) return `write to template ${base}`;
  const abs = normalized.startsWith('/')
    ? path.posix.normalize(normalized)
    : ctx?.cwd
      ? path.posix.normalize(path.posix.join(ctx.cwd, normalized))
      : null;
  if (abs !== null) {
    const worktree = ctx?.worktree ?? null;
    if (worktree !== null && isInside(abs, worktree)) return null;
    if (isInside(abs, '/home/agent')) {
      const entry = abs.split('/')[3];
      if (entry && PROTECTED_HOME_ENTRIES.includes(entry)) return `write to agent home config ${entry}`;
      return null;
    }
    if (isInside(abs, '/tmp') || isInside(abs, '/var/tmp') || abs === '/dev/null') return null;
    if (isInside(abs, '/workspace')) return null;
    for (const prefix of SYSTEM_PREFIXES) {
      if (isInside(abs, prefix)) return `write to system path ${prefix}`;
    }
  }
  return null;
}

function checkWrite(target: string, ctx: ShellCtx, f: Findings): void {
  f.cat('file_write');
  if (ctx.inContainer) return;
  const reason = protectedPathReason(target, { worktree: ctx.worktree, cwd: ctx.cwd });
  if (reason) {
    f.destroy(reason);
    f.cat('protected_path');
    if (isEnvFile(target)) f.cat('env_edit');
  }
}

// --------------------------------------------------------------- SQL rules

function quoteEnd(s: string, start: number, q: string): number {
  let i = start + 1;
  while (i < s.length) {
    if (s[i] === q) {
      if (s[i + 1] === q) {
        i += 2;
        continue;
      }
      return i + 1;
    }
    i += 1;
  }
  return s.length;
}

interface SqlScan {
  statements: string[];
}

/** Splits SQL into statements with comments removed and literal contents masked. */
export function scanSql(sql: string): SqlScan {
  const statements: string[] = [];
  let current = '';
  let i = 0;
  while (i < sql.length) {
    const c = sql[i] as string;
    const n = sql[i + 1];
    if (c === "'") {
      const end = quoteEnd(sql, i, "'");
      current += "''";
      i = end;
      continue;
    }
    if (c === '"') {
      const end = quoteEnd(sql, i, '"');
      current += sql.slice(i, end);
      i = end;
      continue;
    }
    if (c === '$') {
      const m = /^\$([A-Za-z_][A-Za-z0-9_]*)?\$/.exec(sql.slice(i));
      if (m) {
        const tagText = m[0];
        const end = sql.indexOf(tagText, i + tagText.length);
        current += `${tagText} ${tagText}`;
        i = end < 0 ? sql.length : end + tagText.length;
        continue;
      }
    }
    if (c === '-' && n === '-') {
      const end = sql.indexOf('\n', i);
      i = end < 0 ? sql.length : end;
      current += ' ';
      continue;
    }
    if (c === '/' && n === '*') {
      const end = sql.indexOf('*/', i + 2);
      i = end < 0 ? sql.length : end + 2;
      current += ' ';
      continue;
    }
    if (c === ';') {
      if (current.trim()) statements.push(current.trim().replace(/\s+/g, ' '));
      current = '';
      i += 1;
      continue;
    }
    current += c;
    i += 1;
  }
  if (current.trim()) statements.push(current.trim().replace(/\s+/g, ' '));
  return { statements };
}

const TAUTOLOGY = String.raw`(?:TRUE|1\s*=\s*1|(\d+)\s*=\s*\1|''\s*=\s*''|NOT\s+FALSE|1|TRUE\s*=\s*TRUE)`;
const WHERE_TAUTOLOGY = new RegExp(String.raw`\bWHERE\s+\(?\s*${TAUTOLOGY}\s*\)?\s*(?:RETURNING\b|ORDER\b|LIMIT\b|$)`, 'i');
const OR_TAUTOLOGY = new RegExp(String.raw`\bWHERE\b.*\bOR\s+\(?\s*(?:TRUE|1\s*=\s*1|''\s*=\s*''|NOT\s+FALSE)\b`, 'i');

function hasRestrictiveWhere(statement: string): boolean {
  if (!/\bWHERE\b/i.test(statement)) return false;
  if (WHERE_TAUTOLOGY.test(statement)) return false;
  if (OR_TAUTOLOGY.test(statement)) return false;
  return true;
}

export interface SqlRule {
  reason: string;
  test: (statement: string) => boolean;
  /** Extracts the affected table for a table-scoped snapshot. */
  table?: RegExp;
}

const IDENT = String.raw`((?:"[^"]+"|[A-Za-z_][\w$]*)(?:\.(?:"[^"]+"|[A-Za-z_][\w$]*))?)`;

/** Destructive SQL, first match wins per statement (plan: Guard destructive list). */
export const SQL_DESTRUCTIVE_RULES: readonly SqlRule[] = [
  { reason: 'DROP statement', test: (s) => /^DROP\b/i.test(s), table: new RegExp(String.raw`^DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?${IDENT}\s*(?:CASCADE|RESTRICT)?$`, 'i') },
  { reason: 'TRUNCATE', test: (s) => /^TRUNCATE\b/i.test(s), table: new RegExp(String.raw`^TRUNCATE\s+(?:TABLE\s+)?(?:ONLY\s+)?${IDENT}(?:\s+(?:RESTART|CONTINUE)\s+IDENTITY)?(?:\s+(?:CASCADE|RESTRICT))?$`, 'i') },
  { reason: 'ALTER … DROP', test: (s) => /^ALTER\b.*\bDROP\b/i.test(s), table: new RegExp(String.raw`^ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?${IDENT}`, 'i') },
  { reason: 'DELETE without a restrictive WHERE', test: (s) => /^DELETE\s+FROM\b/i.test(s) && !hasRestrictiveWhere(s), table: new RegExp(String.raw`^DELETE\s+FROM\s+(?:ONLY\s+)?${IDENT}`, 'i') },
  { reason: 'UPDATE without a restrictive WHERE', test: (s) => /^UPDATE\b/i.test(s) && !hasRestrictiveWhere(s), table: new RegExp(String.raw`^UPDATE\s+(?:ONLY\s+)?${IDENT}`, 'i') },
  { reason: 'data-modifying CTE (WITH … DELETE/UPDATE)', test: (s) => /^WITH\b/i.test(s) && /\b(DELETE\s+FROM|UPDATE\s+\S+\s+SET)\b/i.test(s) },
  { reason: 'anonymous code block (DO $$)', test: (s) => /^DO\b/i.test(s) },
  { reason: 'COPY … PROGRAM', test: (s) => /^\\?COPY\b.*\bPROGRAM\b/i.test(s) },
  { reason: 'role change', test: (s) => /^(ALTER|CREATE|DROP)\s+(ROLE|USER|GROUP)\b/i.test(s) },
  { reason: 'privilege change', test: (s) => /^(GRANT|REVOKE)\b/i.test(s) || /^ALTER\s+DEFAULT\s+PRIVILEGES\b/i.test(s) },
  { reason: 'ALTER SYSTEM', test: (s) => /^ALTER\s+SYSTEM\b/i.test(s) },
  { reason: 'role switch', test: (s) => /^(SET|RESET)\s+(SESSION\s+|LOCAL\s+)?(ROLE|SESSION\s+AUTHORIZATION)\b/i.test(s) },
  { reason: 'backend termination', test: (s) => /\bpg_(terminate|cancel)_backend\s*\(/i.test(s) },
];

const SQL_WRITE_START =
  /^(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|COPY|DO|CALL|REINDEX|VACUUM|CLUSTER|REFRESH|COMMENT|SECURITY|LOCK|IMPORT|ANALYZE|NOTIFY|LISTEN|UNLISTEN|PREPARE|EXECUTE|DEALLOCATE|DISCARD|LOAD|REASSIGN|CHECKPOINT|RESET\s+ROLE|SET\s+ROLE)\b/i;
const SQL_READ_START = /^(SELECT|TABLE|VALUES|SHOW|EXPLAIN|WITH|BEGIN|START|COMMIT|END|ROLLBACK|ABORT|SAVEPOINT|RELEASE|SET|FETCH|DECLARE|CLOSE|MOVE)\b/i;
const SQL_WRITE_FUNCTIONS = /\b(nextval|setval|pg_terminate_backend|pg_cancel_backend|lo_import|lo_export|lo_unlink|set_config|dblink(_exec)?|pg_advisory_lock|pg_reload_conf|pg_rotate_logfile)\s*\(/i;

/** Classifies SQL text run through psql -c into the findings. */
function classifySql(sql: string, f: Findings): Findings {
  f.dbHint = true;
  const { statements } = scanSql(sql);
  for (const statement of statements) {
    let destructive = false;
    for (const rule of SQL_DESTRUCTIVE_RULES) {
      if (!rule.test(statement)) continue;
      destructive = true;
      f.destroy(rule.reason);
      f.dbDestructive = true;
      // Keep identifier quoting: EF Core tables are quoted PascalCase ("WritingSubmissions"),
      // and pg_dump --table folds unquoted names to lower case.
      const table = rule.table?.exec(statement)?.[1];
      if (table) f.tables.add(table);
      else f.tablesUnknown = true;
      break;
    }
    const isExplainAnalyze = /^EXPLAIN\b.*\bANALYZE\b/i.test(statement);
    const writes =
      destructive ||
      SQL_WRITE_START.test(statement) ||
      SQL_WRITE_FUNCTIONS.test(statement) ||
      (/^SELECT\b/i.test(statement) && /\bINTO\s+(?!STRICT\b)[A-Za-z_"]/i.test(statement) && !/\bINTO\s+STRICT\b/i.test(statement)) ||
      /\bFOR\s+(NO\s+KEY\s+)?(UPDATE|SHARE)\b/i.test(statement) ||
      (/^WITH\b/i.test(statement) && /\b(INSERT\s+INTO|DELETE\s+FROM|UPDATE\s+\S+\s+SET|MERGE\s+INTO)\b/i.test(statement)) ||
      (isExplainAnalyze && /\b(INSERT|UPDATE|DELETE|MERGE)\b/i.test(statement));
    if (writes) {
      f.cat('db_write');
      continue;
    }
    if (SQL_READ_START.test(statement)) {
      f.cat('db_read');
      if (readsUserRows(statement)) f.taint('db_read');
      continue;
    }
    // Unknown statement type: treat as a write.
    f.cat('db_write');
  }
  return f;
}

/** A read of application rows (anything but catalog/`count(*)`) — learner content may be in it. */
function readsUserRows(statement: string): boolean {
  if (/^(SHOW|BEGIN|START|COMMIT|END|ROLLBACK|ABORT|SAVEPOINT|RELEASE|SET)\b/i.test(statement)) return false;
  if (/^EXPLAIN\b/i.test(statement) && !/\bANALYZE\b/i.test(statement)) return false;
  const targets: string[] = [];
  const re = /\b(?:FROM|JOIN)\s+(?:ONLY\s+|LATERAL\s+)?("?[A-Za-z_][\w$]*"?(?:\."?[A-Za-z_][\w$]*"?)?)/gi;
  for (let m = re.exec(statement); m !== null; m = re.exec(statement)) targets.push((m[1] ?? '').replace(/"/g, '').toLowerCase());
  const table = /^TABLE\s+("?[A-Za-z_][\w$.]*"?)/i.exec(statement)?.[1];
  if (table) targets.push(table.replace(/"/g, '').toLowerCase());
  const userTargets = targets.filter(
    (t) => !t.startsWith('pg_') && !t.startsWith('information_schema.') && !t.startsWith('pg_catalog.') && t !== 'generate_series',
  );
  if (userTargets.length === 0) return false;
  if (/^SELECT\s+count\s*\(\s*(\*|1)\s*\)\s+FROM\b/i.test(statement) && !/\b(UNION|JOIN)\b/i.test(statement)) return false;
  return true;
}

// ---------------------------------------------------------- command tables

const READ_COMMANDS: ReadonlySet<string> = new Set([
  'ls', 'll', 'dir', 'cat', 'tac', 'head', 'tail', 'less', 'more', 'wc', 'grep', 'egrep', 'fgrep', 'rg', 'ag', 'ack',
  'fd', 'fdfind', 'tree', 'stat', 'file', 'du', 'df', 'pwd', 'echo', 'printf', 'date', 'whoami', 'id', 'groups',
  'uname', 'hostname', 'which', 'whereis', 'type', 'true', 'false', 'test', '[', '[[', 'basename', 'dirname',
  'realpath', 'readlink', 'sort', 'uniq', 'cut', 'tr', 'paste', 'join', 'fold', 'fmt', 'column', 'nl', 'rev', 'jq',
  'yq', 'diff', 'cmp', 'comm', 'od', 'hexdump', 'strings', 'sha1sum', 'sha256sum', 'sha512sum', 'md5sum', 'cksum',
  'b2sum', 'printenv', 'uptime', 'free', 'ps', 'pgrep', 'pstree', 'lsof', 'sleep', 'seq', 'expr', 'bc', 'getent',
  'locale', 'nproc', 'arch', 'tty', 'clear', 'exit', 'wait', 'jobs', 'history', 'help', 'man', 'cal', 'tput', 'bat',
  'batcat', 'zcat', 'zgrep', 'zless', 'dig', 'nslookup', 'host', 'ping', 'netstat', 'ss', 'unset', 'shopt',
]);

/** Always unparseable: they run code we cannot see, change identity, or open raw channels. */
const OPAQUE_COMMANDS: ReadonlyMap<string, string> = new Map([
  ['eval', 'eval of a dynamic string'],
  ['source', 'sourcing a script'],
  ['.', 'sourcing a script'],
  ['alias', 'alias definition'],
  ['trap', 'trap handler'],
  ['function', 'function definition'],
  ['sudo', 'privilege change'],
  ['su', 'privilege change'],
  ['doas', 'privilege change'],
  ['runuser', 'privilege change'],
  ['setpriv', 'privilege change'],
  ['capsh', 'privilege change'],
  ['chroot', 'namespace/root change'],
  ['nsenter', 'namespace/root change'],
  ['unshare', 'namespace/root change'],
  ['ssh', 'remote shell'],
  ['scp', 'remote copy'],
  ['sftp', 'remote copy'],
  ['rsync', 'remote copy'],
  ['nc', 'raw network tool'],
  ['ncat', 'raw network tool'],
  ['netcat', 'raw network tool'],
  ['socat', 'raw network tool'],
  ['telnet', 'raw network tool'],
  ['ftp', 'raw network tool'],
  ['systemctl', 'service manager'],
  ['service', 'service manager'],
  ['crontab', 'scheduled job'],
  ['at', 'scheduled job'],
  ['mount', 'mount'],
  ['umount', 'mount'],
  ['iptables', 'firewall change'],
  ['sysctl', 'kernel setting'],
  ['modprobe', 'kernel module'],
  ['reboot', 'host power'],
  ['shutdown', 'host power'],
  ['halt', 'host power'],
  ['poweroff', 'host power'],
  ['watch', 'repeated command'],
  ['script', 'terminal recorder'],
  ['parallel', 'parallel runner'],
  ['flock', 'lock wrapper'],
  ['claude', 'nested agent CLI'],
  ['codex', 'nested agent CLI'],
]);

const SHELLS: ReadonlySet<string> = new Set(['sh', 'bash', 'zsh', 'dash', 'ksh', 'fish', 'csh', 'tcsh', 'ash', 'busybox']);
const INTERPRETERS: ReadonlySet<string> = new Set([
  'node', 'nodejs', 'python', 'python2', 'python3', 'ruby', 'php', 'perl', 'deno', 'bun', 'lua', 'Rscript', 'tsx',
  'ts-node', 'java', 'osascript', 'pwsh', 'powershell',
]);
const INLINE_CODE_FLAGS: ReadonlySet<string> = new Set(['-e', '-c', '--eval', '-p', '--print', '-r', '-E', '-x', '--exec', '-Command', '-command', '-EncodedCommand']);
/** Scripts the operating manual explicitly allows (plan: MANUAL "allowed"). */
const ALLOWED_SCRIPTS: ReadonlySet<string> = new Set(['scripts/ship/pre-push-gate.mjs', './scripts/ship/pre-push-gate.mjs']);

const PACKAGE_MANAGERS: ReadonlySet<string> = new Set(['npm', 'pnpm', 'yarn', 'bun']);
const PM_HEAVY: ReadonlySet<string> = new Set([
  'install', 'i', 'ci', 'add', 'build', 'test', 't', 'run', 'run-script', 'exec', 'x', 'dlx', 'rebuild', 'update', 'up',
  'upgrade', 'start', 'create', 'init', 'publish', 'link', 'dev', 'lint', 'tsc',
]);
const COMPUTE_COMMANDS: ReadonlySet<string> = new Set([
  'npx', 'pnpx', 'bunx', 'dotnet', 'make', 'cargo', 'mvn', 'gradle', 'tsc', 'vitest', 'jest', 'playwright', 'next',
  'webpack', 'vite', 'eslint', 'msbuild',
]);

const SENSITIVE_VARIABLE =
  /^(PATH|HOME|IFS|ENV|BASH_ENV|SHELLOPTS|BASHOPTS|PROMPT_COMMAND|CDPATH|NODE_OPTIONS|NODE_PATH|LD_\w+|DOCKER_\w+|PG\w+|GIT_\w+|GH_\w+|GITHUB_\w+|HTTPS?_PROXY|https?_proxy|NO_PROXY|no_proxy|ALL_PROXY|all_proxy|OET_\w+|CLAUDE_\w+|CODEX_\w+|ANTHROPIC_\w+|OPENAI_\w+|PYTHON\w*|PERL5\w*|RUBY\w*)$/;

const DOCKER_READ_VERBS: ReadonlySet<string> = new Set([
  'ps', 'logs', 'inspect', 'images', 'version', 'info', 'stats', 'top', 'port', 'diff', 'events', 'history',
  'container ls', 'container ps', 'container list', 'container inspect', 'container logs', 'container top', 'container port', 'container diff', 'container stats',
  'image ls', 'image list', 'image inspect', 'image history',
  'network ls', 'network list', 'network inspect',
  'volume ls', 'volume list', 'volume inspect',
  'system df', 'system info', 'system events',
  'context ls', 'context list', 'context show', 'context inspect',
]);

const GIT_READ_SUBCOMMANDS: ReadonlySet<string> = new Set([
  'status', 'log', 'diff', 'show', 'blame', 'annotate', 'rev-parse', 'ls-files', 'ls-tree', 'ls-remote', 'grep',
  'describe', 'shortlog', 'cat-file', 'merge-base', 'name-rev', 'for-each-ref', 'show-ref', 'show-branch',
  'whatchanged', 'count-objects', 'fsck', 'cherry', 'version', 'help', 'rev-list', 'verify-commit', 'verify-tag',
  'check-ignore', 'check-attr', 'var', 'range-diff', 'fetch',
]);
const GIT_EXEC_SUBCOMMANDS: ReadonlySet<string> = new Set(['submodule', 'bisect', 'difftool', 'mergetool', 'filter-branch', 'filter-repo', 'daemon', 'instaweb', 'credential', 'upload-pack', 'receive-pack']);
const GIT_CONFIG_EXEC_KEY = /^(alias\.|core\.(hookspath|sshcommand|fsmonitor|pager|editor|askpass|gitproxy)|credential\.|diff\.external|diff\..*\.(command|textconv)|filter\.|merge\..*\.driver|sequence\.editor|gpg\.(program|.*\.program)|pager\.|interactive\.difffilter|web\.browser|browser\.|man\.|http\..*proxy|include|includeif|uploadpack\.|protocol\.)/i;

const GH_READ: ReadonlyMap<string, ReadonlySet<string>> = new Map([
  ['pr', new Set(['view', 'list', 'diff', 'checks', 'status'])],
  ['issue', new Set(['view', 'list', 'status'])],
  ['run', new Set(['view', 'list', 'watch'])],
  ['workflow', new Set(['list', 'view'])],
  ['release', new Set(['list', 'view'])],
  ['repo', new Set(['view', 'list'])],
  ['variable', new Set(['list', 'get'])],
  ['secret', new Set(['list'])],
  ['label', new Set(['list'])],
  ['gist', new Set(['view', 'list'])],
  ['cache', new Set(['list'])],
  ['ruleset', new Set(['list', 'view', 'check'])],
  ['auth', new Set(['status'])],
  ['org', new Set(['list'])],
  ['project', new Set(['list', 'view'])],
  ['search', new Set(['repos', 'issues', 'prs', 'code', 'commits'])],
  ['status', new Set([''])],
]);
/** gh reads whose output is authored by people other than the owner. */
const GH_TAINTING: ReadonlyMap<string, ReadonlySet<string>> = new Map([
  ['pr', new Set(['view', 'list', 'diff', 'checks', 'status'])],
  ['issue', new Set(['view', 'list', 'status'])],
  ['gist', new Set(['view'])],
  ['search', new Set(['repos', 'issues', 'prs', 'code', 'commits'])],
  ['status', new Set([''])],
]);

// ------------------------------------------------------------ the analyzer

function values(words: readonly Word[]): string[] {
  return words.map((w) => w.value);
}

/** Splits `-rf` style clusters; returns the set of single-letter flags and long flags. */
function flagSet(args: readonly string[]): { short: Set<string>; long: Set<string>; positional: string[] } {
  const short = new Set<string>();
  const long = new Set<string>();
  const positional: string[] = [];
  let endOfOptions = false;
  for (const arg of args) {
    if (endOfOptions) {
      positional.push(arg);
    } else if (arg === '--') {
      endOfOptions = true;
    } else if (arg.startsWith('--')) {
      long.add(arg.split('=')[0] as string);
    } else if (arg.startsWith('-') && arg.length > 1) {
      for (const ch of arg.slice(1)) short.add(ch);
    } else {
      positional.push(arg);
    }
  }
  return { short, long, positional };
}

function analyzeSegment(seg: Segment, ctx: ShellCtx): void {
  const f = ctx.findings;
  let words = seg.words;

  const io: Io = { pipedFrom: seg.pipedFrom, pipedTo: seg.pipedTo, stdinFromFile: false, stdoutToFile: false };
  for (const r of seg.redirects) {
    if (r.op === '<' || r.op === '<&' || r.op === '<>') {
      if (r.op !== '<&') io.stdinFromFile = true;
      if (r.op === '<>' && r.target) checkWrite(r.target, ctx, f);
      continue;
    }
    if (r.op === '<<' || r.op === '<<-' || r.op === '<<<') continue;
    const target = r.target ?? '';
    if (r.op === '>&' && (/^\d+$/.test(target) || target === '-')) continue;
    if (['/dev/null', '/dev/stdout', '/dev/stderr', '/dev/tty'].includes(target)) continue;
    io.stdoutToFile = true;
    if (target) checkWrite(target, ctx, f);
    else f.unparse('redirection without a target');
  }

  // Leading shell keywords.
  while (words.length > 0) {
    const first = words[0] as Word;
    if (first.quoted) break;
    if (['{', '}', '!', 'then', 'do', 'else', 'elif', 'if', 'while', 'until', 'fi', 'done', 'esac'].includes(first.value)) {
      words = words.slice(1);
      continue;
    }
    break;
  }
  if (words.length === 0) return;
  const head = (words[0] as Word).value;
  if (!(words[0] as Word).quoted && (head === 'for' || head === 'select' || head === 'case' || head === 'in')) return;

  // NAME=value prefixes.
  let idx = 0;
  const assigned: string[] = [];
  while (idx < words.length) {
    const m = /^([A-Za-z_][A-Za-z0-9_]*)(\[[^\]]*\])?\+?=/.exec((words[idx] as Word).value);
    if (!m) break;
    assigned.push(m[1] as string);
    idx += 1;
  }
  if (assigned.length > 0 && idx < words.length) {
    f.unparse('environment assignment before a command (X=… cmd)');
  } else if (assigned.length > 0) {
    for (const name of assigned) if (SENSITIVE_VARIABLE.test(name)) f.unparse(`assignment to ${name}`);
    return;
  }
  analyzeCommand(words.slice(idx), io, ctx);
}

function analyzeCommand(words: readonly Word[], io: Io, ctx: ShellCtx): void {
  const f = ctx.findings;
  if (words.length === 0) return;
  if (ctx.depth > 8) {
    f.unparse('command nesting too deep');
    return;
  }
  const first = words[0] as Word;
  const name = first.value;
  if (first.hasExpansion || name.includes('$(') || name.includes('`')) {
    f.unparse('dynamic command name');
    return;
  }
  if (first.hasGlob) {
    f.unparse('glob in command name');
    return;
  }
  if (name === '') {
    f.unparse('empty command name');
    return;
  }
  const argWords = words.slice(1);
  const args = values(argWords);

  if (name.includes('/')) {
    if (INTERPRETERS.has(path.posix.basename(name)) || SHELLS.has(path.posix.basename(name))) {
      f.unparse('absolute binary path');
      return;
    }
    if (name.startsWith('/')) f.unparse('absolute binary path');
    else f.unparse('executes a script file');
    return;
  }

  const nested = (inner: readonly Word[], innerIo: Io = io): void =>
    analyzeCommand(inner, innerIo, { ...ctx, depth: ctx.depth + 1 });

  // Transparent wrappers: classify the wrapped command.
  switch (name) {
    case 'command':
    case 'builtin': {
      if (args[0] === '-v' || args[0] === '-V') {
        f.cat('read');
        return;
      }
      nested(argWords.filter((w, i) => !(i === 0 && w.value === '-p')));
      return;
    }
    case 'exec':
    case 'nohup':
    case 'time': {
      let i = 0;
      while (i < args.length && (args[i] as string).startsWith('-')) i += (args[i] === '-a' ? 2 : 1);
      if (i >= args.length) {
        f.cat('read');
        return;
      }
      nested(argWords.slice(i));
      return;
    }
    case 'nice': {
      let i = 0;
      while (i < args.length && (args[i] as string).startsWith('-')) i += args[i] === '-n' ? 2 : 1;
      nested(argWords.slice(i));
      return;
    }
    case 'timeout': {
      let i = 0;
      while (i < args.length && (args[i] as string).startsWith('-')) i += args[i] === '-s' || args[i] === '-k' ? 2 : 1;
      nested(argWords.slice(i + 1));
      return;
    }
    case 'stdbuf':
    case 'setsid':
    case 'ionice': {
      let i = 0;
      while (i < args.length && (args[i] as string).startsWith('-')) {
        const a = args[i] as string;
        i += (name === 'ionice' && (a === '-c' || a === '-n')) || (name === 'stdbuf' && /^-[ioe]$/.test(a)) ? 2 : 1;
      }
      nested(argWords.slice(i));
      return;
    }
    default:
      break;
  }

  const opaque = OPAQUE_COMMANDS.get(name);
  if (opaque) {
    f.unparse(opaque);
    f.cat('unknown');
    return;
  }
  if (SHELLS.has(name)) {
    if (args.some((a) => /^-[a-zA-Z]*c[a-zA-Z]*$/.test(a))) f.unparse(`${name} -c`);
    else if (io.pipedFrom) f.unparse(`pipe into ${name}`);
    else f.unparse(`${name} runs a script or interactive shell`);
    f.cat('unknown');
    return;
  }
  if (INTERPRETERS.has(name)) {
    analyzeInterpreter(name, args, ctx);
    return;
  }
  if (PACKAGE_MANAGERS.has(name)) {
    const sub = args.find((a) => !a.startsWith('-'));
    if (args.length === 1 && (args[0] === '--version' || args[0] === '-v')) {
      f.cat('read');
    } else if (sub === undefined || PM_HEAVY.has(sub)) {
      f.forbid(`${name} ${sub ?? ''}`.trim() + ' — installs/builds/tests run on GitHub Actions only');
      f.cat('compute');
    } else {
      f.cat('read', 'network_read');
    }
    return;
  }
  if (COMPUTE_COMMANDS.has(name)) {
    if (args.length === 1 && (args[0] === '--version' || args[0] === '--info')) {
      f.cat('read');
      return;
    }
    f.forbid(`${name} — compute is not available here and CI runs no automated QA (the owner tests manually and reports bugs)`);
    f.cat('compute');
    return;
  }

  switch (name) {
    case 'psql':
      analyzePsql(args, io, ctx);
      return;
    case 'pg_dump':
    case 'pg_dumpall':
      f.dbHint = true;
      f.cat('db_read');
      f.taint('db_read');
      if (args.some((a) => a === '-f' || a.startsWith('--file'))) f.cat('file_write');
      return;
    case 'pg_restore':
      f.dbHint = true;
      f.dbDestructive = true;
      f.tablesUnknown = true;
      f.destroy('pg_restore into the database');
      f.cat('db_write');
      return;
    case 'docker':
      analyzeDocker(argWords, io, ctx);
      return;
    case 'docker-compose':
      analyzeCompose(args, argWords, io, ctx);
      return;
    case 'git':
      analyzeGit(args, ctx);
      return;
    case 'gh':
      analyzeGh(args, ctx);
      return;
    case 'rm':
    case 'shred':
    case 'unlink':
      analyzeRm(name, args, ctx);
      return;
    case 'find':
      analyzeFind(argWords, io, ctx);
      return;
    case 'xargs':
      analyzeXargs(argWords, io, ctx);
      return;
    case 'sed':
      analyzeSed(args, ctx);
      return;
    case 'awk':
    case 'gawk':
    case 'mawk':
    case 'nawk':
      analyzeAwk(args, ctx);
      return;
    case 'curl':
      analyzeCurl(args, ctx);
      return;
    case 'wget':
      analyzeWget(args, ctx);
      return;
    case 'env':
      if (args.length === 0 || args.every((a) => a === '-0' || a === '--null')) f.cat('read');
      else {
        f.unparse('env X=… cmd');
        f.cat('unknown');
      }
      return;
    case 'cd':
    case 'pushd':
    case 'popd': {
      const target = args.find((a) => !a.startsWith('-'));
      if (name === 'popd' || target === undefined || target === '-' || target.startsWith('~') || target.includes('$')) {
        ctx.cwd = null;
      } else {
        ctx.cwd = resolvePath(target, ctx);
      }
      return;
    }
    case 'export':
    case 'declare':
    case 'typeset':
    case 'readonly':
    case 'local': {
      for (const a of args) {
        const n = /^([A-Za-z_][A-Za-z0-9_]*)/.exec(a)?.[1];
        if (a.startsWith('-')) {
          if (/f/.test(a)) f.unparse('function export');
          continue;
        }
        if (n && SENSITIVE_VARIABLE.test(n)) f.unparse(`${name} of ${n}`);
      }
      return;
    }
    case 'set': {
      if (args.every((a) => /^[-+][euxvo]+$/.test(a) || a === 'pipefail' || a === 'errexit' || a === 'nounset' || a === 'xtrace')) return;
      f.unparse('set changes shell state');
      return;
    }
    case 'base64':
    case 'xxd':
    case 'openssl':
      analyzeDecoder(name, args, io, ctx);
      return;
    case 'tee':
      for (const a of args.filter((x) => !x.startsWith('-'))) checkWrite(a, ctx, f);
      if (args.filter((x) => !x.startsWith('-')).length === 0) f.cat('read');
      return;
    case 'cp':
    case 'mv':
    case 'install':
    case 'ln': {
      const { positional } = flagSet(args.filter((a, i) => !(args[i - 1] === '-t' || args[i - 1] === '--target-directory')));
      const targetDir = args[args.indexOf('-t') + 1];
      const dest = args.includes('-t') && targetDir ? targetDir : positional[positional.length - 1];
      if (dest) checkWrite(dest, ctx, f);
      if (name === 'mv') for (const src of positional.slice(0, -1)) checkWrite(src, ctx, f);
      if (ctx.dynamicArgs) f.cat('file_write');
      return;
    }
    case 'touch':
    case 'mkdir':
    case 'rmdir':
    case 'chmod':
    case 'chown':
    case 'chgrp':
    case 'truncate':
    case 'patch':
    case 'unzip':
    case 'gunzip':
    case 'gzip':
    case 'mktemp':
    case 'split':
    case 'csplit': {
      const { positional } = flagSet(args);
      const targets = name === 'chmod' || name === 'chown' || name === 'chgrp' ? positional.slice(1) : positional;
      if (targets.length === 0) f.cat('file_write');
      for (const t of targets) checkWrite(t, ctx, f);
      return;
    }
    case 'tar': {
      const tarExec = args.find((a) => /^(-I.*|--use-compress-program(=.*)?|--to-command(=.*)?|--checkpoint-action(=.*)?|--info-script(=.*)?|--new-volume-script(=.*)?|-F.*|--rsh-command(=.*)?)$/.test(a));
      if (tarExec) {
        f.unparse(`tar ${tarExec} runs another program`);
        f.cat('unknown');
        return;
      }
      const modeArgs = args.filter((a, idx) => (idx === 0 || a.startsWith('-')) && !a.startsWith('--'));
      const extracting = modeArgs.some((a) => /^-?[a-zA-Z]*x/.test(a)) || args.includes('--extract') || args.includes('--get');
      const creating = modeArgs.some((a) => /^-?[a-zA-Z]*c/.test(a)) || args.includes('--create');
      if (extracting || creating) f.cat('file_write');
      else f.cat('read');
      const outIdx = args.findIndex((a) => a === '-f' || a === '--file');
      if (creating && outIdx >= 0 && args[outIdx + 1]) checkWrite(args[outIdx + 1] as string, ctx, f);
      return;
    }
    case 'dd': {
      const of = args.find((a) => a.startsWith('of='))?.slice(3);
      if (of) {
        if (of.startsWith('/dev/') && of !== '/dev/null') {
          f.destroy(`dd onto device ${of}`);
          f.cat('file_write');
        } else checkWrite(of, ctx, f);
      } else f.cat('read');
      return;
    }
    case 'kill':
    case 'pkill':
    case 'killall':
      f.cat('process');
      return;
    case 'oet-env-edit':
      f.destroy('edit of a deploy .env file (oet-env-edit)');
      f.cat('env_edit', 'file_write');
      return;
    default:
      break;
  }

  if (READ_COMMANDS.has(name)) {
    const execFlag = readCommandExecFlag(name, args);
    if (execFlag) {
      f.unparse(`${name} ${execFlag} runs another program`);
      f.cat('unknown');
      return;
    }
    f.cat('read');
    return;
  }
  f.cat('unknown');
}

/**
 * Options of otherwise read-only tools that execute an arbitrary program
 * (rg --pre, sort --compress-program, man -P, …). Returns the offending flag.
 */
const READ_COMMAND_EXEC_FLAGS: ReadonlyMap<string, RegExp> = new Map([
  ['rg', /^--pre(=|$)/],
  ['sort', /^--compress-program(=|$)/],
  ['man', /^(-P.*|--pager(=.*)?|-H.*|--html(=.*)?)$/],
]);

function readCommandExecFlag(name: string, args: readonly string[]): string | null {
  const re = READ_COMMAND_EXEC_FLAGS.get(name);
  if (!re) return null;
  return args.find((a) => re.test(a)) ?? null;
}

function analyzeInterpreter(name: string, args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  if (args.length === 0) {
    f.unparse(`interactive ${name}`);
    f.cat('unknown');
    return;
  }
  if (args.length === 1 && ['--version', '-v', '-V', 'version', '--help', '-h'].includes(args[0] as string)) {
    f.cat('read');
    return;
  }
  if (name === 'perl' && args.some((a) => /^-[a-zA-Z]*i/.test(a))) {
    const files = args.filter((a) => !a.startsWith('-')).slice(1);
    for (const file of files) {
      checkWrite(file, ctx, f);
      if (isEnvFile(file)) f.destroy(`perl -i on ${path.posix.basename(file)}`);
    }
  }
  if (args.some((a) => INLINE_CODE_FLAGS.has(a) || /^-[a-zA-Z]*[ecE]$/.test(a))) {
    f.unparse(`${name} ${args.find((a) => INLINE_CODE_FLAGS.has(a) || /^-[a-zA-Z]*[ecE]$/.test(a))} inline code`);
    f.cat('unknown');
    return;
  }
  if (args[0] === '-m') {
    if ((name === 'python3' || name === 'python') && args[1] === 'json.tool') {
      f.cat('read');
      return;
    }
    f.unparse(`${name} -m module execution`);
    f.cat('unknown');
    return;
  }
  const script = args.find((a) => !a.startsWith('-'));
  if (name === 'node' && script && ALLOWED_SCRIPTS.has(script)) {
    // Allowed by the manual, but it is agent-editable code: not a read (Read-only
    // denies it) and taint-sensitive (a tainted session asks the owner first).
    f.cat('worktree_code');
    return;
  }
  f.unparse(`${name} runs a script file`);
  f.cat('unknown');
}

/** psql short options that take a value (getopt "c:d:f:h:L:o:p:P:R:T:U:v:F:"). */
const PSQL_VALUE_SHORT = new Set(['c', 'd', 'f', 'h', 'L', 'o', 'p', 'P', 'R', 'T', 'U', 'v', 'F']);

/**
 * getopt-style expansion of bundled short options: `-Atc SQL` → `-A -t -c SQL`, `-tcSQL` →
 * `-t -c SQL`. Engines routinely bundle (`psql -Atc '…'`); without this the SQL was never seen
 * and a plain SELECT was treated as interactive psql (no taint, needless snapshot).
 */
export function expandPsqlShortOptions(args: readonly string[]): string[] {
  const out: string[] = [];
  for (const a of args) {
    if (a.length <= 2 || a.startsWith('--') || !/^-[A-Za-z]/.test(a)) {
      out.push(a);
      continue;
    }
    for (let k = 1; k < a.length; k += 1) {
      const ch = a[k] as string;
      out.push(`-${ch}`);
      if (PSQL_VALUE_SHORT.has(ch)) {
        const attached = a.slice(k + 1);
        if (attached) out.push(attached); // otherwise the value is the next argument
        break;
      }
    }
  }
  return out;
}

function analyzePsql(rawArgs: readonly string[], io: Io, ctx: ShellCtx): void {
  const f = ctx.findings;
  f.dbHint = true;
  const args = expandPsqlShortOptions(rawArgs);
  const sqls: string[] = [];
  let hasFile = false;
  let listOnly = false;
  const withValue = new Set(['-d', '--dbname', '-h', '--host', '-p', '--port', '-U', '--username', '-v', '--set', '--variable', '-P', '--pset', '-F', '--field-separator', '-R', '--record-separator', '-T', '--table-attr']);
  for (let i = 0; i < args.length; i += 1) {
    const a = args[i] as string;
    if (a === '-c' || a === '--command') {
      sqls.push(args[i + 1] ?? '');
      i += 1;
    } else if (a.startsWith('--command=')) {
      sqls.push(a.slice('--command='.length));
    } else if (/^-c.+/.test(a)) {
      sqls.push(a.slice(2));
    } else if (a === '-f' || a === '--file' || a.startsWith('--file=') || /^-f.+/.test(a)) {
      hasFile = true;
      if (a === '-f' || a === '--file') i += 1;
    } else if (a === '-o' || a === '--output' || a === '-L' || a === '--log-file') {
      if (args[i + 1]) checkWrite(args[i + 1] as string, ctx, f);
      i += 1;
    } else if (a === '-l' || a === '--list') {
      listOnly = true;
    } else if (withValue.has(a)) {
      i += 1;
    }
  }
  // SQL the Guard cannot see may also READ learner content: taint conservatively.
  if (hasFile) {
    f.unparse('psql -f (SQL from a file)');
    f.cat('db_write');
    f.taint('db_read');
    f.tablesUnknown = true;
    return;
  }
  if (sqls.length === 0) {
    if (listOnly) {
      f.cat('db_read');
      return;
    }
    f.unparse(io.pipedFrom || io.stdinFromFile ? 'psql reading SQL from stdin' : 'interactive psql');
    f.cat('db_write');
    f.taint('db_read');
    f.tablesUnknown = true;
    return;
  }
  for (const sql of sqls) {
    const trimmed = sql.trim();
    if (trimmed.startsWith('\\')) {
      analyzePsqlMeta(trimmed, ctx);
      continue;
    }
    classifySql(sql, f);
  }
}

function analyzePsqlMeta(meta: string, ctx: ShellCtx): void {
  const f = ctx.findings;
  const cmd = /^\\([A-Za-z!?+]+)/.exec(meta)?.[1] ?? '';
  if (/^(d[A-Za-z+]*|l\+?|conninfo|timing|x|\?|h|encoding|dconfig)$/.test(cmd)) {
    f.cat('db_read');
    return;
  }
  if (cmd === 'copy') {
    if (/\bprogram\b/i.test(meta)) {
      f.destroy('\\copy … PROGRAM');
      f.dbDestructive = true;
      f.tablesUnknown = true;
      f.cat('db_write');
      return;
    }
    if (/\bfrom\b/i.test(meta)) f.cat('db_write');
    else {
      f.cat('db_read');
      f.taint('db_read');
    }
    return;
  }
  f.unparse(`psql meta-command \\${cmd}`);
  f.cat('db_write');
  f.tablesUnknown = true;
}

function dockerBindSource(spec: string): string | null {
  // -v src:dst[:opts] | --mount type=bind,source=src,target=dst
  if (spec.includes('=')) {
    const src = /(?:^|,)(?:source|src)=([^,]+)/.exec(spec)?.[1];
    return src ?? null;
  }
  const src = spec.split(':')[0] ?? '';
  return src.startsWith('/') ? src : null;
}

const ALLOWED_BIND_ROOTS = ['/opt/oetwebapp', '/var/opt/oet-learner/releases'];

function analyzeDockerRun(args: readonly string[], f: Findings): void {
  for (let i = 0; i < args.length; i += 1) {
    const a = args[i] as string;
    const [flag, inline] = a.startsWith('--') ? [a.split('=')[0] as string, a.includes('=') ? a.slice(a.indexOf('=') + 1) : undefined] : [a, undefined];
    const value = (): string => inline ?? (args[i + 1] as string | undefined) ?? '';
    if (flag === '--privileged') f.destroy('docker run --privileged');
    else if (flag === '--cap-add') f.destroy('docker run --cap-add');
    else if (flag === '--device') f.destroy('docker run --device');
    else if (['--pid', '--network', '--net', '--ipc', '--userns', '--uts'].includes(flag) && value() === 'host') {
      f.destroy(`docker run ${flag}=host`);
    } else if (flag === '-v' || flag === '--volume' || flag === '--mount' || /^-v./.test(a)) {
      const spec = /^-v./.test(a) ? a.slice(2).replace(/^=/, '') : value();
      const src = dockerBindSource(spec);
      if (src !== null) {
        const normalized = path.posix.normalize(src);
        if (normalized === '/') f.destroy('docker run -v /:… (host root bind)');
        else if (normalized === '/var/run/docker.sock' || normalized === '/run/docker.sock') f.destroy('docker socket bind');
        else if (!ALLOWED_BIND_ROOTS.some((root) => isInside(normalized, root))) f.destroy(`host bind mount of ${normalized}`);
      }
    }
  }
}

function analyzeDocker(argWords: readonly Word[], io: Io, ctx: ShellCtx): void {
  const f = ctx.findings;
  const args = values(argWords);
  let i = 0;
  // Global options.
  while (i < args.length && (args[i] as string).startsWith('-')) {
    const a = args[i] as string;
    if (a === '-H' || a.startsWith('--host') || a === '-c' || a.startsWith('--context') || a.startsWith('--config')) {
      f.unparse('docker global option that bypasses the session docker config');
    }
    i += ['-H', '--host', '-c', '--context', '--config', '-l', '--log-level', '--tlscacert', '--tlscert', '--tlskey'].includes(a) ? 2 : 1;
  }
  const sub = args[i];
  if (sub === undefined) {
    f.cat('docker_read');
    return;
  }
  if (sub === 'compose') {
    analyzeCompose(args.slice(i + 1), argWords.slice(i + 1), io, ctx);
    return;
  }
  const groups = new Set(['container', 'image', 'volume', 'network', 'system', 'builder', 'buildx', 'context', 'plugin', 'secret', 'service', 'stack', 'swarm', 'node', 'trust', 'manifest', 'config']);
  const verb = groups.has(sub) ? `${sub} ${args[i + 1] ?? ''}`.trim() : sub;
  const rest = args.slice(groups.has(sub) ? i + 2 : i + 1);
  const restWords = argWords.slice(groups.has(sub) ? i + 2 : i + 1);

  if (/(^|\s)prune$/.test(verb)) {
    f.destroy(`docker ${verb}`);
    f.cat('docker_write');
    return;
  }
  switch (verb) {
    case 'volume rm':
    case 'volume remove':
      f.destroy('docker volume rm');
      f.cat('docker_write');
      return;
    case 'rm':
    case 'container rm':
    case 'container remove':
      f.destroy('docker container removal');
      f.cat('docker_write');
      return;
    case 'build':
    case 'image build':
    case 'buildx build':
    case 'builder build':
      f.forbid('docker build — images are built on GitHub Actions only');
      f.cat('compute');
      return;
    case 'run':
    case 'create':
    case 'container run':
    case 'container create':
      analyzeDockerRun(rest, f);
      f.cat('docker_write');
      return;
    case 'exec':
    case 'container exec':
      analyzeDockerExec(restWords, ctx);
      return;
    case 'logs':
    case 'container logs':
      f.cat('docker_read');
      f.taint('docker_logs');
      return;
    default:
      break;
  }
  if (DOCKER_READ_VERBS.has(verb)) {
    f.cat('docker_read');
    return;
  }
  f.cat('docker_write');
}

const DOCKER_EXEC_VALUE_OPTS = new Set(['-u', '--user', '-w', '--workdir', '-e', '--env', '--env-file', '--detach-keys']);

function analyzeDockerExec(restWords: readonly Word[], ctx: ShellCtx): void {
  const f = ctx.findings;
  f.cat('docker_write');
  let i = 0;
  while (i < restWords.length && (restWords[i] as Word).value.startsWith('-')) {
    const a = (restWords[i] as Word).value;
    if (a === '--privileged') f.destroy('docker exec --privileged');
    i += DOCKER_EXEC_VALUE_OPTS.has(a) ? 2 : 1;
  }
  const inner = restWords.slice(i + 1); // skip the container name
  if (inner.length === 0) return;
  const innerName = path.posix.basename((inner[0] as Word).value);
  if (innerName === 'psql' || innerName.startsWith('pg_')) {
    f.unparse('docker exec … psql');
    f.dbHint = true;
    f.tablesUnknown = true;
    f.cat('db_write');
    return;
  }
  const innerCtx: ShellCtx = { ...ctx, depth: ctx.depth + 1, inContainer: true, cwd: '/', worktree: null };
  analyzeCommand(inner, { pipedFrom: false, pipedTo: false, stdinFromFile: false, stdoutToFile: false }, innerCtx);
}

function analyzeCompose(args: readonly string[], argWords: readonly Word[], _io: Io, ctx: ShellCtx): void {
  const f = ctx.findings;
  let i = 0;
  const withValue = new Set(['-f', '--file', '-p', '--project-name', '--project-directory', '--env-file', '--profile', '--ansi', '--parallel', '--progress']);
  while (i < args.length && (args[i] as string).startsWith('-')) i += withValue.has(args[i] as string) ? 2 : 1;
  const sub = args[i];
  const rest = args.slice(i + 1);
  switch (sub) {
    case undefined:
    case 'ps':
    case 'ls':
    case 'config':
    case 'images':
    case 'top':
    case 'version':
    case 'port':
    case 'events':
      f.cat('docker_read');
      return;
    case 'logs':
      f.cat('docker_read');
      f.taint('docker_logs');
      return;
    case 'down':
      f.destroy(rest.some((a) => a === '-v' || a === '--volumes' || /^-[a-zA-Z]*v/.test(a)) ? 'docker compose down -v (removes volumes)' : 'docker compose down (stops and removes services)');
      f.cat('docker_write');
      return;
    case 'rm':
      f.destroy('docker compose rm');
      f.cat('docker_write');
      return;
    case 'build':
      f.forbid('docker compose build — images are built on GitHub Actions only');
      f.cat('compute');
      return;
    case 'run':
      analyzeDockerRun(rest, f);
      f.cat('docker_write');
      return;
    case 'exec': {
      const restWords = argWords.slice(i + 1);
      // compose exec [opts] SERVICE CMD…
      analyzeDockerExec(restWords, ctx);
      return;
    }
    default:
      f.cat('docker_write');
  }
}

function analyzeGit(args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  let i = 0;
  while (i < args.length && (args[i] as string).startsWith('-')) {
    const a = args[i] as string;
    if (a === '-c' || a.startsWith('--config-env') || a.startsWith('--exec-path') || a === '--upload-pack') {
      f.unparse(`git ${a} config/exec override`);
    }
    i += a === '-C' || a === '-c' || a === '--git-dir' || a === '--work-tree' || a === '--namespace' || a === '--config-env' ? 2 : 1;
  }
  const sub = args[i];
  const rest = args.slice(i + 1);
  if (sub === undefined) {
    f.cat('git_read');
    return;
  }
  if (sub === 'push') {
    analyzeGitPush(rest, f);
    return;
  }
  if (GIT_EXEC_SUBCOMMANDS.has(sub)) {
    f.unparse(`git ${sub} can run arbitrary commands`);
    f.cat('git_write');
    return;
  }
  if (sub === 'grep' && rest.some((a) => /^(-O.*|--open-files-in-pager(=.*)?)$/.test(a))) {
    f.unparse('git grep --open-files-in-pager runs another program');
    f.cat('unknown');
    return;
  }
  if (sub === 'rebase' && rest.some((a) => a === '-x' || a.startsWith('--exec'))) {
    f.unparse('git rebase --exec');
    f.cat('git_write');
    return;
  }
  if (sub === 'config') {
    const reading = rest.some((a) => ['--get', '--get-all', '--get-regexp', '--list', '-l', '--show-origin', '--get-urlmatch'].includes(a)) || rest.filter((a) => !a.startsWith('-')).length <= 1;
    const key = rest.find((a) => !a.startsWith('-'));
    if (!reading && key && GIT_CONFIG_EXEC_KEY.test(key)) {
      f.unparse(`git config ${key} can run commands`);
      f.cat('git_write');
      return;
    }
    f.cat(reading ? 'git_read' : 'git_write');
    return;
  }
  const isListing =
    (sub === 'branch' && (rest.length === 0 || rest.every((a) => ['-l', '--list', '-a', '--all', '-r', '--remotes', '-v', '-vv', '--show-current', '--contains', '--merged', '--no-merged'].includes(a) || !a.startsWith('-')) && !rest.some((a) => /^-[dDmMcC]$|^--(delete|move|copy)/.test(a)) && rest.filter((a) => !a.startsWith('-')).length === 0)) ||
    (sub === 'tag' && (rest.length === 0 || rest.includes('-l') || rest.includes('--list'))) ||
    (sub === 'remote' && (rest.length === 0 || rest[0] === '-v' || rest[0] === 'show' || rest[0] === 'get-url')) ||
    (sub === 'stash' && (rest[0] === 'list' || rest[0] === 'show')) ||
    (sub === 'worktree' && rest[0] === 'list') ||
    (sub === 'notes' && (rest[0] === 'show' || rest[0] === 'list')) ||
    (sub === 'reflog' && (rest.length === 0 || rest[0] === 'show'));
  if (GIT_READ_SUBCOMMANDS.has(sub) || isListing) {
    f.cat('git_read');
    return;
  }
  f.cat('git_write');
}

function analyzeGitPush(rest: readonly string[], f: Findings): void {
  f.cat('git_push');
  const positional: string[] = [];
  for (let i = 0; i < rest.length; i += 1) {
    const a = rest[i] as string;
    if (a === '--force' || a.startsWith('--force-with-lease') || a === '--force-if-includes') f.destroy(`git push ${a.split('=')[0]}`);
    else if (a === '--mirror') f.destroy('git push --mirror');
    else if (a === '--delete') f.destroy('git push --delete');
    else if (a === '--prune') f.destroy('git push --prune');
    else if (a === '--all') f.destroy('git push --all');
    else if (a === '--receive-pack' || a === '--exec' || a.startsWith('--receive-pack=') || a.startsWith('--exec=')) f.unparse('git push --receive-pack');
    else if (a === '-o' || a === '--push-option' || a === '--repo') i += 1;
    else if (/^-[a-zA-Z]+$/.test(a)) {
      if (a.includes('f')) f.destroy('git push -f');
      if (a.includes('d')) f.destroy('git push -d');
    } else if (!a.startsWith('-')) positional.push(a);
  }
  for (const refspec of positional.slice(1)) {
    if (refspec.startsWith('+')) f.destroy(`force refspec ${refspec}`);
    if (refspec.startsWith(':')) f.destroy(`delete refspec ${refspec}`);
    const dst = refspec.includes(':') ? refspec.slice(refspec.indexOf(':') + 1) : refspec.replace(/^\+/, '');
    if (/^(refs\/heads\/)?(main|master)$/.test(dst)) f.destroy('push to main');
  }
}

function analyzeGh(args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  let i = 0;
  while (i < args.length && (args[i] as string).startsWith('-')) i += args[i] === '-R' || args[i] === '--repo' ? 2 : 1;
  const group = args[i];
  const verb = args[i + 1] ?? '';
  const rest = args.slice(i + 2);
  if (group === undefined) {
    f.cat('gh_read');
    return;
  }
  if (group === 'api') {
    analyzeGhApi(args.slice(i + 1), f);
    return;
  }
  if (group === 'repo' && verb === 'delete') {
    f.destroy('gh repo delete');
    f.forbid('gh repo delete is never allowed from the console');
    f.cat('gh_write');
    return;
  }
  if (group === 'repo' && verb === 'edit' && rest.some((a) => a.startsWith('--visibility'))) {
    f.destroy('gh repo edit --visibility');
    f.forbid('only the Ship executor changes repository visibility');
    f.cat('gh_write');
    return;
  }
  if (group === 'pr' && verb === 'merge') {
    f.forbid('merges go through Ship only');
    f.cat('gh_write');
    return;
  }
  if ((group === 'alias' && verb !== 'list') || (group === 'extension' && verb !== 'list')) {
    f.unparse(`gh ${group} ${verb} can run arbitrary code`);
    f.cat('gh_write');
    return;
  }
  if (group === 'status') {
    f.cat('gh_read');
    f.taint('github_comments');
    return;
  }
  const readVerbs = GH_READ.get(group);
  if (readVerbs?.has(verb)) {
    f.cat('gh_read');
    if (GH_TAINTING.get(group)?.has(verb)) f.taint('github_comments');
    if (group === 'run' && verb === 'view' && rest.some((a) => a.startsWith('--log'))) f.taint('ci_logs');
    return;
  }
  f.cat('gh_write');
}

const GH_API_VALUE_OPTS = new Set(['-X', '--method', '-H', '--header', '-f', '-F', '--field', '--raw-field', '--input', '-q', '--jq', '-t', '--template', '--cache', '--hostname', '-p', '--preview']);

function analyzeGhApi(args: readonly string[], f: Findings): void {
  let method: string | null = null;
  let hasBody = false;
  let endpoint: string | null = null;
  const fields: string[] = [];
  for (let i = 0; i < args.length; i += 1) {
    const a = args[i] as string;
    if (a === '-X' || a === '--method') {
      method = (args[i + 1] ?? '').toUpperCase();
      i += 1;
    } else if (a.startsWith('--method=')) {
      method = a.slice('--method='.length).toUpperCase();
    } else if (/^-X[A-Za-z]+$/.test(a)) {
      method = a.slice(2).toUpperCase();
    } else if (a === '-f' || a === '-F' || a === '--field' || a === '--raw-field' || a === '--input') {
      hasBody = true;
      fields.push(args[i + 1] ?? '');
      i += 1;
    } else if (GH_API_VALUE_OPTS.has(a)) {
      i += 1;
    } else if (!a.startsWith('-') && endpoint === null) {
      endpoint = a;
    }
  }
  const effective = method ?? (hasBody ? 'POST' : 'GET');
  const ep = (endpoint ?? '').replace(/^\/+/, '');
  if (ep === 'graphql') {
    const query = fields.join(' ');
    if (/\bmutation\b/i.test(query)) {
      f.destroy('gh api graphql mutation');
      f.cat('gh_write');
    } else {
      f.cat('gh_read');
      f.taint('github_comments');
    }
    return;
  }
  if (effective === 'PATCH' || effective === 'PUT' || effective === 'DELETE') {
    f.destroy(`gh api -X ${effective}`);
    f.cat('gh_write');
    if (/^repos\/[^/]+\/[^/]+\/?$/.test(ep)) f.forbid('repository settings/visibility change via gh api');
    if (/\/pulls\/\d+\/merge\/?$/.test(ep)) f.forbid('merges go through Ship only');
    return;
  }
  if (effective !== 'GET' && effective !== 'HEAD') {
    f.cat('gh_write');
    if (/\/pulls\/\d+\/merge\/?$/.test(ep)) f.forbid('merges go through Ship only');
    return;
  }
  f.cat('gh_read');
  if (/(^|\/)(issues|comments|pulls|discussions|reviews|events|notifications|search)(\/|$|\?)/.test(ep)) f.taint('github_comments');
}

function analyzeRm(name: string, args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  f.cat('file_write');
  const { short, long, positional } = flagSet(args);
  const recursive = short.has('r') || short.has('R') || long.has('--recursive') || name === 'shred';
  if (positional.length === 0) {
    if (ctx.dynamicArgs && recursive) f.destroy(`${name} -r on targets read from input`);
    return;
  }
  for (const target of positional) {
    checkWrite(target, ctx, f);
    const abs = resolvePath(target, ctx);
    const volume = abs !== null && VOLUME_PATHS.some((v) => abs === v || (v !== '/' && isInside(abs, v) && abs.split('/').length <= v.split('/').length + 1));
    if (volume) f.destroy(`${name} on volume path ${abs}`);
    else if (recursive && isWorktreeRoot(target, ctx)) f.destroy(`${name} -r of the whole worktree`);
    else if (recursive && outsideWorktree(target, ctx)) f.destroy(`${name} -r outside the session worktree (${target})`);
  }
}

function analyzeFind(argWords: readonly Word[], io: Io, ctx: ShellCtx): void {
  const f = ctx.findings;
  const args = values(argWords);
  const roots: string[] = [];
  let i = 0;
  while (i < args.length && !(args[i] as string).startsWith('-') && args[i] !== '(' && args[i] !== '!') {
    roots.push(args[i] as string);
    i += 1;
  }
  let wrote = false;
  for (; i < args.length; i += 1) {
    const a = args[i] as string;
    if (a === '-delete') {
      wrote = true;
      f.cat('file_write');
      for (const root of roots.length ? roots : ['.']) {
        if (outsideWorktree(root, ctx)) f.destroy(`find ${root} -delete outside the session worktree`);
      }
    } else if (a === '-exec' || a === '-execdir' || a === '-ok' || a === '-okdir') {
      const end = args.findIndex((x, j) => j > i && (x === ';' || x === '+'));
      const inner = argWords.slice(i + 1, end < 0 ? args.length : end);
      const innerName = inner[0]?.value;
      if (innerName === 'rm' || innerName === 'shred') {
        const innerArgs = values(inner.slice(1)).filter((x) => x !== '{}');
        analyzeRm(innerName, [...innerArgs, ...(roots.length ? roots : ['.'])], ctx);
      } else {
        analyzeCommand(inner, io, { ...ctx, depth: ctx.depth + 1, dynamicArgs: true });
      }
      wrote = true;
      i = end < 0 ? args.length : end;
    } else if (a === '-fprint' || a === '-fprintf' || a === '-fls' || a === '-fprint0') {
      if (args[i + 1]) checkWrite(args[i + 1] as string, ctx, f);
      wrote = true;
      i += 1;
    }
  }
  if (!wrote) f.cat('read');
}

function analyzeXargs(argWords: readonly Word[], io: Io, ctx: ShellCtx): void {
  const args = values(argWords);
  const withValue = new Set(['-n', '-I', '-i', '-P', '-d', '-L', '-l', '-s', '-a', '-E', '-e', '--max-args', '--replace', '--max-procs', '--delimiter', '--max-lines', '--arg-file', '--eof']);
  let i = 0;
  while (i < args.length && (args[i] as string).startsWith('-')) i += withValue.has(args[i] as string) ? 2 : 1;
  const inner = argWords.slice(i);
  if (inner.length === 0) {
    ctx.findings.cat('read'); // xargs defaults to echo
    return;
  }
  analyzeCommand(inner, io, { ...ctx, depth: ctx.depth + 1, dynamicArgs: true });
}

function analyzeSed(args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  let inPlace = false;
  const scripts: string[] = [];
  const files: string[] = [];
  for (let i = 0; i < args.length; i += 1) {
    const a = args[i] as string;
    if (a === '-i' || a.startsWith('--in-place') || /^-i\S*$/.test(a) || /^-[nEsrzu]*i/.test(a)) inPlace = true;
    else if (a === '-e' || a === '--expression') {
      scripts.push(args[i + 1] ?? '');
      i += 1;
    } else if (a.startsWith('--expression=')) scripts.push(a.slice(13));
    else if (a === '-f' || a === '--file' || a.startsWith('--file=')) {
      f.unparse('sed -f (script from a file)');
      if (a === '-f' || a === '--file') i += 1;
    } else if (a.startsWith('-')) continue;
    else if (scripts.length === 0) scripts.push(a);
    else files.push(a);
  }
  const script = scripts.join('\n');
  if (/(^|[;\n{}])\s*[0-9,$]*\s*e\b/.test(script) || /s(.).*?\1.*?\1[gpiImM0-9]*e/.test(script)) {
    f.unparse('sed e command executes shell');
  }
  if (/(^|[;\n{}])\s*[0-9,$/]*\s*[wW]\s+\S/.test(script) || /s(.).*?\1.*?\1[gpiImM0-9]*w\s+\S/.test(script)) {
    f.cat('file_write');
  }
  if (!inPlace) {
    f.cat('read');
    return;
  }
  f.cat('file_write');
  for (const file of files) {
    checkWrite(file, ctx, f);
    if (isEnvFile(file)) f.destroy(`sed -i on ${path.posix.basename(file)}`);
  }
}

function analyzeAwk(args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  let program: string | null = null;
  for (let i = 0; i < args.length; i += 1) {
    const a = args[i] as string;
    if (a === '-f' || a.startsWith('--file')) {
      f.unparse('awk -f (program from a file)');
      return;
    }
    if (a === '-i' && args[i + 1] === 'inplace') {
      f.cat('file_write');
      i += 1;
      continue;
    }
    if (a === '-v' || a === '-F') {
      i += 1;
      continue;
    }
    if (a.startsWith('-')) continue;
    program = a;
    break;
  }
  if (program === null) {
    f.cat('read');
    return;
  }
  if (/\bsystem\s*\(/.test(program) || /\|\s*getline/.test(program) || /\|\s*"/.test(program) || /\|&/.test(program)) {
    f.unparse('awk runs shell commands');
    return;
  }
  if (/\bprintf?\b[^;{}]*>/.test(program)) f.cat('file_write');
  else f.cat('read');
}

function isOwnHealthUrl(url: string): boolean {
  try {
    const u = new URL(url);
    return u.protocol === 'https:' && /(^|\.)oetwithdrhesham\.co\.uk$/.test(u.hostname) && /^\/(api\/)?health(\/|$)/.test(u.pathname);
  } catch {
    return false;
  }
}

function analyzeCurl(args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  const urls: string[] = [];
  let write = false;
  const valueOpts = new Set(['-H', '--header', '-u', '--user', '-A', '--user-agent', '-e', '--referer', '-m', '--max-time', '--connect-timeout', '-w', '--write-out', '-b', '--cookie', '-c', '--cookie-jar', '-r', '--range', '--retry', '-x', '--proxy', '--noproxy', '--resolve', '--cacert', '--cert', '--key', '-E']);
  for (let i = 0; i < args.length; i += 1) {
    const a = args[i] as string;
    if (a === '-X' || a === '--request') {
      if (!['GET', 'HEAD'].includes((args[i + 1] ?? '').toUpperCase())) write = true;
      i += 1;
    } else if (/^-X[A-Za-z]+$/.test(a)) {
      if (!['GET', 'HEAD'].includes(a.slice(2).toUpperCase())) write = true;
    } else if (/^(-d|--data.*|--json|-F|--form.*|-T|--upload-file)$/.test(a.split('=')[0] as string)) {
      write = true;
      if (!a.includes('=')) i += 1;
    } else if (a === '-o' || a === '--output') {
      if (args[i + 1] && args[i + 1] !== '-' && args[i + 1] !== '/dev/null') checkWrite(args[i + 1] as string, ctx, f);
      i += 1;
    } else if (a === '-O' || a === '--remote-name' || a === '--remote-name-all') {
      f.cat('file_write');
    } else if (a === '-K' || a === '--config') {
      f.unparse('curl --config (options from a file)');
      i += 1;
    } else if (valueOpts.has(a)) {
      i += 1;
    } else if (!a.startsWith('-')) {
      urls.push(a);
    }
  }
  if (urls.some((u) => /oet-agent-(dockerproxy|console|egress|dbproxy)/.test(u) || /:2375\b/.test(u))) {
    f.unparse('raw call to an internal control service');
  }
  f.cat(write ? 'network_write' : 'network_read');
  if (urls.length === 0 || !urls.every(isOwnHealthUrl)) f.taint('web');
}

function analyzeWget(args: readonly string[], ctx: ShellCtx): void {
  const f = ctx.findings;
  const urls = args.filter((a) => !a.startsWith('-') && /^[a-z]+:\/\//i.test(a));
  const toStdout = args.some((a, i) => (a === '-O' && args[i + 1] === '-') || a === '-O-' || /^-[a-zA-Z]*O-$/.test(a) || a === '--output-document=-');
  const outIdx = args.findIndex((a) => a === '-O' || a === '--output-document');
  if (args.some((a) => a.startsWith('--post-data') || a.startsWith('--post-file') || a.startsWith('--method') || a.startsWith('--body-data'))) f.cat('network_write');
  else f.cat('network_read');
  if (!toStdout && !args.includes('--spider')) {
    const dest = outIdx >= 0 ? args[outIdx + 1] : undefined;
    if (dest) checkWrite(dest, ctx, f);
    else f.cat('file_write');
  }
  if (urls.some((u) => /oet-agent-(dockerproxy|console|egress|dbproxy)/.test(u))) f.unparse('raw call to an internal control service');
  if (urls.length === 0 || !urls.every(isOwnHealthUrl)) f.taint('web');
}

function analyzeDecoder(name: string, args: readonly string[], io: Io, ctx: ShellCtx): void {
  const f = ctx.findings;
  const decoding =
    (name === 'base64' && args.some((a) => a === '-d' || a === '--decode' || a === '-D' || /^-[a-zA-Z]*d/.test(a))) ||
    (name === 'xxd' && args.some((a) => a === '-r' || /^-[a-zA-Z]*r/.test(a))) ||
    (name === 'openssl' && args.includes('-d'));
  if (decoding && (io.pipedTo || io.stdoutToFile)) {
    f.unparse(`${name} decode piped onward (base64 pipe)`);
    f.cat('unknown');
    return;
  }
  f.cat('read');
}

// --------------------------------------------------------------- top level

function finish(f: Findings, grantKey: string, rawForHints: string): Classification {
  const unparseable = f.unparseable.length > 0;
  const destructive = f.destructive.length > 0;
  if (unparseable && /\b(psql|pg_restore|pg_dump|oet-postgres|DATABASE_URL|postgres(ql)?:\/\/)/i.test(rawForHints)) {
    f.dbHint = true;
    f.tablesUnknown = true;
  }
  const needsDbSnapshot = (f.dbDestructive || (unparseable && f.dbHint)) && (destructive || unparseable);
  const snapshotTables = needsDbSnapshot && !f.tablesUnknown && f.tables.size === 1 ? [...f.tables] : [];
  const reasons = [
    ...f.forbidden.map((r) => `forbidden: ${r}`),
    ...f.destructive.map((r) => `destructive: ${r}`),
    ...f.unparseable.map((r) => `unparseable: ${r}`),
  ];
  if (f.taintSource) reasons.push(`taint source: ${f.taintSource}`);
  if (f.categories.size === 0) f.categories.add('read');
  const classification: Classification = {
    destructive,
    unparseable,
    reasons,
    write: f.write || destructive,
    forbidden: f.forbidden.length > 0,
    categories: [...f.categories],
    needsDbSnapshot,
    snapshotTables,
    grantKey,
  };
  if (f.taintSource) classification.taintSource = f.taintSource;
  return classification;
}

export function normalizeCommand(command: string): string {
  return command.trim().replace(/\s+/g, ' ');
}

/** Classifies one shell command line. */
export function classifyCommand(command: string, context: ClassifyContext = {}): Classification {
  const f = new Findings();
  const worktree = normalizeDir(context.worktree);
  const ctx: ShellCtx = {
    worktree,
    cwd: normalizeDir(context.cwd) ?? worktree,
    findings: f,
    depth: 0,
    dynamicArgs: false,
    inContainer: false,
  };
  if (command.trim() === '') {
    f.unparse('empty command');
    return finish(f, `cmd:${normalizeCommand(command)}`, command);
  }
  const { segments, flags } = lexShell(command);
  if (flags.has('heredoc')) f.unparse('heredoc');
  if (flags.has('command_substitution')) f.unparse('command substitution');
  if (flags.has('process_substitution')) f.unparse('process substitution');
  if (flags.has('ansi_c_quoting')) f.unparse("ANSI-C quoting ($'…')");
  if (flags.has('unterminated_quote')) f.unparse('unterminated quote');
  for (const seg of segments) analyzeSegment(seg, ctx);
  return finish(f, `cmd:${normalizeCommand(command)}`, command);
}

/**
 * `["bash","-lc","<script>"]` (how Codex runs every command) → "<script>";
 * any other argv is shell-quoted and joined.
 */
export function shellCommandFromArgv(argv: readonly string[]): string {
  if (argv.length === 3 && SHELLS.has(path.posix.basename(argv[0] as string)) && /^-l?i?c$|^-c$|^-lc$|^-ic$/.test(argv[1] as string)) {
    return argv[2] as string;
  }
  return argv.map((a) => (/^[A-Za-z0-9_@%+=:,./-]+$/.test(a) ? a : `'${a.replace(/'/g, `'\\''`)}'`)).join(' ');
}

const READ_TOOLS: ReadonlySet<string> = new Set([
  'Read', 'Glob', 'Grep', 'LS', 'NotebookRead', 'TodoWrite', 'TodoRead', 'Task', 'Agent', 'ExitPlanMode', 'BashOutput',
  'KillShell', 'KillBash', 'view_image', 'update_plan', 'read_file', 'list_dir', 'grep_files',
  // Newer Claude Code bookkeeping tools: task lists, deferred-tool lookup, subagent messages.
  'ToolSearch', 'TaskCreate', 'TaskUpdate', 'TaskList', 'TaskGet', 'TaskOutput', 'TaskStop', 'SendMessage',
]);
const WEB_TOOLS: ReadonlySet<string> = new Set(['WebFetch', 'WebSearch', 'web_search', 'web_fetch']);
const WRITE_TOOLS: ReadonlySet<string> = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit', 'apply_patch', 'fileChange', 'file_change', 'write_file']);
// Monitor runs its `command` in the background: classified like Bash.
const SHELL_TOOLS: ReadonlySet<string> = new Set(['Bash', 'Monitor', 'shell', 'exec', 'commandExecution', 'command_execution', 'local_shell', 'exec_command', 'unified_exec', 'container.exec']);

function stringField(input: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const v = input[key];
    if (typeof v === 'string' && v.length > 0) return v;
  }
  return undefined;
}

/**
 * Codex network approvals carry `networkApprovalContext { host, protocol }` (surfaced as
 * `input.network`). The egress proxy stays the boundary; here the call is marked
 * network_write (taint-sensitive, so a tainted session asks the owner) and the host is
 * named on the card.
 */
function withNetworkContext(c: Classification, network: unknown): Classification {
  if (!network || typeof network !== 'object' || Array.isArray(network)) return c;
  const n = network as Record<string, unknown>;
  const host = typeof n.host === 'string' ? n.host.slice(0, 253) : 'unknown host';
  const protocol = typeof n.protocol === 'string' ? n.protocol.slice(0, 16) : 'network';
  return {
    ...c,
    write: true,
    categories: c.categories.includes('network_write') ? c.categories : [...c.categories.filter((x) => x !== 'read'), 'network_write'],
    reasons: [...c.reasons, `network access requested: ${protocol} ${host}`],
    grantKey: `${c.grantKey}|net:${host}`,
  };
}

/** Classifies a tool call surfaced by an engine (Claude PreToolUse / Codex approval request). */
export function classifyToolCall(req: ToolCallRequest, context: ClassifyContext = {}): Classification {
  const ctx: ClassifyContext = { worktree: context.worktree, cwd: req.cwd ?? context.cwd };
  const input = req.input ?? {};

  if (req.command !== undefined || SHELL_TOOLS.has(req.name)) {
    let command = req.command;
    if (command === undefined) {
      const raw = input.command ?? input.cmd;
      if (Array.isArray(raw) && raw.every((x) => typeof x === 'string')) command = shellCommandFromArgv(raw as string[]);
      else if (typeof raw === 'string') command = raw;
    }
    if (command === undefined) {
      const f = new Findings();
      f.unparse(`${req.name} without a readable command`);
      f.cat('unknown');
      return finish(f, `tool:${req.name}`, '');
    }
    return withNetworkContext(classifyCommand(command, ctx), input.network);
  }

  const f = new Findings();
  const shellCtx: ShellCtx = {
    worktree: normalizeDir(ctx.worktree),
    cwd: normalizeDir(ctx.cwd) ?? normalizeDir(ctx.worktree),
    findings: f,
    depth: 0,
    dynamicArgs: false,
    inContainer: false,
  };

  if (WRITE_TOOLS.has(req.name) || (req.writePaths && req.writePaths.length > 0)) {
    const paths = req.writePaths && req.writePaths.length > 0
      ? req.writePaths
      : [stringField(input, 'file_path', 'path', 'notebook_path')].filter((p): p is string => p !== undefined);
    if (paths.length === 0) {
      f.cat('file_write');
    }
    for (const p of paths) checkWrite(p, shellCtx, f);
    return finish(f, `write:${[...paths].sort().join('|')}`, '');
  }
  if (WEB_TOOLS.has(req.name)) {
    f.cat('network_read');
    f.taint('web');
    return finish(f, `tool:${req.name}`, '');
  }
  if (READ_TOOLS.has(req.name)) {
    f.cat('read');
    return finish(f, `tool:${req.name}`, '');
  }
  if (req.name.startsWith('mcp__')) {
    f.unparse(`MCP tool ${req.name} is not classified`);
    f.cat('unknown');
    return finish(f, `tool:${req.name}`, '');
  }
  f.unparse(`unknown tool ${req.name}`);
  f.cat('unknown');
  return finish(f, `tool:${req.name}`, '');
}

/** Pure entry point used by tests: a command string or a tool call. */
export function classify(input: string | ToolCallRequest, context: ClassifyContext = {}): Classification {
  return typeof input === 'string' ? classifyCommand(input, context) : classifyToolCall(input, context);
}

// ---------------------------------------------------------------- decision

export type GuardDecision =
  | { action: 'allow'; snapshot: boolean; autoApproved: boolean; reasons: string[] }
  | { action: 'ask'; reasons: string[] }
  | { action: 'deny'; message: string; reasons: string[] };

export interface DecideInput {
  mode: Mode;
  classification: Classification;
  /** The session/turn has read attacker-writable content. */
  tainted: boolean;
  /** approve_session grants for this session (dropped once tainted). */
  grants?: ReadonlySet<string>;
}

/**
 * Mode + taint rules:
 *  - forbidden                      → deny (all modes)
 *  - read_only                      → deny any write/destructive/unparseable
 *  - tainted & (risky | sensitive)  → ask, even in Autopilot; grants ignored
 *  - guarded & risky                → ask (unless an approve_session grant matches)
 *  - autopilot & risky              → allow after a pre-snapshot (when DB-related)
 *  - everything else                → allow
 */
export function decide(input: DecideInput): GuardDecision {
  const c = input.classification;
  const reasons = [...c.reasons];
  if (c.forbidden) {
    return { action: 'deny', message: `Blocked by the console Guard: ${reasons.join('; ')}`, reasons };
  }
  const risky = c.destructive || c.unparseable;
  if (input.mode === 'read_only') {
    if (c.write || risky) {
      return {
        action: 'deny',
        message: `Read-only session: this call would modify something (${c.categories.join(', ')}). Switch the session to Guarded to allow it.`,
        reasons,
      };
    }
    return { action: 'allow', snapshot: false, autoApproved: false, reasons };
  }
  const sensitiveCategories = c.categories.filter((category) => TAINT_SENSITIVE_CATEGORIES.has(category));
  if (input.tainted && (risky || sensitiveCategories.length > 0)) {
    // Name the category on the owner's card (e.g. worktree_code for the pre-push gate).
    const why = sensitiveCategories.length > 0 ? [`taint-sensitive: ${sensitiveCategories.join(', ')}`] : [];
    return { action: 'ask', reasons: [...reasons, ...why, 'session is tainted: untrusted content was read earlier'] };
  }
  if (!risky) return { action: 'allow', snapshot: false, autoApproved: false, reasons };
  if (input.mode === 'autopilot') {
    return { action: 'allow', snapshot: c.needsDbSnapshot, autoApproved: true, reasons };
  }
  if (input.grants?.has(c.grantKey)) {
    return { action: 'allow', snapshot: c.needsDbSnapshot, autoApproved: false, reasons: [...reasons, 'approved for this session'] };
  }
  return { action: 'ask', reasons };
}

export interface TaintState {
  tainted: boolean;
  grants: Set<string>;
}

/**
 * Applies the taint transition for an allowed call: the first attacker-writable
 * read taints the session and drops every approve_session grant.
 */
export function applyTaint(state: TaintState, classification: Classification): { changed: boolean; source?: string } {
  if (!classification.taintSource) return { changed: false };
  if (state.tainted) return { changed: false, source: classification.taintSource };
  state.tainted = true;
  state.grants.clear();
  return { changed: true, source: classification.taintSource };
}
