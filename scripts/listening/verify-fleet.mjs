#!/usr/bin/env node
/**
 * Fleet verifier for Atlas/Nova/Kaplan Listening audio. RUNS ON GITHUB ACTIONS (repo compute policy).
 *
 *   node scripts/listening/verify-fleet.mjs --plan plan.json --evidence dirA[,dirB] --timers timers.json \
 *        --inventory source-inventory.json [--actions actions.json] [--out report]
 *
 * Applies lib/semantic.mjs to every A1/A2/B/C1/C2 section of every published paper: cue at the destination head,
 * preparation window, next-extract intro left in the source tail, same file / duplicate speech / repeated
 * previous-section speech, trailing silence and abrupt ends, learner timer vs real audio, and duration
 * reconciliation against the original source. A paper is never PASS while any of its sections needs review.
 * Writes report.json, report.csv, report.md (table = the owner's final-output columns) and repair proposals.
 */
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { CUE, checkDestinationHead, checkPair, checkSourceTail, checkTail, checkTimer, findCue, overall } from './lib/semantic.mjs';

const arg = (n, d = null) => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : d; };
const readJson = (f) => JSON.parse(readFileSync(f, 'utf-8'));
const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];
const res = (id, level, detail) => ({ id, level, detail });

const plan = readJson(arg('--plan', 'plan.json'));
// The plan is a snapshot from the audit; overrides point sections at assets attached since (repairs), so the report reflects the current state.
for (const o of arg('--overrides') ? readJson(arg('--overrides')) : []) {
  const p = plan.papers.find((x) => x.paperId === o.paperId);
  if (p) p.sections[o.section] = { assetId: o.assetId, dbDur: o.dbDur, file: o.file, viaKey: o.section };
}
const timers = readJson(arg('--timers', 'timers.json'));
// measured on the owner's original source audio (scan of the second extract's preparation pause); optional
const srcPrep = arg('--source-prep') ? readJson(arg('--source-prep')) : {};
const inv = readJson(arg('--inventory', 'source-inventory.json'));
const actions = arg('--actions') ? readJson(arg('--actions')) : [];
const runNote = arg('--run-note', 'Whisper small.en');

// evidence: later directories only fill in what earlier ones lack (a failed asset from run 1 is re-done in run 3)
const ev = {};
for (const dir of (arg('--evidence', 'evidence') || '').split(',').filter(Boolean)) {
  for (const f of readdirSync(dir).filter((n) => /^evidence-\d+\.json$/.test(n))) {
    for (const [id, a] of Object.entries(readJson(join(dir, f)).assets)) if (!ev[id] || ev[id].error || !ev[id].windows) ev[id] = a;
  }
}

