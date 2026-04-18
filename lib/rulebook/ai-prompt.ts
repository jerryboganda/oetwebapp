/**
 * ============================================================================
 * Rulebook-Grounded AI Prompt Builder — SINGLE SOURCE OF TRUTH FOR AI
 * ============================================================================
 *
 * MISSION CRITICAL. Every AI call in this platform (OpenAI, Anthropic,
 * Google, local models — anything) MUST build its system prompt through
 * this module. This ensures:
 *
 *   1. The AI is grounded in Dr. Ahmed Hesham's rulebook (content-as-code).
 *   2. The AI is grounded in the canonical OET scoring system (country-aware).
 *   3. The AI cannot invent rules outside the active rulebook.
 *   4. The AI produces structured output that can be audited and rendered.
 *   5. Rule versions are auditable (which rulebook did the AI see?).
 *
 * Never pass a system prompt to an AI without going through
 * `buildAiGroundedPrompt`. The backend enforces the same invariant via
 * `RulebookPrompt.cs` / `AiGatewayService`.
 * ============================================================================
 */

import {
  OET_SCALED_PASS_B,
  OET_SCALED_PASS_C_PLUS,
  getWritingPassThreshold,
} from '../scoring';
import { criticalRules, loadRulebook } from './loader';
import type {
  AiGroundingContext,
  AiGroundedPrompt,
  Rule,
  Rulebook,
} from './types';

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export function buildAiGroundedPrompt(ctx: AiGroundingContext): AiGroundedPrompt {
  const book = loadRulebook(ctx.kind, ctx.profession);

  // Scoring threshold applicable to this call.
  let passMark: number = OET_SCALED_PASS_B;
  let passGrade: 'B' | 'C+' = 'B';
  if (ctx.kind === 'writing') {
    const threshold = getWritingPassThreshold(ctx.candidateCountry ?? null);
    if (threshold) {
      passMark = threshold.threshold;
      passGrade = threshold.grade;
    }
  }

  const applicable = selectApplicableRules(book, ctx);
  const system = renderSystemPrompt(book, applicable, ctx, passMark, passGrade);
  const taskInstruction = renderTaskInstruction(ctx, passMark, passGrade);

  return {
    system,
    taskInstruction,
    metadata: {
      rulebookVersion: book.version,
      rulebookKind: book.kind,
      profession: book.profession,
      scoringPassMark: passMark,
      scoringGrade: passGrade,
      appliedRulesCount: applicable.length,
    },
  };
}

// ---------------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------------

function selectApplicableRules(book: Rulebook, ctx: AiGroundingContext): Rule[] {
  const filterContext = ctx.letterType ?? ctx.cardType;
  return book.rules.filter((rule) => {
    if (!rule.appliesTo || rule.appliesTo === 'all') return true;
    if (!filterContext) return true;
    return Array.isArray(rule.appliesTo) && rule.appliesTo.includes(filterContext);
  });
}

function renderSystemPrompt(
  book: Rulebook,
  applicable: Rule[],
  ctx: AiGroundingContext,
  passMark: number,
  passGrade: 'B' | 'C+',
): string {
  const critical = applicable.filter((r) => r.severity === 'critical');
  const major = applicable.filter((r) => r.severity === 'major');
  const header = renderHeader(book, ctx, passMark, passGrade);
  const scoring = renderScoringSection(ctx);
  const rulesBlock = renderRulesBlock(critical, major, applicable.length);
  const guardrails = renderGuardrails(ctx);
  const replyFormat = renderReplyFormat(ctx);

  return [header, scoring, rulesBlock, guardrails, replyFormat].join('\n\n');
}

function renderHeader(
  book: Rulebook,
  ctx: AiGroundingContext,
  passMark: number,
  passGrade: 'B' | 'C+',
): string {
  return [
    '# OET AI — Rulebook-Grounded System Prompt',
    '',
    'You are the AI assistant for the OET Preparation platform by Dr. Ahmed Hesham. Your knowledge about OET exam rules, grading, and feedback comes EXCLUSIVELY from the authoritative rulebook and scoring system reproduced below. Do not invent, extrapolate, or rely on outside opinions about OET.',
    '',
    `Rulebook: ${book.kind.toUpperCase()} / ${book.profession.toUpperCase()} / v${book.version}`,
    book.authoritySource ? `Authority: ${book.authoritySource}` : '',
    `Task mode: ${ctx.task}`,
    ctx.candidateCountry ? `Candidate target country: ${ctx.candidateCountry}` : '',
    `Applied pass mark: ${passMark}/500 (Grade ${passGrade})`,
  ]
    .filter(Boolean)
    .join('\n');
}

function renderScoringSection(ctx: AiGroundingContext): string {
  return [
    '## Canonical OET Scoring (non-negotiable)',
    '',
    '- LISTENING: Grade B at 350/500; raw 30/42 ≡ 350/500 EXACTLY.',
    '- READING: Grade B at 350/500; raw 30/42 ≡ 350/500 EXACTLY.',
    `- WRITING (country-aware): Grade B at ${OET_SCALED_PASS_B}/500 for UK/IE/AU/NZ/CA; Grade C+ at ${OET_SCALED_PASS_C_PLUS}/500 for US/QA. Without a confirmed country, return "country_required" instead of a grade.`,
    '- SPEAKING: Grade B at 350/500, universal (no country variation).',
    '',
    ctx.kind === 'writing'
      ? '**This call concerns WRITING** — apply the country-aware pass mark above. Never use the universal 350 threshold for Writing without verifying the country.'
      : '**This call concerns SPEAKING** — apply the universal 350/500 pass mark regardless of country.',
    '',
    'Always reference pass/fail using the exact OET grade letters: A, B, C+, C, D, E. Never use generic "pass/high" without the grade letter.',
  ].join('\n');
}

