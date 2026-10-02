#!/usr/bin/env node
/**
 * Agent working-memory ledger: validate, inspect and record state.
 *
 * Owns three artifacts (exclusive ownership — no other file has these jobs):
 *   SESSION_STATE.md   current run: objective, decisions, gates, next action
 *   TASKS.json         the execution queue
 *   VERIFICATION.md    machine-written evidence index (never hand-edit)
 * and writes raw command output to the gitignored evidence journal
 *   .github/agent-state.local.md
 *
 * Compute policy (AGENTS.md "GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE
 * ENVIRONMENT"): this script performs STATIC FILE READS and read-only `gh`
 * calls ONLY. It must never run pnpm/npm/dotnet/next/docker, never install,
 * never build and never test. `record` and `verify` are the only networked
 * commands and are never reachable from CI.
 *
 * Usage:
 *   node scripts/agent/state.mjs check [--warn] [--json]
 *   node scripts/agent/state.mjs status
 *   node scripts/agent/state.mjs next
 *   node scripts/agent/state.mjs fingerprint
 *   node scripts/agent/state.mjs init [--force] [--goal <text>] [--session <slug>]
 *   node scripts/agent/state.mjs record [--limit <n>]
 *   node scripts/agent/state.mjs verify
 *   node scripts/agent/state.mjs --self-test
 */
import { execFileSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));

export const PATHS = {
  state: resolve(root, 'SESSION_STATE.md'),
  tasks: resolve(root, 'TASKS.json'),
  verification: resolve(root, 'VERIFICATION.md'),
  journal: resolve(root, '.github/agent-state.local.md'),
  template: resolve(root, 'scripts/agent/session-state.template.md'),
};

export const HEADER_KEYS = ['Session', 'Goal', 'Mode', 'Updated', 'Branch', 'HEAD'];
export const MODES = ['plan', 'execute', 'verify', 'blocked', 'done'];
export const STATUSES = ['pending', 'in_progress', 'blocked', 'done'];

export const REQUIRED_SECTIONS = [
  'Objective',
  'Acceptance criteria',
  'Decisions (do not revisit)',
  'Touched files',
  'Verification gates',
  'Blockers',
  'Next action',
];

const BLANK_VALUES = new Set(['', '-', '--', '—', 'n/a', 'na', 'none', 'tbd', 'not set', 'null']);
const PASS_RESULTS = new Set(['pass', 'passed', 'ok', 'green', 'success', 'succeeded']);
const SEPARATOR_CELL = /^:?-{2,}:?$/;

function isBlank(value) {
  return BLANK_VALUES.has(String(value ?? '').trim().toLowerCase());
}

function norm(value) {
  return String(value ?? '').trim().replace(/[.!]+$/, '').toLowerCase();
}

// ---------------------------------------------------------------- parsing

/**
 * Key: value pairs from the block above the first H2. An inline `# comment`
 * needs two or more spaces before the `#` so a real sentence can still
 * contain a hash without being truncated.
 */
