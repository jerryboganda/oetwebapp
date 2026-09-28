#!/usr/bin/env node
/**
 * AES-256-GCM seal/open for QA evidence (transcripts of licensed audio must never sit in a public
 * Actions artifact). Key material comes from env EVIDENCE_KEY (a random secret, never printed).
 *   node scripts/listening/seal.mjs seal <in> <out>
 *   node scripts/listening/seal.mjs open <in> <out>
 */
import { createCipheriv, createDecipheriv, randomBytes, scryptSync } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

const MAGIC = Buffer.from('OETSEAL1');
const [cmd, inp, out] = process.argv.slice(2);
const pass = process.env.EVIDENCE_KEY;
if (!pass || !inp || !out || !['seal', 'open'].includes(cmd)) {
  console.error('usage: EVIDENCE_KEY=... seal.mjs <seal|open> <in> <out>');
  process.exit(1);
}
if (cmd === 'seal') {
  const salt = randomBytes(16), iv = randomBytes(12);
  const c = createCipheriv('aes-256-gcm', scryptSync(pass, salt, 32), iv);
  const enc = Buffer.concat([c.update(readFileSync(inp)), c.final()]);
  writeFileSync(out, Buffer.concat([MAGIC, salt, iv, c.getAuthTag(), enc]));
} else {
  const b = readFileSync(inp);
  if (!b.subarray(0, 8).equals(MAGIC)) throw new Error('not a sealed file');
  const d = createDecipheriv('aes-256-gcm', scryptSync(pass, b.subarray(8, 24), 32), b.subarray(24, 36));
  d.setAuthTag(b.subarray(36, 52));
  writeFileSync(out, Buffer.concat([d.update(b.subarray(52)), d.final()]));
}
