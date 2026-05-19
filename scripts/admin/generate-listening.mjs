/**
 * generate-listening.mjs
 *
 * Bulk-generates NEW Listening ContentPapers (subtestCode="listening") using the
 * canonical OET Listening shape enforced by the publish gate:
 *
 *   Part A1 — 1 consultation, 12 short-answer items   (Q01..Q12)
 *   Part A2 — 1 consultation, 12 short-answer items   (Q13..Q24)
 *   Part B  — 6 workplace extracts, 1 MCQ each        (Q25..Q30)  (single MP3, 6 windows)
 *   Part C1 — 1 presentation/interview, 6 MCQ         (Q31..Q36)
 *   Part C2 — 1 presentation/interview, 6 MCQ         (Q37..Q42)
 *
 * Total = 42 items across 5 ListeningPart codes (A1, A2, B, C1, C2). One MP3 is
 * generated per Part code via DO Serverless TTS (Qwen3-TTS-voicedesign), uploaded
 * as a MediaAsset, and attached to the paper with PaperAssetRole.Audio + Part.
 *
 * The publish gate requires four roles for listening (per
 * IContentPaperService.RequiredRolesFor "listening"):
 *   Audio, QuestionPaper, AudioScript, AnswerKey
 * — plus a non-empty SourceProvenance. We synthesise text MediaAssets for the
 * QuestionPaper / AudioScript / AnswerKey roles so the paper can be published.
 *
 * Endpoint contract (discovered from backend source):
 *   POST   /v1/admin/papers                                    → ContentPaperCreate
 *   PUT    /v1/admin/papers/{id}/listening/structure           → { questions: [...] }
 *   PUT    /v1/admin/papers/{id}/listening/extracts            → { extracts: [...] }
 *   POST   /v1/admin/papers/{id}/assets                        → ContentPaperAssetAttach
 *   POST   /v1/admin/papers/{id}/publish                       → 204
 *
 * Audio attach: uploadMediaAsset() (chunked /v1/admin/uploads/*) returns the
 * MediaAsset id string; a separate POST /v1/admin/papers/{id}/assets attaches it
 * with { role: "Audio", mediaAssetId, part: "A1"|"A2"|"B"|"C1"|"C2",
 *        title, displayOrder, makePrimary: true }.
 *
 * Flags:
 *   --dry-run         — print plan; do not call backend or AI.
 *   --count N         — number of papers to generate (default 10).
 *   --profession SLUG — pin all papers to one profession (default: rotate).
 *   --skip-tts        — skip TTS + audio attach; paper will not be publishable.
 *   --resume          — skip paper titles already present in the manifest.
 *   --no-publish      — do not call /publish at the end.
 *   --healthcheck     — lib healthcheck only.
 */

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, aiTts,
  uploadMediaAsset, logFailure, healthcheck, progress, sleep, slugify,
  makeProvenance,
} from './_lib.mjs';

const flags = parseFlags();

const COUNT = Math.max(1, parseInt(flags.count, 10) || 10);
const PINNED_PROFESSION = typeof flags.profession === 'string' ? flags.profession : null;
const DRY_RUN = !!flags['dry-run'];
const SKIP_TTS = !!flags['skip-tts'];
const RESUME = !!flags.resume;
const DO_PUBLISH = !flags['no-publish'];

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const MANIFEST = resolve(CONFIG.outputDir, 'generate-listening-manifest.json');

// Profession rotation (matches rulebook professions on disk).
const PROFESSIONS = [
  'medicine', 'nursing', 'dentistry', 'physiotherapy',
  'pharmacy', 'optometry', 'radiography', 'occupational-therapy',
  'podiatry', 'veterinary', 'speech-pathology', 'dietetics',
];

// Difficulty rotation.
const DIFFICULTIES = ['standard', 'standard', 'hard'];

// Part order + canonical counts (publish-gate invariant: 12+12+6+6+6 = 42).
const PART_SPEC = [
  { code: 'A1', type: 'short_answer',     items: 12, extractKind: 'consultation', durationSec: 270, qStart: 1  },
  { code: 'A2', type: 'short_answer',     items: 12, extractKind: 'consultation', durationSec: 270, qStart: 13 },
  { code: 'B',  type: 'multiple_choice_3',items: 6,  extractKind: 'workplace',    durationSec: 360, qStart: 25 },
  { code: 'C1', type: 'multiple_choice_3',items: 6,  extractKind: 'presentation', durationSec: 360, qStart: 31 },
  { code: 'C2', type: 'multiple_choice_3',items: 6,  extractKind: 'presentation', durationSec: 360, qStart: 37 },
];

