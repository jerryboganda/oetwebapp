/**
 * bulk-backfill.mjs
 *
 * Orchestrator that runs all admin bulk-backfill generators in dependency order.
 * Single entry point: "use the admin UI properly like a human will do" —
 * end-to-end content seeding for a fresh environment.
 *
 * Children run as separate node processes (via child_process.spawn) so each
 * generator owns its own run id, failure-log file, and exit code. Stdio is
 * inherited so all logs stream live to the operator terminal.
 *
 * Dependency order (when --domain all):
 *
 *   Phase 1   seed-rulebooks.mjs --publish
 *   Phase 2   publish-vocab.mjs
 *   Phase 3   (parallel) generate-pronunciation.mjs
 *                        generate-grammar.mjs
 *                        generate-speaking.mjs
 *   Phase 4   (parallel) generate-reading.mjs
 *                        generate-listening.mjs        (TTS-heavy)
 *   Phase 5   generate-conversation.mjs
 *   Phase 6   generate-mocks.mjs                       (depends on phase 4 papers)
 *
 * If a generator script does not exist on disk yet (peer subagents may still
 * be authoring it), the phase logs "SKIPPED (script missing): <name>" and the
 * chain continues so partial orchestration is useful immediately.
 *
 * Usage:
 *   node scripts/admin/bulk-backfill.mjs [flags]
 *
 *   --domain <name>          rulebooks | vocab | pronunciation | grammar |
 *                            speaking | reading | listening | conversation |
 *                            mocks | all   (default: all)
 *   --dry-run                Pass --dry-run through to every child.
 *   --skip-tts               Pass --skip-tts through to generate-listening.
 *   --continue-on-failure    Keep running later phases even after a child fails.
 *                            Default off — abort whole chain on first failure.
 *   --healthcheck            Run lib healthcheck and exit.
 *
 * Env vars (inherited automatically by every child):
 *   API_BASE, ADMIN_EMAIL, ADMIN_PASSWORD, AI__ApiKey, AI__BaseUrl,
 *   AI__ChatModel, AI__TtsModel, AI__TtsBaseUrl, AI__TtsVoice, etc.
 */

import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  parseFlags, startRun, endRun, healthcheck, logFailure,
} from './_lib.mjs';

const flags = parseFlags();
const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));

// Each entry is the script filename (resolved relative to scripts/admin/) and
// the per-script flag overrides this orchestrator should append on top of the
// shared pass-through flags (--dry-run).
const SCRIPTS = {
  rulebooks:     { file: 'seed-rulebooks.mjs',         extra: ['--publish'] },
  vocab:         { file: 'publish-vocab.mjs',          extra: [] },
  pronunciation: { file: 'generate-pronunciation.mjs', extra: [] },
  grammar:       { file: 'generate-grammar.mjs',       extra: [] },
  speaking:      { file: 'generate-speaking.mjs',      extra: [] },
  reading:       { file: 'generate-reading.mjs',       extra: [] },
  listening:     { file: 'generate-listening.mjs',     extra: [] },
  conversation:  { file: 'generate-conversation.mjs',  extra: [] },
  mocks:         { file: 'generate-mocks.mjs',         extra: [] },
};

// Phases for --domain all. Each phase is an array — if length > 1 the entries
// run in parallel via Promise.all; otherwise sequentially.
const PHASES = [
  ['rulebooks'],
  ['vocab'],
  ['pronunciation', 'grammar', 'speaking'],
  ['reading', 'listening'],
  ['conversation'],
  ['mocks'],
];

/**
 * Spawn one child script and resolve with { domain, code, durationMs, skipped }.
 * Never rejects — callers inspect exit code.
 */
