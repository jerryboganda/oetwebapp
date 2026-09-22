#!/usr/bin/env node
/**
 * build-issue-log-report.mjs — assemble the owner deliverable tables from the
 * listening pipeline's state directory (no API access needed):
 *
 *   Table 1 (required by the issue log):
 *     Series | Test | Section | Production media/file | Before duration |
 *     After duration (if changed) | Source used for verification | Result
 *
 *   Table 2: Issue 5 boundary-dataset reconciliation for the eight disputed
 *     masters (old 28 Aug audit vs computed bounds vs ASR cue evidence).
 *
 *   Table 3: papers/pairs still needing manual attention (never forced).
 *
 * Usage:
 *   node scripts/listening/build-issue-log-report.mjs [--out <markdown path>]
 *
 * Inputs (latest-wins by timestamp in the filename):
 *   scripts/listening/state/audit-*.json            structural audit (before)
 *   scripts/listening/state/fix/<id>/assets.json    per-pair before-state
 *   scripts/listening/state/fix/<id>/plan.json      verdicts + applied results
 *   scripts/listening/state/apply-*.json            apply reports
 *   scripts/listening/state/verify/content-verify-*.json  content verdicts
 *
 * Secrets never appear in any input; the report contains media ids, filenames,
 * durations, verdicts only.
 */

import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const STATE = join(__dirname, 'state');
const FIX = join(STATE, 'fix');
const VERIFY = join(STATE, 'verify');
const BOUNDS = join(__dirname, '..', 'materials', 'test_audio_boundaries_computed.json');

const OUT = (() => {
  const i = process.argv.indexOf('--out');
  return i >= 0 ? process.argv[i + 1] : join(__dirname, 'state', 'issue-log-report.md');
})();

const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];

// Issue 5 dispute table (from the owner issue log) — verification list ONLY.
const DISPUTED = [
  { file: 'L18 Mrs Melissa.mp3', delta: 143 },
  { file: 'L10 Bryan Haris.mp3', delta: 109 },
  { file: 'L17 Greg Mathew.mp3', delta: 49 },
  { file: 'L4 Ray Sands.mp3', delta: 49 },
  { file: 'L20 Polina Semyonova.mp3', delta: 49 },
  { file: 'L13 Magtanggol.mp3', delta: 49 },
  { file: 'L11 Ms Elissa.mp3', delta: 47 },
  { file: 'L12 Chong Walton.mp3', delta: 47 },
];

function latestJson(dir, prefix) {
  if (!existsSync(dir)) return null;
  const files = readdirSync(dir).filter((f) => f.startsWith(prefix) && f.endsWith('.json')).sort();
  if (!files.length) return null;
  try { return JSON.parse(readFileSync(join(dir, files[files.length - 1]), 'utf-8')); } catch { return null; }
}

function fmtDur(sec) {
  if (sec == null || !Number.isFinite(Number(sec))) return '—';
  return `${Math.round(Number(sec))}s`;
}

const audit = latestJson(STATE, 'audit-');
const content = latestJson(VERIFY, 'content-verify-');

const auditById = new Map((audit?.rows ?? []).map((r) => [r.paperId, r]));
const contentById = new Map((content?.rows ?? []).map((r) => [r.paperId, r]));

// paper plan + assets state
const papers = existsSync(FIX)
  ? readdirSync(FIX).filter((d) => !d.includes('.') && existsSync(join(FIX, d, 'assets.json'))).map((id) => {
      const dir = join(FIX, id);
      const assets = JSON.parse(readFileSync(join(dir, 'assets.json'), 'utf-8'));
      let plan = null;
      try { plan = JSON.parse(readFileSync(join(dir, 'plan.json'), 'utf-8')); } catch {}
      return { id, dir, assets, plan };
    })
  : [];

const bounds = existsSync(BOUNDS) ? JSON.parse(readFileSync(BOUNDS, 'utf-8')) : [];

// ── Table 1 ──────────────────────────────────────────────────────────────────
const rows1 = [];
for (const p of papers) {
  const a = auditById.get(p.id);
  const c = contentById.get(p.id);
  const series = (a?.series ?? 'atlas').toUpperCase();
  const test = p.assets.title ?? a?.title ?? p.id;
  const pairBySection = { A1: 'A', A2: 'A', C1: 'C', C2: 'C' };
  for (const code of SECTIONS) {
    const auditInfo = a?.audio?.[code] ?? null;
    const secContent = c?.sections?.[code] ?? null;
    let before = auditInfo?.dur ?? null;
    let after = before;
    let media = auditInfo?.mediaAssetId ?? null;
    let file = auditInfo?.file ?? null;
    let source = '—';
    let result = 'not audited';

    // Applied repair overrides durations + media ids (plan records new assets).
    const pairId = pairBySection[code];
    const pair = pairId ? p.plan?.pairs?.[pairId] : null;
    if (pair?.mode === 'applied') {
      const isSrc = code === `${pairId}1`;
      const newDur = isSrc ? pair.srcNewDur : pair.dstNewDur;
      const newAsset = pair.assets?.[code] ?? null;
      if (newDur != null) after = newDur;
      if (newAsset) media = newAsset;
      source = `ASR cue scan (${pair.mode} at ${pair.cueSec}s; STT ${pair.jev === 'confirmed' ? '+ Jev confirmed' : pair.jev ?? ''})`;
    } else if (pair?.mode === 'skipped') {
      source = `pipeline: skipped (${pair.reason ?? 'not applicable'})`;
    } else if (pair?.mode && pair.mode !== 'ok') {
      source = `pipeline verdict: ${pair.mode}${pair.cueSec != null ? ` (cue ${pair.cueSec}s)` : ''}`;
    } else if (pair?.mode === 'ok') {
      source = 'ASR cue scan (cue already at destination head)';
    }

    if (secContent) {
      result = secContent.verdict ?? '—';
      if (secContent.evidence?.headCue || secContent.evidence?.tailCueHits != null) {
        const evBits = [];
        if (source === '—') source = 'ASR content verification (head/tail windows)';
        if (secContent.evidence.headCue) evBits.push(`cue@${secContent.evidence.headCue.start}s`);
        if (secContent.evidence.prepAfterCueSec != null) evBits.push(`prep ${secContent.evidence.prepAfterCueSec}s`);
        if (evBits.length && !source.includes('content verification')) source += ' + content checks';
      }
    } else if (a) {
      const v = a.verdict?.[code] ?? a.verdict?.boundary ?? null;
      result = v ?? 'ok (structural)';
    }

    if (!auditInfo && !secContent) continue; // section absent everywhere
    const changed = after != null && before != null && Math.abs(after - before) > 0.5;
    rows1.push(`| ${series} | ${test} | ${code} | \`${file ?? media ?? '—'}\` (${media ?? '—'}) | ${fmtDur(before)} | ${changed ? fmtDur(after) : 'unchanged'} | ${source} | ${result} |`);
  }
}