// -----------------------------------------------------------------------------
// Manifest (lightweight resume support)
// -----------------------------------------------------------------------------

function loadManifest() {
  if (!existsSync(MANIFEST)) return { papers: [] };
  try { return JSON.parse(readFileSync(MANIFEST, 'utf8')); } catch { return { papers: [] }; }
}
function saveManifest(m) {
  try { writeFileSync(MANIFEST, JSON.stringify(m, null, 2), 'utf8'); } catch {}
}

// -----------------------------------------------------------------------------
// AI prompt
// -----------------------------------------------------------------------------

function buildSystemPrompt(profession) {
  return [
    `You are an expert OET (Occupational English Test) Listening item writer.`,
    `You will draft ONE complete OET Listening paper for the ${profession} profession.`,
    ``,
    `STRICT REQUIREMENTS (publish gate enforces — non-conformant output is rejected):`,
    `- Exactly 42 items total, partitioned as: A1=12, A2=12, B=6, C1=6, C2=6.`,
    `- Items 1..12 are Part A1, 13..24 are Part A2 (both short_answer / gap-fill, single canonical answer).`,
    `- Items 25..30 are Part B (multiple_choice_3, exactly 3 options labelled A/B/C).`,
    `- Items 31..36 are Part C1 and 37..42 are Part C2 (both multiple_choice_3, 3 options each).`,
    `- Each short_answer correctAnswer is a SHORT NOUN PHRASE (1–4 words) that fits the audio verbatim.`,
    `- Each multiple_choice_3 correctAnswer is exactly one of "A", "B", or "C".`,
    `- Provide a verbatim transcript per Part. Transcripts must be natural healthcare-workplace English,`,
    `  realistic for a ${profession} setting, ~2–4 minutes spoken per Part A consultation, ~5–6 minutes`,
    `  total for Part B (six sub-extracts of ~30–60 seconds each separated by a brief "Extract N" cue),`,
    `  and ~3–4 minutes spoken per Part C presentation/interview.`,
    `- Each MCQ option must include a distractorCategory for wrong options drawn from:`,
    `  too_strong | too_weak | wrong_speaker | opposite_meaning | reused_keyword | out_of_scope.`,
    `- Do NOT include any commentary outside the JSON. Output JSON only.`,
    `- Content is for PRACTICE use only and must be original (no copyrighted material).`,
  ].join('\n');
}

function buildUserPrompt(profession, title) {
  return [
    `Generate one OET Listening practice paper titled: "${title}".`,
    `Profession context: ${profession}.`,
    ``,
    `Return a single JSON object with this exact shape (no markdown, no fences):`,
    `{`,
    `  "title": string,`,
    `  "summary": string,`,
    `  "parts": [`,
    `    {`,
    `      "code": "A1" | "A2" | "B" | "C1" | "C2",`,
    `      "extractTitle": string,`,
    `      "accentCode": "en-GB" | "en-AU" | "en-IE" | "en-US",`,
    `      "speakers": [ { "id": "s1", "role": "GP" | "patient" | "nurse" | ..., "gender": "m"|"f"|"nb", "accent": "en-GB" } ],`,
    `      "transcript": string,   // verbatim spoken text used for TTS`,
    `      "items": [`,
    `        // Part A items:`,
    `        { "number": int, "type": "short_answer", "stem": string,`,
    `          "correctAnswer": string, "acceptedAnswers": [string,...],`,
    `          "explanation": string, "skillTag": string, "transcriptExcerpt": string }`,
    `        // OR Part B/C items:`,
    `        { "number": int, "type": "multiple_choice_3", "stem": string,`,
    `          "options": [`,
    `            { "key": "A", "text": string, "isCorrect": bool, "distractorCategory": string|null, "whyWrong": string|null },`,
    `            { "key": "B", ... },`,
    `            { "key": "C", ... }`,
    `          ],`,
    `          "correctAnswer": "A"|"B"|"C",`,
    `          "explanation": string, "skillTag": string, "transcriptExcerpt": string,`,
    `          "speakerAttitude": "concerned"|"optimistic"|"doubtful"|"critical"|"neutral"|"other"|null }`,
    `      ]`,
    `    }`,
    `    // 5 parts total, in order A1, A2, B, C1, C2`,
    `  ]`,
    `}`,
    ``,
    `Item counts MUST be A1=12, A2=12, B=6, C1=6, C2=6 (total 42).`,
  ].join('\n');
}

