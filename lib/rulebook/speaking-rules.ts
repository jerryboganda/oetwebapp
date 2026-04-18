/**
 * ============================================================================
 * Speaking Rule Engine — Deterministic Detectors
 * ============================================================================
 *
 * Consumes a transcript of turns plus optional audio-timing metadata and
 * produces `LintFinding[]` tied to rulebook rule IDs. Pure and
 * unit-testable. Mirrors backend/src/OetLearner.Api/Services/SpeakingRules.cs.
 * ============================================================================
 */

import { loadRulebook, rulesApplicableTo } from './loader';
import type {
  LintFinding,
  Rule,
  SpeakingAuditInput,
  SpeakingTurn,
} from './types';

function finding(rule: Rule, message: string, extras: Partial<LintFinding> = {}): LintFinding {
  return { ruleId: rule.id, severity: rule.severity, message, ...extras };
}

function candidateTurns(transcript: SpeakingTurn[]): SpeakingTurn[] {
  return transcript.filter((t) => t.speaker === 'candidate');
}

function combinedCandidateText(transcript: SpeakingTurn[]): string {
  return candidateTurns(transcript).map((t) => t.text).join('\n');
}

type Detector = (rule: Rule, input: SpeakingAuditInput) => LintFinding[];

const DETECTORS: Record<string, Detector> = {
  // RULE_06 / 07 / 08 / 09 / 10 — jargon detector
  speaking_jargon_detector(rule, input) {
    const book = loadRulebook('speaking', input.profession ?? 'medicine');
    const tables = (book.tables ?? {}) as { forbiddenJargonTokens?: string[]; laymanGlossary?: { medical: string; plain: string }[] };
    const tokens = tables.forbiddenJargonTokens ?? [];
    const glossary = tables.laymanGlossary ?? [];
    const findings: LintFinding[] = [];

    for (const turn of candidateTurns(input.transcript)) {
      for (const token of tokens) {
        const re = new RegExp(`\\b${token.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\b`, 'i');
        if (re.test(turn.text)) {
          // Look-ahead: was the token followed by a plain-English explanation within the next 60 chars?
          const idx = turn.text.search(re);
          const context = turn.text.slice(idx, idx + 80).toLowerCase();
          const hasExplanation = /i'?m sorry for the medical term|which means|that is|in plain|basically|in other words/i.test(context);
          if (hasExplanation) continue;
          const plain = glossary.find((g) => g.medical.toLowerCase() === token.toLowerCase())?.plain ?? 'plain English';
          findings.push(
            finding(rule, `Candidate used medical jargon "${token}" without a plain-English explanation.`, {
              quote: turn.text.slice(Math.max(0, idx - 10), idx + token.length + 20),
              fixSuggestion: plain,
            }),
          );
          if (findings.length >= 10) return findings;
        }
      }
    }
    return findings;
  },

  // RULE_15 / RULE_20 / RULE_21 — stage coverage (empathy, recap, closure)
  speaking_stage_coverage(rule, input) {
    const text = combinedCandidateText(input.transcript).toLowerCase();
    const empathyMarkers = /(sorry to hear|must have been|i can imagine|that sounds)/i;
    const recapMarkers = /(to recap|to summari[sz]e|let me summari[sz]e|so to recap)/i;
    const closureMarkers = /(don't hesitate|please come back|take care|all the best|wish you well)/i;

    const findings: LintFinding[] = [];
    if (rule.id === 'RULE_15' && !empathyMarkers.test(text)) {
      findings.push(finding(rule, 'No empathy statement detected before moving to questions (RULE 15).'));
    }
    if (rule.id === 'RULE_20' && !recapMarkers.test(text)) {
      findings.push(finding(rule, 'No recap/summary detected before closing (RULE 20).'));
    }
    if (rule.id === 'RULE_21' && !closureMarkers.test(text)) {
      findings.push(finding(rule, 'No warm closure detected at the end of the consultation (RULE 21).'));
    }
    return findings;
  },

  // RULE_22 — monologue detector
  speaking_monologue_detector(rule, input) {
    const params = (rule.params as { maxConsecutiveCandidateWords?: number } | undefined) ?? {};
    const max = params.maxConsecutiveCandidateWords ?? 120;
    let longestRun = 0;
    let run = 0;
    for (const turn of input.transcript) {
      if (turn.speaker === 'candidate') {
        run += (turn.text.match(/\b\w+\b/g) ?? []).length;
        if (run > longestRun) longestRun = run;
      } else {
        run = 0;
      }
    }
    if (longestRun > max) {
      return [finding(rule, `Candidate spoke for ${longestRun} consecutive words without patient input. Keep the conversation a ping-pong dialogue (RULE 22).`)];
    }
    return [];
  },

  // RULE_23 — weight sensitivity
  speaking_weight_sensitivity(rule, input) {
    const text = combinedCandidateText(input.transcript);
    if (/\bwhat is your weight\b/i.test(text) || /\bhow much do you weigh\b/i.test(text)) {
      return [finding(rule, 'Never ask "What is your weight?" directly. Approach the topic sensitively (RULE 23).')];
    }
    return [];
  },

  // RULE_27 — smoking ladder order
  speaking_smoking_ladder_order(rule, input) {
    const turns = candidateTurns(input.transcript);
    let cessationIdx = -1;
    let reductionIdx = -1;
    for (let i = 0; i < turns.length; i++) {
      const text = turns[i].text.toLowerCase();
      if (cessationIdx === -1 && /\b(quit smoking|stop smoking|cessation|completely stop|quit completely)\b/.test(text)) {
        cessationIdx = i;
      }
      if (reductionIdx === -1 && /\b(reduce|cut down|smoke less|fewer cigarettes)\b/.test(text)) {
        reductionIdx = i;
      }
    }
    if (reductionIdx !== -1 && (cessationIdx === -1 || reductionIdx < cessationIdx)) {
      return [finding(rule, 'Smoking negotiation must start with recommending complete cessation (Black Area) before offering reduction (Grey Zone). You started with "reduce" (RULE 27).')];
    }
    return [];
  },

  // RULE_32 — over-diagnosis guard
  speaking_no_overdiagnosis(rule, input) {
    const text = combinedCandidateText(input.transcript).toLowerCase();
    const findings: LintFinding[] = [];
    if (/\byou have hypertension\b/.test(text)) {
      findings.push(finding(rule, 'Do not diagnose hypertension from a single reading. Phrase it as "your reading is on the higher side" and ask about prior history (RULE 32).'));
    }
    if (/\byou have diabetes\b/.test(text) && !/\bdiagnosed (with|as)\b/.test(text)) {
      findings.push(finding(rule, 'Do not diagnose diabetes from one reading. Inform calmly and ask about prior history (RULE 32).'));
    }
    return findings;
  },

  // RULE_41–47 — Breaking Bad News protocol step order
  speaking_bbn_protocol_order(rule, input) {
    if (input.cardType !== 'breaking_bad_news') return [];
    const params = (rule.params as { step?: number } | undefined) ?? {};
    const step = params.step ?? 0;
    const turns = candidateTurns(input.transcript);
    const text = turns.map((t) => t.text.toLowerCase());

    const markers: Record<number, RegExp> = {
      1: /(anyone you'd like to have here|someone with you|support system|partner or family with you)/i,
      2: /(not quite what we had hoped|something more serious|results aren'?t quite|brace yourself)/i,
      3: /(showing signs of cancer|i(?:'| a)m very sorry to tell you|diagnosed with|results have come back positive)/i,
      5: /(i can see this is|take all the time you need|lot to take in|i'?m so sorry)/i,
      6: /(early stage|effective treatment|we have options|there is hope|treatment plan)/i,
      7: /(here for you|someone who can be with you today|call me any time|my number|ongoing support)/i,
    };

    const re = markers[step];
    if (!re) return [];
    const found = text.some((t) => re.test(t));
    if (!found) {
      const stepName: Record<number, string> = {
        1: 'Step 1 — ask about support system',
        2: 'Step 2 — warning shots',
        3: 'Step 3 — deliver the diagnosis',
        5: 'Step 5 — respond to the emotional reaction',
        6: 'Step 6 — give hope and next steps',
        7: 'Step 7 — end with support system',
      };
      return [finding(rule, `Breaking Bad News protocol: ${stepName[step] ?? 'step ' + step} not detected (${rule.id}).`)];
    }
    return [];
  },

  // RULE_44 — measurable silence (needs audio timing)
  speaking_bbn_silence(rule, input) {
    if (input.cardType !== 'breaking_bad_news') return [];
    const params = (rule.params as { minSilenceSeconds?: number; maxSilenceSeconds?: number } | undefined) ?? {};
    const minSec = params.minSilenceSeconds ?? 3;
    const observed = input.silenceAfterDiagnosisMs;
    if (observed === undefined || observed === null) {
      return [finding(rule, 'Silence after diagnosis could not be measured — ensure audio timing is captured. Target 3–4 seconds (RULE 44).')];
    }
    if (observed < minSec * 1000) {
      return [finding(rule, `Silence after diagnosis was ${(observed / 1000).toFixed(1)}s — below the 3–4 second minimum (RULE 44).`)];
    }
    return [];
  },
};

function runForbiddenPatterns(rule: Rule, input: SpeakingAuditInput): LintFinding[] {
  if (!rule.forbiddenPatterns?.length) return [];
  const text = combinedCandidateText(input.transcript);
  const findings: LintFinding[] = [];
  for (const pat of rule.forbiddenPatterns) {
    const re = new RegExp(pat, 'gi');
    const m = re.exec(text);
    if (m) findings.push(finding(rule, `Rule ${rule.id}: "${m[0]}" is not acceptable.`, { quote: m[0], start: m.index, end: m.index + m[0].length }));
  }
  return findings;
}

export function auditSpeakingTranscript(input: SpeakingAuditInput): LintFinding[] {
  const profession = input.profession ?? 'medicine';
  const book = loadRulebook('speaking', profession);
  const applicable = rulesApplicableTo(book, input.cardType);

  const findings: LintFinding[] = [];
  for (const rule of applicable) {
    if (rule.checkId && DETECTORS[rule.checkId]) {
      findings.push(...DETECTORS[rule.checkId](rule, input));
    }
    if (rule.forbiddenPatterns?.length) {
      findings.push(...runForbiddenPatterns(rule, input));
    }
  }

  // Dedup + sort
  const seen = new Set<string>();
  const unique: LintFinding[] = [];
  for (const f of findings) {
    const key = `${f.ruleId}|${f.message}|${f.quote ?? ''}`;
    if (seen.has(key)) continue;
    seen.add(key);
    unique.push(f);
  }
  const sevRank: Record<string, number> = { critical: 0, major: 1, minor: 2, info: 3 };
  unique.sort((a, b) => (sevRank[a.severity] ?? 99) - (sevRank[b.severity] ?? 99));
  return unique;
}
