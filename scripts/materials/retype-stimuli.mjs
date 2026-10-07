#!/usr/bin/env node
// ─────────────────────────────────────────────────────────────────────────────
// Generalised raster→vector retype for Writing stimulus PDFs (owner patch
// 7 Oct 2026, "every Writing sample sharp on a phone"; content verbatim,
// source-quality fix only).
//
// For every ledger row: download the LIVE stimulus PDF (via a per-profession
// QA learner's signed media URL), render each page at 300 dpi, OCR it with
// word bounding boxes + confidences (tesseract TSV), and rebuild the page as
// REAL TEXT at the words' original positions on an A4 canvas (reportlab).
// Nothing is reworded or corrected here — whatever OCR read is what ships,
// flagged by confidence so a human verifies low-confidence words before any
// swap. The built PDF lands in built/<oldAssetId>.pdf plus a PNG preview and
// a report row.
//
// Runs ONLY in .github/workflows/writing-samples-retype.yml (needs poppler,
// tesseract, fonts and the admin/QA secrets). Read-only against production.
//
//   node scripts/materials/retype-stimuli.mjs --from ledger.json --out built/
//   env: OET_ADMIN_EMAIL/OET_ADMIN_PASSWORD; per-row input:
//     { scenarioId, oldAssetId, profession, title }
//   output: report.json (per-row verdicts + low-confidence words)
// ─────────────────────────────────────────────────────────────────────────────
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
const fromIndex = argv.indexOf('--from');
const outIndex = argv.indexOf('--out');
const LIMIT = (() => { const i = argv.indexOf('--limit'); return i >= 0 ? Number(argv[i + 1]) : 0; })();
const LEDGER = path.resolve(argv[fromIndex >= 0 ? fromIndex + 1 : 'ledger.json']);
const OUT_DIR = path.resolve(outIndex >= 0 ? argv[outIndex + 1] : path.join(HERE, 'built'));
const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/$/, '');
const MIN_CONFIDENCE = 85;
const DPI = 300;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const run = (cmd, args, opts = {}) => execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, ...opts });

// ── minimal admin client (same conventions as swap-stimulus-pdf.mjs) ─────────
let accessToken = process.env.OET_ADMIN_TOKEN || '';
let refreshToken = process.env.OET_ADMIN_REFRESH_TOKEN || '';
let tokenExpiresAt = accessToken ? Date.now() + 15 * 60_000 : 0;
async function authPost(urlPath, body) {
  const res = await fetch(`${API_BASE}${urlPath}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const text = await res.text();
  if (!res.ok) throw new Error(`POST ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  return JSON.parse(text);
}
function applySession(json) {
  accessToken = json.accessToken;
  if (json.refreshToken) refreshToken = json.refreshToken;
  tokenExpiresAt = json.accessTokenExpiresAt ? new Date(json.accessTokenExpiresAt).getTime() : Date.now() + 15 * 60_000;
  if (!accessToken) throw new Error('no accessToken in auth response');
}
async function signIn() {
  const email = process.env.OET_ADMIN_EMAIL || '';
  const password = process.env.OET_ADMIN_PASSWORD || '';
  if (email && password) { applySession(await authPost('/v1/auth/sign-in', { email, password, rememberMe: true })); return; }
  if (refreshToken) { applySession(await authPost('/v1/auth/refresh', { refreshToken })); return; }
  if (accessToken) return;
  throw new Error('no admin credentials');
}
async function ensureFresh() {
  if (tokenExpiresAt && Date.now() < tokenExpiresAt - 60_000) return;
  if (refreshToken) applySession(await authPost('/v1/auth/refresh', { refreshToken }));
  else if (process.env.OET_ADMIN_EMAIL) await signIn();
}
async function api(method, urlPath, { json, raw } = {}) {
  await ensureFresh();
  const res = await fetch(`${API_BASE}${urlPath}`, {
    method,
    headers: { ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}), ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) },
    body: json !== undefined ? JSON.stringify(json) : raw,
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  return text;
}
const apiJson = async (method, urlPath, json) => JSON.parse(await api(method, urlPath, { json }));

// ── per-profession QA learners (a learner may read its profession's stimuli) ──
const learnerTokens = new Map(); // profession -> bearer
const DEVICE_ID = (() => {
  const hex = '0123456789abcdef';
  let out = '';
  for (let i = 0; i < 32; i++) out += hex[Math.floor(Math.random() * 16)];
  return out;
})();