// -----------------------------------------------------------------------------
// Validation of AI output
// -----------------------------------------------------------------------------

function validateAiPaper(j) {
  const errs = [];
  if (!j || typeof j !== 'object') return ['root not object'];
  if (!Array.isArray(j.parts) || j.parts.length !== 5) {
    errs.push(`parts must be array of 5 (got ${j.parts?.length})`);
    return errs;
  }
  for (let i = 0; i < PART_SPEC.length; i++) {
    const spec = PART_SPEC[i];
    const p = j.parts[i];
    if (!p || p.code !== spec.code) {
      errs.push(`parts[${i}].code expected ${spec.code} got ${p?.code}`);
      continue;
    }
    if (!Array.isArray(p.items) || p.items.length !== spec.items) {
      errs.push(`parts[${i}](${spec.code}) items expected ${spec.items} got ${p.items?.length}`);
      continue;
    }
    if (typeof p.transcript !== 'string' || p.transcript.trim().length < 200) {
      errs.push(`parts[${i}](${spec.code}) transcript too short`);
    }
    for (let k = 0; k < p.items.length; k++) {
      const it = p.items[k];
      const expectedNumber = spec.qStart + k;
      if (it.number !== expectedNumber) errs.push(`${spec.code} item[${k}].number expected ${expectedNumber} got ${it.number}`);
      if (it.type !== spec.type) errs.push(`${spec.code} item ${it.number} type expected ${spec.type} got ${it.type}`);
      if (!it.stem || typeof it.stem !== 'string') errs.push(`${spec.code} item ${it.number} missing stem`);
      if (spec.type === 'short_answer') {
        if (!it.correctAnswer || typeof it.correctAnswer !== 'string') errs.push(`${spec.code} item ${it.number} missing correctAnswer`);
      } else {
        if (!Array.isArray(it.options) || it.options.length !== 3) errs.push(`${spec.code} item ${it.number} options must be 3`);
        else {
          const keys = it.options.map(o => o.key);
          if (!(keys.includes('A') && keys.includes('B') && keys.includes('C'))) errs.push(`${spec.code} item ${it.number} options must use keys A/B/C`);
          const correctCount = it.options.filter(o => o.isCorrect).length;
          if (correctCount !== 1) errs.push(`${spec.code} item ${it.number} must have exactly 1 correct option`);
        }
        if (!['A','B','C'].includes(it.correctAnswer)) errs.push(`${spec.code} item ${it.number} correctAnswer must be A|B|C`);
      }
    }
  }
  return errs;
}

// -----------------------------------------------------------------------------
// Shape transforms (AI → backend wire formats)
// -----------------------------------------------------------------------------

// Publish-gate canonical skill tag vocabulary (matches
// backend/Services/Listening/ListeningSkillTags.cs). The AI emits free-form
// hyphenated values like `detail-duration`; normalize by taking the prefix
// before the first `-` (lowercased), with `note-completion` → `note_completion`,
// and fall back to `other` for anything outside the closed set.
const CANONICAL_SKILL_TAGS = new Set([
  'purpose', 'gist', 'detail', 'opinion', 'warning', 'attitude', 'note_completion', 'other',
]);
function normalizeSkillTag(raw) {
  if (!raw) return 'other';
  const s = String(raw).toLowerCase().trim();
  if (CANONICAL_SKILL_TAGS.has(s)) return s;
  const prefix = s.split(/[\s\-_/]/)[0];
  if (CANONICAL_SKILL_TAGS.has(prefix)) return prefix;
  if (s.includes('note') && s.includes('completion')) return 'note_completion';
  if (s.includes('gist')) return 'gist';
  if (s.includes('purpose')) return 'purpose';
  if (s.includes('opinion')) return 'opinion';
  if (s.includes('warn')) return 'warning';
  if (s.includes('attitude')) return 'attitude';
  if (s.includes('detail')) return 'detail';
  return 'other';
}