// ── Table 2 (Issue 5) ────────────────────────────────────────────────────────
const oldAuditA1 = {
  // From docs/listening/audio-split-audit-report.md (2026-08-28, structural).
  'L4 Ray Sands.mp3': 394.26,
  'L10 Bryan Haris.mp3': 259.35,
  'L11 Ms Elissa.mp3': 341.58,
  'L12 Chong Walton.mp3': 301.81,
  'L13 Magtanggol.mp3': 294.94,
  'L17 Greg Mathew.mp3': 272.86,
  'L18 Mrs Melissa.mp3': 304.69,
  'L20 Polina Semyonova.mp3': 294.88,
};
const rows2 = [];
for (const d of DISPUTED) {
  const master = bounds.find((b) => b.filename === d.file);
  const computedA1 = master?.bounds?.A1?.dur ?? null;
  const oldA1 = oldAuditA1[d.file] ?? null;
  const actualDelta = oldA1 != null && computedA1 != null ? Math.round(computedA1 - oldA1) : null;
  // Which production papers matched this master in the structural audit?
  const matched = (audit?.rows ?? []).filter((r) => r.master?.source === d.file).map((r) => r.title);
  const evidence = matched.flatMap((t) => {
    const p = papers.find((x) => (x.assets.title ?? '') === t);
    if (!p?.plan?.pairs) return [];
    return Object.entries(p.plan.pairs).map(([pid, pair]) => `${t} [${pid}] ${pair.mode}${pair.cueSec != null ? `@${pair.cueSec}s` : ''}`);
  });
  rows2.push(`| \`${d.file}\` | ${fmtDur(oldA1)} | ${fmtDur(computedA1)} | ${actualDelta ?? d.delta}s (log) | ${matched.length ? matched.join('; ') : 'no production paper matched'} | ${evidence.length ? evidence.join('; ') : 'ASR cue evidence pending'} | verify-only — recut only via confirmed ASR cue, never from this table |`);
}

// ── Table 3 (manual attention) ───────────────────────────────────────────────
const rows3 = [];
for (const p of papers) {
  for (const [pid, pair] of Object.entries(p.plan?.pairs ?? {})) {
    if (pair.mode === 'unknown') {
      rows3.push(`| ${p.assets.title} | ${pid} | ${pair.reason ?? 'unknown'} |`);
    }
  }
}
for (const r of content?.rows ?? []) {
  for (const [code, sec] of Object.entries(r.sections ?? {})) {
    if (String(sec.verdict ?? '').startsWith('Needs manual review')) {
      rows3.push(`| ${r.title} | ${code} (content) | ${sec.verdict} |`);
    }
  }
}

const md = [
  '# OET with Dr Ahmed Hesham — Atlas/Nova Listening audio issue log — results',
  '',
  `Generated: ${new Date().toISOString()} · structural audit: ${audit?.generatedAt ?? 'n/a'} · content verification: ${content?.generatedAt ?? 'n/a'} (${content?.jev ?? 'n/a'})`,
  '',
  '## Table 1 — Series | Test | Section | Production media/file | Before duration | After duration (if changed) | Source used for verification | Result',
  '',
  '| Series | Test | Section | Production media/file | Before duration | After duration (if changed) | Source used for verification | Result |',
  '|---|---|---|---|---|---|---|---|',
  ...rows1,
  '',
  '## Table 2 — Issue 5 boundary-dataset reconciliation (verification list, not a recut instruction)',
  '',
  '| Source audio | Old audit A1 (28 Aug) | Computed A1 | Actual Δ | Matched production paper(s) | ASR evidence | Action |',
  '|---|---|---|---|---|---|---|',
  ...rows2,
  '',
  '## Table 3 — Remaining manual-attention list (never auto-forced)',
  '',
  rows3.length ? '| Paper | Pair/Section | Reason |' : '_None._',
  '|---|---|---|',
  ...rows3,
  '',
].join('\n');

writeFileSync(OUT, md);
console.log(`Report written: ${OUT}`);
console.log(`Table 1 rows: ${rows1.length} | Table 2 rows: ${rows2.length} | Table 3 rows: ${rows3.length}`);
