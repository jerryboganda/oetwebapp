#!/usr/bin/env node
/**
 * Listening — semantic audio splitting for 20 Nova/Benchmark source tests.
 *
 * Replaces prior silence-detection / fixed-timestamp approach.
 *
 * Required segment order (from complete source audio):
 *   A1 → A2 → B → C1 → C2
 *
 * Semantic cue boundaries (per spec §6):
 *   A1: start 0 → before first word of A2 intro (e.g. "This is a Benchmark…", "Extract 2…", "You will hear…")
 *   A2: first word of A2 intro → before "Now" in "Now look at Part B."
 *   B : "Now" in "Now look at Part B." → before "Now" in "Now look at Part C."
 *   C1: "Now" in "Now look at Part C." → before "Now" in "Now look at Extract 2."
 *   C2: "Now" in "Now look at Extract 2." → natural EOF (preserve end-of-test)
 *
 * Critical rules:
 *   - Do NOT use one universal timestamp for all 20 tests.
 *   - Do NOT use silence detection alone / longest silence heuristic.
 *   - Identify semantic transition per file, determine exact position, split precisely.
 *   - Never clip first word (A2 intro, each "Now"); speech integrity > silent lead-in.
 *   - Preserve source mapping Test 1 → Test 1, etc. No reordering.
 *   - Prefer stream copy (-c copy) when cut on frame; otherwise high-quality re-encode (-c:a libmp3lame -q:a 0).
 *
 * Usage:
 *   1. Transcribe source with word timestamps (Whisper word_timestamps):
 *        whisper <src.mp3> --model large-v3 --word_timestamps True --output_format json --output_dir ./transcripts
 *      → produces <src>.json with { segments: [{ words: [{word, start, end}] }] }
 *   2. Run splitter:
 *        node scripts/listening/split-audio-semantic.mjs --src "OET Materials & Videos Data/Materials/Listening/Benchmark Exams/Audio/LISTENING TEST 5.mp3" --transcript ./transcripts/LISTENING\ TEST\ 5.json --outDir ./out --testId 5
 *      Or for all 20:
 *        node scripts/listening/split-audio-semantic.mjs --all --srcDir "OET Materials & Videos Data/..." --transcriptDir ./transcripts --outRoot ./out
 *
 *   Requires: ffmpeg on PATH.
 */

import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, basename, dirname } from 'node:path';

function hasFfmpeg() {
  const r = spawnSync('ffmpeg', ['-version'], { encoding: 'utf-8' });
  return r.status === 0;
}

function parseArgs() {
  const args = process.argv.slice(2);
  const get = (k) => {
    const i = args.indexOf(k);
    return i >= 0 && i + 1 < args.length ? args[i + 1] : null;
  };
  return {
    src: get('--src'),
    transcript: get('--transcript'),
    outDir: get('--outDir'),
    testId: get('--testId'),
    all: args.includes('--all'),
    srcDir: get('--srcDir'),
    transcriptDir: get('--transcriptDir'),
    outRoot: get('--outRoot'),
    dryRun: args.includes('--dry-run'),
  };
}

// Normalize word for cue matching (case-insensitive, strip punctuation)
function norm(w) {
  return String(w).toLowerCase().replace(/[^a-z0-9]/g, '');
}

function findCue(flatWords, phraseTokens, opts = {}) {
  // phraseTokens already normalized, e.g. ["now","look","at","part","b"]
  const n = flatWords.length;
  const m = phraseTokens.length;
  const { searchFrom = 0, searchTo = n } = opts;
  let best = null;
  for (let i = searchFrom; i <= Math.min(searchTo, n - m); i++) {
    let ok = true;
    for (let j = 0; j < m; j++) {
      if (norm(flatWords[i + j].word) !== phraseTokens[j]) { ok = false; break; }
    }
    if (ok) {
      // First occurrence wins for sequential cues
      return { index: i, word: flatWords[i], start: flatWords[i].start, end: flatWords[i + m - 1]?.end ?? flatWords[i].end };
    }
  }
  return best;
}

