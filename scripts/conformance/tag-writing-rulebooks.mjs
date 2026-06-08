#!/usr/bin/env node
/**
 * Reclassify the untagged critical/major Writing rules with an honest
 * enforcement marker (decision 1 / deviation D2). These rules have no
 * deterministic detector; they are graded by the rulebook-grounded Writing AI
 * assessor (`ai-grounded`) — except the five-minute reading window, which is an
 * exam-DELIVERY behaviour, not letter grading (`human-review-only`).
 *
 * Only ADDS an `enforcement` field; never changes ids, severities, bodies, or
 * any checkId. Idempotent. Run: node scripts/conformance/tag-writing-rulebooks.mjs
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();

// All graded by the rulebook-grounded Writing AI assessor.
const AI_GROUNDED = [
  'R01.8', 'R03.5', 'R03.7', 'R06.4', 'R06.5', 'R07.2', 'R08.2', 'R08.11', 'R08.15',
  'R09.6', 'R10.3', 'R10.4', 'R10.7', 'R10.12', 'R10.13', 'R13.5', 'R13.6', 'R13.7',
  'R13.8', 'R14.5', 'R14.11', 'R14.13', 'R15.3', 'R15.4', 'R15.8',
  'R16.2', 'R16.3', 'R16.4', 'R16.5',
];
// Exam-process / delivery behaviour, not letter grading.
const HUMAN_REVIEW = ['R02.2'];

const MAP = new Map();
for (const id of AI_GROUNDED) MAP.set(id, 'ai-grounded');
for (const id of HUMAN_REVIEW) MAP.set(id, 'human-review-only');

const PROFESSIONS = [
  'medicine', 'nursing', 'dentistry', 'pharmacy', 'physiotherapy', 'veterinary',
  'optometry', 'radiography', 'occupational-therapy', 'speech-pathology',
  'podiatry', 'dietetics', 'other-allied-health',
];

let totalTagged = 0;
for (const profession of PROFESSIONS) {
  const rel = `rulebooks/writing/${profession}/rulebook.v1.json`;
  const abs = join(ROOT, rel);
  const book = JSON.parse(readFileSync(abs, 'utf8').replace(/^﻿/, ''));
  let tagged = 0;
  for (const rule of book.rules) {
    const enforcement = MAP.get(rule.id);
    if (!enforcement) continue;
    if (rule.checkId) continue; // never override a real detector
    rule.enforcement = enforcement;
    tagged += 1;
  }
  writeFileSync(abs, `${JSON.stringify(book, null, 2)}\n`);
  totalTagged += tagged;
  console.log(`${rel}: tagged ${tagged} rule(s)`);
}
console.log(`done. total ${totalTagged} rule(s) across ${PROFESSIONS.length} professions.`);
