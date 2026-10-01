#!/usr/bin/env node
/**
 * local-stt.mjs — Node bridge to scripts/listening/local_transcribe.py
 * (local faster-whisper, CPU). Emits the SAME transcript JSON shape as the QA
 * transcribe endpoint, so the boundary-repair pipeline and the content
 * verifier work with either STT source interchangeably.
 *
 * The production whisper-asr credential in the AI Providers registry started
 * returning 401 upstream (and one blue/green instance still serves the stale
 * "mock provider" 503) — until the owner pastes a fresh key, local
 * faster-whisper is the deterministic, free fallback the repo already ships
 * tooling for (whisper_cue_scan.py lineage).
 */

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SCRIPT = join(__dirname, 'local_transcribe.py');

export function localSttConfigured(pythonBin = process.env.OET_STT_PYTHON || 'python') {
  try {
    const r = spawnSync(pythonBin, ['-c', 'import faster_whisper'], { encoding: 'utf-8', timeout: 60_000, windowsHide: true });
    return r.status === 0;
  } catch {
    return false;
  }
}

/**
 * Transcribe one audio file (window cut) locally.
 * @returns parsed transcript JSON (same shape as the QA endpoint)
 */
export async function transcribeLocal(windowPath, { model = process.env.OET_STT_MODEL || 'small', pythonBin = process.env.OET_STT_PYTHON || 'python', timeoutMs = 900_000 } = {}) {
  if (!existsSync(windowPath)) throw new Error(`local STT: file missing ${windowPath}`);
  const r = spawnSync(pythonBin, [SCRIPT, '--audio', windowPath, '--model', model], {
    encoding: 'utf-8',
    timeout: timeoutMs,
    windowsHide: true,
    maxBuffer: 64 * 1024 * 1024,
  });
  if (r.error) throw new Error(`local STT: ${r.error.message}`);
  if (r.status !== 0) {
    const tail = String(r.stderr ?? '').slice(-300).replace(/\s+/g, ' ');
    throw new Error(`local STT failed (exit ${r.status}): ${tail || 'no stderr'}`);
  }
  const out = String(r.stdout ?? '').trim();
  const start = out.indexOf('{');
  if (start < 0) throw new Error('local STT: no JSON on stdout');
  const parsed = JSON.parse(out.slice(start));
  if (parsed.error) throw new Error(`local STT: ${parsed.error}`);
  if (!Array.isArray(parsed.segments)) throw new Error('local STT: transcript has no segments');
  return parsed;
}

/** Read an existing transcript JSON if present and parseable. */
export function readTranscriptCache(path) {
  if (!existsSync(path)) return null;
  try { return JSON.parse(readFileSync(path, 'utf-8')); } catch { return null; }
}
