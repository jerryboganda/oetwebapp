/**
 * generate-reading.mjs
 *
 * Bulk-generates 10 NEW Reading ContentPapers via the admin API, each one
 * conforming to the canonical OET Reading shape:
 *
 *   Part A — 4 texts (one topic) + 20 items
 *           Q1-7    = MatchingTextReference  (answer ∈ {"A","B","C","D"} → text 1-4)
 *           Q8-14   = ShortAnswer            (string answer, NO synonyms)
 *           Q15-20  = SentenceCompletion     (string answer, NO synonyms)
 *
 *   Part B — 6 short workplace texts + 6 items
 *           1 × MultipleChoice3 per text     (answer ∈ {"A","B","C"})
 *
 *   Part C — 2 long articles + 16 items
 *           8 × MultipleChoice4 per text     (answer ∈ {"A","B","C","D"})
 *
 * Backend gate enforcement (`ReadingStructureService.ValidatePaperAsync`):
 *   - Exactly 20/6/16 items, points=1, contiguous DisplayOrders.
 *   - Part A questions 1-7 must be MatchingTextReference, 8-14 ShortAnswer,
 *     15-20 SentenceCompletion. Synonyms are *forbidden* on Part A.
 *   - Every text needs a Source attribution.
 *   - Every question must reach ReviewState=Published before paper publish
 *     (linear walk Draft → AcademicReview → MedicalReview → LanguageReview
 *      → Pilot → Published — 5 transitions per question, 210 per paper).
 *   - Required primary assets for kind=reading: QuestionPaper + AnswerKey.
 *     We synthesise plain-text assets from the generated content.
 *
 * Usage:
 *   node scripts/admin/generate-reading.mjs [flags]
 *     --dry-run              Generate + validate JSON locally; no API writes.
 *     --count N              Number of papers to create (default 10).
 *     --profession <slug>    Force profession (medicine|nursing|dentistry|pharmacy).
 *                            Default rotates across all four.
 *     --resume               Skip papers whose title already exists in the
 *                            local resume manifest (output/admin-bulk/generate-reading-resume.json).
 *     --no-publish           Build structure + transition to Published review
 *                            state but skip the paper-level publish step.
 *     --healthcheck          Run lib healthcheck and exit.
 */

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, logFailure,
  healthcheck, progress, sleep, aiChatJson, uploadMediaAsset,
  makeProvenance, slugify,
} from './_lib.mjs';

const flags = parseFlags();

const PROFESSIONS = ['medicine', 'nursing', 'dentistry', 'pharmacy'];
const RESUME_PATH = resolve(CONFIG.outputDir, 'generate-reading-resume.json');

// ── Topic pool (rotated across runs to keep papers distinct) ────────────────
const TOPIC_POOL = [
  'paediatric asthma management',
  'antimicrobial stewardship in primary care',
  'chronic wound assessment and dressing selection',
  'post-operative pain management in elderly patients',
  'diabetic foot ulcer prevention',
  'stroke rehabilitation pathways',
  'community palliative care coordination',
  'venous thromboembolism prophylaxis',
  'mental health triage in emergency settings',
  'medication reconciliation at discharge',
  'infection control in dental practice',
  'pharmacist-led hypertension review clinics',
  'oral cancer screening protocols',
  'chronic kidney disease staging and referral',
  'sepsis recognition in adult inpatients',
];

// Linear review-state forward walk required by the backend transition machine.
const REVIEW_WALK = [
  'AcademicReview',
  'MedicalReview',
  'LanguageReview',
  'Pilot',
  'Published',
];

// ── Resume manifest ─────────────────────────────────────────────────────────

function loadResume() {
  if (!existsSync(RESUME_PATH)) return { createdTitles: [] };
  try { return JSON.parse(readFileSync(RESUME_PATH, 'utf8')); }
  catch { return { createdTitles: [] }; }
}

function saveResume(state) {
  try { writeFileSync(RESUME_PATH, JSON.stringify(state, null, 2), 'utf8'); }
  catch {}
}

// ── AI prompt + strict client-side validation ───────────────────────────────