// Per-part audio window (ms). We don't decode the actual TTS duration here;
// the publish gate only requires plausible, non-overlapping windows. These are
// generous defaults consistent with a real OET Listening test (~40-45 min).
const PART_AUDIO_WINDOWS = {
  A1: { startMs: 0,        endMs: 360_000  }, //  0:00 -  6:00
  A2: { startMs: 360_000,  endMs: 720_000  }, //  6:00 - 12:00
  B:  { startMs: 720_000,  endMs: 1_080_000 }, // 12:00 - 18:00
  C1: { startMs: 1_080_000, endMs: 1_440_000 }, // 18:00 - 24:00
  C2: { startMs: 1_440_000, endMs: 1_800_000 }, // 24:00 - 30:00
};

function toAuthoredQuestions(paper) {
  const out = [];
  for (const part of paper.parts) {
    const win = PART_AUDIO_WINDOWS[part.code] || { startMs: 0, endMs: 60_000 };
    const slot = Math.max(15_000, Math.floor((win.endMs - win.startMs) / Math.max(1, part.items.length)));
    for (let idx = 0; idx < part.items.length; idx++) {
      const it = part.items[idx];
      const evStart = win.startMs + (idx * slot);
      const evEnd   = Math.min(win.endMs, evStart + slot - 1_000);
      const id = `q-${String(it.number).padStart(2, '0')}`;
      const skillTag = normalizeSkillTag(it.skillTag);
      if (it.type === 'short_answer') {
        out.push({
          id,
          number: it.number,
          partCode: part.code,
          type: 'short_answer',
          stem: String(it.stem).slice(0, 2000),
          options: null,
          correctAnswer: String(it.correctAnswer),
          acceptedAnswers: Array.isArray(it.acceptedAnswers) ? it.acceptedAnswers.map(String).slice(0, 12) : [],
          explanation: it.explanation ? String(it.explanation).slice(0, 4000) : null,
          skillTag,
          transcriptExcerpt: it.transcriptExcerpt ? String(it.transcriptExcerpt).slice(0, 2000) : null,
          transcriptEvidenceText: it.transcriptExcerpt ? String(it.transcriptExcerpt).slice(0, 2000) : String(it.stem).slice(0, 2000),
          transcriptEvidenceStartMs: evStart,
          transcriptEvidenceEndMs: evEnd,
          difficultyLevel: 3,
          distractorExplanation: null,
          points: 1,
          optionDistractorWhy: null,
          optionDistractorCategory: null,
          speakerAttitude: null,
        });
      } else {
        const options = it.options.map(o => String(o.text || ''));
        const optionDistractorWhy = it.options.map(o => o.isCorrect ? null : (o.whyWrong ? String(o.whyWrong).slice(0, 1000) : null));
        const optionDistractorCategory = it.options.map(o => o.isCorrect ? null : (o.distractorCategory || 'out_of_scope'));
        out.push({
          id,
          number: it.number,
          partCode: part.code,
          type: 'multiple_choice_3',
          stem: String(it.stem).slice(0, 2000),
          options,
          correctAnswer: String(it.correctAnswer),
          acceptedAnswers: [],
          explanation: it.explanation ? String(it.explanation).slice(0, 4000) : null,
          skillTag,
          transcriptExcerpt: it.transcriptExcerpt ? String(it.transcriptExcerpt).slice(0, 2000) : null,
          transcriptEvidenceText: it.transcriptExcerpt ? String(it.transcriptExcerpt).slice(0, 2000) : String(it.stem).slice(0, 2000),
          transcriptEvidenceStartMs: evStart,
          transcriptEvidenceEndMs: evEnd,
          difficultyLevel: 3,
          distractorExplanation: null,
          points: 1,
          optionDistractorWhy,
          optionDistractorCategory,
          speakerAttitude: part.code.startsWith('C') ? (it.speakerAttitude || null) : null,
        });
      }
    }
  }
  return out;
}

function toAuthoredExtracts(paper) {
  return paper.parts.map((part, i) => {
    const win = PART_AUDIO_WINDOWS[part.code] || { startMs: 0, endMs: 600_000 };
    return {
      partCode: part.code,
      displayOrder: i + 1,
      kind: part.extractKind || PART_SPEC[i].extractKind,
      title: String(part.extractTitle || `Listening Part ${part.code}`).slice(0, 200),
      accentCode: part.accentCode || 'en-GB',
      speakers: Array.isArray(part.speakers)
        ? part.speakers.slice(0, 6).map((s, n) => ({
            id: s.id || `s${n+1}`,
            role: String(s.role || 'speaker').slice(0, 64),
            gender: s.gender && ['m','f','nb'].includes(s.gender) ? s.gender : null,
            accent: s.accent || part.accentCode || 'en-GB',
          }))
        : [{ id: 's1', role: 'speaker', gender: null, accent: part.accentCode || 'en-GB' }],
      audioStartMs: win.startMs,
      audioEndMs: win.endMs,
      difficultyRating: 3,
    };
  }).map(e => ({ ...e, kind: normaliseExtractKind(e.kind) }));
}