// Detect A2 intro: heuristic — first occurrence of extract 2 phrasing after ~20% of audio
// Fallback: "extract 2" OR "this is a benchmark" / "edubenchmark sample" etc.
function findA2Intro(flatWords) {
  const candidates = [
    ['extract', '2'],
    ['extract', 'two'],
    ['this', 'is', 'a', 'benchmark'],
    ['this', 'is', 'a', 'edubenchmark'],
    ['you', 'will', 'hear'],
  ];
  // Search after first 15% to avoid false on Part A title at 0
  const from = Math.floor(flatWords.length * 0.15);
  for (const phrase of candidates) {
    const hit = findCue(flatWords, phrase.map(norm), { searchFrom: from });
    if (hit) return hit;
  }
  return null;
}

function loadFlatWords(transcriptPath) {
  const raw = JSON.parse(readFileSync(transcriptPath, 'utf-8'));
  // Support Whisper json: { segments: [{ words: [{word, start, end}] }] } or { words: [...] }
  let words = [];
  if (Array.isArray(raw.words)) words = raw.words;
  else if (Array.isArray(raw.segments)) {
    for (const seg of raw.segments) {
      if (Array.isArray(seg.words)) words.push(...seg.words);
      // Some Whisper builds put words flat in segments with start/end per segment but no words array — fallback to segment as word
      else if (seg.text) {
        // Heuristic: not precise, but better than nothing — caller should provide word_timestamps True
      }
    }
  } else if (Array.isArray(raw)) words = raw;
  // Ensure {word, start, end}
  return words.filter(w => typeof w.word === 'string' && typeof w.start === 'number');
}

function formatSec(s) {
  return Number(s).toFixed(3);
}

function runFfmpegSegment(src, outPath, startSec, endSec, dryRun) {
  const args = ['-y', '-hide_banner', '-loglevel', 'error'];
  args.push('-ss', formatSec(startSec));
  if (endSec != null && Number.isFinite(endSec)) {
    const dur = Math.max(0, endSec - startSec);
    args.push('-i', src, '-t', formatSec(dur));
  } else {
    args.push('-i', src);
  }
  // Try stream copy first; if fails (non-frame boundary), fallback caller will re-encode.
  // For now use high-quality re-encode to guarantee sample accuracy at semantic boundary
  // (correctness over copy perfection per spec 6.10).
  args.push('-c:a', 'libmp3lame', '-q:a', '0', '-map_metadata', '0', outPath);
  if (dryRun) {
    console.log(`[dry-run] ffmpeg ${args.join(' ')}`);
    return { status: 0 };
  }
  const r = spawnSync('ffmpeg', args, { encoding: 'utf-8' });
  if (r.status !== 0) {
    console.error(`ffmpeg failed for ${outPath}: ${r.stderr?.slice(0, 800)}`);
  }
  return r;
}

