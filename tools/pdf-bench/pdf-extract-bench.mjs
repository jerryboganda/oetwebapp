#!/usr/bin/env node
// MANUAL TOOL, INERT: run manually by the owner from a machine the owner provisions; no CI runs this;
// agents never run it (AGENTS.md "NO AUTOMATED QA ANYWHERE"; benchmarks stay separate labelled tools).
//
// PDF extraction benchmark harness. Build the kernel first, then run this against it:
//   dotnet build tools/pdf-bench/PdfExtractBench/PdfExtractBench.csproj -c Release -o bench-out
//   node tools/pdf-bench/pdf-extract-bench.mjs --bench-dll bench-out/PdfExtractBench.dll \
//        --out pdf-bench.json --md pdf-bench.md [--steady] [--iterations N] [--warmup W] [--max-pdfs N]
//
// LABEL: this is BENCHMARK EVIDENCE for sizing and for the Rust decision. It is not a release proof and
// it proves nothing about a deployed build.
//
// Contenders (each is a command that takes a PDF path and prints ONE JSON line on stdout):
//   oracle  the API's own PdfPigPdfTextExtractor, in-process on the primary today. ALWAYS present; it is
//           the parity oracle (whatever it extracts is correct by definition) and the baseline.
//   kernel  the remote-worker agent's extraction kernel (optional; pass --kernel "<cmd> {pdf}").
//   rust    a Rust candidate (optional; pass --rust "<cmd> {pdf}"). Owner decision D6: Rust is NOT built;
//           this slot exists so a candidate can be judged by the gate below if one ever appears.
// Output contract of a contender (pdf.extract result envelope, hashes only, never text):
//   { pageCount, embeddedChars, textSha256, pageSha256s:[64-hex...], stats?:{durationMs,cpuMs,peakRssMiB} }
//
// Rust gate (decided once, here): a candidate is worth building only if parity is byte-exact on EVERY
// document AND (p95 extraction time is at least 30 % lower OR CPU per job is at least 40 % lower than
// the .NET baseline it would replace: the kernel when supplied, else the oracle).
//
// The harness itself does no extraction: it drives the contenders and compares their hashes.

import { spawnSync } from 'node:child_process';
import { statSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export const GATE = Object.freeze({ p95Improvement: 0.30, cpuImprovement: 0.40 });
export const RESULT_SCHEMA = 'oet-pdf-bench/1';
const HEX64 = /^[0-9a-f]{64}$/;

// ---- small pure helpers -------------------------------------------------------------------------------

/** Nearest-rank percentile of a numeric list (p in 0..100). null for an empty list. */
export function percentile(values, p) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const rank = Math.max(1, Math.ceil((p / 100) * sorted.length));
  return sorted[Math.min(sorted.length, rank) - 1];
}

export const mean = (values) => (values.length ? values.reduce((a, b) => a + b, 0) / values.length : null);

/** Split a command template into argv (double quotes group; `{pdf}` is substituted AFTER splitting). */
export function splitCommand(template) {
  const out = [];
  let current = '';
  let quoted = false;
  let started = false;
  for (const char of String(template)) {
    if (char === '"') { quoted = !quoted; started = true; continue; }
    if (!quoted && /\s/.test(char)) {
      if (started) { out.push(current); current = ''; started = false; }
      continue;
    }
    current += char;
    started = true;
  }
  if (quoted) throw new Error(`unbalanced quote in command: ${template}`);
  if (started) out.push(current);
  return out;
}

export function commandFor(template, pdf) {
  const argv = splitCommand(template);
  if (!argv.some((part) => part.includes('{pdf}'))) throw new Error(`command must contain {pdf}: ${template}`);
  return argv.map((part) => part.split('{pdf}').join(pdf));
}