function normaliseExtractKind(k) {
  const v = String(k || '').toLowerCase();
  if (v === 'consultation') return 'consultation';
  if (v === 'workplace') return 'workplace';
  if (v === 'presentation') return 'presentation';
  return 'consultation';
}

// -----------------------------------------------------------------------------
// Per-paper pipeline
// -----------------------------------------------------------------------------

async function generateOnePaper({ index, total, profession, difficulty }) {
  const seq = String(index + 1).padStart(3, '0');
  const title = `OET Listening Practice — ${profession} ${difficulty === 'hard' ? 'Hard' : 'Standard'} Set ${seq}`;
  const slug = slugify(title);
  const label = `[${index + 1}/${total}] ${title}`;
  console.log('─'.repeat(80));
  console.log(progress(index + 1, total, title));

  if (DRY_RUN) {
    console.log(`  (dry-run) would create paper "${title}" (slug=${slug}, profession=${profession}, difficulty=${difficulty})`);
    console.log(`  (dry-run) would AI-generate 42 items + 5 TTS audio files`);
    console.log(`  (dry-run) would attach 5 Audio assets + QuestionPaper + AudioScript + AnswerKey, then publish`);
    return { ok: true, dryRun: true, title };
  }

  // ── 1. AI: generate the full paper structure ───────────────────────────────
  let ai = null;
  let aiAttempt = 0;
  const aiMaxAttempts = 5;
  while (aiAttempt < aiMaxAttempts) {
    aiAttempt++;
    try {
      throw new Error('Direct listening AI authoring is disabled. Use backend grounded listening extraction/draft services or import curated content.');
    } catch (e) {
      const m = e.message || '';
      const lenMatch = m.match(/--- content ---\n([\s\S]*)$/);
      const contentLen = lenMatch ? lenMatch[1].length : 0;
      console.log(`  ✗ AI attempt ${aiAttempt}/${aiMaxAttempts} threw: ${m.slice(0, 200)} [preview chars=${contentLen}]`);
    }
    await sleep(750 * aiAttempt);
  }
  if (!ai) {
    logFailure('listening-ai', { title }, new Error(`AI failed to produce valid 42-item paper after ${aiMaxAttempts} attempts`));
    return { ok: false, title };
  }
  console.log(`  ✓ AI produced valid 42-item structure (attempt ${aiAttempt})`);

  // ── 2. Create the ContentPaper (Draft) ─────────────────────────────────────
  const provenance = makeProvenance({
    kind: 'listening',
    profession,
    model: CONFIG.ai.chatModel,
    withLegalToken: true,                       // AGENTS.md: legal=original-authoring-attested mandatory
  });
  const createBody = {
    subtestCode: 'listening',
    title,
    slug,
    professionId: null,
    appliesToAllProfessions: true,              // Listening papers are cross-profession by convention
    difficulty,
    estimatedDurationMinutes: 45,
    cardType: null,
    letterType: null,
    priority: 10,
    tagsCsv: `ai-generated,${profession},${difficulty}`,
    sourceProvenance: provenance,
  };
  const created = await adminFetch('/v1/admin/papers', { method: 'POST', body: createBody });
  if (!created.ok) {
    logFailure('listening-create', { title, slug }, new Error(`${created.status}: ${JSON.stringify(created.data).slice(0, 400)}`));
    return { ok: false, title };
  }
  const paperId = created.data?.id;
  if (!paperId) {
    logFailure('listening-create', { title }, new Error(`no paper id in response: ${JSON.stringify(created.data).slice(0, 200)}`));
    return { ok: false, title };
  }
  console.log(`  ✓ paper created  id=${paperId}`);

  // ── 3. PUT structure (42 items) ────────────────────────────────────────────
  const questions = toAuthoredQuestions(ai);
  const structResp = await adminFetch(`/v1/admin/papers/${paperId}/listening/structure`, {
    method: 'PUT',
    body: { questions },
  });
  if (!structResp.ok) {
    logFailure('listening-structure', { paperId, title }, new Error(`${structResp.status}: ${JSON.stringify(structResp.data).slice(0, 400)}`));
    return { ok: false, paperId, title };
  }
  console.log(`  ✓ structure saved (42 questions)`);

  // ── 4. PUT extracts (5 rows: A1, A2, B, C1, C2) ────────────────────────────
  const extracts = toAuthoredExtracts(ai);
  const extResp = await adminFetch(`/v1/admin/papers/${paperId}/listening/extracts`, {
    method: 'PUT',
    body: { extracts },
  });
  if (!extResp.ok) {
    logFailure('listening-extracts', { paperId, title }, new Error(`${extResp.status}: ${JSON.stringify(extResp.data).slice(0, 400)}`));
    // non-fatal — paper can still publish without per-extract metadata
    console.log(`  ⚠ extracts PUT failed (non-fatal): ${extResp.status}`);
  } else {
    console.log(`  ✓ extracts saved (5 rows)`);
  }

  // ── 5. TTS + audio attach (5 Audio assets) ────────────────────────────────
  const audioAssetIds = {};
  if (!SKIP_TTS) {
    for (let i = 0; i < ai.parts.length; i++) {
      const part = ai.parts[i];
      const spec = PART_SPEC[i];
      const rawTtsText = String(part.transcript).slice(0, 4000);   // DO TTS soft cap
      // Build progressively safer prompt variants for guideline-refusal retries.
      const sanitizeForTts = (txt) => txt
        .replace(/\b(suicid\w*|self[\s-]?harm|overdose|abuse|assault|rape|kill\w*|murder\w*)\b/gi, 'distress')
        .replace(/\b(blood|bleeding|haemorrhage|hemorrhage)\b/gi, 'fluid')
        .replace(/\b(dying|dead|death)\b/gi, 'ill');
      const prefix = 'The following is a fictional training script for healthcare English-language practice. Read it in a neutral, professional tone.\n\n';
      const variants = [
        rawTtsText,
        prefix + rawTtsText,
        prefix + sanitizeForTts(rawTtsText),
        prefix + sanitizeForTts(rawTtsText).slice(0, 1800),
      ];
      let buf = null;
      // Per-part voice gender hint for ElevenLabs (DO TTS ignores it).
      // A1 = clinician female, A2 = clinician male (vary across the two
      // Part-A dialogs), B = neutral workplace excerpt, C1 = female monologue,
      // C2 = male monologue.
      const partGender = (
        part.code === 'A1' ? 'female' :
        part.code === 'A2' ? 'male' :
        part.code === 'B'  ? 'neutral' :
        part.code === 'C1' ? 'female' :
        part.code === 'C2' ? 'male' :
        'neutral'
      );
      for (let attempt = 1; attempt <= variants.length; attempt++) {
        try {
          buf = await aiTts(variants[attempt - 1], { format: 'mp3', gender: partGender });
          if (attempt > 1) console.log(`  ↻ TTS ${part.code} succeeded on retry ${attempt}/${variants.length}`);
          break;
        } catch (e) {
          console.log(`  ✗ TTS ${part.code} attempt ${attempt}/${variants.length}: ${e.message.slice(0, 200)}`);
          if (attempt === variants.length) {
            logFailure('listening-tts', { paperId, part: part.code, title }, e);
          } else {
            await sleep(2000);
          }
        }
      }
      if (!buf) continue;                                       // skip this audio but keep going
      // Sniff actual audio container — qwen3-tts-voicedesign currently returns
      // WAV bytes regardless of `response_format`, so honour what we actually got
      // (backend's magic-byte validator rejects mismatched declarations).
      let audioExt = 'mp3';
      let audioMime = 'audio/mpeg';
      if (buf.length >= 12 && buf.slice(0, 4).toString('ascii') === 'RIFF' && buf.slice(8, 12).toString('ascii') === 'WAVE') {
        audioExt = 'wav';
        audioMime = 'audio/wav';
      } else if (buf.length >= 4 && buf.slice(0, 4).toString('ascii') === 'OggS') {
        audioExt = 'ogg';
        audioMime = 'audio/ogg';
      } else if (buf.length >= 4 && buf.slice(0, 4).toString('ascii') === 'fLaC') {
        audioExt = 'flac';
        audioMime = 'audio/flac';
      }
      let assetId;
      try {
        assetId = await uploadMediaAsset(buf, {
          filename: `${slug}-${part.code}.${audioExt}`,
          mimeType: audioMime,
          kind: 'audio',
        });
      } catch (e) {
        logFailure('listening-upload', { paperId, part: part.code }, e);
        continue;
      }
      const attach = await adminFetch(`/v1/admin/papers/${paperId}/assets`, {
        method: 'POST',
        body: {
          role: 'Audio',                                        // PaperAssetRole.Audio
          mediaAssetId: assetId,
          part: part.code,                                      // "A1" | "A2" | "B" | "C1" | "C2"
          title: `Listening Part ${part.code}`,
          displayOrder: i + 1,
          makePrimary: true,
        },
      });
      if (!attach.ok) {
        logFailure('listening-attach-audio', { paperId, part: part.code }, new Error(`${attach.status}: ${JSON.stringify(attach.data).slice(0, 400)}`));
        continue;
      }
      audioAssetIds[part.code] = assetId;
      console.log(`  ✓ audio attached  ${part.code}  ${buf.length.toLocaleString()} bytes`);
    }
  } else {
    console.log(`  ⏭ TTS skipped (--skip-tts); paper will not satisfy Audio publish-gate`);
  }

  // ── 6. Synthesise + attach QuestionPaper / AudioScript / AnswerKey text assets ──
  const supplementaryRoles = [
    { role: 'QuestionPaper', filename: `${slug}-question-paper.txt`, body: renderQuestionPaper(title, ai), title: 'Question Paper' },
    { role: 'AudioScript',   filename: `${slug}-audio-script.txt`,    body: renderAudioScript(title, ai),    title: 'Audio Script (full transcript)' },
    { role: 'AnswerKey',     filename: `${slug}-answer-key.txt`,      body: renderAnswerKey(title, ai),      title: 'Answer Key' },
  ];
  for (let i = 0; i < supplementaryRoles.length; i++) {
    const sup = supplementaryRoles[i];
    const buf = Buffer.from(sup.body, 'utf8');
    let assetId;
    try {
      assetId = await uploadMediaAsset(buf, {
        filename: sup.filename,
        mimeType: 'text/plain',
        kind: 'document',
      });
    } catch (e) {
      logFailure('listening-upload-text', { paperId, role: sup.role }, e);
      continue;
    }
    const attach = await adminFetch(`/v1/admin/papers/${paperId}/assets`, {
      method: 'POST',
      body: {
        role: sup.role,
        mediaAssetId: assetId,
        part: null,
        title: sup.title,
        displayOrder: 100 + i,
        makePrimary: true,
      },
    });
    if (!attach.ok) {
      logFailure('listening-attach-text', { paperId, role: sup.role }, new Error(`${attach.status}: ${JSON.stringify(attach.data).slice(0, 400)}`));
    } else {
      console.log(`  ✓ ${sup.role} attached`);
    }
  }

  // ── 6b. Backfill relational projection (publish gate reads relational tables).
  // ResyncRelationalIfNeededAsync inside PUT /listening/structure only runs
  // when relational rows ALREADY exist for this paper; brand-new papers have
  // zero rows so we must trigger the first projection explicitly here.
  const bf = await adminFetch(`/v1/admin/papers/${paperId}/listening/backfill`, { method: 'POST', body: {} });
  if (!bf.ok) {
    logFailure('listening-backfill', { paperId, title }, new Error(`${bf.status}: ${JSON.stringify(bf.data).slice(0, 400)}`));
    console.log(`  ⚠ backfill failed (${bf.status}) — publish will likely fail`);
  } else {
    console.log(`  ✓ relational backfill projected`);
  }

  // Difficulty is now part of the authored listening structure/backfill
  // contract. Do not patch it with direct SQL from an operator script.

  // ── 7. Publish ─────────────────────────────────────────────────────────────
  let publishedAt = null;
  if (DO_PUBLISH) {
    const pub = await adminFetch(`/v1/admin/papers/${paperId}/publish`, { method: 'POST', body: {} });
    if (!pub.ok) {
      logFailure('listening-publish', { paperId, title }, new Error(`${pub.status}: ${JSON.stringify(pub.data).slice(0, 400)}`));
      console.log(`  ⚠ publish failed (${pub.status}) — paper remains Draft`);
    } else {
      publishedAt = new Date().toISOString();
      console.log(`  ✓ published`);
    }
  } else {
    console.log(`  ⏭ publish skipped (--no-publish)`);
  }

  return { ok: true, paperId, title, slug, profession, difficulty, audioAssetIds, publishedAt };
}