async function learnerTokenFor(profession) {
  if (learnerTokens.has(profession)) return learnerTokens.get(profession);
  const email = `wqa-retype-${profession}-${Date.now().toString(36)}@oetwithdrhesham.co.uk`.toLowerCase();
  const password = `Qa-${Date.now().toString(36)}-x7Kq`;
  const created = await apiJson('POST', '/v1/admin/users', {
    name: `WQA retype ${profession}`, email, role: 'learner', professionId: profession,
    mobileNumber: null, targetExamDate: new Date(Date.now() + 60 * 86_400_000).toISOString().slice(0, 10),
    password, sendInvite: false,
  });
  if (!created?.id) throw new Error(`learner create failed for ${profession}`);
  const res = await fetch(`${API_BASE}/v1/auth/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-OET-Client-Platform': 'web', 'X-OET-Device-Id': DEVICE_ID },
    body: JSON.stringify({ email, password, rememberMe: false }),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`learner sign-in HTTP ${res.status}: ${text.slice(0, 200)}`);
  const session = JSON.parse(text);
  if (!session.accessToken) throw new Error('learner sign-in returned no accessToken');
  learnerTokens.set(profession, session.accessToken);
  return session.accessToken;
}

async function downloadStimulus(profession, mediaAssetId, destFile) {
  const bearer = await learnerTokenFor(profession);
  // A published Writing stimulus of the learner's own profession passes
  // MediaAssetAccessService.CanAccessAsync on the generic media content route.
  const res = await fetch(`${API_BASE}/v1/media/${mediaAssetId}/content`, {
    headers: { Authorization: `Bearer ${bearer}`, 'X-OET-Client-Platform': 'web', 'X-OET-Device-Id': DEVICE_ID },
  });
  if (!res.ok) throw new Error(`media content HTTP ${res.status} for ${mediaAssetId}`);
  const buf = Buffer.from(await res.arrayBuffer());
  if (buf.length < 1000) throw new Error(`download too small (${buf.length}B) for ${mediaAssetId}`);
  fs.writeFileSync(destFile, buf);
  return buf.length;
}

// ── OCR: tesseract TSV -> words with boxes + confidence ──────────────────────

/** A word is GARBAGE when it is mostly non-word symbols at low confidence —
 * the OCR's reading of logos, form boxes and candidate-number circles. Those
 * are never shipped: they are dropped and counted in the report for review. */
function isGarbageWord(word) {
  const text = word.text ?? '';
  const alphanumeric = (text.match(/[0-9A-Za-z]/g) ?? []).length;
  if (alphanumeric === 0) return true;
  if (alphanumeric / text.length < 0.5 && word.confidence < 90) return true;
  if (text.length >= 8 && /[^(](?:[()]|[@#§€£]){4,}/.test(text) && word.confidence < 80) return true;
  return false;
}

const droppedWords = [];
const droppedGarbage = (word) => {
  droppedWords.push(word.text);
};

function ocrPageTsv(pagePng) {
  const base = pagePng.replace(/\.png$/, '');
  run('tesseract', [pagePng, base, '--dpi', String(DPI), '-c', 'preserve_interword_spaces=1', 'tsv']);
  const tsv = fs.readFileSync(`${base}.tsv`, 'utf8').split('\n');
  const words = [];
  for (const line of tsv.slice(1)) {
    const [level, _page, _block, _par, _line, _w, left, top, width, height, conf, text] = line.split('\t');
    if (level !== '5' || !text?.trim()) continue;
    const confidence = Number(conf);
    if (!Number.isFinite(confidence) || confidence < 0) continue;
    const word = { left: Number(left), top: Number(top), width: Number(width), height: Number(height), confidence, text: text.trim() };
    // Logo/letterhead glyphs are far taller than body text at 300 dpi.
    if (word.height > 110 && confidence < 90) { droppedGarbage(word); continue; }
    if (isGarbageWord(word)) { droppedGarbage(word); continue; }
    words.push(word);
  }
  try { fs.unlinkSync(`${base}.tsv`); } catch { /* ignore */ }
  try { fs.unlinkSync(`${base}.txt`); } catch { /* ignore */ }
  return words;
}

// ── rebuild one page as vector text at the words' original positions ─────────
// Word boxes are PNG pixels at DPI; the canvas is A4 points, so the scale is
// derived from the RENDERED PNG's dimensions (never the PDF page box, whose
// points lie about the raster's true pixel geometry).

const RETYPE_DIR = path.join(OUT_DIR, '..', 'retype-work');
function pngSize(pngPath) {
  const fd = fs.openSync(pngPath, 'r');
  const head = Buffer.alloc(24);
  fs.readSync(fd, head, 0, 24, 0);
  fs.closeSync(fd);
  if (head.readUInt32BE(0) !== 0x89504e47) throw new Error(`not a png: ${pngPath}`);
  return { w: head.readUInt32BE(16), h: head.readUInt32BE(20) };
}

function buildVectorPdf(pdfPath, outPath, report, assetId) {
  const pages = Number((run('pdfinfo', [pdfPath]).match(/^Pages:\s+(\d+)/m) ?? [])[1] ?? 0);
  if (!pages) throw new Error('no pages');
  fs.mkdirSync(RETYPE_DIR, { recursive: true });
  run('pdftoppm', ['-r', String(DPI), '-png', pdfPath, path.join(RETYPE_DIR, 'page')]);
  const pyInputs = [];
  for (let p = 1; p <= pages; p++) {
    const png = path.join(RETYPE_DIR, `page-${p}.png`);
    if (!fs.existsSync(png)) throw new Error(`render missing: ${png}`);
    const words = ocrPageTsv(png);
    const dims = pngSize(png);
    if (!dims.w || !dims.h) throw new Error(`no png dimensions for ${png}`);
    const lines = groupLines(words);
    pyInputs.push({ png, pageW: dims.w, pageH: dims.h, words: lines });
    report.wordCount += words.length;
    report.lowConfidence.push(...words.filter((wd) => wd.confidence < MIN_CONFIDENCE).map((wd) => ({ page: p, ...wd })));
    // Keep the SOURCE render for the human review pair (original vs rebuilt).
    fs.copyFileSync(png, path.join(OUT_DIR, `source-${assetId}-${p}.png`));
  }
  const pyScript = path.join(RETYPE_DIR, 'build.py');
  const payload = { out: outPath, pages: pyInputs, a4: [595.276, 841.89] };
  fs.writeFileSync(path.join(RETYPE_DIR, 'payload.json'), JSON.stringify(payload));
  fs.writeFileSync(pyScript, PY_BUILDER);
  run('python3', [pyScript, path.join(RETYPE_DIR, 'payload.json')]);
  report.droppedGarbage = droppedWords.length;
  droppedWords.length = 0;
  // cleanup page pngs (big)
  for (const f of fs.readdirSync(RETYPE_DIR)) if (f.startsWith('page-')) fs.unlinkSync(path.join(RETYPE_DIR, f));
}

/** Group OCR words into visual LINES (top within tolerance), drop ghost
 * duplicates (show-through produces a second, lower-confidence copy of the
 * same line), and emit one line record per row. Drawing one string per line
 * kills the word-by-word overlap chaos on photo scans. */
function groupLines(words) {
  const sorted = [...words].sort((a, b) => a.top - b.top || a.left - b.left);
  const lines = [];
  for (const w of sorted) {
    const h = w.height || 1;
    const line = lines.find((l) => {
      const lh = l.height || 1;
      const overlap = Math.min(l.top + lh, w.top + h) - Math.max(l.top, w.top);
      return overlap > 0.45 * Math.min(lh, h);
    });
    if (line) {
      line.words.push(w);
      line.top = Math.min(line.top, w.top);
      line.height = Math.max(line.height, w.top + h) - line.top;
    } else {
      lines.push({ top: w.top, height: h, words: [w] });
    }
  }
  const out = [];
  for (const line of lines) {
    const ws = [...line.words].sort((a, b) => a.left - b.left);
    // Ghost dedupe: same text (or >70% char overlap) starting within 0.6 of a
    // box width -> keep the higher-confidence one.
    const kept = [];
    for (const w of ws) {
      const ghost = kept.find((k) => {
        const overlapStart = Math.abs(k.left - w.left) < 0.6 * Math.max(k.width, w.width);
        const sameish = k.text.toLowerCase().includes(w.text.toLowerCase()) || w.text.toLowerCase().includes(k.text.toLowerCase());
        return overlapStart && sameish && w.confidence <= k.confidence;
      });
      if (ghost) continue;
      const weaker = kept.findIndex((k) => {
        const overlapStart = Math.abs(k.left - w.left) < 0.6 * Math.max(k.width, w.width);
        const sameish = k.text.toLowerCase().includes(w.text.toLowerCase()) || w.text.toLowerCase().includes(k.text.toLowerCase());
        return overlapStart && sameish && k.confidence <= w.confidence;
      });
      if (weaker >= 0) kept.splice(weaker, 1);
      kept.push(w);
    }
    if (!kept.length) continue;
    // Split the line into SEGMENTS at large gaps (label/value tables), so each
    // segment is drawn at its own true x: no cumulative drift across the page.
    const segments = [];
    let seg = [kept[0]];
    for (let i = 1; i < kept.length; i++) {
      const gap = kept[i].left - (kept[i - 1].left + kept[i - 1].width);
      const em = Math.max(kept[i].height, kept[i - 1].height);
      if (gap > 2.2 * em) {
        segments.push(seg);
        seg = [kept[i]];
      } else {
        seg.push(kept[i]);
      }
    }
    segments.push(seg);
    for (const s of segments) {
      let text = s[0].text;
      for (let i = 1; i < s.length; i++) {
        const gap = s[i].left - (s[i - 1].left + s[i - 1].width);
        const em = Math.max(s[i].height, s[i - 1].height);
        text += ' '.repeat(Math.max(1, Math.min(4, Math.round(gap / (0.55 * em))))) + s[i].text;
      }
      out.push({
        left: Math.min(...s.map((k) => k.left)),
        top: line.top,
        width: Math.max(...s.map((k) => k.left + k.width)) - Math.min(...s.map((k) => k.left)),
        height: line.height,
        text,
      });
    }
  }
  return out;
}

const PY_BUILDER = String.raw`
import json, sys
from reportlab.pdfgen import canvas
from reportlab.lib.colors import white, black
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
import os

def font(*paths):
    for p in paths:
        if os.path.exists(p):
            return p
    raise SystemExit("font missing: " + " | ".join(paths))

REG = TTFont("Body", font("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"))
pdfmetrics.registerFont(REG)

payload = json.load(open(sys.argv[1]))
A4W, A4H = payload["a4"]
c = canvas.Canvas(payload["out"], pagesize=(A4W, A4H))
for page in payload["pages"]:
    c.setFillColor(white)
    c.rect(0, 0, A4W, A4H, stroke=0, fill=1)
    c.setFillColor(black)
    scale = min(A4W / page["pageW"], A4H / page["pageH"])
    offX = (A4W - page["pageW"] * scale) / 2.0
    offY = (A4H - page["pageH"] * scale) / 2.0
    for w in page["words"]:
        # PDF user space is bottom-up; PNG boxes are top-down.
        x = offX + w["left"] * scale
        width_pt = w["width"] * scale
        height_pt = w["height"] * scale
        y = A4H - (offY + (w["top"] + w["height"]) * scale)
        size = max(4.0, min(28.0, height_pt * 0.82))
        c.setFont("Body", size)
        # Left-align at the box x, baseline at box bottom. Draw only if it fits.
        c.drawString(x, y, w["text"])
    c.showPage()
c.save()
`;

// ── verify the rebuild: real fonts, zero raster images ───────────────────────
function verifyBuilt(outPath, report) {
  const fonts = run('pdffonts', [outPath]);
  report.fontsEmbedded = /yes\s+/m.test(fonts.split('\n').slice(2).join('\n'));
  const images = run('pdfimages', ['-list', outPath]);
  report.imageCount = images.split('\n').filter((l) => /^\s*\d+\s+\d+/.test(l)).length;
  report.sizeBytes = fs.statSync(outPath).size;
}

async function main() {
  await signIn();
  const rows = JSON.parse(fs.readFileSync(LEDGER, 'utf8'));
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const report = [];
  const todo = LIMIT ? rows.slice(0, LIMIT) : rows;
  for (const row of todo) {
    const entry = { ...row, ok: false };
    const t0 = Date.now();
    try {
      const tmpPdf = path.join(OUT_DIR, `src-${row.oldAssetId}.pdf`);
      entry.downloadedBytes = await downloadStimulus(row.profession, row.oldAssetId, tmpPdf);
      entry.wordCount = 0;
      entry.lowConfidence = [];
      const outPath = path.join(OUT_DIR, `${row.oldAssetId}.pdf`);
      buildVectorPdf(tmpPdf, outPath, entry, row.oldAssetId);
      verifyBuilt(outPath, entry);
      entry.ok = entry.imageCount === 0 && entry.fontsEmbedded;
      // preview at 110 dpi
      run('pdftoppm', ['-r', '110', '-png', outPath, path.join(OUT_DIR, `preview-${row.oldAssetId}`)]);
      fs.unlinkSync(tmpPdf);
    } catch (error) {
      entry.error = String(error.message ?? error).slice(0, 300);
    }
    entry.ms = Date.now() - t0;
    report.push(entry);
    console.log(JSON.stringify({ oldAssetId: row.oldAssetId, title: row.title, ok: entry.ok, words: entry.wordCount, lowConf: entry.lowConfidence?.length ?? 0, error: entry.error }));
  }
  fs.writeFileSync(path.join(OUT_DIR, '..', 'retype-report.json'), JSON.stringify(report, null, 2));
  const okCount = report.filter((r) => r.ok).length;
  console.error(`RETYPE: ${okCount}/${report.length} built clean; low-confidence words need human review before any swap`);
  if (okCount < report.length) process.exit(2);
}

main().catch((e) => { console.error(e); process.exit(1); });
