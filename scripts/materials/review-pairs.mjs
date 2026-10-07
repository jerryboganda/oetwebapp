#!/usr/bin/env node
// Build side-by-side review pairs (source render LEFT, rebuilt render RIGHT)
// from a retype run artifact, for the human visual QA of every rebuilt sample.
//
//   node scripts/materials/review-pairs.mjs <retype-artifact-dir> <out-dir>
//
// Output: <out-dir>/<oldAssetId>-p<N>.png  (one composite per page)
//         <out-dir>/summary.json            (per-row word/lowConf stats)
import fs from 'node:fs';
import path from 'node:path';

const [artifactDir, outDir] = process.argv.slice(2);
if (!artifactDir || !outDir) {
  console.error('usage: review-pairs.mjs <retype-artifact-dir> <out-dir>');
  process.exit(1);
}
fs.mkdirSync(outDir, { recursive: true });

// report.json may sit at the artifact root or its built-retype subdir
const findFile = (name, dir) => {
  const direct = path.join(dir, name);
  if (fs.existsSync(direct)) return direct;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      const hit = findFile(name, path.join(dir, entry.name));
      if (hit) return hit;
    }
  }
  return null;
};
const reportPath = findFile('retype-report.json', artifactDir);
// PNGs may sit next to the report or in a subdir (built-retype/)
const findPng = (name) => {
  const direct = path.join(path.dirname(reportPath), name);
  if (fs.existsSync(direct)) return direct;
  const sibling = path.join(builtDirPreview(), name);
  return fs.existsSync(sibling) ? sibling : null;
};
function builtDirPreview() {
  return path.join(path.dirname(reportPath), 'built-retype');
}
const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

// Compositing without native deps: build a minimal PNG combiner via pymupdf? No —
// plain Node: use the canvas-free approach — write an HTML index instead of PNGs
// (opens in any browser; every pair side by side, scrollable, zoomable).
const rows = [];
for (const row of report) {
  const pages = [];
  for (let p = 1; p <= 12; p++) {
    const src = findPng(`source-${row.oldAssetId}-${p}.png`);
    const rebuilt = findPng(`preview-${row.oldAssetId}-${p}.png`);
    if (!src && !rebuilt) break;
    const toData = (file) => (file ? `data:image/png;base64,${fs.readFileSync(file).toString('base64')}` : '');
    pages.push({ p, src: toData(src), rebuilt: toData(rebuilt) });
  }
  rows.push({ title: row.title, profession: row.profession, oldAssetId: row.oldAssetId, scenarioId: row.scenarioId, ok: row.ok, words: row.wordCount, lowConf: row.lowConfidence?.length ?? 0, dropped: row.droppedGarbage ?? 0, sizeBytes: row.sizeBytes, error: row.error ?? null, pages });
}

const html = `<!doctype html><meta charset="utf-8"><title>Writing retype review</title>
<style>body{font-family:system-ui;margin:0;background:#222;color:#eee}
header{position:sticky;top:0;background:#111;padding:8px 16px;font-size:14px}
.pair{display:flex;gap:4px;margin:8px 0}
.pair img{width:50%;height:auto;background:#fff}
h2{font-size:16px;margin:24px 16px 4px}
.meta{margin:0 16px;font-size:12px;color:#aaa}
.bad{color:#f88}</style>
<h1 style="font-size:18px;margin:12px 16px">Writing retype review — original (left) vs rebuilt (right)</h1>
${rows.map((r) => `<section id="${r.oldAssetId}">
<h2>${r.title} <span class="${r.ok ? '' : 'bad'}">[${r.ok ? 'built' : 'FAILED'}]</span></h2>
<p class="meta">${r.profession} · scenario ${r.scenarioId} · words ${r.words} · lowConf ${r.lowConf} · dropped ${r.dropped} · ${r.sizeBytes ?? '—'} B ${r.error ? `· <span class="bad">${r.error}</span>` : ''}</p>
${r.pages.map((pg) => `<div class="pair"><img src="${pg.src}" alt="original p${pg.p}"><img src="${pg.rebuilt}" alt="rebuilt p${pg.p}"></div>`).join('')}
</section>`).join('')}`;
fs.writeFileSync(path.join(outDir, 'review.html'), html);
fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify(rows.map(({ pages, ...r }) => ({ ...r, pageCount: pages.length })), null, 2));
console.error(`review pairs: ${rows.length} document(s) -> ${path.join(outDir, 'review.html')}`);