/** Parse and validate the last JSON line a contender printed. Throws a precise error otherwise. */
export function parseResult(stdout, label) {
  const lines = String(stdout).split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  if (!lines.length) throw new Error(`${label}: no output`);
  let value;
  try {
    value = JSON.parse(lines[lines.length - 1]);
  } catch (error) {
    throw new Error(`${label}: last line is not JSON`);
  }
  if (!Number.isInteger(value.pageCount) || value.pageCount < 0) throw new Error(`${label}: pageCount missing`);
  if (!Number.isInteger(value.embeddedChars) || value.embeddedChars < 0) throw new Error(`${label}: embeddedChars missing`);
  if (typeof value.textSha256 !== 'string' || !HEX64.test(value.textSha256)) throw new Error(`${label}: textSha256 is not a sha-256`);
  if (!Array.isArray(value.pageSha256s) || value.pageSha256s.length !== value.pageCount || !value.pageSha256s.every((h) => HEX64.test(h))) {
    throw new Error(`${label}: pageSha256s must hold one sha-256 per page`);
  }
  return value;
}

/** Byte-exactness: same page count, same characters, same text hash, same hash for every page. */
export function compareParity(oracle, other) {
  if (oracle.pageCount !== other.pageCount) return { ok: false, reason: `pageCount ${other.pageCount} != ${oracle.pageCount}` };
  if (oracle.embeddedChars !== other.embeddedChars) return { ok: false, reason: `embeddedChars ${other.embeddedChars} != ${oracle.embeddedChars}` };
  for (let i = 0; i < oracle.pageSha256s.length; i += 1) {
    if (oracle.pageSha256s[i] !== other.pageSha256s[i]) return { ok: false, reason: `page ${i + 1} differs` };
  }
  if (oracle.textSha256 !== other.textSha256) return { ok: false, reason: 'textSha256 differs' };
  return { ok: true, reason: null };
}

// ---- measurement --------------------------------------------------------------------------------------

/**
 * Run one contender over every PDF. `exec(argv)` returns { stdout, status, wallMs } (injectable).
 * Per PDF: `warmup` throw-away runs, then `iterations` measured runs. Returns per-document records.
 */
export function measureContender({ name, template, pdfs, iterations, warmup, exec }) {
  const documents = [];
  for (const pdf of pdfs) {
    const argv = commandFor(template, pdf.path);
    const walls = [];
    const durations = [];
    const cpus = [];
    const rss = [];
    let result = null;
    let error = null;
    for (let i = 0; i < warmup + iterations && error === null; i += 1) {
      const run = exec(argv);
      if (run.status !== 0) { error = `${name}: exit ${run.status} for ${pdf.path}`; break; }
      try {
        result = parseResult(run.stdout, `${name} ${pdf.path}`);
      } catch (parseError) {
        error = parseError.message;
        break;
      }
      if (i < warmup) continue;
      walls.push(run.wallMs);
      if (typeof result.stats?.durationMs === 'number') durations.push(result.stats.durationMs);
      if (typeof result.stats?.cpuMs === 'number') cpus.push(result.stats.cpuMs);
      if (typeof result.stats?.peakRssMiB === 'number') rss.push(result.stats.peakRssMiB);
    }
    documents.push({
      path: pdf.path, bytes: pdf.bytes, error,
      pageCount: result?.pageCount ?? null,
      result,
      processWallMs: percentile(walls, 50),
      durationMs: percentile(durations.length ? durations : walls, 50),
      cpuMs: percentile(cpus, 50),
      peakRssMiB: rss.length ? Math.max(...rss) : null,
    });
  }
  return { name, documents };
}

/** Corpus-level numbers of one contender: p50 / p95 over documents of the per-document median. */
export function summarizeContender(measured) {
  const ok = measured.documents.filter((d) => d.error === null);
  const durations = ok.map((d) => d.durationMs).filter((v) => v !== null);
  const cpus = ok.map((d) => d.cpuMs).filter((v) => v !== null);
  const walls = ok.map((d) => d.processWallMs).filter((v) => v !== null);
  return {
    name: measured.name,
    documents: measured.documents.length,
    failed: measured.documents.length - ok.length,
    durationMs: { p50: percentile(durations, 50), p95: percentile(durations, 95), total: durations.reduce((a, b) => a + b, 0) },
    processWallMs: { p50: percentile(walls, 50), p95: percentile(walls, 95) },
    cpuMsPerJob: mean(cpus),
    peakRssMiB: ok.reduce((m, d) => Math.max(m, d.peakRssMiB ?? 0), 0) || null,
    pdfBytes: measured.documents.reduce((a, d) => a + (d.bytes ?? 0), 0),
  };
}