export function parseHeader(source) {
  const header = {};
  for (const raw of String(source ?? '').split(/\r?\n/)) {
    if (/^##\s/.test(raw)) break;
    const line = raw.trim();
    const match = /^([A-Za-z][A-Za-z ]*?)\s*:\s*(.+)$/.exec(line);
    if (!match) continue;
    header[match[1].trim()] = match[2].replace(/\s{2,}#.*$/, '').trim();
  }
  return header;
}

export function parseSections(source) {
  const sections = [];
  let current = null;
  for (const raw of String(source ?? '').split(/\r?\n/)) {
    const match = /^##\s+(.+?)\s*$/.exec(raw);
    if (match) {
      current = { title: match[1].trim(), lines: [] };
      sections.push(current);
      continue;
    }
    if (current) current.lines.push(raw);
  }
  return sections.map((section) => ({
    title: section.title,
    body: section.lines.join('\n').trim(),
  }));
}

export function sectionBody(source, title) {
  const wanted = norm(title);
  const found = parseSections(source).find((section) => norm(section.title) === wanted);
  return found ? found.body : '';
}

function tableCells(line) {
  const trimmed = line.trim();
  if (!trimmed.startsWith('|')) return null;
  return trimmed
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());
}

/** Data rows of a markdown table, skipping the header and the `---` separator. */
export function parseTable(body) {
  const rows = [];
  for (const line of String(body ?? '').split(/\r?\n/)) {
    const cells = tableCells(line);
    if (!cells) continue;
    if (cells.every((cell) => SEPARATOR_CELL.test(cell))) continue;
    rows.push(cells);
  }
  return rows.slice(1);
}

export function parseGateRows(source) {
  return parseTable(sectionBody(source, 'Verification gates'))
    .filter((cells) => cells.length >= 4)
    .map((cells) => ({
      gate: cells[0],
      command: cells[1],
      evidence: cells[2],
      result: cells[3],
    }));
}

export function parseTouchedRows(source) {
  return parseTable(sectionBody(source, 'Touched files')).filter(
    (cells) => cells.length >= 2 && !isBlank(cells[0]),
  );
}

export function isPassResult(result) {
  return PASS_RESULTS.has(norm(result));
}

/**
 * Where a PASS may point: a GitHub Actions run id, a workflow file, or a
 * `local:<command>` marker. Anything else is a claim with no evidence.
 */
export function isValidEvidence(evidence) {
  const value = String(evidence ?? '').trim();
  if (isBlank(value)) return false;
  if (/^(run\s+)?\d{6,}$/i.test(value)) return true;
  if (/^[A-Za-z0-9_.-]+\.ya?ml$/.test(value)) return true;
  if (/^local:[^\s]+/.test(value)) return true;
  return false;
}

export function runIdsIn(text) {
  const ids = new Set();
  for (const match of String(text ?? '').matchAll(/\b(\d{6,})\b/g)) ids.add(match[1]);
  return [...ids];
}

/**
 * Run ids cited as evidence in the "Verification gates" table. A bare workflow
 * file or a local:<command> marker carries no run id and is skipped.
 */
export function gateRunEvidence(source) {
  const cited = [];
  for (const gate of parseGateRows(source)) {
    const match = /^(?:run\s+)?(\d{6,})$/i.exec(String(gate.evidence ?? '').trim());
    if (match) cited.push({ gate: gate.gate, id: match[1], result: gate.result });
  }
  return cited;
}

/**
 * Compare the run ids cited by gates with what GitHub reports. `runs` maps a
 * run id to { status, conclusion, headSha }, or to null when GitHub could not
 * confirm the run. Errors: unconfirmed run, run not completed, PASS gate whose
 * run did not succeed. Warning: the run verified a commit that is not an
 * ancestor of HEAD, so it may not cover the current code.
 */
export function compareGateRuns(cited, runs, options = {}) {
  const isAncestor = options.isAncestor ?? (() => true);
  const errors = [];
  const warnings = [];
  for (const item of cited) {
    const run = runs.get(item.id);
    const label = `gate "${item.gate}" cites run ${item.id}`;
    if (!run) {
      errors.push(`${label} but GitHub could not confirm it (no such run, or gh unavailable)`);
      continue;
    }
    if (run.status !== 'completed') {
      errors.push(`${label} but the run is ${run.status}, not completed`);
      continue;
    }
    if (isPassResult(item.result) && String(run.conclusion) !== 'success') {
      errors.push(`${label} as PASS but GitHub says ${String(run.conclusion).toUpperCase()}`);
      continue;
    }
    if (run.headSha && !isAncestor(run.headSha)) {
      warnings.push(`${label}, which verified ${String(run.headSha).slice(0, 9)}: not an ancestor of HEAD, so it may not cover the current code`);
    }
  }
  return { errors, warnings };
}

// ------------------------------------------------------------- task queue

export function validateTasks(doc) {
  const errors = [];
  const warnings = [];

  if (!doc || typeof doc !== 'object' || Array.isArray(doc)) {
    return { errors: ['TASKS.json must be a JSON object'], warnings };
  }

  const tasks = doc.tasks;
  if (!Array.isArray(tasks)) {
    return { errors: ['TASKS.json is missing a "tasks" array'], warnings };
  }

  const byId = new Map();
  for (const [index, task] of tasks.entries()) {
    const where = `tasks[${index}]`;
    if (!task || typeof task !== 'object' || Array.isArray(task)) {
      errors.push(`${where} must be an object`);
      continue;
    }
    if (isBlank(task.id)) errors.push(`${where} is missing an id`);
    else if (byId.has(String(task.id))) errors.push(`duplicate task id "${task.id}"`);
    else byId.set(String(task.id), task);

    if (isBlank(task.title)) errors.push(`${where} (${task.id ?? '?'}) is missing a title`);
    if (!STATUSES.includes(String(task.status))) {
      errors.push(
        `${where} (${task.id ?? '?'}) has status "${task.status}" — expected one of ${STATUSES.join(', ')}`,
      );
    }
    if (task.blockedBy !== undefined && !Array.isArray(task.blockedBy)) {
      errors.push(`${where} (${task.id ?? '?'}) blockedBy must be an array`);
    }
    if (task.evidence !== undefined && !Array.isArray(task.evidence)) {
      errors.push(`${where} (${task.id ?? '?'}) evidence must be an array`);
    }
  }

  for (const task of byId.values()) {
    for (const dep of task.blockedBy ?? []) {
      if (!byId.has(String(dep))) {
        errors.push(`task "${task.id}" is blockedBy unknown task "${dep}"`);
      }
    }
  }

  // Cycle detection over blockedBy.
  const state = new Map();
  const visit = (id, trail) => {
    const colour = state.get(id);
    if (colour === 'done') return false;
    if (colour === 'open') {
      errors.push(`blockedBy cycle: ${[...trail, id].join(' -> ')}`);
      return true;
    }
    state.set(id, 'open');
    const task = byId.get(id);
    for (const dep of task?.blockedBy ?? []) {
      if (!byId.has(String(dep))) continue;
      if (visit(String(dep), [...trail, id])) {
        state.set(id, 'done');
        return true;
      }
    }
    state.set(id, 'done');
    return false;
  };
  for (const id of byId.keys()) visit(id, []);

  const inProgress = [...byId.values()].filter((task) => task.status === 'in_progress');
  if (inProgress.length > 1) {
    warnings.push(
      `${inProgress.length} tasks are in_progress (${inProgress.map((t) => t.id).join(', ')}) — keep at most one`,
    );
  }

  return { errors: dedupe(errors), warnings };
}

export function readyTasks(doc) {
  const tasks = Array.isArray(doc?.tasks) ? doc.tasks : [];
  const statusById = new Map(tasks.map((task) => [String(task.id), String(task.status)]));
  return tasks.filter(
    (task) =>
      String(task.status) === 'pending' &&
      (task.blockedBy ?? []).every((dep) => statusById.get(String(dep)) === 'done'),
  );
}

function dedupe(list) {
  return [...new Set(list)];
}

// ---------------------------------------------------------------- checking

export function checkState({ stateSource, tasksSource, git = null, now = new Date() } = {}) {
  const errors = [];
  const warnings = [];

  if (stateSource == null || !String(stateSource).trim()) {
    return { errors: ['SESSION_STATE.md is missing or empty'], warnings };
  }

  const header = parseHeader(stateSource);
  for (const key of HEADER_KEYS) {
    if (isBlank(header[key])) errors.push(`SESSION_STATE.md header is missing "${key}:"`);
  }

  if (!isBlank(header.Mode) && !MODES.includes(header.Mode)) {
    errors.push(`Mode "${header.Mode}" is invalid — expected one of ${MODES.join(', ')}`);
  }

  let updatedAt = null;
  if (!isBlank(header.Updated)) {
    updatedAt = new Date(header.Updated);
    if (Number.isNaN(updatedAt.getTime())) {
      errors.push(`Updated "${header.Updated}" is not a parseable ISO-8601 timestamp`);
      updatedAt = null;
    }
  }

  const sections = parseSections(stateSource).map((section) => norm(section.title));
  const positions = REQUIRED_SECTIONS.map((title) => sections.indexOf(norm(title)));
  REQUIRED_SECTIONS.forEach((title, index) => {
    if (positions[index] === -1) errors.push(`SESSION_STATE.md is missing the "## ${title}" section`);
  });
  const present = positions.filter((position) => position !== -1);
  if (
    present.length === REQUIRED_SECTIONS.length &&
    present.some((position, index) => index > 0 && position < present[index - 1])
  ) {
    errors.push(`SESSION_STATE.md sections are out of order — expected ${REQUIRED_SECTIONS.join(' -> ')}`);
  }

  const gates = parseGateRows(stateSource);
  for (const [index, gate] of gates.entries()) {
    const label = `Verification gates row ${index + 1} ("${gate.gate}")`;
    if (isBlank(gate.gate)) errors.push(`${label} has no gate name`);
    if (isBlank(gate.evidence)) errors.push(`${label} has no Evidence — a gate without evidence is not a gate`);
    if (isBlank(gate.result)) errors.push(`${label} has no Result`);
    if (!isBlank(gate.result) && !isBlank(gate.evidence) && isPassResult(gate.result) && !isValidEvidence(gate.evidence)) {
      errors.push(
        `${label} claims PASS with unusable evidence "${gate.evidence}" — use a run id, a workflow file, or local:<command>`,
      );
    }
  }

  const nextAction = sectionBody(stateSource, 'Next action');
  const nextItems = nextAction
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line && !/^[-*]\s*$/.test(line));
  if (!nextItems.length || /^[-*[]?\s*\]?\s*\.{0,3}$/.test(nextItems[0] ?? '')) {
    errors.push('SESSION_STATE.md "## Next action" is empty — a handoff needs one concrete next step');
  }

  if (git) {
    const declared = String(header.HEAD ?? '').trim();
    if (declared && git.head) {
      const isSelf = git.head.startsWith(declared) || declared.startsWith(git.head);
      if (!isSelf && !git.descendsFrom) {
        errors.push(
          `HEAD "${declared}" is neither the current HEAD (${git.head}) nor an ancestor of it — the ledger is from a different history`,
        );
      }
    }
  }

  let tasksDoc = null;
  if (tasksSource == null || !String(tasksSource).trim()) {
    errors.push('TASKS.json is missing or empty');
  } else {
    try {
      tasksDoc = JSON.parse(tasksSource);
      const result = validateTasks(tasksDoc);
      errors.push(...result.errors);
      warnings.push(...result.warnings);
    } catch (error) {
      errors.push(`TASKS.json is not valid JSON: ${error.message}`);
    }
  }

  if (updatedAt && !isBlank(header.Mode) && header.Mode !== 'done') {
    const ageDays = (now.getTime() - updatedAt.getTime()) / 86400000;
    if (ageDays > 7) {
      warnings.push(`ledger was last updated ${Math.floor(ageDays)} days ago while Mode=${header.Mode}`);
    }
  }

  if (parseTouchedRows(stateSource).length && !gates.length) {
    warnings.push('files were touched but no verification gates are recorded');
  }

  if (tasksDoc && header.Mode === 'done') {
    const pending = (tasksDoc.tasks ?? []).filter((task) => String(task.status) !== 'done');
    if (pending.length) {
      warnings.push(`Mode=done but ${pending.length} task(s) are not done: ${pending.map((t) => t.id).join(', ')}`);
    }
  }

  return { errors: dedupe(errors), warnings: dedupe(warnings) };
}

