#!/usr/bin/env node
/**
 * Jev (TypeSafe System One) client for Listening content verification.
 *
 * Wiring proven live 2026-09-19 (AGENT-HANDOFF-TYPESAFE-INTEGRATION.md §3/§4):
 *   POST https://api.typesafe.ai/v1/systemone
 *   Authorization: Bearer <TYPESAFE_API_KEY>
 *   { state, model: "jev-1.13.0", questions: { id: {type, instructions, ...} } }
 *   → { model, answers: { id: <typed answer> }, usage }
 *
 * Contract (mirrors D:/Projects/chatgpt-jev/src/lib/judge.ts):
 * - Jev judges meaning; exact facts, timings and thresholds stay in code.
 * - Missing, failed or uncertain decisions stop explicitly — the caller marks
 *   the section "Needs manual review"; there is NO heuristic fallback.
 * - One retry inside a hard deadline; identical inputs may reuse a logged
 *   verdict (content hash cached next to the transcripts).
 * - Privacy: the key is read from env (TYPESAFE_API_KEY) or the gitignored
 *   .env.local at the repo root; it is never printed, logged, or embedded in
 *   error messages, reports, or cache files.
 */

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const API_URL = 'https://api.typesafe.ai/v1/systemone';
// Pinned, never the `jev-latest` alias: thresholds are tuned against a fixed
// version. Mirrors TypeSafeOptions.Model; bump deliberately and re-run the
// jev-calibrate.yml workflow first.
const MODEL = 'jev-1.13.0';
export const JEV_TIMEOUT_MS = 45_000;
export const NOUL_YES = 0.7;
export const NOUL_NO = 0.3;

export class JevError extends Error {
  constructor(site, detail) {
    super(`Jev judgment unavailable at ${site}: ${detail}`);
    this.name = 'JevError';
    this.site = site;
  }
}

/** Locate the key without ever revealing it. Env wins over .env.local. */
export function loadTypesafeKey(repoRoot = join(__dirname, '..', '..')) {
  const fromEnv = process.env.TYPESAFE_API_KEY?.trim();
  if (fromEnv) return fromEnv;
  const envLocal = join(repoRoot, '.env.local');
  if (!existsSync(envLocal)) return null;
  for (const line of readFileSync(envLocal, 'utf-8').split(/\r?\n/)) {
    const m = line.match(/^TYPESAFE_API_KEY=(.+)\s*$/);
    if (m) return m[1].trim();
  }
  return null;
}

export function jevConfigured() {
  return Boolean(loadTypesafeKey());
}

function extractAnswers(payload) {
  // Documented shape: { model, answers: {...}, usage } — tolerate a flat
  // {<id>: answer} body in case the gateway trims the envelope.
  if (payload && typeof payload === 'object') {
    if (payload.answers && typeof payload.answers === 'object') return payload.answers;
    if (!Array.isArray(payload)) return payload;
  }
  throw new JevError('response', 'unrecognised SystemOne answer envelope');
}

export function normalizeAnswer(raw) {
  if (raw == null) return null;
  if (typeof raw === 'number') return { probability: raw };
  if (typeof raw !== 'object') return null;
  // Wire format (proven live): noul → {"type":"noul","noul":<p>},
  // score → {"type":"score","score":<n>}, choice → {"type":"choice",
  // "choice":"<id>","probabilities":{...},"confidence":<n>}.
  if (typeof raw.noul === 'number') return { probability: raw.noul };
  if (typeof raw.probability === 'number') return { probability: raw.probability };
  if (typeof raw.score === 'number') return { score: raw.score };
  if (typeof raw.choice === 'string') {
    return { choice: raw.choice, probabilities: raw.probabilities ?? null, confidence: raw.confidence ?? null };
  }
  return null;
}

/** One batch of independent questions over shared state. */
export async function jevJudge(site, state, questions, { timeoutMs = JEV_TIMEOUT_MS } = {}) {
  const key = loadTypesafeKey();
  if (!key) throw new JevError(site, 'TYPESAFE_API_KEY not configured');
  const body = JSON.stringify({ state, model: MODEL, questions });
  let lastErr = null;
  for (let attempt = 1; attempt <= 2; attempt++) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
      const res = await fetch(API_URL, {
        method: 'POST',
        headers: { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json', Accept: 'application/json' },
        body,
        signal: controller.signal,
      });
      const text = await res.text();
      if (res.status === 429 || res.status === 529 || res.status >= 500) {
        lastErr = new JevError(site, `gateway HTTP ${res.status}`);
        if (attempt === 1) {
          await new Promise((r) => setTimeout(r, 3000));
          continue;
        }
        throw lastErr;
      }
      if (!res.ok) throw new JevError(site, `HTTP ${res.status}`);
      let parsed;
      try { parsed = JSON.parse(text); } catch { throw new JevError(site, 'non-JSON response body'); }
      if (parsed?.error) throw new JevError(site, `reported error ${String(parsed.error).slice(0, 120)}`);
      const answers = extractAnswers(parsed);
      const out = {};
      for (const [id, q] of Object.entries(questions)) {
        const norm = normalizeAnswer(answers?.[id]);
        if (!norm) throw new JevError(site, `missing/invalid typed answer for "${id}"`);
        out[id] = norm;
      }
      return out;
    } catch (err) {
      if (err instanceof JevError) { lastErr = err; if (err.message.includes('gateway HTTP') && attempt === 1) continue; throw err; }
      lastErr = err.name === 'AbortError' ? new JevError(site, `timed out after ${timeoutMs}ms`) : new JevError(site, err.message || 'request failed');
      if (attempt === 1) {
        await new Promise((r) => setTimeout(r, 3000));
        continue;
      }
      throw lastErr;
    } finally {
      clearTimeout(timer);
    }
  }
  throw lastErr ?? new JevError(site, 'exhausted attempts');
}

/** Decisive Noul: 'yes' | 'no' | 'unsure' (unsure → manual review upstream). */
export function noulVerdict(answer, { yes = NOUL_YES, no = NOUL_NO } = {}) {
  const p = answer?.probability;
  if (!Number.isFinite(p)) return 'unsure';
  if (p >= yes) return 'yes';
  if (p <= no) return 'no';
  return 'unsure';
}

/** Decisive Choice: top option only when clearly separated from the runner-up. */
export function confidentChoice(answer, { minProbability = 0.7, minMargin = 0.3 } = {}) {
  const probs = answer?.probabilities;
  if (answer?.choice && !probs) return { choice: answer.choice, confident: true };
  if (!probs || typeof probs !== 'object') return { choice: null, confident: false };
  const sorted = Object.entries(probs).sort(([, a], [, b]) => b - a);
  const [top, topP] = [sorted[0]?.[0], sorted[0]?.[1]];
  const runnerUp = sorted[1]?.[1] ?? 0;
  if (topP == null || !Number.isFinite(topP)) return { choice: null, confident: false };
  const confident = topP >= minProbability && topP - runnerUp >= minMargin;
  return { choice: top, confident };
}