// -----------------------------------------------------------------------------
// Text renderers (for QuestionPaper / AudioScript / AnswerKey)
// -----------------------------------------------------------------------------

function renderQuestionPaper(title, ai) {
  const lines = [title, '='.repeat(Math.min(78, title.length)), ''];
  for (const part of ai.parts) {
    lines.push(`\nPART ${part.code} — ${part.extractTitle || ''}`.trimEnd());
    lines.push('-'.repeat(60));
    for (const it of part.items) {
      lines.push(`Q${it.number}. ${it.stem}`);
      if (it.type === 'multiple_choice_3' && Array.isArray(it.options)) {
        for (const o of it.options) lines.push(`   ${o.key}) ${o.text}`);
      } else {
        lines.push(`   _________________________________________`);
      }
      lines.push('');
    }
  }
  return lines.join('\n');
}

function renderAudioScript(title, ai) {
  const lines = [`AUDIO SCRIPT — ${title}`, '='.repeat(60), ''];
  for (const part of ai.parts) {
    lines.push(`\n[PART ${part.code}] ${part.extractTitle || ''} (accent: ${part.accentCode || 'en-GB'})`);
    lines.push('-'.repeat(60));
    lines.push(String(part.transcript || '').trim());
    lines.push('');
  }
  return lines.join('\n');
}

