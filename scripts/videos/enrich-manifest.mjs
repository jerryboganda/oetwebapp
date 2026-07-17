#!/usr/bin/env node
// Turn the Bunny upload state (scripts/videos/state/state.json) into an app-wiring
// registration plan: per-video metadata + gating, plus the ordered VideoCategory
// (learner shelf) list that mirrors the on-disk folder hierarchy.
//
// Output: scripts/videos/state/registration-plan.json
//   { categories: [{ title, moduleOrder, sortIndex }], videos: [{ bunnyVideoId,
//     collectionGuid, title, subtestCode, targetProfessionIds, professionConfidence,
//     accessTier, tagsCsv, categoryTitle }] }
//
// Read-only: hits no network, writes only the plan file. Safe to run anytime.

import fsp from 'node:fs/promises';
import path from 'node:path';

const STATE_DIR = process.env.VIDEO_STATE_DIR || path.join(process.cwd(), 'scripts', 'videos', 'state');
const STATE_PATH = path.join(STATE_DIR, 'state.json');
const OUT_PATH = path.join(STATE_DIR, 'registration-plan.json');

const MODULE_ORDER = { listening: 0, reading: 1, speaking: 2, writing: 3 };
const PROFESSIONS = ['medicine', 'nursing', 'pharmacy', 'physiotherapy', 'radiography', 'dentistry'];

// Default: every instructional video is premium (the course paywall). Flip specific
// intro/teaser videos to "free" by editing the plan before registering, if desired.
const DEFAULT_ACCESS_TIER = 'premium';

function detectProfession(segments) {
  const lower = segments.map((s) => s.toLowerCase());
  // High confidence: a path segment IS a profession.
  for (const p of PROFESSIONS) if (lower.includes(p)) return { profession: p, confidence: 'high' };
  // Medium confidence: a profession word appears inside a segment (e.g. "New Medicine Crash Course").
  for (const p of PROFESSIONS) if (lower.some((s) => s.includes(p))) return { profession: p, confidence: 'medium' };
  return { profession: null, confidence: 'none' };
}

function buildTags({ module, language, contentType, pathSegments }) {
  const tags = [];
  if (module) tags.push(`module:${module.toLowerCase()}`);
  if (language) tags.push(`lang:${language.toLowerCase()}`);
  if (contentType) tags.push(`type:${contentType.toLowerCase()}`);
  // Batch = a segment mentioning "batch" or "crash course".
  const batch = pathSegments.find((s) => /batch|crash course/i.test(s));
  if (batch) tags.push(`batch:${batch.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')}`);
  return tags.join(',');
}

async function main() {
  const state = JSON.parse(await fsp.readFile(STATE_PATH, 'utf8'));
  const videos = Object.values(state.videos || {});
  if (!videos.length) {
    console.error('No videos in state.json yet — run the upload first.');
    process.exit(1);
  }

  // Category list = distinct collectionName, ordered by module block then name.
  const catNames = [...new Set(videos.map((v) => v.collectionName))];
  catNames.sort((a, b) => {
    const ma = MODULE_ORDER[(a.split(' / ')[0] || '').toLowerCase()] ?? 99;
    const mb = MODULE_ORDER[(b.split(' / ')[0] || '').toLowerCase()] ?? 99;
    return ma - mb || a.localeCompare(b);
  });
  const categories = catNames.map((title, i) => ({
    title: title.length > 128 ? title.slice(0, 125) + '…' : title,
    fullTitle: title,
    moduleOrder: MODULE_ORDER[(title.split(' / ')[0] || '').toLowerCase()] ?? 99,
    sortIndex: i,
  }));

  const plan = {
    generatedFrom: STATE_PATH,
    accessTierDefault: DEFAULT_ACCESS_TIER,
    categories,
    videos: videos.map((v) => {
      const segs = v.pathSegments || v.collectionName.split(' / ');
      const prof = detectProfession(segs);
      return {
        relPath: v.relPath,
        bunnyVideoId: v.bunnyVideoId || null,
        collectionGuid: v.collectionGuid || null,
        uploadStatus: v.status,
        title: v.title,
        subtestCode: (v.module || segs[0] || '').toLowerCase() || null,
        targetProfessionIds: prof.profession ? [prof.profession] : [],
        professionConfidence: prof.confidence,
        accessTier: DEFAULT_ACCESS_TIER,
        tagsCsv: buildTags(v),
        categoryTitle: v.collectionName.length > 128 ? v.collectionName.slice(0, 125) + '…' : v.collectionName,
      };
    }),
  };

  await fsp.writeFile(OUT_PATH, JSON.stringify(plan, null, 2));

  // Human summary
  console.log(`Wrote ${OUT_PATH}`);
  console.log(`Videos: ${plan.videos.length}  (with guid: ${plan.videos.filter((v) => v.bunnyVideoId).length})`);
  console.log(`Categories (learner shelves): ${plan.categories.length}`);
  console.log(`\nProfession gating summary:`);
  const byProf = {};
  for (const v of plan.videos) {
    const key = v.targetProfessionIds[0] || '(all professions)';
    byProf[key] = (byProf[key] || 0) + 1;
  }
  for (const [k, n] of Object.entries(byProf)) console.log(`  ${k}: ${n}`);
  const medium = plan.videos.filter((v) => v.professionConfidence === 'medium');
  if (medium.length) {
    console.log(`\n⚠ MEDIUM-confidence profession (review these ${medium.length}):`);
    for (const v of medium) console.log(`  [${v.targetProfessionIds[0]}] ${v.relPath}`);
  }
}

main().catch((e) => { console.error('FATAL', e.message); process.exit(1); });