const SYSTEM_PROMPT = [
  'You author OET Reading practice papers for healthcare professionals.',
  'Output STRICT JSON only — no prose, no markdown fences, no commentary.',
  '',
  'The OET Reading canonical structure is FIXED and non-negotiable:',
  '',
  'PART A: 4 short texts (Text 1, 2, 3, 4) on ONE shared clinical topic.',
  '  Each text ~150 words (≈600 words total). Each text MUST have a plausible',
  '  citation source (journal, guideline body, or textbook).',
  '  Items 1-20:',
  '    Q1-Q7   matching: stem is a statement; answer is "A","B","C", or "D"',
  '            indicating which of the four texts contains the information.',
  '    Q8-Q14  short-answer: stem asks a focused factual question; answer is a',
  '            SHORT verbatim span from one of the four texts (1-5 words).',
  '            NEVER provide synonyms. Specify which text via sourceTextIndex (1-4).',
  '    Q15-Q20 sentence-completion: stem is a sentence with a missing phrase;',
  '            answer is the missing phrase as it appears in the text',
  '            (1-6 words). Specify sourceTextIndex (1-4).',
  '',
  'PART B: 6 short workplace extracts (~100 words each — policies, emails,',
  '  notices, guidelines, handover notes). Each extract has a citation source.',
  '  Items 21-26: exactly ONE three-option multiple-choice question per extract.',
  '  Each MCQ has options A, B, C, one correct.',
  '',
  'PART C: 2 long articles (~700-850 words each) on healthcare topics.',
  '  Each article has a citation source.',
  '  Items 27-42: exactly 8 four-option multiple-choice questions per article.',
  '  Each MCQ has options A, B, C, D, one correct. Mix detail / inference /',
  '  vocabulary-in-context / writer-attitude questions.',
  '',
  'Reply with this exact JSON schema:',
  '{',
  '  "title": "Reading Paper — <topic> (<profession>)",',
  '  "partA": {',
  '    "texts": [ { "title": "Text 1 — …", "source": "…", "bodyHtml": "<p>…</p>" }, ... ×4 ],',
  '    "items": [',
  '      { "displayOrder": 1, "type": "matching",   "stem": "…", "answer": "A", "sourceTextIndex": null, "explanation": "…" },',
  '      ... Q1-Q7 matching, Q8-Q14 short-answer (each with sourceTextIndex 1-4 and answer string),',
  '      ... Q15-Q20 sentence-completion (each with sourceTextIndex 1-4 and answer string)',
  '    ]',
  '  },',
  '  "partB": {',
  '    "items": [',
  '      { "displayOrder": 21, "text": { "title": "…", "source": "…", "bodyHtml": "<p>…</p>" },',
  '        "stem": "According to the notice …", "options": [{"label":"A","text":"…"},{"label":"B","text":"…"},{"label":"C","text":"…"}],',
  '        "answer": "B", "explanation": "…" }, ... ×6',
  '    ]',
  '  },',
  '  "partC": {',
  '    "texts": [ { "title": "…", "source": "…", "bodyHtml": "<p>…</p>" }, { … } ],',
  '    "items": [',
  '      { "displayOrder": 27, "sourceTextIndex": 1, "stem": "…",',
  '        "options": [{"label":"A","text":"…"},{"label":"B","text":"…"},{"label":"C","text":"…"},{"label":"D","text":"…"}],',
  '        "answer": "C", "explanation": "…" }, ... ×16 (8 per text)',
  '    ]',
  '  }',
  '}',
  '',
  'Hard constraints:',
  '- Use British English clinical register at OET B2 level.',
  '- bodyHtml must be sanitised HTML (<p>, <ul>, <li>, <strong>, <em> only).',
  '- Answers must be VERBATIM substrings of the source text for Part A short-answer / sentence-completion.',
  '- NEVER include synonyms or accepted alternates anywhere — Part A is strict marking.',
  '- All clinical content must be plausible but fictionalised (no copyright).',
].join('\n');

function userPrompt({ profession, topic, paperNumber }) {
  return [
    `Generate Reading Paper #${paperNumber} for the ${profession} profession.`,
    `Shared topic for Part A: "${topic}".`,
    `Choose two different healthcare topics for the two Part C articles.`,
    `Choose six varied workplace contexts for the Part B extracts.`,
    `Return strict JSON exactly matching the schema in the system message.`,
  ].join(' ');
}

/**
 * Validate AI output shape. Throws on first violation so the retry loop can
 * regenerate.
 */