function renderRulesBlock(critical: Rule[], major: Rule[], appliedTotal: number): string {
  const lines: string[] = [];
  lines.push('## Active Rulebook');
  lines.push('');
  lines.push(`Applied rules for this task: ${appliedTotal} (critical: ${critical.length}, major: ${major.length}).`);
  lines.push('');
  lines.push('### CRITICAL rules (violations are auto-mark-deductions; flag them first)');
  lines.push('');
  for (const rule of critical) lines.push(renderRuleLine(rule));
  lines.push('');
  lines.push('### MAJOR rules (significant feedback items)');
  lines.push('');
  for (const rule of major.slice(0, 60)) lines.push(renderRuleLine(rule));
  if (major.length > 60) lines.push(`… and ${major.length - 60} more major rules.`);
  return lines.join('\n');
}

function renderRuleLine(rule: Rule): string {
  const exemplar = rule.exemplarPhrases?.[0] ? ` · ex: "${rule.exemplarPhrases[0]}"` : '';
  return `- **${rule.id}** (${rule.severity}) — ${rule.title}: ${rule.body}${exemplar}`;
}

function renderGuardrails(ctx: AiGroundingContext): string {
  return [
    '## Guardrails (STRICT)',
    '',
    '1. Cite rule IDs explicitly in every feedback finding (e.g. "R03.4", "RULE_27").',
    '2. Do NOT invent, rename, or extend rules. If a concern falls outside the rulebook, say so plainly.',
    '3. Do NOT produce a numeric grade that contradicts the country-aware scoring table above.',
    '4. Do NOT replace expert grading — your output is advisory. Mark it clearly as AI-generated.',
    '5. Never request the candidate\'s OET score from them; derive grades from the rulebook + the inputs provided.',
    '6. Be concise, clinical, and direct. No filler praise. No motivational platitudes.',
    '7. Use the same tone Dr. Hesham uses: professional, specific, and example-driven.',
    ctx.kind === 'speaking'
      ? '8. For speaking: respect the 13-stage consultation state machine and the Breaking Bad News 7-step protocol when analysing transcripts.'
      : '8. For writing: respect the letter structure order (Address → Date → Salutation → Re: line → Body → Yours sincerely/faithfully → Doctor) and flag layout violations.',
  ].join('\n');
}

function renderReplyFormat(ctx: AiGroundingContext): string {
  switch (ctx.task) {
    case 'score':
      return [
        '## Reply format (JSON)',
        '',
        'Return a SINGLE JSON object with this exact shape:',
        '```json',
        '{',
        '  "findings": [',
        '    { "ruleId": "R03.4", "severity": "critical", "quote": "...", "message": "...", "fixSuggestion": "..." }',
        '  ],',
        '  "criteriaScores": { "purpose": 0, "content": 0, "conciseness_clarity": 0, "genre_style": 0, "organisation_layout": 0, "language": 0 },',
        '  "estimatedScaledScore": 0,',
        '  "estimatedGrade": "B",',
        '  "passed": true,',
        '  "passRequires": { "scaled": 0, "grade": "B" },',
        '  "advisory": "AI-generated — pending expert review"',
        '}',
        '```',
      ].join('\n');
    case 'coach':
      return [
        '## Reply format (JSON)',
        '',
        '```json',
        '{',
        '  "findings": [ { "ruleId": "...", "severity": "...", "message": "...", "fixSuggestion": "..." } ],',
        '  "nextBestAction": "...",',
        '  "encouragement": "Max one short sentence, no filler."',
        '}',
        '```',
      ].join('\n');
    case 'correct':
      return [
        '## Reply format (JSON)',
        '',
        '```json',
        '{',
        '  "findings": [...],',
        '  "revisedText": "the fully-corrected letter / turn",',
        '  "changesSummary": "..."',
        '}',
        '```',
      ].join('\n');
    case 'generate_feedback':
      return [
        '## Reply format (JSON)',
        '',
        '```json',
        '{',
        '  "sections": [ { "title": "...", "bullets": ["..."] } ],',
        '  "ruleCitations": ["R03.4", "R08.7"]',
        '}',
        '```',
      ].join('\n');
    case 'generate_content':
      return [
        '## Reply format (JSON)',
        '',
        '```json',
        '{',
        '  "content": "...",',
        '  "appliedRuleIds": ["R03.4"],',
        '  "selfCheckNotes": "..."',
        '}',
        '```',
      ].join('\n');
    case 'summarise':
    default:
      return [
        '## Reply format',
        '',
        'Plain text, concise, ≤ 200 words. Cite rule IDs in parentheses when invoking rules.',
      ].join('\n');
  }
}

function renderTaskInstruction(
  ctx: AiGroundingContext,
  passMark: number,
  passGrade: 'B' | 'C+',
): string {
  const base =
    ctx.kind === 'writing'
      ? `Task: analyse the candidate's OET Writing letter (${ctx.letterType ?? 'letter type TBD'}) against the active rulebook, and produce rule-cited feedback.`
      : `Task: analyse the candidate's OET Speaking transcript (${ctx.cardType ?? 'card type TBD'}) against the active rulebook, and produce rule-cited feedback.`;
  return `${base} Apply the ${passMark}/500 (Grade ${passGrade}) pass mark for this ${ctx.kind} call. Respond strictly in the reply format above.`;
}
