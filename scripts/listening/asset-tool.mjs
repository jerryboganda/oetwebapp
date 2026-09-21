#!/usr/bin/env node
/**
 * Listening audio asset tool (owner-approved repairs, 2026-09-22). Upload never attaches; attach snapshots first;
 * nothing is deleted (a replaced primary is demoted, not removed) so every step is reversible.
 *
 *   node scripts/listening/asset-tool.mjs upload <file> [--name <originalFilename>]
 *   node scripts/listening/asset-tool.mjs list <paperId>
 *   node scripts/listening/asset-tool.mjs attach <paperId> <part A1|A2|C1|C2> <mediaAssetId> <durationSec> [--title <t>]
 *   node scripts/listening/asset-tool.mjs rollback <paperId> <part> <oldMediaAssetId> <durationSec>
 *
 * Env: OET_ADMIN_EMAIL, OET_ADMIN_PASSWORD (never printed), OET_API_BASE. Snapshots: state/repair/<paperId>/.
 */
import { mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { open } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const API = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const GAP_MS = 2500;
const arg = (n, d = null) => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : d; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const writeJson = (f, d) => { mkdirSync(dirname(f), { recursive: true }); writeFileSync(f, JSON.stringify(d, null, 2)); };

let token = '', tokenAt = 0;
async function signIn() {
  const res = await fetch(`${API}/v1/auth/sign-in`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD, rememberMe: true }) });
  if (!res.ok) throw new Error(`sign-in failed: HTTP ${res.status}`);
  token = (await res.json()).accessToken; tokenAt = Date.now();
}
// A new sign-in invalidates every earlier token of the account (single active session), so any concurrent
// sign-in elsewhere (CI runners) revokes ours: re-sign-in with jitter and retry a few times.
async function api(method, path, { json, body, retried = 0 } = {}) {
  if (!token || Date.now() - tokenAt > 10 * 60_000) await signIn();
  const headers = { Accept: 'application/json', Authorization: `Bearer ${token}` };
  if (json !== undefined) headers['Content-Type'] = 'application/json'; else if (body !== undefined) headers['Content-Type'] = 'application/octet-stream';
  const res = await fetch(`${API}${path}`, { method, headers, body: json !== undefined ? JSON.stringify(json) : body });
  if (res.status === 401 && retried < 5) { token = ''; await sleep(1500 + Math.random() * 3500); return api(method, path, { json, body, retried: retried + 1 }); }
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> HTTP ${res.status}: ${text.slice(0, 240)}`);
  return text ? JSON.parse(text) : null;
}

const audioRows = (paper, part) => (paper.assets ?? []).filter((a) => a.role === 'Audio' && (!part || String(a.part ?? '').toUpperCase() === part))
  .map((a) => ({ part: a.part, mediaAssetId: a.mediaAssetId, isPrimary: a.isPrimary, displayOrder: a.displayOrder, dur: a.media?.durationSeconds ?? null, file: a.media?.originalFilename ?? null }));

async function upload() {
  const file = process.argv[3];
  if (!file) throw new Error('usage: upload <file>');
  const size = statSync(file).size;
  const name = arg('--name', basename(file));
  const started = await api('POST', '/v1/admin/uploads', { json: { originalFilename: name, declaredMimeType: 'audio/mpeg', declaredSizeBytes: size, intendedRole: 'Audio' } });
  const chunk = started.chunkSizeBytes;
  const totalParts = Math.max(1, Math.ceil(size / chunk));
  const fh = await open(file, 'r');
  try {
    for (let part = 1; part <= totalParts; part++) {
      const offset = (part - 1) * chunk, len = Math.min(chunk, size - offset);
      const buf = Buffer.allocUnsafe(len);
      await fh.read(buf, 0, len, offset);
      await sleep(GAP_MS);
      await api('PUT', `/v1/admin/uploads/${started.uploadId}/parts/${part}`, { body: buf });
    }
  } finally { await fh.close(); }
  await sleep(GAP_MS);
  const done = await api('POST', `/v1/admin/uploads/${started.uploadId}/complete`, { json: {} });
  const dur = parseFloat(spawnSync('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file], { encoding: 'utf-8' }).stdout);
  const rec = { file: name, bytes: size, mediaAssetId: done.mediaAssetId, durationSeconds: +dur.toFixed(3) };
  console.log(JSON.stringify(rec));
  writeJson(join(__dirname, 'state', 'repair', `upload-${name}.json`), rec);
}

async function list() {
  const paper = await api('GET', `/v1/admin/papers/${process.argv[3]}`);
  console.log(`${paper.title} | ${paper.status} | candidateVisible=${paper.candidateVisible}`);
  console.table(audioRows(paper));
}

async function attach(rollback = false) {
  const [paperId, partRaw, mediaAssetId, dur] = process.argv.slice(3);
  const part = String(partRaw ?? '').toUpperCase();
  if (!paperId || !/^(A1|A2|C1|C2)$/.test(part) || !mediaAssetId || !(Number(dur) > 0)) throw new Error('usage: attach|rollback <paperId> <A1|A2|C1|C2> <mediaAssetId> <durationSec>');
  const before = await api('GET', `/v1/admin/papers/${paperId}`);
  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+Z$/, 'Z');
  const dir = join(__dirname, 'state', 'repair', paperId);
  writeJson(join(dir, `before-${part}-${stamp}.json`), { paper: before });
  await sleep(GAP_MS);
  await api('POST', `/v1/admin/papers/${paperId}/assets`, {
    json: { role: 0, mediaAssetId, part, title: (arg('--title') ?? `${before.title} - ${part}${rollback ? ' (rollback)' : ''}`).slice(0, 200), displayOrder: 1, makePrimary: true, durationSeconds: Math.ceil(Number(dur)) },
  });
  const after = await api('GET', `/v1/admin/papers/${paperId}`);
  writeJson(join(dir, `after-${part}-${stamp}.json`), { paper: after });
  const rows = audioRows(after, part);
  const primary = rows.find((r) => r.isPrimary);
  console.log(`${part} rows after attach:`); console.table(rows);
  if (primary?.mediaAssetId !== mediaAssetId) { console.error('NEW ASSET IS NOT PRIMARY'); process.exitCode = 3; }
  console.log(`previous rows recorded in ${join(dir, `before-${part}-${stamp}.json`)}`);
}

const commands = { upload, list, attach: () => attach(false), rollback: () => attach(true) };
const c = commands[process.argv[2]];
if (!c) { console.error('usage: asset-tool.mjs <upload|list|attach|rollback>'); process.exit(1); }
c().catch((e) => { console.error(e.message || e); process.exit(1); });