function validatePaperShape(p) {
  const errs = [];
  if (!p || typeof p !== 'object') errs.push('root not object');
  if (!p.title || typeof p.title !== 'string') errs.push('title missing');

  // Part A
  if (!Array.isArray(p.partA?.texts) || p.partA.texts.length !== 4)
    errs.push(`partA.texts must be 4 (got ${p.partA?.texts?.length})`);
  if (!Array.isArray(p.partA?.items) || p.partA.items.length !== 20)
    errs.push(`partA.items must be 20 (got ${p.partA?.items?.length})`);
  for (const t of p.partA?.texts || []) {
    if (!t.title || !t.source || !t.bodyHtml) errs.push('partA.text missing title/source/bodyHtml');
  }
  for (const [i, it] of (p.partA?.items || []).entries()) {
    const n = i + 1;
    if (n >= 1 && n <= 7) {
      if (!['A','B','C','D'].includes(String(it.answer)))
        errs.push(`partA Q${n} (matching) answer must be A-D (got ${it.answer})`);
    } else if (n >= 8 && n <= 20) {
      if (typeof it.answer !== 'string' || !it.answer.trim())
        errs.push(`partA Q${n} answer must be non-empty string`);
      const idx = Number(it.sourceTextIndex);
      if (!Number.isInteger(idx) || idx < 1 || idx > 4)
        errs.push(`partA Q${n} sourceTextIndex must be 1-4 (got ${it.sourceTextIndex})`);
    }
    if (!it.stem || typeof it.stem !== 'string') errs.push(`partA Q${n} stem missing`);
  }

  // Part B
  if (!Array.isArray(p.partB?.items) || p.partB.items.length !== 6)
    errs.push(`partB.items must be 6 (got ${p.partB?.items?.length})`);
  for (const [i, it] of (p.partB?.items || []).entries()) {
    const n = i + 1;
    if (!it.text?.title || !it.text?.source || !it.text?.bodyHtml)
      errs.push(`partB item ${n} text missing title/source/bodyHtml`);
    if (!Array.isArray(it.options) || it.options.length !== 3)
      errs.push(`partB item ${n} must have exactly 3 options`);
    if (!['A','B','C'].includes(String(it.answer)))
      errs.push(`partB item ${n} answer must be A/B/C (got ${it.answer})`);
    for (const o of it.options || []) {
      if (!['A','B','C'].includes(String(o.label)) || !o.text)
        errs.push(`partB item ${n} option malformed`);
    }
  }

  // Part C
  if (!Array.isArray(p.partC?.texts) || p.partC.texts.length !== 2)
    errs.push(`partC.texts must be 2 (got ${p.partC?.texts?.length})`);
  if (!Array.isArray(p.partC?.items) || p.partC.items.length !== 16)
    errs.push(`partC.items must be 16 (got ${p.partC?.items?.length})`);
  for (const t of p.partC?.texts || []) {
    if (!t.title || !t.source || !t.bodyHtml) errs.push('partC.text missing title/source/bodyHtml');
  }
  // 8 questions per text
  const perText = { 1: 0, 2: 0 };
  for (const [i, it] of (p.partC?.items || []).entries()) {
    const n = i + 1;
    const idx = Number(it.sourceTextIndex);
    if (idx !== 1 && idx !== 2) errs.push(`partC item ${n} sourceTextIndex must be 1 or 2 (got ${it.sourceTextIndex})`);
    else perText[idx]++;
    if (!Array.isArray(it.options) || it.options.length !== 4)
      errs.push(`partC item ${n} must have exactly 4 options`);
    if (!['A','B','C','D'].includes(String(it.answer)))
      errs.push(`partC item ${n} answer must be A-D (got ${it.answer})`);
    for (const o of it.options || []) {
      if (!['A','B','C','D'].includes(String(o.label)) || !o.text)
        errs.push(`partC item ${n} option malformed`);
    }
    if (!it.stem) errs.push(`partC item ${n} stem missing`);
  }
  if (perText[1] !== 8 || perText[2] !== 8)
    errs.push(`partC must have 8 questions per text (got text1=${perText[1]}, text2=${perText[2]})`);

  if (errs.length) {
    throw new Error('paper shape invalid:\n  - ' + errs.slice(0, 8).join('\n  - '));
  }
}

