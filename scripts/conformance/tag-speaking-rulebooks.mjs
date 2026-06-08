#!/usr/bin/env node
/**
 * Reclassify the untagged critical/major Speaking rules with an honest
 * enforcement marker (decisions / deviations D5, D7).
 *
 *   - consultation & content rules  -> ai-grounded   (the rulebook-grounded
 *     Speaking AI assessor evaluates them from the transcript)
 *   - tone of voice (RULE_40)       -> human-review-only (acoustic, not text)
 *   - exam-process facts + CBT      -> human-review-only (exam design / proctor
 *     / device-check flow — not transcript-gradeable)
 *
 * The three dead `speaking_cbt_*` checkIds (no detector in either engine) are
 * REMOVED and replaced with human-review-only.
 *
 * Idempotent; only touches `enforcement` and removes the dead CBT checkIds.
 * Run: node scripts/conformance/tag-speaking-rulebooks.mjs
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();

const AI_GROUNDED = [
  'RULE_05', 'RULE_13', 'RULE_14', 'RULE_16', 'RULE_17', 'RULE_18', 'RULE_26',
  'RULE_28', 'RULE_29', 'RULE_30', 'RULE_31', 'RULE_36', 'RULE_48', 'RULE_56', 'RULE_70',
];
const HUMAN_REVIEW = ['RULE_40', 'RULE_57', 'RULE_58', 'RULE_61', 'RULE_71', 'RULE_72', 'RULE_75'];
// human-review-only AND strip the dead checkId
const HUMAN_REVIEW_DROP_CHECKID = ['RULE_59', 'RULE_60', 'RULE_62', 'RULE_73', 'RULE_74', 'RULE_76'];

const MAP = new Map();
for (const id of AI_GROUNDED) MAP.set(id, { enforcement: 'ai-grounded' });
for (const id of HUMAN_REVIEW) MAP.set(id, { enforcement: 'human-review-only' });
for (const id of HUMAN_REVIEW_DROP_CHECKID) MAP.set(id, { enforcement: 'human-review-only', dropCheckId: true });

const PROFESSIONS = ['medicine', 'nursing', 'dentistry', 'pharmacy', 'physiotherapy', 'other-allied-health'];

let total = 0;
for (const profession of PROFESSIONS) {
  const rel = `rulebooks/speaking/${profession}/rulebook.v1.json`;
  const abs = join(ROOT, rel);
  const book = JSON.parse(readFileSync(abs, 'utf8').replace(/^﻿/, ''));
  let tagged = 0;
  for (const rule of book.rules) {
    const m = MAP.get(rule.id);
    if (!m) continue;
    if (m.dropCheckId) delete rule.checkId;
    if (rule.checkId) continue; // never override a real detector
    rule.enforcement = m.enforcement;
    tagged += 1;
  }
  writeFileSync(abs, `${JSON.stringify(book, null, 2)}\n`);
  total += tagged;
  console.log(`${rel}: tagged ${tagged} rule(s)`);
}
console.log(`done. total ${total} rule(s) across ${PROFESSIONS.length} professions.`);