const short = (t) => t.replace(/ Practice Series — (Listening )?/, ' ').replace('Sample Test ', 'ST').replace('Listening Practice Test', '').replace(/\s*\(Q.*$/, '').trim();
const testNo = (t) => (/kaplan/i.test(t) ? 'Kaplan' : String(t.match(/(?:Test)\s+(\d+)/i)?.[1] ?? ''));

function buildSection(p, code) {
  const s = p.sections[code];
  if (!s) return null;
  const e = ev[s.assetId];
  const w = e?.windows ?? {};
  const dur = e?.realDur ?? null;
  const segs = w.full?.segments ?? [];
  return {
    code, assetId: s.assetId, file: e?.file ?? s.file, dur, segs, silences: e?.silences ?? [], tailMaxDb: e?.tailMaxDb,
    hasAsr: !!(w.full || w.head), error: e?.error,
    headSegs: w.head?.segments ?? segs.filter((x) => x.start < 150),
    tailSegs: w.tail?.segments ?? (dur ? segs.filter((x) => x.end > dur - 120) : []),
  };
}

const rows = [];
const proposals = [];
for (const p of plan.papers) {
  const n = testNo(p.title);
  const S = Object.fromEntries(SECTIONS.map((c) => [c, buildSection(p, c)]));
  const T = timers[p.paperId];
  const learner = T?.learner ?? T?.admin ?? {};
  const timerSource = T?.learner ? 'learner runtime (database)' : 'authored JSON (JSON-only paper)';
  const checks = Object.fromEntries(SECTIONS.map((c) => [c, []]));
  const intentionallyMissing = /unavailable/i.test(p.title);

  for (const c of SECTIONS) {
    const s = S[c];
    if (!s) { checks[c].push(res('present', intentionallyMissing && c === 'C2' ? 'pass' : 'fail', intentionallyMissing && c === 'C2' ? 'not published; the paper title states Q37-42 are unavailable (intentional)' : 'no audio published for this section')); continue; }
    if (s.error || !s.hasAsr) { checks[c].push(res('asr_evidence', 'review', s.error ? `no evidence (${s.error})` : 'no speech-to-text evidence')); continue; }
    checks[c].push(res('asr_evidence', 'pass', 'transcript and silence map collected'));
    checks[c].push(...checkTimer(learner[c] ?? null, s.dur), ...checkTail(s.silences, s.dur, s.tailMaxDb, s.tailSegs));
  }
  for (const [src, dst] of [['A1', 'A2'], ['C1', 'C2']]) {
    const a = S[src], b = S[dst];
    if (a?.hasAsr) checks[src].push(...checkSourceTail(a.segs, a.dur, src));
    if (b?.hasAsr) checks[dst].push(...checkDestinationHead(dst, b.segs, b.silences, srcPrep[p.paperId]?.[src === 'A1' ? 'A' : 'C']?.prepLen ?? null));
    if (a?.hasAsr && b?.hasAsr) {
      const pc = checkPair({ assetId: a.assetId, dur: a.dur, segs: a.segs }, { assetId: b.assetId, dur: b.dur, segs: b.segs });
      checks[dst].push(...pc);
      const same = pc.find((x) => x.id === 'same_asset' && x.level === 'fail');
      if (same) checks[src].push(same);
    }
  }

  // duration reconciliation against the original source (audio silently dropped by an earlier cut shows up here)
  const recon = (code, got, want, what) => {
    if (want == null || got == null) return;
    const d = +(got - want).toFixed(1);
    checks[code].push(res('source_duration', Math.abs(d) <= 3 ? 'pass' : d < -3 ? 'fail' : 'review', `${what}: ${got.toFixed(1)} s vs source ${want} s (${d >= 0 ? '+' : ''}${d} s${d < -3 ? ', audio missing' : ''})`));
  };
  let srcNote = '';
  if (p.series === 'atlas' && n !== 'Kaplan' && inv.atlas[n]) {
    const I = inv.atlas[n];
    srcNote = `Atlas source folder "${Number(n) + 1}- Sample Test ${n}": Part A ${I.A} s, Part B ${I.B} s, Part C ${I.C} s`;
    if (S.A1?.dur && S.A2?.dur) recon('A2', S.A1.dur + S.A2.dur, I.A, 'A1+A2');
    if (S.B?.dur) recon('B', S.B.dur, I.B, 'B');
    if (S.C1?.dur && S.C2?.dur) recon('C2', S.C1.dur + S.C2.dur, I.C, 'C1+C2');
    else if (S.C1?.dur && !S.C2) checks.C1.push(res('source_duration', 'review', `C1 ${S.C1.dur.toFixed(1)} s but the source Part C is ${I.C} s (C2 not published)`));
  } else {
    const total = n === 'Kaplan' ? inv.kaplanTotal : inv.nova[n];
    srcNote = n === 'Kaplan' ? `Kaplan source: single file ${total} s` : `Nova source: full-test file "Test ${n}.mp3" ${total} s`;
    const sum = SECTIONS.reduce((t, c) => t + (S[c]?.dur ?? 0), 0);
    if (total && sum) { const pct = ((sum - total) / total) * 100; checks.A1.push(res('source_total', Math.abs(pct) <= 8 ? 'pass' : 'review', `sections total ${sum.toFixed(0)} s vs source ${total} s (${pct >= 0 ? '+' : ''}${pct.toFixed(1)}%; intros/tags are not part of the sections)`)); }
  }

  for (const [src, dst] of [['A1', 'A2'], ['C1', 'C2']]) {
    const bad = checks[dst].filter((c) => c.level === 'fail' && ['head_cue', 'prep_window', 'source_duration', 'duplicate_content', 'same_asset'].includes(c.id)).concat(checks[src].filter((c) => c.level === 'fail' && c.id === 'next_intro_in_tail'));
    if (!bad.length) continue;
    const cueSrc = S[src]?.hasAsr ? findCue(S[src].segs, new RegExp([CUE.extractTwo, dst === 'C2' ? CUE.questionsC2 : CUE.questionsA2, CUE.intro].map((r) => r.source).join('|'), 'i'), Math.max(0, S[src].dur - 120)) : null;
    const cueDst = S[dst]?.hasAsr ? findCue(S[dst].segs, new RegExp([CUE.extractTwo, dst === 'C2' ? CUE.questionsC2 : CUE.questionsA2, CUE.intro].map((r) => r.source).join('|'), 'i'), 0, 75) : null;
    proposals.push({
      paperId: p.paperId, title: p.title, pair: `${src}/${dst}`, srcDur: S[src]?.dur ?? null, dstDur: S[dst]?.dur ?? null,
      issues: bad.map((b) => `${b.id}: ${b.detail}`),
      cueAtConcatenatedSec: cueSrc ? +cueSrc.start.toFixed(1) : cueDst && S[src]?.dur != null ? +(S[src].dur + cueDst.start).toFixed(1) : null,
      cueLocation: cueSrc ? `tail of ${src}` : cueDst ? `head of ${dst}` : 'not found',
      source: srcNote,
    });
  }

  const hidden = p.candidateVisible === false;
  const humanAlways = /Sample Test (8|9)\b/i.test(p.title);
  for (const c of SECTIONS) {
    const s = S[c];
    const lv = overall(checks[c]);
    const asrIds = ['asr_evidence', 'head_cue', 'prep_window', 'next_intro_in_tail', 'same_asset', 'duplicate_content', 'previous_speech_at_head', 'source_duration', 'source_total', 'present'];
    const asrChecks = checks[c].filter((x) => asrIds.includes(x.id));
    const act = actions.find((a) => a.paperId === p.paperId && (a.section === c || a.section === '*')) ?? {};
    const issues = checks[c].filter((x) => x.level !== 'pass').map((x) => `${x.id}: ${x.detail}`);
    const needsHuman = lv !== 'pass' || humanAlways;
    rows.push({
      series: p.series === 'nova' ? 'Nova' : 'Atlas', test: n === 'Kaplan' ? 'Kaplan' : `Test ${n}`, paperTitle: short(p.title), paperId: p.paperId, section: c,
      file: s ? `${s.file ?? ''} (${s.assetId})` : '(none)',
      currentDuration: s?.dur ? +s.dur.toFixed(1) : null,
      currentTimer: learner[c] ?? null, timerSource,
      source: `${srcNote}; ASR ${runNote}`,
      issue: issues.join(' | ') || 'none found',
      action: act.action ?? (hidden ? 'paper hidden from learners' : 'none'),
      newDuration: act.newDuration ?? '', newTimer: act.newTimer ?? '',
      asr: `${overall(asrChecks).toUpperCase()}: ${asrChecks.map((x) => `${x.id}=${x.level}`).join(', ')}`,
      humanReview: needsHuman ? 'Yes' : 'No',
      result: hidden ? 'HIDDEN' : lv === 'pass' && !humanAlways ? 'PASS' : 'NEEDS REVIEW',
      checks: checks[c],
    });
  }
}

const paperResult = {};
for (const p of plan.papers) {
  const rs = rows.filter((r) => r.paperId === p.paperId);
  paperResult[p.paperId] = rs.every((r) => r.result === 'HIDDEN') ? 'HIDDEN' : rs.every((r) => r.result === 'PASS') ? 'PASS' : 'NEEDS REVIEW';
}
for (const r of rows) r.paperResult = paperResult[r.paperId];

const out = arg('--out', 'report');
mkdirSync(out, { recursive: true });
const summary = {
  generatedAt: new Date().toISOString(), papers: plan.papers.length, sections: rows.length,
  sectionResults: rows.reduce((m, r) => ((m[r.result] = (m[r.result] || 0) + 1), m), {}),
  paperResults: Object.values(paperResult).reduce((m, r) => ((m[r] = (m[r] || 0) + 1), m), {}),
  proposals: proposals.length,
};
writeFileSync(join(out, 'report.json'), JSON.stringify({ summary, rows, proposals }, null, 1));

const cols = [['Series', 'series'], ['Test', 'test'], ['Section', 'section'], ['Production media or file', 'file'], ['Current duration (s)', 'currentDuration'], ['Current timer (s)', 'currentTimer'], ['Source used for verification', 'source'], ['Issue found', 'issue'], ['Action taken', 'action'], ['New duration if changed (s)', 'newDuration'], ['New timer if changed (s)', 'newTimer'], ['ASR verification result', 'asr'], ['Human review required', 'humanReview'], ['Final result', 'result'], ['Paper result', 'paperResult']];
const q = (v) => `"${String(v ?? '').replace(/"/g, '""')}"`;
writeFileSync(join(out, 'report.csv'), [cols.map(([h]) => q(h)).join(','), ...rows.map((r) => cols.map(([, k]) => q(r[k])).join(','))].join('\n'));
const md = [`# Listening audio fleet audit (${summary.generatedAt})`, '', `Sections: ${JSON.stringify(summary.sectionResults)} | Papers: ${JSON.stringify(summary.paperResults)}`, '',
  `| ${cols.map(([h]) => h).join(' | ')} |`, `|${cols.map(() => '---').join('|')}|`,
  ...rows.map((r) => `| ${cols.map(([, k]) => String(r[k] ?? '').replace(/\|/g, '/').replace(/\n/g, ' ')).join(' | ')} |`), '',
  '## Repair proposals (batch report, nothing applied)', '', ...proposals.map((x) => `- **${short(x.title)} ${x.pair}**: ${x.issues.join('; ')} (cue at ${x.cueAtConcatenatedSec ?? '?'} s in A1+A2 order, ${x.cueLocation}); ${x.source}`)].join('\n');
writeFileSync(join(out, 'report.md'), md);
console.log(JSON.stringify(summary));