// ------------------------------------------------------------- git / shell

function git(args) {
  try {
    return execFileSync('git', args, {
      encoding: 'utf8',
      cwd: root,
      stdio: ['ignore', 'pipe', 'pipe'],
    }).trim();
  } catch {
    return '';
  }
}

export function gitInfo() {
  const head = git(['rev-parse', '--short', 'HEAD']);
  if (!head) return null;
  return {
    head,
    branch: git(['rev-parse', '--abbrev-ref', 'HEAD']) || '(detached)',
    headFull: git(['rev-parse', 'HEAD']),
    dirty: git(['status', '--short']).split(/\r?\n/).filter(Boolean),
  };
}

function descendsFrom(sha, head) {
  if (!sha || !head) return false;
  try {
    execFileSync('git', ['merge-base', '--is-ancestor', sha, head], {
      cwd: root,
      stdio: ['ignore', 'ignore', 'ignore'],
    });
    return true;
  } catch {
    return false;
  }
}

export function readLedger({ withGit = true } = {}) {
  const read = (path) => (existsSync(path) ? readFileSync(path, 'utf8') : null);
  let gitState = null;
  if (withGit) {
    gitState = gitInfo();
    if (gitState) gitState.descendsFrom = false;
  }
  const stateSource = read(PATHS.state);
  if (gitState && stateSource) {
    const declared = parseHeader(stateSource).HEAD;
    gitState.descendsFrom = descendsFrom(declared, gitState.head);
  }
  return {
    stateSource,
    tasksSource: read(PATHS.tasks),
    verificationSource: read(PATHS.verification),
    git: gitState,
  };
}