/** Parity of one contender against the oracle, document by document. */
export function checkParity(oracleMeasured, otherMeasured) {
  const byPath = new Map(oracleMeasured.documents.map((d) => [d.path, d]));
  const mismatches = [];
  let checked = 0;
  for (const doc of otherMeasured.documents) {
    const reference = byPath.get(doc.path);
    if (!reference || reference.result === null) continue;
    if (doc.result === null) {
      mismatches.push({ path: doc.path, reason: doc.error ?? 'no result' });
      checked += 1;
      continue;
    }
    checked += 1;
    const verdict = compareParity(reference.result, doc.result);
    if (!verdict.ok) mismatches.push({ path: doc.path, reason: verdict.reason });
  }
  return { checked, mismatches, ok: mismatches.length === 0 && checked > 0 };
}

/**
 * The Rust gate. `baseline` is the .NET contender the candidate would replace (kernel when supplied,
 * else the oracle); `candidate` is the Rust summary or null; `parity` its parity record.
 */
export function evaluateRustGate({ baseline, candidate, parity }) {
  if (!candidate) {
    return {
      evaluated: false,
      pass: false,
      decision: 'NOT BUILT (owner decision D6). No Rust candidate was supplied: this run records the .NET baseline a candidate would have to beat.',
      thresholds: { p95Improvement: GATE.p95Improvement, cpuImprovement: GATE.cpuImprovement },
    };
  }
  const reasons = [];
  if (!parity || !parity.ok) reasons.push(`parity is not byte-exact (${parity?.mismatches?.length ?? 'no'} document(s) differ)`);
  const p95Base = baseline.durationMs.p95;
  const p95Cand = candidate.durationMs.p95;
  const cpuBase = baseline.cpuMsPerJob;
  const cpuCand = candidate.cpuMsPerJob;
  const p95Gain = p95Base && p95Cand !== null ? 1 - p95Cand / p95Base : null;
  const cpuGain = cpuBase && cpuCand !== null && cpuBase !== null ? 1 - cpuCand / cpuBase : null;
  const p95Pass = p95Gain !== null && p95Gain >= GATE.p95Improvement;
  const cpuPass = cpuGain !== null && cpuGain >= GATE.cpuImprovement;
  if (!p95Pass && !cpuPass) {
    reasons.push(`neither threshold met: p95 improvement ${fmtPct(p95Gain)} (< ${Math.round(GATE.p95Improvement * 100)} %), CPU/job improvement ${fmtPct(cpuGain)} (< ${Math.round(GATE.cpuImprovement * 100)} %)`);
  }
  return {
    evaluated: true,
    pass: reasons.length === 0,
    decision: reasons.length === 0 ? 'BUILD IS JUSTIFIED: parity is exact and a threshold is met.' : `DO NOT BUILD: ${reasons.join('; ')}`,
    p95Gain,
    cpuGain,
    thresholds: { p95Improvement: GATE.p95Improvement, cpuImprovement: GATE.cpuImprovement },
  };
}

const fmtPct = (v) => (v === null ? 'n/a' : `${(v * 100).toFixed(1)} %`);
const fmtMs = (v) => (v === null || v === undefined ? 'n/a' : `${v.toFixed(v < 10 ? 2 : 0)} ms`);