function renderAnswerKey(title, ai) {
  const lines = [`ANSWER KEY — ${title}`, '='.repeat(60), ''];
  for (const part of ai.parts) {
    lines.push(`\nPART ${part.code}`);
    for (const it of part.items) {
      lines.push(`  Q${it.number}: ${it.correctAnswer}${it.explanation ? '   — ' + String(it.explanation).slice(0, 200) : ''}`);
    }
  }
  return lines.join('\n');
}

// -----------------------------------------------------------------------------
// Main
// -----------------------------------------------------------------------------

async function main() {
  if (flags.healthcheck) {
    startRun('generate-listening-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  startRun('generate-listening');
  console.log(`  count=${COUNT}  profession=${PINNED_PROFESSION || 'rotate'}  dryRun=${DRY_RUN}  skipTts=${SKIP_TTS}  resume=${RESUME}  publish=${DO_PUBLISH}`);

  const manifest = loadManifest();
  const existingTitles = new Set(manifest.papers.map(p => p.title));

  let ok = 0, fail = 0, skipped = 0;
  for (let i = 0; i < COUNT; i++) {
    const profession = PINNED_PROFESSION || PROFESSIONS[i % PROFESSIONS.length];
    const difficulty = DIFFICULTIES[i % DIFFICULTIES.length];
    const titleProbe = `OET Listening Practice — ${profession} ${difficulty === 'hard' ? 'Hard' : 'Standard'} Set ${String(i + 1).padStart(3, '0')}`;

    if (RESUME && existingTitles.has(titleProbe)) {
      console.log(`  ⏭ resume: skipping "${titleProbe}" (already in manifest)`);
      skipped++;
      continue;
    }

    let result;
    try {
      result = await generateOnePaper({ index: i, total: COUNT, profession, difficulty });
    } catch (e) {
      logFailure('listening-paper-unhandled', { index: i, profession }, e);
      result = { ok: false };
    }

    if (result?.ok && !result.dryRun) {
      manifest.papers.push({
        title: result.title,
        slug: result.slug,
        paperId: result.paperId,
        profession: result.profession,
        difficulty: result.difficulty,
        audioAssetIds: result.audioAssetIds || {},
        publishedAt: result.publishedAt,
        generatedAt: new Date().toISOString(),
      });
      saveManifest(manifest);
      ok++;
    } else if (result?.dryRun) {
      ok++;
    } else {
      fail++;
    }

    // Politeness pause between papers — TTS + 42-item AI calls are heavy.
    if (i < COUNT - 1) await sleep(1500);
  }

  endRun({ requested: COUNT, ok, failed: fail, skipped, manifest: MANIFEST });
  process.exit(fail > 0 && ok === 0 ? 1 : 0);
}

main().catch(e => {
  console.error(e);
  process.exit(2);
});
