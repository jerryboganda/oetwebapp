#!/usr/bin/env node
/**
 * Remote-ASR helper for Listening QA. Cuts a window with ffmpeg (seconds of CPU) and sends it to the
 * deployed QA endpoint (POST /v1/admin/listening/qa/transcribe: remote Whisper, the VPS only relays),
 * so no speech-to-text ever runs on this PC.
 *
 *   node scripts/listening/asr-window.mjs <audioFile> <startSec> <durSec> [--json out.json]
 *
 * Prints segments with ABSOLUTE times (seconds in <audioFile>). Env: OET_ADMIN_EMAIL, OET_ADMIN_PASSWORD, OET_API_BASE.
 * Note: each call leaves one small temp object under qa/cuescan/ in production storage (endpoint limitation),
 * so keep windows few and large enough (<= 25 MB).
 */
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const API = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let token = '';
async function signIn() {
  const res = await fetch(`${API}/v1/auth/sign-in`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD, rememberMe: true }) });
  if (!res.ok) throw new Error(`sign-in failed: HTTP ${res.status}`);
  token = (await res.json()).accessToken;
}

/** Transcribe [start, start+dur) of file. Returns { model, segments:[{start,end,text}] } with absolute seconds. */
export async function asrWindow(file, start, dur) {
  const dir = mkdtempSync(join(tmpdir(), 'oet-asr-'));
  const clip = join(dir, 'window.mp3');
  try {
    const cut = spawnSync('ffmpeg', ['-y', '-v', 'error', '-ss', String(start), '-t', String(dur), '-i', file, '-ac', '1', '-c:a', 'libmp3lame', '-q:a', '5', clip], { encoding: 'utf-8' });
    if (cut.status !== 0) throw new Error(`ffmpeg cut failed: ${cut.stderr.slice(-200)}`);
    const bytes = readFileSync(clip);
    let last = '';
    for (let attempt = 0; attempt < 6; attempt++) {
      if (!token) await signIn();
      const form = new FormData();
      form.append('audio', new Blob([bytes], { type: 'audio/mpeg' }), 'window.mp3');
      form.append('language', 'en');
      const res = await fetch(`${API}/v1/admin/listening/qa/transcribe`, { method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: form });
      if (res.status === 401) { token = ''; continue; }
      if (res.status === 503 || res.status === 429 || res.status === 502) { last = `HTTP ${res.status}: ${(await res.text()).slice(0, 160)}`; await sleep(4000 * (attempt + 1)); continue; } // 503 = STT registry not warm on this instance
      const text = await res.text();
      if (!res.ok) throw new Error(`transcribe HTTP ${res.status}: ${text.slice(0, 200)}`);
      const j = JSON.parse(text);
      return { model: j.model, segments: (j.segments ?? []).map((s) => ({ start: +(start + (s.startMs ?? 0) / 1000).toFixed(2), end: +(start + (s.endMs ?? 0) / 1000).toFixed(2), text: String(s.text ?? '').trim() })) };
    }
    throw new Error(`transcribe kept failing after retries (last: ${last || 'auth'})`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, '/')}` || process.argv[1]?.endsWith('asr-window.mjs')) {
  const [file, start, dur] = process.argv.slice(2);
  if (!file || !(Number(dur) > 0)) { console.error('usage: asr-window.mjs <audioFile> <startSec> <durSec> [--json out.json]'); process.exit(1); }
  const out = await asrWindow(file, Number(start), Number(dur));
  for (const s of out.segments) console.log(`${s.start.toFixed(1).padStart(7)}-${s.end.toFixed(1).padEnd(7)} | ${s.text}`);
  const j = process.argv.indexOf('--json');
  if (j >= 0) writeFileSync(process.argv[j + 1], JSON.stringify({ file, start: Number(start), dur: Number(dur), ...out }, null, 2));
}