// ── AI generation with retry ────────────────────────────────────────────────

async function generatePaperJson({ profession, topic, paperNumber }) {
  let lastError = null;
  for (let attempt = 1; attempt <= 5; attempt++) {
    try {
      const r = await aiChatJson({
        system: SYSTEM_PROMPT,
        user: userPrompt({ profession, topic, paperNumber }),
        model: CONFIG.ai.chatModel,
        temperature: 0.55,
        maxTokens: 16000,
        retries: 2,
      });
      validatePaperShape(r.json);
      return r.json;
    } catch (e) {
      lastError = e;
      console.log(`    AI attempt ${attempt}/5 failed: ${String(e.message).slice(0, 200)}`);
      await sleep(1500 * attempt);
    }
  }
  throw new Error(`AI failed after 5 attempts: ${lastError?.message || 'unknown'}`);
}

// ── Synthesise plain-text Question Paper + Answer Key assets ────────────────

function synthQuestionPaperText(p) {
  const lines = [`READING PAPER — ${p.title}`, '', '=== PART A ==='];
  p.partA.texts.forEach((t, i) => {
    lines.push('', `--- Text ${i + 1}: ${t.title} ---`, `(Source: ${t.source})`, htmlToText(t.bodyHtml));
  });
  lines.push('', 'Part A Questions:');
  p.partA.items.forEach((it, i) => lines.push(`${i + 1}. ${it.stem}`));
  lines.push('', '=== PART B ===');
  p.partB.items.forEach((it, i) => {
    const n = i + 1;
    lines.push('', `--- Extract ${n}: ${it.text.title} ---`, `(Source: ${it.text.source})`,
      htmlToText(it.text.bodyHtml), '', `Q${20 + n}. ${it.stem}`,
      ...it.options.map(o => `  ${o.label}. ${o.text}`));
  });
  lines.push('', '=== PART C ===');
  p.partC.texts.forEach((t, i) => {
    lines.push('', `--- Article ${i + 1}: ${t.title} ---`, `(Source: ${t.source})`, htmlToText(t.bodyHtml));
  });
  p.partC.items.forEach((it, i) => {
    const n = 27 + i;
    lines.push('', `Q${n}. (Article ${it.sourceTextIndex}) ${it.stem}`,
      ...it.options.map(o => `  ${o.label}. ${o.text}`));
  });
  return lines.join('\n');
}

function synthAnswerKeyText(p) {
  const lines = [`ANSWER KEY — ${p.title}`, ''];
  p.partA.items.forEach((it, i) => {
    const n = i + 1;
    const type = n <= 7 ? 'matching' : n <= 14 ? 'short-answer' : 'sentence-completion';
    lines.push(`Q${n} [${type}] → ${it.answer}${it.explanation ? `   // ${it.explanation}` : ''}`);
  });
  p.partB.items.forEach((it, i) => {
    lines.push(`Q${20 + i + 1} [MCQ3] → ${it.answer}${it.explanation ? `   // ${it.explanation}` : ''}`);
  });
  p.partC.items.forEach((it, i) => {
    lines.push(`Q${26 + i + 1} [MCQ4 art${it.sourceTextIndex}] → ${it.answer}${it.explanation ? `   // ${it.explanation}` : ''}`);
  });
  return lines.join('\n');
}

function htmlToText(html) {
  return String(html || '')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<\/p>/gi, '\n')
    .replace(/<li>/gi, '  • ')
    .replace(/<\/li>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&#39;/g, "'")
    .replace(/&quot;/g, '"')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

function wordCount(html) {
  return htmlToText(html).split(/\s+/).filter(Boolean).length;
}

// ── Backend orchestration for a single paper ────────────────────────────────

async function createPaperShell({ paper, profession }) {
  const dto = {
    SubtestCode: 'reading',
    Title: paper.title,
    Slug: slugify(paper.title),
    ProfessionId: profession,
    AppliesToAllProfessions: false,
    Difficulty: 'B2',
    EstimatedDurationMinutes: 60,
    CardType: null,
    LetterType: null,
    Priority: 0,
    TagsCsv: `ai-generated,reading,${profession}`,
    SourceProvenance: makeProvenance({ kind: 'reading', profession, model: CONFIG.ai.chatModel }),
  };
  const r = await adminFetch('/v1/admin/papers', { method: 'POST', body: dto });
  if (!r.ok) throw new Error(`POST /v1/admin/papers ${r.status}: ${JSON.stringify(r.data).slice(0, 400)}`);
  const id = r.data?.id || r.data?.paperId;
  if (!id) throw new Error(`paper create returned no id: ${JSON.stringify(r.data).slice(0, 300)}`);
  return id;
}

async function ensureCanonicalParts(paperId) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/reading/ensure-canonical`, {
    method: 'POST', body: {},
  });
  if (!r.ok) throw new Error(`ensure-canonical ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
}

async function getStructure(paperId) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/reading/structure`);
  if (!r.ok) throw new Error(`get structure ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
  return r.data;
}

async function upsertText(paperId, body) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/reading/texts`, { method: 'POST', body });
  if (!r.ok) throw new Error(`texts ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
  return r.data;
}

async function upsertQuestion(paperId, body) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/reading/questions`, { method: 'POST', body });
  if (!r.ok) throw new Error(`questions ${r.status}: ${JSON.stringify(r.data).slice(0, 400)}`);
  return r.data;
}

async function transitionQuestion(paperId, questionId, toState) {
  const r = await adminFetch(
    `/v1/admin/papers/${paperId}/reading/questions/${questionId}/review-transition`,
    { method: 'POST', body: { ToState: toState, Note: 'AI bulk generation', IsAdminOverride: false } });
  if (!r.ok) throw new Error(`transition ${toState} ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
}