export function renderMarkdown(report) {
  const lines = [];
  lines.push('# PDF extraction benchmark');
  lines.push('');
  lines.push('> **Benchmark evidence, not a release proof.** Timings come from whatever machine the owner ran it on and are noisy; read the shape, not the third digit. The in-process oracle is the parity reference: whatever it extracts is correct by definition.');
  lines.push('');
  lines.push(`Corpus: ${report.corpus.documents} tracked PDFs, ${report.corpus.bytes} bytes. Runs per document: ${report.iterations} measured after ${report.warmup} warm-up. Runner: ${report.env.os}, ${report.env.cpus} vCPU.`);
  lines.push('');
  lines.push('| contender | docs | failed | p50 extract | p95 extract | CPU / job | peak RSS | process wall p95 |');
  lines.push('| --- | --- | --- | --- | --- | --- | --- | --- |');
  for (const c of Object.values(report.contenders)) {
    lines.push(`| ${c.name} | ${c.documents} | ${c.failed} | ${fmtMs(c.durationMs.p50)} | ${fmtMs(c.durationMs.p95)} | ${fmtMs(c.cpuMsPerJob)} | ${c.peakRssMiB === null ? 'n/a' : `${c.peakRssMiB} MiB`} | ${fmtMs(c.processWallMs.p95)} |`);
  }
  lines.push('');
  if (report.steadyState) {
    lines.push(`In-process steady state (what the API pays per document on the primary, no process start): p50 ${fmtMs(report.steadyState.wallMs.p50)}, p95 ${fmtMs(report.steadyState.wallMs.p95)}, CPU per document ${fmtMs(report.steadyState.cpuMsPerJob)}.`);
    lines.push('');
  }
  lines.push('## Parity');
  lines.push('');
  for (const [name, parity] of Object.entries(report.parity)) {
    lines.push(`- ${name}: ${parity.ok ? 'byte-exact on every document' : `**${parity.mismatches.length} mismatch(es)**`} (${parity.checked} documents compared).`);
    for (const m of parity.mismatches.slice(0, 10)) lines.push(`  - \`${m.path}\`: ${m.reason}`);
  }
  if (Object.keys(report.parity).length === 0) lines.push('- No second contender was supplied, so only the oracle was measured; parity was not exercised. The listening-paper corpus gap stays open: no Listening question paper is tracked, so Part B/C parity needs the production shadow gate.');
  lines.push('');
  lines.push('## Rust gate');
  lines.push('');
  lines.push(`Gate: parity exact AND (p95 at least ${Math.round(GATE.p95Improvement * 100)} % lower OR CPU per job at least ${Math.round(GATE.cpuImprovement * 100)} % lower than the .NET baseline).`);
  lines.push('');
  lines.push(report.rustGate.evaluated
    ? `**${report.rustGate.pass ? 'PASS' : 'FAIL'}**: ${report.rustGate.decision} (p95 gain ${fmtPct(report.rustGate.p95Gain)}, CPU gain ${fmtPct(report.rustGate.cpuGain)}).`
    : report.rustGate.decision);
  lines.push('');
  return lines.join('\n');
}

// ---- runner -------------------------------------------------------------------------------------------

export function listPdfs(git = (args) => spawnSync('git', args, { encoding: 'utf8' }), stat = statSync) {
  const result = git(['ls-files', '-z', '--', '*.pdf']);
  if (result.status !== 0) throw new Error('git ls-files failed');
  return result.stdout.split('\0').filter(Boolean).sort().map((path) => ({ path, bytes: stat(path).size }));
}