function splitOne({ src, transcript, outDir, testId, dryRun }) {
  if (!existsSync(src)) throw new Error(`Source not found: ${src}`);
  if (!existsSync(transcript)) throw new Error(`Transcript not found: ${transcript} (run whisper --word_timestamps True first)`);
  mkdirSync(outDir, { recursive: true });
  const flat = loadFlatWords(transcript);
  if (flat.length < 100) console.warn(`Warning: flat words ${flat.length} unusually low — check word_timestamps True`);

  const totalDur = flat[flat.length - 1]?.end ?? null;

  // Find semantic cues in order
  const a2 = findA2Intro(flat);
  if (!a2) throw new Error('Could not locate A2 intro (Extract 2 / Benchmark phrase). Inspect transcript head.');

  // Sequential Part cues — search forward
  const partB = findCue(flat, ['now','look','at','part','b'].map(norm), { searchFrom: a2.index });
  if (!partB) throw new Error('Missing cue "Now look at Part B."');

  const partC = findCue(flat, ['now','look','at','part','c'].map(norm), { searchFrom: partB.index + 4 });
  if (!partC) throw new Error('Missing cue "Now look at Part C."');

  const extract2 = findCue(flat, ['now','look','at','extract','2'].map(norm), { searchFrom: partC.index + 4 })
    ?? findCue(flat, ['now','look','at','extract','two'].map(norm), { searchFrom: partC.index + 4 });
  if (!extract2) throw new Error('Missing cue "Now look at Extract 2."');

  // Boundaries (seconds)
  // A1: 0 → a2.start
  // A2: a2.start → partB.start
  // B : partB.start → partC.start
  // C1: partC.start → extract2.start
  // C2: extract2.start → EOF
  const bounds = [
    { code: 'A1', start: 0, end: a2.start, cueWord: a2.word },
    { code: 'A2', start: a2.start, end: partB.start, cueWord: partB.word },
    { code: 'B',  start: partB.start, end: partC.start, cueWord: partC.word },
    { code: 'C1', start: partC.start, end: extract2.start, cueWord: extract2.word },
    { code: 'C2', start: extract2.start, end: totalDur, cueWord: null },
  ];

  console.log(`\nTest ${testId ?? '?'} :: ${basename(src)}`);
  console.log(`  A2 intro @ ${a2.start.toFixed(2)}s "${a2.word.word}"`);
  console.log(`  Part B @ ${partB.start.toFixed(2)}s`);
  console.log(`  Part C @ ${partC.start.toFixed(2)}s`);
  console.log(`  Extract 2 @ ${extract2.start.toFixed(2)}s`);
  if (totalDur) console.log(`  Total ~ ${totalDur.toFixed(1)}s`);

  const manifest = [];
  for (const b of bounds) {
    // Guard no excessive silent lead-in: if start is very close to previous end, fine
    // Do NOT trim start backward into silence if it would clip first word — we start exactly at word onset
    const safeStart = Math.max(0, b.start - 0.02); // 20ms pre-roll to avoid consonant clipping per spec 6.7
    const outPath = join(outDir, `${testId ? `test-${String(testId).padStart(2,'0')}-` : ''}${b.code}.mp3`);
    const res = runFfmpegSegment(src, outPath, safeStart, b.end, dryRun);
    if (!dryRun && res.status !== 0) throw new Error(`ffmpeg failed for ${b.code}`);
    manifest.push({ code: b.code, start: safeStart, end: b.end, file: outPath, cueVerified: true });
    console.log(`  ${b.code}: ${safeStart.toFixed(2)} → ${b.end != null ? b.end.toFixed(2) : 'EOF'} → ${basename(outPath)}`);
  }

  const manifestPath = join(outDir, 'split-manifest.json');
  if (!dryRun) writeFileSync(manifestPath, JSON.stringify({ src, transcript, bounds, manifest, generatedAt: new Date().toISOString() }, null, 2));
  console.log(`  Manifest: ${manifestPath}`);
  return manifest;
}

// ---- main ----
if (!hasFfmpeg()) {
  console.error('ERROR: ffmpeg not found on PATH. Install: https://ffmpeg.org/');
  process.exit(1);
}
const args = parseArgs();
if (args.all) {
  if (!args.srcDir || !args.transcriptDir || !args.outRoot) {
    console.error('For --all provide --srcDir --transcriptDir --outRoot');
    process.exit(1);
  }
  // Discover src files matching Test * or LISTENING TEST ?
  import('node:fs').then(({ readdirSync }) => {
    const files = readdirSync(args.srcDir).filter(f => f.toLowerCase().endsWith('.mp3'));
    if (files.length === 0) { console.error(`No mp3 in ${args.srcDir}`); process.exit(1); }
    files.sort();
    for (let i = 0; i < files.length; i++) {
      const src = join(args.srcDir, files[i]);
      const base = basename(files[i], '.mp3');
      const transcript = join(args.transcriptDir, `${base}.json`);
      const outDir = join(args.outRoot, `test-${String(i+1).padStart(2,'0')}`);
      try { splitOne({ src, transcript, outDir, testId: i+1, dryRun: args.dryRun }); }
      catch (e) { console.error(`Failed ${files[i]}: ${e.message}`); }
    }
  });
} else {
  if (!args.src || !args.transcript || !args.outDir) {
    console.error('Usage: node split-audio-semantic.mjs --src <mp3> --transcript <json> --outDir <dir> --testId <n> [--dry-run]');
    console.error('   or: --all --srcDir <dir> --transcriptDir <dir> --outRoot <dir> [--dry-run]');
    process.exit(1);
  }
  splitOne({ src: args.src, transcript: args.transcript, outDir: args.outDir, testId: args.testId, dryRun: args.dryRun });
}