// ------------------------------------------------------------------- gh

function ghJson(args) {
  const raw = execFileSync('gh', args, {
    encoding: 'utf8',
    cwd: root,
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 64 * 1024 * 1024,
  });
  return JSON.parse(raw);
}

function ghText(args) {
  try {
    return execFileSync('gh', args, {
      encoding: 'utf8',
      cwd: root,
      stdio: ['ignore', 'pipe', 'pipe'],
      maxBuffer: 64 * 1024 * 1024,
    });
  } catch (error) {
    const out = `${error.stdout ?? ''}${error.stderr ?? ''}`.trim();
    return out || `gh ${args.join(' ')} failed: ${error.message}`;
  }
}

// --------------------------------------------------------- verification.md

const VERIFICATION_HEADER = [
  '# Verification Ledger',
  '',
  'Append-only. Every row is machine-written by `pnpm run ax:record` from GitHub Actions',
  '(`gh run list` / `gh run view`). Never hand-edit a Result — re-check it with',
  '`pnpm run ax:verify`. A claim with no run id is not evidence.',
  '',
  '| Date (UTC) | Claim / gate | Workflow | Run | Job(s) | Conclusion | SHA |',
  '| --- | --- | --- | --- | --- | --- | --- |',
];

export function renderVerificationRows(rows) {
  return rows.map(
    (row) =>
      `| ${row.date} | ${row.claim} | ${row.workflow} | ${row.run} | ${row.jobs} | ${row.conclusion} | ${row.sha} |`,
  );
}

/** Newest first: inserted directly under the table separator, skipping run ids already present. */
export function insertVerificationRows(existingSource, rows) {
  const source = existingSource && existingSource.trim() ? existingSource.replace(/\s+$/, '') : VERIFICATION_HEADER.join('\n');
  const present = new Set(runIdsIn(source));
  const fresh = rows.filter((row) => !present.has(String(row.run)));
  if (!fresh.length) return { source: `${source}\n`, inserted: 0 };

  const lines = source.split(/\r?\n/);
  const separatorAt = lines.findIndex((line) => {
    const cells = tableCells(line);
    return Boolean(cells && cells.length && cells.every((cell) => SEPARATOR_CELL.test(cell)));
  });

  const rendered = renderVerificationRows(fresh);
  if (separatorAt === -1) {
    const block = [...VERIFICATION_HEADER, ...rendered];
    return { source: `${[...block, ...lines].join('\n').replace(/\n{3,}/g, '\n\n')}\n`, inserted: fresh.length };
  }

  lines.splice(separatorAt + 1, 0, ...rendered);
  return { source: `${lines.join('\n')}\n`, inserted: fresh.length };
}

