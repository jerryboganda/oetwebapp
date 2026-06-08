#!/usr/bin/env node
/**
 * Tag the Listening + Reading AUTHORING rulebooks with their enforcement.
 *
 * Structural rules get a deterministic `checkId` (backed by a detector in
 * lib/rulebook/{listening,reading}-rules.ts); content-judgement rules get
 * `enforcement: 'human-review-only'` (a human author/reviewer enforces them).
 * Idempotent — safe to re-run. Run: node scripts/conformance/tag-authoring-rulebooks.mjs
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();

const READING_MAP = {
  'R01.1': { checkId: 'reading_shape_42' },
  'R01.2': { enforcement: 'human-review-only' },
  'R02.1': { checkId: 'reading_part_a_four_texts' },
  'R02.2': { enforcement: 'human-review-only' },
  'R03.1': { checkId: 'reading_part_b_three_options' },
  'R04.1': { checkId: 'reading_part_c_four_options' },
  'R05.1': { enforcement: 'human-review-only' },
  'R05.2': { enforcement: 'human-review-only' },
};

const LISTENING_MAP = {
  'L01.1': { checkId: 'listening_shape_42' },
  'L01.2': { checkId: 'listening_part_split_24_6_12' },
  'L01.3': { enforcement: 'human-review-only' },
  'L02.1': { checkId: 'listening_part_a_short_answer_only' },
  'L02.2': { enforcement: 'human-review-only' },
  'L02.3': { enforcement: 'human-review-only' },
  'L03.1': { checkId: 'listening_part_b_three_options' },
  'L03.2': { enforcement: 'human-review-only' },
  'L04.1': { checkId: 'listening_part_c_three_options' },
  'L04.2': { enforcement: 'human-review-only' },
  'L05.1': { checkId: 'listening_distractor_categories_valid' },
  'L05.2': { enforcement: 'human-review-only' },
  'L06.1': { checkId: 'listening_item_has_transcript_evidence' },
  'L06.2': { checkId: 'listening_transcript_timecodes_valid' },
  'L07.1': { checkId: 'listening_speaker_attitude_part_c_only' },
  'L07.2': { checkId: 'listening_speaker_attitude_enum_valid' },
  'L08.1': { enforcement: 'human-review-only' },
};

const READING_BOOKS = [
  'rulebooks/reading/medicine/rulebook.v1.json',
  'rulebooks/reading/nursing/rulebook.v1.json',
];
const LISTENING_BOOKS = [
  'rulebooks/listening/medicine/rulebook.v1.json',
  'rulebooks/listening/nursing/rulebook.v1.json',
  'rulebooks/listening/dentistry/rulebook.v1.json',
  'rulebooks/listening/physiotherapy/rulebook.v1.json',
];

function applyMap(relPath, map) {
  const abs = join(ROOT, relPath);
  const book = JSON.parse(readFileSync(abs, 'utf8').replace(/^﻿/, ''));
  let tagged = 0;
  for (const rule of book.rules) {
    const m = map[rule.id];
    if (!m) continue;
    if (m.checkId) {
      rule.checkId = m.checkId;
      delete rule.enforcement;
    } else if (m.enforcement) {
      rule.enforcement = m.enforcement;
      delete rule.checkId;
    }
    tagged += 1;
  }
  writeFileSync(abs, `${JSON.stringify(book, null, 2)}\n`);
  console.log(`${relPath}: tagged ${tagged} rule(s)`);
}

for (const p of READING_BOOKS) applyMap(p, READING_MAP);
for (const p of LISTENING_BOOKS) applyMap(p, LISTENING_MAP);
console.log('done.');