async function walkToPublished(paperId, questionId) {
  for (const state of REVIEW_WALK) {
    await transitionQuestion(paperId, questionId, state);
  }
}

async function attachAsset(paperId, role, mediaAssetId, title) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/assets`, {
    method: 'POST',
    body: {
      Role: role,
      MediaAssetId: mediaAssetId,
      Part: null,
      Title: title,
      DisplayOrder: 0,
      MakePrimary: true,
    },
  });
  if (!r.ok) throw new Error(`assets ${role} ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
}

async function publishPaper(paperId) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/publish`, { method: 'POST', body: {} });
  if (!r.ok) throw new Error(`publish ${r.status}: ${JSON.stringify(r.data).slice(0, 400)}`);
}

// Build one Reading paper end-to-end.
async function buildReadingPaper({ paper, profession, doPublish }) {
  const paperId = await createPaperShell({ paper, profession });
  console.log(`  ✓ shell created: ${paperId}`);

  await ensureCanonicalParts(paperId);
  const structure = await getStructure(paperId);
  const partsByCode = {};
  for (const p of structure.parts || structure.Parts || []) {
    const code = p.partCode || p.PartCode;
    partsByCode[String(code).toUpperCase()] = p.id || p.Id;
  }
  if (!partsByCode.A || !partsByCode.B || !partsByCode.C)
    throw new Error(`structure missing canonical parts: ${JSON.stringify(partsByCode)}`);

  // ── Part A: 4 texts, then 20 items keyed back to text ids ────────────────
  const partAtextIds = [];
  for (let i = 0; i < paper.partA.texts.length; i++) {
    const t = paper.partA.texts[i];
    const res = await upsertText(paperId, {
      Id: null,
      ReadingPartId: partsByCode.A,
      DisplayOrder: i + 1,
      Title: t.title,
      Source: t.source,
      BodyHtml: t.bodyHtml,
      WordCount: wordCount(t.bodyHtml),
      TopicTag: 'part-a',
    });
    partAtextIds.push(res.id || res.Id);
  }
  for (let i = 0; i < paper.partA.items.length; i++) {
    const it = paper.partA.items[i];
    const n = i + 1;
    let questionType, optionsJson, correctAnswerJson, textId;
    if (n >= 1 && n <= 7) {
      questionType = 'MatchingTextReference';
      // For matching items, options unused; we still send a JSON array of the 4 text labels.
      optionsJson = JSON.stringify([
        { label: 'A', text: 'Text 1' },
        { label: 'B', text: 'Text 2' },
        { label: 'C', text: 'Text 3' },
        { label: 'D', text: 'Text 4' },
      ]);
      correctAnswerJson = JSON.stringify(String(it.answer).trim().toUpperCase());
      // Part A matching may reference any Part A text — pick the one matching the answer letter.
      const letterIdx = { A: 0, B: 1, C: 2, D: 3 }[String(it.answer).trim().toUpperCase()] ?? 0;
      textId = partAtextIds[letterIdx];
    } else if (n >= 8 && n <= 14) {
      questionType = 'ShortAnswer';
      optionsJson = '[]';
      correctAnswerJson = JSON.stringify(String(it.answer).trim());
      textId = partAtextIds[(Number(it.sourceTextIndex) || 1) - 1];
    } else {
      questionType = 'SentenceCompletion';
      optionsJson = '[]';
      correctAnswerJson = JSON.stringify(String(it.answer).trim());
      textId = partAtextIds[(Number(it.sourceTextIndex) || 1) - 1];
    }
    const res = await upsertQuestion(paperId, {
      Id: null,
      ReadingPartId: partsByCode.A,
      ReadingTextId: textId,
      DisplayOrder: n,
      Points: 1,
      QuestionType: questionType,
      Stem: it.stem,
      OptionsJson: optionsJson,
      CorrectAnswerJson: correctAnswerJson,
      AcceptedSynonymsJson: null, // Part A: synonyms forbidden by gate
      CaseSensitive: false,
      ExplanationMarkdown: it.explanation || null,
      SkillTag: n <= 7 ? 'scan-match' : n <= 14 ? 'detail' : 'detail',
    });
    await walkToPublished(paperId, res.id || res.Id);
  }
  console.log(`  ✓ Part A: 4 texts + 20 questions (Published)`);

  // ── Part B: 6 short texts, 1 MCQ3 each ───────────────────────────────────
  for (let i = 0; i < paper.partB.items.length; i++) {
    const it = paper.partB.items[i];
    const t = it.text;
    const textRes = await upsertText(paperId, {
      Id: null,
      ReadingPartId: partsByCode.B,
      DisplayOrder: i + 1,
      Title: t.title,
      Source: t.source,
      BodyHtml: t.bodyHtml,
      WordCount: wordCount(t.bodyHtml),
      TopicTag: 'part-b',
    });
    const optionsJson = JSON.stringify(it.options.map(o => ({ label: String(o.label).toUpperCase(), text: String(o.text) })));
    const qRes = await upsertQuestion(paperId, {
      Id: null,
      ReadingPartId: partsByCode.B,
      ReadingTextId: textRes.id || textRes.Id,
      DisplayOrder: i + 1,
      Points: 1,
      QuestionType: 'MultipleChoice3',
      Stem: it.stem,
      OptionsJson: optionsJson,
      CorrectAnswerJson: JSON.stringify(String(it.answer).trim().toUpperCase()),
      AcceptedSynonymsJson: null,
      CaseSensitive: false,
      ExplanationMarkdown: it.explanation || null,
      SkillTag: 'gist',
    });
    await walkToPublished(paperId, qRes.id || qRes.Id);
  }
  console.log(`  ✓ Part B: 6 texts + 6 MCQ3 questions (Published)`);

  // ── Part C: 2 articles, 8 MCQ4 each ──────────────────────────────────────
  const partCtextIds = [];
  for (let i = 0; i < paper.partC.texts.length; i++) {
    const t = paper.partC.texts[i];
    const res = await upsertText(paperId, {
      Id: null,
      ReadingPartId: partsByCode.C,
      DisplayOrder: i + 1,
      Title: t.title,
      Source: t.source,
      BodyHtml: t.bodyHtml,
      WordCount: wordCount(t.bodyHtml),
      TopicTag: 'part-c',
    });
    partCtextIds.push(res.id || res.Id);
  }
  for (let i = 0; i < paper.partC.items.length; i++) {
    const it = paper.partC.items[i];
    const textId = partCtextIds[(Number(it.sourceTextIndex) || 1) - 1];
    const optionsJson = JSON.stringify(it.options.map(o => ({ label: String(o.label).toUpperCase(), text: String(o.text) })));
    const qRes = await upsertQuestion(paperId, {
      Id: null,
      ReadingPartId: partsByCode.C,
      ReadingTextId: textId,
      DisplayOrder: i + 1,
      Points: 1,
      QuestionType: 'MultipleChoice4',
      Stem: it.stem,
      OptionsJson: optionsJson,
      CorrectAnswerJson: JSON.stringify(String(it.answer).trim().toUpperCase()),
      AcceptedSynonymsJson: null,
      CaseSensitive: false,
      ExplanationMarkdown: it.explanation || null,
      SkillTag: 'inference',
    });
    await walkToPublished(paperId, qRes.id || qRes.Id);
  }
  console.log(`  ✓ Part C: 2 texts + 16 MCQ4 questions (Published)`);

  // ── Required primary assets (QuestionPaper + AnswerKey) ──────────────────
  const qpBuf = Buffer.from(synthQuestionPaperText(paper), 'utf8');
  const akBuf = Buffer.from(synthAnswerKeyText(paper), 'utf8');
  const qpAssetId = await uploadMediaAsset(qpBuf, {
    filename: `${slugify(paper.title)}-question-paper.txt`,
    mimeType: 'text/plain',
    kind: 'document',
  });
  await attachAsset(paperId, 'QuestionPaper', qpAssetId, 'Question Paper (AI-generated)');
  const akAssetId = await uploadMediaAsset(akBuf, {
    filename: `${slugify(paper.title)}-answer-key.txt`,
    mimeType: 'text/plain',
    kind: 'document',
  });
  await attachAsset(paperId, 'AnswerKey', akAssetId, 'Answer Key (AI-generated)');
  console.log(`  ✓ assets attached: QuestionPaper + AnswerKey`);

  if (doPublish) {
    await publishPaper(paperId);
    console.log(`  ✓ paper published`);
  }
  return paperId;
}

// ── Main ────────────────────────────────────────────────────────────────────

async function main() {
  if (flags.healthcheck) {
    startRun('generate-reading-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  const count = Math.max(1, Number(flags.count) || 10);
  const forcedProfession = flags.profession ? String(flags.profession).toLowerCase() : null;
  const dryRun = !!flags['dry-run'];
  const doPublish = !flags['no-publish'];
  const resume = !!flags.resume;

  startRun('generate-reading');
  console.log(`  count=${count}  publish=${doPublish}  dryRun=${dryRun}  resume=${resume}`);
  console.log(`  profession=${forcedProfession || 'rotate'}  topics=${TOPIC_POOL.length}`);

  const resumeState = resume ? loadResume() : { createdTitles: [] };

  let created = 0, skipped = 0, failed = 0;

  for (let i = 0; i < count; i++) {
    const profession = forcedProfession || PROFESSIONS[i % PROFESSIONS.length];
    const topic = TOPIC_POOL[(i + Math.floor(Date.now() / 86400000)) % TOPIC_POOL.length];
    const label = `paper ${i + 1}/${count}  ${profession}  topic="${topic}"`;
    console.log('');
    console.log(progress(i + 1, count, label));

    try {
      // Generate AI JSON (validated client-side, with retry).
      const paperJson = await generatePaperJson({
        profession, topic, paperNumber: i + 1,
      });

      // Resume: skip if title already created this run-set.
      if (resume && resumeState.createdTitles.includes(paperJson.title)) {
        console.log(`  ⓘ resume: skipping "${paperJson.title}" (already created)`);
        skipped++;
        continue;
      }

      if (dryRun) {
        console.log(`  (dry-run) "${paperJson.title}"`);
        console.log(`    partA texts=${paperJson.partA.texts.length} items=${paperJson.partA.items.length}`);
        console.log(`    partB items=${paperJson.partB.items.length}`);
        console.log(`    partC texts=${paperJson.partC.texts.length} items=${paperJson.partC.items.length}`);
        skipped++;
        continue;
      }

      const paperId = await buildReadingPaper({
        paper: paperJson,
        profession,
        doPublish,
      });

      resumeState.createdTitles.push(paperJson.title);
      saveResume(resumeState);
      created++;
      console.log(`  ✓ DONE  paperId=${paperId}  title="${paperJson.title}"`);
    } catch (e) {
      failed++;
      logFailure('generate-reading', { index: i + 1, profession, topic }, e);
      if (failed >= 5 && created === 0) {
        console.error('  ⚠ aborting — 5 consecutive failures, no successes');
        break;
      }
    }

    await sleep(150);
  }

  endRun({ requested: count, created, skipped, failed });
  process.exit(failed > 0 && created === 0 ? 1 : 0);
}

main().catch(e => {
  console.error(e);
  process.exit(2);
});