export function parseVerificationRows(source) {
  return parseTable(source.replace(/^#[^\n]*$/gm, ''))
    .filter((cells) => cells.length >= 7)
    .map((cells) => ({
      date: cells[0],
      claim: cells[1],
      workflow: cells[2],
      run: cells[3],
      jobs: cells[4],
      conclusion: cells[5],
      sha: cells[6],
    }));
}

// ---------------------------------------------------------------- commands

function loadForCheck() {
  const ledger = readLedger();
  return { ledger, result: checkState(ledger) };
}

function reportState(result, { prefix = '' } = {}) {
  for (const message of result.errors) console.log(`${prefix}ERROR ${message}`);
  for (const message of result.warnings) console.log(`${prefix}WARN  ${message}`);
}

function cmdCheck(argv) {
  const warnOnly = argv.includes('--warn');
  const { result } = loadForCheck();
  if (argv.includes('--json')) {
    console.log(JSON.stringify(result, null, 2));
  } else {
    reportState(result);
  }
  if (result.errors.length && !warnOnly) {
    console.error(`ax:check FAILED (${result.errors.length} error(s))`);
    return 1;
  }
  if (!result.errors.length && !result.warnings.length) console.log('ax:check OK');
  return 0;
}

function cmdStatus() {
  const ledger = readLedger();
  const result = checkState(ledger);
  const header = parseHeader(ledger.stateSource ?? '');
  const gates = parseGateRows(ledger.stateSource ?? '');
  let tasks = [];
  try {
    tasks = JSON.parse(ledger.tasksSource ?? '{}').tasks ?? [];
  } catch {
    tasks = [];
  }

  console.log('SESSION_STATE.md');
  console.log(`  session  ${header.Session ?? '(missing)'}`);
  console.log(`  goal     ${header.Goal ?? '(missing)'}`);
  console.log(`  mode     ${header.Mode ?? '(missing)'}`);
  console.log(`  updated  ${header.Updated ?? '(missing)'}`);
  console.log(
    `  git      ${ledger.git ? `${ledger.git.branch}@${ledger.git.head}${ledger.git.dirty.length ? ` (+${ledger.git.dirty.length} dirty)` : ''}` : 'unavailable'}`,
  );
  console.log(`  gates    ${gates.length} recorded, ${gates.filter((g) => isPassResult(g.result)).length} PASS`);
  console.log(
    `  tasks    ${tasks.filter((t) => t.status === 'done').length}/${tasks.length} done`,
  );

  const ready = readyTasks({ tasks });
  if (ready.length) console.log(`  ready    ${ready.map((task) => task.id).join(', ')}`);

  const blockers = sectionBody(ledger.stateSource ?? '', 'Blockers');
  if (blockers && !isBlank(blockers)) console.log(`\nBlockers\n  ${blockers.split(/\r?\n/).join('\n  ')}`);

  const next = sectionBody(ledger.stateSource ?? '', 'Next action');
  if (next) console.log(`\nNext action\n  ${next.split(/\r?\n/).join('\n  ')}`);

  if (result.errors.length || result.warnings.length) {
    console.log('');
    reportState(result);
  }
  return 0;
}

function cmdNext() {
  const { tasksSource } = readLedger({ withGit: false });
  let doc = {};
  try {
    doc = JSON.parse(tasksSource ?? '{}');
  } catch (error) {
    console.error(`ax:next: TASKS.json is not valid JSON: ${error.message}`);
    return 1;
  }
  const ready = readyTasks(doc);
  if (!ready.length) {
    const pending = (doc.tasks ?? []).filter((task) => task.status !== 'done');
    if (!pending.length) {
      console.log('ax:next: queue is empty — every task is done');
      return 0;
    }
    console.log(`ax:next: no task is ready (${pending.length} still blocked):`);
    for (const task of pending) {
      console.log(`  ${task.id} [${task.status}] ${task.title} — blockedBy ${(task.blockedBy ?? []).join(', ') || '(none)'}`);
    }
    return 0;
  }
  const task = ready[0];
  console.log(`ax:next: ${task.id} — ${task.title}`);
  if (task.verify) console.log(`  verify: ${task.verify}`);
  if ((task.files ?? []).length) console.log(`  files:  ${task.files.join(', ')}`);
  return 0;
}

function cmdFingerprint() {
  const gitState = gitInfo();
  if (!gitState) {
    console.error('ax:fingerprint: git unavailable');
    return 1;
  }
  console.log(`Session: <slug>`);
  console.log(`Goal: <one sentence — what "done" means>`);
  console.log('Mode: execute');
  console.log(`Updated: ${new Date().toISOString().replace(/\.\d{3}Z$/, 'Z')}`);
  console.log(`Branch: ${gitState.branch}`);
  console.log(`HEAD: ${gitState.head}`);
  console.log(`# dirty files: ${gitState.dirty.length}`);
  for (const file of gitState.dirty.slice(0, 40)) console.log(`#   ${file}`);
  return 0;
}

function cmdInit(argv) {
  const force = argv.includes('--force');
  const readFlag = (name) => {
    const index = argv.indexOf(`--${name}`);
    return index !== -1 && argv[index + 1] ? argv[index + 1] : '';
  };

  if (existsSync(PATHS.state) && !force) {
    console.error(`ax:init: ${PATHS.state} already exists — re-goal it by hand, or pass --force to overwrite`);
    return 1;
  }
  if (!existsSync(PATHS.template)) {
    console.error(`ax:init: template missing at ${PATHS.template}`);
    return 1;
  }

  const gitState = gitInfo();
  const goal = readFlag('goal') || '<one sentence — what "done" means>';
  const session = readFlag('session') || '<slug>';
  const stamp = new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');

  const source = readFileSync(PATHS.template, 'utf8')
    .replace(/^Session:.*$/m, `Session: ${session}`)
    .replace(/^Goal:.*$/m, `Goal: ${goal}`)
    .replace(/^Updated:.*$/m, `Updated: ${stamp}`)
    .replace(/^Branch:.*$/m, `Branch: ${gitState?.branch ?? '<branch>'}`)
    .replace(/^HEAD:.*$/m, `HEAD: ${gitState?.head ?? '<sha>'}`);

  writeFileSync(PATHS.state, source);
  console.log(`ax:init: wrote ${PATHS.state}`);
  return 0;
}

function cmdRecord(argv) {
  const limitIndex = argv.indexOf('--limit');
  const limit = limitIndex !== -1 && argv[limitIndex + 1] ? Number(argv[limitIndex + 1]) : 12;

  const gitState = gitInfo();
  if (!gitState?.headFull) {
    console.error('ax:record: cannot resolve HEAD');
    return 1;
  }

  let runs;
  try {
    runs = ghJson([
      'run', 'list',
      '--commit', gitState.headFull,
      '--limit', String(limit),
      '--json', 'databaseId,workflowName,status,conclusion,headSha,createdAt,url',
    ]);
  } catch (error) {
    console.error(`ax:record: gh run list failed — ${error.message}`);
    console.error('ax:record: this command needs an authenticated gh CLI; it is never run from CI.');
    return 1;
  }

  if (!Array.isArray(runs) || !runs.length) {
    console.log(`ax:record: no Actions runs found for ${gitState.head}`);
    return 0;
  }

  const rows = [];
  const journal = [];
  for (const run of runs) {
    if (run.status !== 'completed') continue;
    let jobs = '';
    try {
      const view = ghJson(['run', 'view', String(run.databaseId), '--json', 'jobs']);
      jobs = (view.jobs ?? [])
        .map((job) => `${job.name}=${job.conclusion ?? job.status}`)
        .join(' ');
    } catch {
      jobs = '(jobs unavailable)';
    }
    rows.push({
      date: String(run.createdAt ?? '').slice(0, 16).replace('T', ' '),
      claim: run.workflowName ?? '(workflow)',
      workflow: run.workflowName ?? '',
      run: String(run.databaseId),
      jobs: jobs.slice(0, 400) || '(none)',
      conclusion: String(run.conclusion ?? '').toUpperCase(),
      sha: gitState.head,
    });

    journal.push(`### run ${run.databaseId} — ${run.workflowName} — ${String(run.conclusion).toUpperCase()}`);
    journal.push(`url: ${run.url}`);
    if (run.conclusion !== 'success') {
      journal.push('');
      journal.push('```text');
      journal.push(ghText(['run', 'view', String(run.databaseId), '--log-failed']).slice(0, 200000));
      journal.push('```');
    }
    journal.push('');
  }

  if (!rows.length) {
    console.log(`ax:record: ${runs.length} run(s) for ${gitState.head} but none completed yet`);
    return 0;
  }

  const existing = existsSync(PATHS.verification) ? readFileSync(PATHS.verification, 'utf8') : null;
  const { source, inserted } = insertVerificationRows(existing, rows);
  writeFileSync(PATHS.verification, source);

  mkdirSync(dirname(PATHS.journal), { recursive: true });
  appendFileSync(
    PATHS.journal,
    [`\n## ax:record ${new Date().toISOString()} — ${gitState.branch}@${gitState.head}`, '', ...journal].join('\n'),
  );

  console.log(`ax:record: ${inserted} new row(s) in VERIFICATION.md (${rows.length} completed run(s) seen)`);
  for (const row of rows) {
    console.log(`  ${row.conclusion.padEnd(8)} ${row.run} ${row.workflow}`);
  }
  console.log(`ax:record: raw logs appended to .github/agent-state.local.md`);
  return 0;
}

function cmdVerify() {
  const ledger = readLedger();
  const rows = ledger.verificationSource
    ? parseVerificationRows(ledger.verificationSource).filter((row) => runIdsIn(row.run).length)
    : [];
  // Run ids typed into a SESSION_STATE.md gate row are claims too: confirm each one.
  const cited = gateRunEvidence(ledger.stateSource ?? '');
  if (!rows.length && !cited.length) {
    console.log('ax:verify: no recorded run ids to verify');
    return 0;
  }

  let failures = 0;
  const runs = new Map();
  for (const id of new Set(cited.map((item) => item.id))) {
    try {
      runs.set(id, ghJson(['run', 'view', id, '--json', 'status,conclusion,headSha,workflowName']));
    } catch {
      runs.set(id, null);
    }
  }
  const compared = compareGateRuns(cited, runs, {
    isAncestor: (sha) => descendsFrom(sha, ledger.git?.head),
  });
  for (const message of compared.errors) {
    console.log(`ax:verify: ERROR ${message}`);
    failures += 1;
  }
  for (const message of compared.warnings) console.log(`ax:verify: WARN  ${message}`);
  if (cited.length && !compared.errors.length) {
    console.log(`ax:verify: ${cited.length} run id(s) cited by gates exist on GitHub`);
  }

  for (const row of rows) {
    const id = runIdsIn(row.run)[0];
    try {
      const view = ghJson(['run', 'view', id, '--json', 'status,conclusion,headSha,workflowName']);
      const actual = String(view.conclusion ?? view.status ?? '').toUpperCase();
      const claimed = String(row.conclusion ?? '').toUpperCase();
      const matches = actual === claimed || (PASS_RESULTS.has(norm(actual)) && PASS_RESULTS.has(norm(claimed)));
      if (!matches) {
        console.log(`ax:verify: MISMATCH run ${id} — ledger says ${claimed}, GitHub says ${actual}`);
        failures += 1;
      } else {
        console.log(`ax:verify: OK run ${id} ${actual}`);
      }
    } catch (error) {
      console.log(`ax:verify: UNVERIFIABLE run ${id} — ${error.message}`);
      failures += 1;
    }
  }

  if (failures) {
    console.error(`ax:verify FAILED (${failures} row(s) do not match GitHub)`);
    return 1;
  }
  console.log(`ax:verify OK (${rows.length} ledger row(s) and ${cited.length} gate run id(s) match GitHub)`);
  return 0;
}

// ---------------------------------------------------------------- self-test

function fixtureState({ header = {}, sections = {}, omit = [], order = REQUIRED_SECTIONS } = {}) {
  const defaults = {
    Objective: 'Make agent working memory explicit and machine-checked.',
    'Acceptance criteria': '- [ ] AC-1 ax:check passes',
    'Decisions (do not revisit)': '- D-1 tracked SESSION_STATE.md',
    'Touched files': '| Path | Change |\n| --- | --- |\n| SESSION_STATE.md | new |',
    'Verification gates':
      '| Gate | Command / workflow | Evidence | Result |\n| --- | --- | --- | --- |\n| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |',
    Blockers: '- None.',
    'Next action': '1. Run pnpm run ax:check',
  };
  const merged = { ...defaults, ...sections };
  const meta = {
    Session: 'ax-test',
    Goal: 'test the checker',
    Mode: 'execute',
    Updated: '2026-10-01T00:00:00Z',
    Branch: 'main',
    HEAD: 'abc1234',
    ...header,
  };
  const head = ['# SESSION STATE', '', ...HEADER_KEYS.map((key) => `${key}: ${meta[key]}`)].join('\n');
  const body = order
    .filter((title) => !omit.includes(title))
    .map((title) => `## ${title}\n${merged[title] ?? ''}`)
    .join('\n\n');
  return `${head}\n\n${body}\n`;
}

function fixtureTasks(tasks) {
  return JSON.stringify({ version: 1, updated: '2026-10-01', tasks });
}

export function selfTest() {
  const failures = [];
  const expect = (name, condition, detail = '') => {
    if (!condition) failures.push(`${name}${detail ? `: ${detail}` : ''}`);
  };

  const okState = fixtureState();
  const okTasks = fixtureTasks([
    { id: 'T-1', title: 'first', status: 'done', blockedBy: [] },
    { id: 'T-2', title: 'second', status: 'pending', blockedBy: ['T-1'] },
  ]);

  const valid = checkState({ stateSource: okState, tasksSource: okTasks, now: new Date('2026-10-02T00:00:00Z') });
  expect('valid ledger passes', valid.errors.length === 0, JSON.stringify(valid.errors));

  const missing = checkState({
    stateSource: fixtureState({ omit: ['Blockers'] }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('missing section fails', missing.errors.some((e) => e.includes('Blockers')));

  const badMode = checkState({
    stateSource: fixtureState({ header: { Mode: 'autopilot' } }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('invalid Mode fails', badMode.errors.some((e) => e.includes('Mode')));

  const badTimestamp = checkState({
    stateSource: fixtureState({ header: { Updated: 'yesterday' } }),
    tasksSource: okTasks,
  });
  expect('unparseable Updated fails', badTimestamp.errors.some((e) => e.includes('ISO-8601')));

  const noEvidence = checkState({
    stateSource: fixtureState({
      sections: {
        'Verification gates':
          '| Gate | Command / workflow | Evidence | Result |\n| --- | --- | --- | --- |\n| deploy | deploy.yml | - | PASS |',
      },
    }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('PASS with blank evidence fails', noEvidence.errors.some((e) => e.includes('no Evidence')));

  const weakEvidence = checkState({
    stateSource: fixtureState({
      sections: {
        'Verification gates':
          '| Gate | Command / workflow | Evidence | Result |\n| --- | --- | --- | --- |\n| deploy | deploy.yml | I think so | PASS |',
      },
    }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('PASS with prose evidence fails', weakEvidence.errors.some((e) => e.includes('unusable evidence')));

  const honestNotRun = checkState({
    stateSource: fixtureState({
      sections: {
        'Verification gates':
          '| Gate | Command / workflow | Evidence | Result |\n| --- | --- | --- | --- |\n| e2e | qa-smoke.yml | NOT RUN | NOT RUN |',
      },
    }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('NOT RUN row is legal', honestNotRun.errors.length === 0, JSON.stringify(honestNotRun.errors));

  const runIdEvidence = checkState({
    stateSource: fixtureState({
      sections: {
        'Verification gates':
          '| Gate | Command / workflow | Evidence | Result |\n| --- | --- | --- | --- |\n| deploy | deploy.yml | run 36824151971 | PASS |',
      },
    }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('run id evidence passes', runIdEvidence.errors.length === 0, JSON.stringify(runIdEvidence.errors));

  const outOfOrder = checkState({
    stateSource: fixtureState({
      order: ['Objective', 'Verification gates', 'Touched files', 'Acceptance criteria', 'Decisions (do not revisit)', 'Blockers', 'Next action'],
    }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('out-of-order sections fail', outOfOrder.errors.some((e) => e.includes('out of order')));

  const emptyNext = checkState({
    stateSource: fixtureState({ sections: { 'Next action': '' } }),
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('empty Next action fails', emptyNext.errors.some((e) => e.includes('Next action')));

  const badJson = checkState({ stateSource: okState, tasksSource: '{ nope' });
  expect('unparseable TASKS.json fails', badJson.errors.some((e) => e.includes('not valid JSON')));

  const cycle = checkState({
    stateSource: okState,
    tasksSource: fixtureTasks([
      { id: 'T-1', title: 'a', status: 'pending', blockedBy: ['T-2'] },
      { id: 'T-2', title: 'b', status: 'pending', blockedBy: ['T-1'] },
    ]),
  });
  expect('blockedBy cycle fails', cycle.errors.some((e) => e.includes('cycle')));

  const unknownDep = checkState({
    stateSource: okState,
    tasksSource: fixtureTasks([{ id: 'T-1', title: 'a', status: 'pending', blockedBy: ['T-9'] }]),
  });
  expect('unknown blockedBy fails', unknownDep.errors.some((e) => e.includes('unknown task')));

  const badStatus = checkState({
    stateSource: okState,
    tasksSource: fixtureTasks([{ id: 'T-1', title: 'a', status: 'nearly' }]),
  });
  expect('invalid task status fails', badStatus.errors.some((e) => e.includes('status')));

  const twoLive = checkState({
    stateSource: okState,
    tasksSource: fixtureTasks([
      { id: 'T-1', title: 'a', status: 'in_progress' },
      { id: 'T-2', title: 'b', status: 'in_progress' },
    ]),
  });
  expect('two in_progress warns', twoLive.warnings.some((w) => w.includes('in_progress')));

  const stale = checkState({ stateSource: okState, tasksSource: okTasks, now: new Date('2026-12-01T00:00:00Z') });
  expect('stale ledger warns', stale.warnings.some((w) => w.includes('days ago')));

  const doneWithPending = checkState({
    stateSource: fixtureState({ header: { Mode: 'done' } }),
    tasksSource: fixtureTasks([{ id: 'T-1', title: 'a', status: 'pending' }]),
    now: new Date('2026-10-02T00:00:00Z'),
  });
  expect('Mode=done with pending tasks warns', doneWithPending.warnings.some((w) => w.includes('not done')));

  const foreignHead = checkState({
    stateSource: okState,
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
    git: { head: 'ffffff0', descendsFrom: false },
  });
  expect('foreign HEAD fails', foreignHead.errors.some((e) => e.includes('different history')));

  const ancestorHead = checkState({
    stateSource: okState,
    tasksSource: okTasks,
    now: new Date('2026-10-02T00:00:00Z'),
    git: { head: 'ffffff0', descendsFrom: true },
  });
  expect('ancestor HEAD passes', ancestorHead.errors.length === 0, JSON.stringify(ancestorHead.errors));

  const missingState = checkState({ stateSource: null, tasksSource: okTasks });
  expect('missing SESSION_STATE fails', missingState.errors.some((e) => e.includes('SESSION_STATE.md')));

  const queue = readyTasks(
    JSON.parse(
      fixtureTasks([
        { id: 'T-1', title: 'a', status: 'done' },
        { id: 'T-2', title: 'b', status: 'pending', blockedBy: ['T-1'] },
        { id: 'T-3', title: 'c', status: 'pending', blockedBy: ['T-4'] },
        { id: 'T-4', title: 'd', status: 'pending' },
      ]),
    ),
  );
  expect('readyTasks respects deps', queue.map((t) => t.id).join(',') === 'T-2,T-4', queue.map((t) => t.id).join(','));

  const inserted = insertVerificationRows(null, [
    { date: '2026-10-01 12:00', claim: 'deploy', workflow: 'deploy.yml', run: '111111', jobs: 'a=success', conclusion: 'SUCCESS', sha: 'abc' },
  ]);
  expect('insert creates the table', inserted.inserted === 1 && inserted.source.includes('| 111111 |'));

  const deduped = insertVerificationRows(inserted.source, [
    { date: '2026-10-01 12:00', claim: 'deploy', workflow: 'deploy.yml', run: '111111', jobs: 'a=success', conclusion: 'SUCCESS', sha: 'abc' },
  ]);
  expect('insert dedupes run ids', deduped.inserted === 0);

  const newestFirst = insertVerificationRows(inserted.source, [
    { date: '2026-10-01 13:00', claim: 'deploy', workflow: 'deploy.yml', run: '222222', jobs: 'a=success', conclusion: 'SUCCESS', sha: 'abc' },
  ]);
  expect(
    'insert keeps newest first',
    newestFirst.source.indexOf('| 222222 |') < newestFirst.source.indexOf('| 111111 |'),
  );

  expect('evidence validator accepts run ids', isValidEvidence('36824151971'));
  expect('evidence validator accepts workflow files', isValidEvidence('deploy.yml'));
  expect('evidence validator accepts local markers', isValidEvidence('local:ship:gate'));
  expect('evidence validator rejects prose', !isValidEvidence('should be fine'));

  const gatesTable = [
    '| Gate | Command / workflow | Evidence | Result |',
    '| --- | --- | --- | --- |',
    '| deploy | deploy.yml | run 36824151971 | PASS |',
    '| lint | qa-smoke.yml | 36824151972 | PASS |',
    '| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |',
    '| e2e | qa-smoke.yml | NOT RUN | NOT RUN |',
  ].join('\n');
  const cited = gateRunEvidence(fixtureState({ sections: { 'Verification gates': gatesTable } }));
  expect(
    'gate run evidence extracts only run ids',
    cited.map((item) => item.id).join(',') === '36824151971,36824151972',
    JSON.stringify(cited),
  );

  const goodRun = { status: 'completed', conclusion: 'success', headSha: 'abc1234def' };
  const runMap = (entries) => new Map(Object.entries(entries));
  const confirmed = compareGateRuns(cited, runMap({ 36824151971: goodRun, 36824151972: goodRun }));
  expect('cited runs that succeeded pass', confirmed.errors.length === 0 && confirmed.warnings.length === 0, JSON.stringify(confirmed));

  const invented = compareGateRuns(cited, runMap({ 36824151971: goodRun, 36824151972: null }));
  expect('an invented run id fails', invented.errors.some((e) => e.includes('could not confirm')), JSON.stringify(invented));

  const failedRun = compareGateRuns(
    cited,
    runMap({ 36824151971: { ...goodRun, conclusion: 'failure' }, 36824151972: goodRun }),
  );
  expect('a PASS gate on a failed run fails', failedRun.errors.some((e) => e.includes('FAILURE')), JSON.stringify(failedRun));

  const pendingRun = compareGateRuns(
    cited,
    runMap({ 36824151971: { ...goodRun, status: 'in_progress', conclusion: null }, 36824151972: goodRun }),
  );
  expect('an in-progress run fails', pendingRun.errors.some((e) => e.includes('not completed')), JSON.stringify(pendingRun));

  const foreign = compareGateRuns(cited, runMap({ 36824151971: goodRun, 36824151972: goodRun }), {
    isAncestor: () => false,
  });
  expect('a run on a foreign commit warns', foreign.errors.length === 0 && foreign.warnings.length === 2, JSON.stringify(foreign));

  return { ok: failures.length === 0, failures };
}

// ------------------------------------------------------------------- main

export function main(argv) {
  const [command] = argv;

  if (argv.includes('--self-test') || command === 'self-test') {
    const result = selfTest();
    if (!result.ok) {
      console.error('ax self-test FAILED');
      for (const line of result.failures) console.error(`  ${line}`);
      return 1;
    }
    console.log('ax self-test OK');
    return 0;
  }

  switch (command) {
    case 'check':
      return cmdCheck(argv);
    case 'status':
      return cmdStatus();
    case 'next':
      return cmdNext();
    case 'fingerprint':
      return cmdFingerprint();
    case 'init':
      return cmdInit(argv);
    case 'record':
      return cmdRecord(argv);
    case 'verify':
      return cmdVerify();
    default:
      console.error(`ax: unknown command "${command ?? ''}"`);
      console.error('usage: state.mjs <check|status|next|fingerprint|init|record|verify|--self-test>');
      return 2;
  }
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) {
  process.exitCode = main(process.argv.slice(2));
}
