/**
 * Content Migration Script
 * ========================
 * Transforms Google Drive OET content JSON into platform-native import format.
 *
 * Usage:
 *   node scripts/migrate-content.mjs <input.json> [--output <output.json>] [--dry-run]
 *
 * Input format: Array of objects with at minimum { title, subtestCode }
 * Output format: Array of ContentImportRow objects ready for /v1/admin/content/bulk-import
 *
 * Transformations applied:
 * 1. Normalise subtest codes (Writing → writing, etc.)
 * 2. Infer source provenance from filename patterns
 * 3. Map difficulty from legacy labels to canonical values
 * 4. Detect and set content type (task, strategy_guide, recall, benchmark)
 * 5. Parse criteria focus from legacy tag fields
 * 6. Set freshness confidence based on dates
 */

import { readFileSync, writeFileSync } from 'node:fs';

const SUBTEST_MAP = {
  writing: 'writing', w: 'writing', wr: 'writing',
  speaking: 'speaking', s: 'speaking', sp: 'speaking',
  reading: 'reading', r: 'reading', rd: 'reading',
  listening: 'listening', l: 'listening', ls: 'listening',
};

const DIFFICULTY_MAP = {
  easy: 'easy', beginner: 'easy', basic: 'easy',
  medium: 'intermediate', intermediate: 'intermediate', moderate: 'intermediate',
  hard: 'advanced', advanced: 'advanced', difficult: 'advanced', challenging: 'advanced',
};

const PROVENANCE_PATTERNS = [
  [/official[-_\s]?sample/i, 'official_sample'],
  [/recall/i, 'recall'],
  [/benchmark/i, 'benchmark'],
  [/contributed|community/i, 'contributed'],
];

function normaliseSubtest(raw) {
  if (!raw) return null;
  return SUBTEST_MAP[raw.toLowerCase().trim()] ?? raw.toLowerCase().trim();
}

function normaliseDifficulty(raw) {
  if (!raw) return 'intermediate';
  return DIFFICULTY_MAP[raw.toLowerCase().trim()] ?? 'intermediate';
}

function inferProvenance(item) {
  const haystack = `${item.title ?? ''} ${item.filename ?? ''} ${item.source ?? ''}`;
  for (const [pattern, provenance] of PROVENANCE_PATTERNS) {
    if (pattern.test(haystack)) return provenance;
  }
  return 'original';
}

function inferContentType(item) {
  const title = (item.title ?? '').toLowerCase();
  if (title.includes('strategy') || title.includes('guide') || title.includes('tip')) return 'strategy_guide';
  if (title.includes('recall')) return 'recall';
  if (title.includes('benchmark') || title.includes('sample answer')) return 'benchmark';
  return 'task';
}

function inferFreshness(item) {
  if (!item.date && !item.createdAt && !item.lastModified) return 'likely_current';
  const dateStr = item.date || item.createdAt || item.lastModified;
  const date = new Date(dateStr);
  if (isNaN(date.getTime())) return 'likely_current';
  const ageMonths = (Date.now() - date.getTime()) / (1000 * 60 * 60 * 24 * 30);
  if (ageMonths < 6) return 'current';
  if (ageMonths < 18) return 'likely_current';
  if (ageMonths < 36) return 'aging';
  return 'superseded';
}

function parseCriteriaFocus(item) {
  const tags = [];
  if (item.criteriaFocus) {
    if (Array.isArray(item.criteriaFocus)) return JSON.stringify(item.criteriaFocus);
    if (typeof item.criteriaFocus === 'string') {
      tags.push(...item.criteriaFocus.split(/[,;|]/).map(t => t.trim()).filter(Boolean));
    }
  }
  if (item.tags && typeof item.tags === 'string') {
    tags.push(...item.tags.split(/[,;|]/).map(t => t.trim()).filter(Boolean));
  }
  return JSON.stringify([...new Set(tags)]);
}

function transformRow(item, index) {
  const subtestCode = normaliseSubtest(item.subtestCode || item.subtest || item.type);
  if (!subtestCode) {
    console.warn(`  Row ${index}: Skipping — no subtest code.`);
    return null;
  }
  if (!item.title) {
    console.warn(`  Row ${index}: Skipping — no title.`);
    return null;
  }

  return {
    rowIndex: index,
    title: item.title.trim(),
    subtestCode,
    contentType: inferContentType(item),
    professionId: item.professionId || item.profession || null,
    difficulty: normaliseDifficulty(item.difficulty || item.level),
    estimatedDurationMinutes: item.estimatedDurationMinutes || item.duration || null,
    scenarioType: item.scenarioType || item.scenario || null,
    detailJson: item.detailJson || item.detail ? JSON.stringify(item.detail ?? item.detailJson) : null,
    modelAnswerJson: item.modelAnswerJson || item.modelAnswer ? JSON.stringify(item.modelAnswer ?? item.modelAnswerJson) : null,
    instructionLanguage: item.instructionLanguage || item.language || 'en',
    contentLanguage: item.contentLanguage || item.language || 'en',
    sourceProvenance: item.sourceProvenance || inferProvenance(item),
    rightsStatus: item.rightsStatus || 'owned',
    freshnessConfidence: item.freshnessConfidence || inferFreshness(item),
    canonicalSourcePath: item.canonicalSourcePath || item.sourcePath || item.driveUrl || null,
    qualityScore: item.qualityScore ?? 0,
    criteriaFocusJson: parseCriteriaFocus(item),
    modeSupportJson: item.modeSupportJson || JSON.stringify(['practice']),
  };
}

// ── CLI ──

const args = process.argv.slice(2);
const inputPath = args.find(a => !a.startsWith('--'));
const outputPath = args.includes('--output') ? args[args.indexOf('--output') + 1] : null;
const dryRun = args.includes('--dry-run');

if (!inputPath) {
  console.error('Usage: node scripts/migrate-content.mjs <input.json> [--output <output.json>] [--dry-run]');
  process.exit(1);
}

console.log(`Reading ${inputPath}…`);
const raw = JSON.parse(readFileSync(inputPath, 'utf-8'));
const items = Array.isArray(raw) ? raw : [raw];
console.log(`  Found ${items.length} item(s).`);

const rows = items.map(transformRow).filter(Boolean);
console.log(`  Transformed ${rows.length} valid row(s).`);

if (dryRun) {
  console.log('\n-- DRY RUN: First 3 rows --');
  console.log(JSON.stringify(rows.slice(0, 3), null, 2));
  console.log(`\nDone. ${rows.length} row(s) would be imported.`);
} else {
  const dest = outputPath ?? inputPath.replace(/\.json$/, '.import.json');
  writeFileSync(dest, JSON.stringify(rows, null, 2), 'utf-8');
  console.log(`  Output written to ${dest}`);
  console.log(`\nNext: POST the output to /v1/admin/content/bulk-import with { "batchTitle": "...", "rows": [...] }`);
}