function runChild(domain) {
  const meta = SCRIPTS[domain];
  if (!meta) {
    return Promise.resolve({ domain, code: 2, durationMs: 0, skipped: false, missing: true });
  }
  const scriptPath = resolve(SCRIPT_DIR, meta.file);
  if (!existsSync(scriptPath)) {
    console.log(`  SKIPPED (script missing): ${meta.file}`);
    return Promise.resolve({ domain, code: 0, durationMs: 0, skipped: true });
  }

  // Build args: per-script extras + global pass-throughs.
  const args = [scriptPath, ...meta.extra];
  if (flags['dry-run']) args.push('--dry-run');
  if (flags['skip-tts'] && domain === 'listening') args.push('--skip-tts');

  console.log(`  ▶ ${meta.file} ${args.slice(1).join(' ')}`);
  const start = Date.now();

  return new Promise((resolvePromise) => {
    const child = spawn(process.execPath, args, {
      stdio: 'inherit',
      env: process.env,
      cwd: process.cwd(),
    });
    child.on('error', (err) => {
      console.error(`  ✗ ${meta.file} failed to spawn:`, err.message);
      resolvePromise({ domain, code: 127, durationMs: Date.now() - start, skipped: false, spawnError: err.message });
    });
    child.on('exit', (code, signal) => {
      const durationMs = Date.now() - start;
      const finalCode = code ?? (signal ? 130 : 1);
      const status = finalCode === 0 ? '✓' : '✗';
      console.log(`  ${status} ${meta.file} exit=${finalCode} (${(durationMs / 1000).toFixed(1)}s)`);
      resolvePromise({ domain, code: finalCode, durationMs, skipped: false });
    });
  });
}

async function runPhase(phaseIndex, domains) {
  const parallel = domains.length > 1;
  console.log('───────────────────────────────────────────────────────────────────');
  console.log(`Phase ${phaseIndex + 1}: ${parallel ? 'PARALLEL' : 'sequential'} — ${domains.join(', ')}`);
  const start = Date.now();

  let results;
  if (parallel) {
    results = await Promise.all(domains.map(runChild));
  } else {
    results = [];
    for (const d of domains) results.push(await runChild(d));
  }

  const elapsed = ((Date.now() - start) / 1000).toFixed(1);
  const failures = results.filter(r => r.code !== 0);
  console.log(`Phase ${phaseIndex + 1} finished in ${elapsed}s — ${failures.length} failure(s).`);
  for (const f of failures) {
    logFailure('bulk-backfill', { phase: phaseIndex + 1, domain: f.domain }, new Error(`exit=${f.code} ${f.spawnError ?? ''}`.trim()));
  }
  return { results, failures };
}

async function main() {
  if (flags.healthcheck) {
    startRun('bulk-backfill-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  startRun('bulk-backfill');

  const domain = String(flags.domain ?? 'all').toLowerCase();
  const continueOnFailure = !!flags['continue-on-failure'];

  // Build the phase list for the requested domain.
  let phases;
  if (domain === 'all') {
    phases = PHASES;
  } else if (SCRIPTS[domain]) {
    phases = [[domain]];
  } else {
    console.error(`Unknown --domain "${domain}". Valid: ${['all', ...Object.keys(SCRIPTS)].join(', ')}`);
    endRun({ ok: false });
    process.exit(2);
  }

  console.log(`Orchestrating ${phases.length} phase(s); continue-on-failure=${continueOnFailure} dry-run=${!!flags['dry-run']}`);

  const wallStart = Date.now();
  let totalFailures = 0;
  const phaseTimings = [];

  for (let i = 0; i < phases.length; i++) {
    const phaseStart = Date.now();
    const { failures } = await runPhase(i, phases[i]);
    phaseTimings.push({ phase: i + 1, domains: phases[i].join(','), seconds: ((Date.now() - phaseStart) / 1000).toFixed(1), failures: failures.length });
    totalFailures += failures.length;

    if (failures.length > 0 && !continueOnFailure) {
      console.error(`Aborting after phase ${i + 1} due to failure(s). Pass --continue-on-failure to keep going.`);
      break;
    }
  }

  console.log('═══════════════════════════════════════════════════════════════════');
  console.log(`Wall-clock total: ${((Date.now() - wallStart) / 1000).toFixed(1)}s`);
  for (const t of phaseTimings) {
    console.log(`  phase ${t.phase} [${t.domains}]: ${t.seconds}s   failures=${t.failures}`);
  }
  endRun({ totalFailures });
  process.exit(totalFailures === 0 ? 0 : 1);
}

main().catch(e => {
  console.error('bulk-backfill fatal:', e?.stack || e);
  process.exit(2);
});