export function realExec(argv) {
  const started = process.hrtime.bigint();
  const run = spawnSync(argv[0], argv.slice(1), { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return { stdout: run.stdout ?? '', status: run.status ?? 1, wallMs: Number(process.hrtime.bigint() - started) / 1e6 };
}

export function parseArgs(argv) {
  const args = { benchDll: '', out: 'pdf-bench.json', md: 'pdf-bench.md', iterations: 5, warmup: 1, maxPdfs: 0, kernel: '', rust: '', steady: false, requireParity: false };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const value = () => {
      i += 1;
      if (argv[i] === undefined) throw new Error(`${arg} needs a value`);
      return argv[i];
    };
    if (arg === '--bench-dll') args.benchDll = value();
    else if (arg === '--out') args.out = value();
    else if (arg === '--md') args.md = value();
    else if (arg === '--iterations') args.iterations = Number(value());
    else if (arg === '--warmup') args.warmup = Number(value());
    else if (arg === '--max-pdfs') args.maxPdfs = Number(value());
    else if (arg === '--kernel') args.kernel = value();
    else if (arg === '--rust') args.rust = value();
    else if (arg === '--steady') args.steady = true;
    else if (arg === '--require-parity') args.requireParity = true;
    else throw new Error(`unknown argument ${arg}`);
  }
  if (!args.benchDll) throw new Error('--bench-dll <path to PdfExtractBench.dll> is required');
  for (const key of ['iterations', 'warmup', 'maxPdfs']) {
    if (!Number.isInteger(args[key]) || args[key] < 0) throw new Error(`--${key.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)} must be a non-negative integer`);
  }
  if (args.iterations < 1) throw new Error('--iterations must be >= 1');
  return args;
}

export function run(args, deps = {}) {
  const exec = deps.exec ?? realExec;
  const pdfs = (deps.listPdfs ?? listPdfs)().slice(0, args.maxPdfs > 0 ? args.maxPdfs : undefined);
  if (pdfs.length === 0) throw new Error('no tracked PDFs found');
  const env = { os: deps.os ?? process.platform, cpus: deps.cpus ?? (globalThis.navigator?.hardwareConcurrency ?? 0) };
  const oracleTemplate = `dotnet "${args.benchDll}" extract {pdf}`;
  const measured = { oracle: measureContender({ name: 'oracle (in-process .NET)', template: oracleTemplate, pdfs, iterations: args.iterations, warmup: args.warmup, exec }) };
  if (args.kernel) measured.kernel = measureContender({ name: 'agent kernel', template: args.kernel, pdfs, iterations: args.iterations, warmup: args.warmup, exec });
  if (args.rust) measured.rust = measureContender({ name: 'rust candidate', template: args.rust, pdfs, iterations: args.iterations, warmup: args.warmup, exec });

  const contenders = Object.fromEntries(Object.entries(measured).map(([key, value]) => [key, summarizeContender(value)]));
  const parity = {};
  for (const key of ['kernel', 'rust']) if (measured[key]) parity[key] = checkParity(measured.oracle, measured[key]);

  let steadyState = null;
  if (args.steady) {
    const wall = [];
    const cpu = [];
    for (const pdf of pdfs) {
      const out = exec(['dotnet', args.benchDll, 'loop', pdf.path, '--iterations', String(args.iterations), '--warmup', String(args.warmup)]);
      if (out.status !== 0) continue;
      try {
        const loop = JSON.parse(out.stdout.trim().split(/\r?\n/).pop());
        wall.push(percentile(loop.iterations.map((r) => r.wallMs), 50));
        cpu.push(percentile(loop.iterations.map((r) => r.cpuMs), 50));
      } catch (error) { /* a failed loop is simply not counted */ }
    }
    steadyState = { documents: wall.length, wallMs: { p50: percentile(wall, 50), p95: percentile(wall, 95) }, cpuMsPerJob: mean(cpu) };
  }

  const baseline = contenders.kernel ?? contenders.oracle;
  const rustGate = evaluateRustGate({ baseline, candidate: contenders.rust ?? null, parity: parity.rust });
  const report = {
    schema: RESULT_SCHEMA,
    label: 'benchmark evidence, not a release proof',
    env,
    iterations: args.iterations,
    warmup: args.warmup,
    corpus: { documents: pdfs.length, bytes: pdfs.reduce((a, p) => a + p.bytes, 0) },
    contenders,
    parity,
    steadyState,
    rustGate,
  };
  const parityFailed = Object.values(parity).some((p) => !p.ok);
  const oracleFailed = contenders.oracle.failed > 0;
  return { report, markdown: renderMarkdown(report), exitCode: (oracleFailed || (args.requireParity && parityFailed)) ? 1 : 0 };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  try {
    const args = parseArgs(process.argv.slice(2));
    const { report, markdown, exitCode } = run(args, { cpus: (await import('node:os')).availableParallelism() });
    writeFileSync(args.out, JSON.stringify(report, null, 2));
    writeFileSync(args.md, markdown);
    process.stdout.write(markdown);
    process.exitCode = exitCode;
  } catch (error) {
    process.stderr.write(`pdf-extract-bench: ${error.message}\n`);
    process.exitCode = 2;
  }
}
