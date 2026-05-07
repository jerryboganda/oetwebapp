/**
 * ============================================================================
 * Writing Rule Engine — Deterministic Detectors
 * ============================================================================
 *
 * Pure, testable, mirrors backend/src/OetLearner.Api/Services/WritingRules.cs.
 *
 * Detectors consume the raw letter text plus a structured context object
 * and produce `LintFinding[]`. Each finding references a ruleId from the
 * active rulebook — so as the rulebook evolves, detector text updates for
 * free.
 * ============================================================================
 */

import { loadRulebook, rulesApplicableTo } from './loader';
import type { LintFinding, Rule, WritingLintInput } from './types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface LetterStructure {
  lines: string[];
  paragraphs: string[][]; // paragraphs as blocks of lines
  body: string; // text between salutation and closure
  bodyParagraphs: string[]; // extracted body paragraphs (plain strings)
  introIndex?: number;
  closureIndex?: number;
  salutationIndex?: number;
  reLineIndex?: number;
  dateIndex?: number;
  yoursIndex?: number;
}

const WORD_RE = /\b[\w'-]+\b/g;

function parseLetter(text: string): LetterStructure {
  const lines = text.split(/\r?\n/);
  const paragraphs: string[][] = [];
  let current: string[] = [];
  for (const line of lines) {
    if (line.trim().length === 0) {
      if (current.length) paragraphs.push(current);
      current = [];
    } else {
      current.push(line);
    }
  }
  if (current.length) paragraphs.push(current);

  const findIndex = (re: RegExp): number | undefined => {
    for (let i = 0; i < lines.length; i++) {
      if (re.test(lines[i])) return i;
    }
    return undefined;
  };

  const salutationIndex = findIndex(/^\s*Dear\b/i);
  const reLineIndex = findIndex(/^\s*Re\s*:/i);
  const dateIndex = findIndex(/^\s*(\d{1,2}[\/\s-]\w+|\d{1,2}\/\d{1,2}\/\d{2,4}|\w+\s\d{1,2},?\s\d{2,4})\s*$/);
  const yoursIndex = findIndex(/^\s*Yours\s+(sincerely|faithfully)\b/i);

  // Body paragraphs sit between Re: line (+ intro) and Yours closure.
  let bodyParagraphs: string[] = [];
  let body = '';
  if (salutationIndex !== undefined && yoursIndex !== undefined) {
    const firstBodyLine = Math.max(
      (reLineIndex ?? salutationIndex) + 1,
      salutationIndex + 1,
    );
    const lastBodyLine = yoursIndex - 1;
    if (lastBodyLine > firstBodyLine) {
      body = lines.slice(firstBodyLine, lastBodyLine + 1).join('\n');
      bodyParagraphs = body
        .split(/\n\s*\n/)
        .map((p) => p.trim())
        .filter((p) => p.length > 0);
    }
  }

  return {
    lines,
    paragraphs,
    body,
    bodyParagraphs,
    salutationIndex,
    reLineIndex,
    dateIndex,
    yoursIndex,
  };
}

function findQuote(text: string, regex: RegExp): { quote: string; start: number; end: number } | null {
  const m = regex.exec(text);
  if (!m) return null;
  return { quote: m[0], start: m.index, end: m.index + m[0].length };
}

function ruleFinding(
  rule: Rule,
  message: string,
  extras: Partial<LintFinding> = {},
): LintFinding {
  return {
    ruleId: rule.id,
    severity: rule.severity,
    message,
    ...extras,
  };
}

// ---------------------------------------------------------------------------
// Detector registry — pure functions keyed by checkId in the rulebook
// ---------------------------------------------------------------------------

type Detector = (
  rule: Rule,
  input: WritingLintInput,
  structure: LetterStructure,
) => LintFinding[];

const DETECTORS: Record<string, Detector> = {
  // R03.4 — Smoking/drinking always included unless recipient is OT
  content_requires_smoking_drinking(rule, input) {
    const findings: LintFinding[] = [];
    const recipient = (input.recipientSpecialty ?? '').toLowerCase();
    const excluded = (rule.params as { excludeRecipientSpecialties?: string[] } | undefined)
      ?.excludeRecipientSpecialties?.map((s) => s.toLowerCase()) ?? [];
    if (excluded.some((x) => recipient.includes(x))) return findings;

    const body = input.letterText.toLowerCase();
    const hasSmoking = /\b(smok|tobacco|cigarett)/.test(body);
    const hasDrinking = /\b(alcohol|drink(s|ing)?|units? per week)\b/.test(body);

    if (!hasSmoking) {
      findings.push(
        ruleFinding(rule, 'Smoking status must be mentioned (positive or negative) unless writing to an occupational therapist.'),
      );
    }
    if (!hasDrinking) {
      findings.push(
        ruleFinding(rule, 'Drinking status must be mentioned (positive or negative) unless writing to an occupational therapist.'),
      );
    }
    return findings;
  },

  // R03.6 — Allergy always for atopic conditions
  content_requires_allergy_for_atopic(rule, input) {
    const atopic = Boolean(input.caseNotesMarkers?.atopicCondition);
    if (!atopic) return [];
    const hasAllergy = /\ballerg/i.test(input.letterText);
    return hasAllergy
      ? []
      : [ruleFinding(rule, 'Allergy status (positive or negative) must be included for atopic conditions (asthma, eczema, hay fever).')];
  },

  // R03.8 — Body length advisory (180–200 words, configurable per profession via
  // rule.params.min/max in rulebooks/writing/<profession>/rulebook.v1.json).
  //
  // Soft warning only: emitted at severity 'minor' regardless of rule.severity
  // (the rulebook lists it as 'major' for AI-grounding context, but the live
  // lint surface treats it as advisory per owner directive 2026-05-07). Submit
  // is never blocked. The AI gateway still receives the rule text via grounding.
  //
  // Robust to incomplete drafts: parseLetter only populates bodyParagraphs when
  // both salutation and closure are present. While drafting, fall back to the
  // total non-whitespace word count of letterText. Suppress the finding entirely
  // while the draft is still very short (< 80 words) to avoid noisy warnings
  // during brainstorming.
  letter_body_length(rule, input, structure) {
    const params = (rule.params as { min?: number; max?: number } | undefined) ?? {};
    const min = params.min ?? 180;
    const max = params.max ?? 200;
    const SUPPRESS_BELOW_WORDS = 80;

    const fromBody = structure.bodyParagraphs.join(' ').match(/\S+/g)?.length ?? 0;
    const fallback = input.letterText.match(/\S+/g)?.length ?? 0;
    const count = fromBody > 0 ? fromBody : fallback;

    if (count < SUPPRESS_BELOW_WORDS) return [];
    if (count >= min && count <= max) return [];

    const direction = count < min ? 'short' : 'long';
    const message = `Letter body is ${count} word(s) (target ${min}–${max}). Too ${direction} — ${
      direction === 'short' ? 'you may be missing relevant data' : 'you may be including semi-relevant data'
    }. Advisory only; submission is not blocked.`;
    return [{ ruleId: rule.id, severity: 'minor' as const, message }];
  },

  // R03.9 / R08.1 — paragraph count
  letter_paragraph_count(rule, _input, structure) {
    const params = (rule.params as { min?: number; max?: number } | undefined) ?? {};
    const min = params.min ?? 2;
    const max = params.max ?? 4;
    const n = structure.bodyParagraphs.length;
    if (n < min) return [ruleFinding(rule, `Body has ${n} paragraph(s). Minimum is ${min}.`)];
    if (n > max) return [ruleFinding(rule, `Body has ${n} paragraphs. Maximum is ${max}.`)];
    return [];
  },

  min_body_paragraphs(rule, _input, structure) {
    return DETECTORS.letter_paragraph_count(rule, _input, structure).filter((f) =>
      f.message.startsWith('Body has') && f.message.includes('Minimum'),
    );
  },

  // R04.1 — letter structure order: must see address -> date -> salutation -> Re -> body -> Yours
  letter_structure_order(rule, _input, structure) {
    const missing: string[] = [];
    if (structure.dateIndex === undefined) missing.push('Date');
    if (structure.salutationIndex === undefined) missing.push('Salutation (Dear ...)');
    if (structure.reLineIndex === undefined) missing.push('Re: line');
    if (structure.yoursIndex === undefined) missing.push('Yours sincerely/faithfully');
    if (missing.length) {
      return [ruleFinding(rule, `Letter structure is missing: ${missing.join(', ')}.`)];
    }
    // Order check
    const indices = [structure.dateIndex!, structure.salutationIndex!, structure.reLineIndex!, structure.yoursIndex!];
    for (let i = 1; i < indices.length; i++) {
      if (indices[i] <= indices[i - 1]) {
        return [ruleFinding(rule, 'Letter structure order is wrong. Expected: Address → Date → Salutation → Re: line → Body → Yours sincerely/faithfully.')];
      }
    }
    return [];
  },

  // R04.2 — Salutation and Re: line on consecutive lines (no blank between)
  salutation_re_adjacent(rule, _input, structure) {
    if (structure.salutationIndex === undefined || structure.reLineIndex === undefined) return [];
    const gap = structure.reLineIndex - structure.salutationIndex;
    if (gap !== 1) {
      return [
        ruleFinding(rule, "No blank line allowed between 'Dear ...' and 'Re:'. They must be on consecutive lines."),
      ];
    }
    return [];
  },

  // R04.4 — one blank line between paragraphs (we approximate: body split yields ≥2 paragraphs and no paragraph has leading/trailing blanks)
  blank_line_between_paragraphs(rule, _input, structure) {
    // If we only have one body block but the raw body contains multiple logical sentences split by single newlines, flag.
    if (structure.body.length > 0 && structure.bodyParagraphs.length === 1 && structure.body.includes('\n')) {
      return [ruleFinding(rule, 'Separate body paragraphs with exactly one blank line (press Enter twice).')];
    }
    return [];
  },

  // R05.2 — address no commas/full stops, component caps
  address_punctuation(rule, _input, structure) {
    // Address is everything before the date (or before salutation if no date).
    const boundary = structure.dateIndex ?? structure.salutationIndex ?? structure.lines.length;
    const addressLines = structure.lines
      .slice(0, boundary)
      .filter((line) => line.trim().length > 0);
    const findings: LintFinding[] = [];
    for (const line of addressLines) {
      if (/[,.]$/.test(line.trim())) {
        findings.push(ruleFinding(rule, `Address line contains punctuation: "${line.trim()}". No commas or full stops in the address.`));
      }
    }
    return findings;
  },

  // R05.8 — No 'Date:' prefix
  no_date_prefix(rule, input) {
    const m = /^\s*Date\s*:/im.exec(input.letterText);
    if (m) return [ruleFinding(rule, "Do not write 'Date:' before the date. The date stands on its own line.", { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R05.9 — date blank-line sandwich
  date_blank_line_sandwich(rule, _input, structure) {
    if (structure.dateIndex === undefined) return [];
    const above = structure.dateIndex - 1 >= 0 ? structure.lines[structure.dateIndex - 1] : 'ABSENT';
    const below = structure.dateIndex + 1 < structure.lines.length ? structure.lines[structure.dateIndex + 1] : 'ABSENT';
    const findings: LintFinding[] = [];
    if (above !== 'ABSENT' && above.trim().length !== 0) {
      findings.push(ruleFinding(rule, 'The date must have exactly one blank line above it.'));
    }
    if (below !== 'ABSENT' && below.trim().length !== 0) {
      findings.push(ruleFinding(rule, 'The date must have exactly one blank line below it, before the salutation.'));
    }
    return findings;
  },

  // R06.1 — standard salutation uses last name
  salutation_last_name_only(rule, _input, structure) {
    if (structure.salutationIndex === undefined) return [];
    const line = structure.lines[structure.salutationIndex];
    // Flag first-name + last-name pattern
    const firstLast = /^Dear\s+(Dr\.?|Mr\.?|Ms\.?|Mrs\.?|Miss)\s+\w+\s+\w+/i;
    if (firstLast.test(line) && !/Sir\/?Madam/i.test(line)) {
      return [ruleFinding(rule, `Salutation should use LAST name only (e.g. "Dear Dr Smith,"), not first+last.`, { quote: line.trim() })];
    }
    return [];
  },

  // R06.7 / R12.2 / R08.14 — body must not say "the patient"
  body_forbidden_phrase_the_patient(rule, _input, structure) {
    const re = /\bthe patient\b/gi;
    const m = re.exec(structure.body);
    if (m) return [ruleFinding(rule, "Do not use 'the patient' in the body. Use title + last name (e.g. 'Ms Miller') or a pronoun.", { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  body_uses_last_name_only(rule, input, structure) {
    // If we know the recipient's full name via Re: line, we could cross-check; for now we
    // piggy-back on the_patient detector + flag occurrences of the last-name-less "Dr" outside salutation.
    return DETECTORS.body_forbidden_phrase_the_patient(rule, input, structure);
  },

  // R06.8 — Age / DOB handling in Re:
  re_line_age_dob(rule, _input, structure) {
    if (structure.reLineIndex === undefined) return [];
    const reLine = structure.lines[structure.reLineIndex];
    // Flag "aged 40:" or "Aged:" forms
    if (/\bAge\s*:/i.test(reLine)) {
      return [ruleFinding(rule, "Use 'aged 40' (lowercase, no colon) not 'Age: 40' in the Re: line.", { quote: reLine.trim() })];
    }
    return [];
  },

  // R06.10 — Children: no title in Re: line, first name in body
  minor_naming_convention(rule, input, structure) {
    if (!input.patientIsMinor) return [];
    if (structure.reLineIndex === undefined) return [];
    const reLine = structure.lines[structure.reLineIndex];
    if (/^\s*Re\s*:\s*(Mr|Ms|Miss|Mrs|Dr|Master)\s/i.test(reLine)) {
      return [ruleFinding(rule, 'For minors (under 18), do NOT use a title in the Re: line. Write the full name only.', { quote: reLine.trim() })];
    }
    return [];
  },

  // R06.11 — sincerely when named, faithfully when anonymous
  yours_sincerely_vs_faithfully(rule, _input, structure) {
    if (structure.salutationIndex === undefined || structure.yoursIndex === undefined) return [];
    const salutation = structure.lines[structure.salutationIndex];
    const yours = structure.lines[structure.yoursIndex];
    const isAnonymous = /Sir\/?Madam|Dear Doctor\b/i.test(salutation) && !/Dr\s+\w+/i.test(salutation);
    const usesSincerely = /sincerely/i.test(yours);
    if (isAnonymous && usesSincerely) {
      return [ruleFinding(rule, "When the salutation is 'Dear Sir/Madam' or 'Dear Doctor', use 'Yours faithfully', not 'Yours sincerely'.", { quote: yours.trim() })];
    }
    if (!isAnonymous && !usesSincerely) {
      return [ruleFinding(rule, "When the recipient is named (e.g. 'Dear Dr Smith'), use 'Yours sincerely', not 'Yours faithfully'.", { quote: yours.trim() })];
    }
    return [];
  },

  // R06.12 — Yours capitalisation + spelling
  yours_sincerely_capitalisation(rule, _input, structure) {
    if (structure.yoursIndex === undefined) return [];
    const yours = structure.lines[structure.yoursIndex].trim();
    // Lowercase 'yours' is wrong
    if (/^yours\s+(sincerely|faithfully)/i.test(yours) && !/^Yours\s+(sincerely|faithfully)/.test(yours)) {
      return [ruleFinding(rule, "Capitalise 'Yours' (capital Y, lowercase s in 'sincerely').", { quote: yours })];
    }
    // Misspellings
    if (/sincerly|sincerley|sincrely|faithfuly|faithfully|sincerely/i.test(yours)) {
      if (/sincerly|sincerley|sincrely|faithfuly/i.test(yours)) {
        return [ruleFinding(rule, "Check spelling of the closing phrase (S-I-N-C-E-R-E-L-Y).", { quote: yours })];
      }
    }
    return [];
  },

  // R07.1 — intro ≤3 sentences
  intro_sentence_count(rule, _input, structure) {
    if (structure.salutationIndex === undefined || structure.bodyParagraphs.length === 0) return [];
    const intro = structure.bodyParagraphs[0];
    const sentences = intro.split(/(?<=[.!?])\s+/).filter((s) => s.trim().length);
    const params = (rule.params as { maxSentences?: number } | undefined) ?? {};
    const max = params.maxSentences ?? 3;
    if (sentences.length > max) {
      return [ruleFinding(rule, `Introduction has ${sentences.length} sentences. Keep it to ${max} maximum.`)];
    }
    return [];
  },

  // R07.3 — intro contains a purpose/request
  intro_contains_purpose(rule, _input, structure) {
    if (structure.bodyParagraphs.length === 0) return [];
    const intro = structure.bodyParagraphs[0];
    const purposeMarkers = /\b(I am writing to|I am referring|I would like to refer|I am requesting|requesting|refer|update you|regarding|for (your|specialist|further) (assessment|management|review))\b/i;
    if (!purposeMarkers.test(intro)) {
      return [ruleFinding(rule, 'Introduction does not state the purpose/request. Always include why you are writing.')];
    }
    return [];
  },

  // R07.6 / R13.2 — urgent intro must contain 'urgent'
  urgent_intro_contains_urgent(rule, input, structure) {
    if (input.letterType !== 'urgent_referral') return [];
    const intro = structure.bodyParagraphs[0] ?? '';
    if (!/\burgent/i.test(intro)) {
      return [ruleFinding(rule, "Urgent referral introduction MUST contain the word 'urgent'.")];
    }
    return [];
  },

  // R07.7 / R14.3 — discharge intro does not describe identity
  discharge_intro_no_identity(rule, input, structure) {
    if (input.letterType !== 'discharge') return [];
    const intro = structure.bodyParagraphs[0] ?? '';
    if (/\b\d+-year-old\b|\boccupation\b|\baged\s+\d+\b/i.test(intro)) {
      return [ruleFinding(rule, 'Discharge intro must not describe patient identity (age, occupation). The GP already knows the patient.')];
    }
    return [];
  },

  // R08.7 / R10.14 — forbidden 'next visit'
  body_forbidden_phrase_next_visit(rule, _input, structure) {
    const re = /\bnext visit\b/gi;
    const m = re.exec(structure.body);
    if (m) return [ruleFinding(rule, "Never write 'next visit'. Use 'on the following visit' / 'later on' / 'later that month'.", { quote: m[0], start: m.index, end: m.index + m[0].length, fixSuggestion: 'on the following visit' })];
    return [];
  },

  // R08.8 — never write today's date in body
  body_no_todays_date(rule, _input, structure) {
    const dateRe = /\b\d{1,2}[\/\-\s](\d{1,2}|[A-Za-z]+)[\/\-\s]\d{2,4}\b/g;
    const m = dateRe.exec(structure.body);
    if (m) return [ruleFinding(rule, "Never write today's date in the body. Use 'On today's visit' or 'On today's presentation'.", { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R08.9 — 'yesterday'
  body_forbidden_phrase_yesterday(rule, _input, structure) {
    const re = /\byesterday\b/gi;
    const m = re.exec(structure.body);
    if (m) return [ruleFinding(rule, "'Yesterday' is never used in medical letters. Write 'one day prior' or 'one day ago' instead.", { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R09.2 / R13.3 — urgent closure
  urgent_closure_phrase(rule, input) {
    if (input.letterType !== 'urgent_referral') return [];
    if (!/at your earliest convenience/i.test(input.letterText)) {
      return [ruleFinding(rule, "Urgent closure must include 'at your earliest convenience.'")];
    }
    return [];
  },

  // R09.3 — urgent token not repeated after intro
  urgent_token_not_repeated(rule, input, structure) {
    if (input.letterType !== 'urgent_referral') return [];
    const bodyLessIntro = structure.bodyParagraphs.slice(1).join('\n\n');
    const occurrences = (bodyLessIntro.match(/\burgent/gi) ?? []).length;
    if (occurrences > 0) {
      return [ruleFinding(rule, "Use 'urgent' only in the introduction. Use 'at your earliest convenience' in the closure to imply urgency.")];
    }
    return [];
  },

  // R09.4 / R14.10 — follow-up mentioned if case notes flagged it
  closure_mentions_review_if_required(rule, input) {
    const followUp = input.caseNotesMarkers?.followUpDate;
    if (!followUp) return [];
    if (!/\b(review|follow[- ]up|see you (in|again)|appointment)\b/i.test(input.letterText)) {
      return [ruleFinding(rule, `Case notes mention a follow-up (${followUp}) — closure must reference the review.`)];
    }
    return [];
  },

  // R09.5 / R14.14 — enclosed results phrase
  enclosure_results_phrase(rule, input) {
    if (!input.caseNotesMarkers?.resultsEnclosed) return [];
    if (!/please find enclosed/i.test(input.letterText)) {
      return [ruleFinding(rule, "Results/imaging marked as enclosed — add a phrase like 'Please find enclosed a copy of Mr Smith's pathology results.'")];
    }
    return [];
  },

  // R09.7 — patient-initiated referral phrase
  closure_mentions_patient_request_if_flagged(rule, input) {
    if (!input.caseNotesMarkers?.patientInitiatedReferral) return [];
    if (!/\bat (his|her|mr|ms|mrs|miss|dr) .*?(\brequest|\bown request)\b|upon (his|her) request/i.test(input.letterText)) {
      return [ruleFinding(rule, "Patient-initiated referral — include 'upon his/her request' or 'at [Name]'s request'.")];
    }
    return [];
  },

  // R09.8 — consent statement
  closure_mentions_consent_if_flagged(rule, input) {
    if (!input.caseNotesMarkers?.consentDocumented) return [];
    if (!/\b(fully informed|has consented|has been informed)\b/i.test(input.letterText)) {
      return [ruleFinding(rule, "Consent was documented in case notes — include the consent statement in closure.")];
    }
    return [];
  },

  // R09.9 — blank line before Yours sincerely
  blank_before_closing_phrase(rule, _input, structure) {
    if (structure.yoursIndex === undefined || structure.yoursIndex === 0) return [];
    const prev = structure.lines[structure.yoursIndex - 1];
    if (prev.trim().length > 0) {
      return [ruleFinding(rule, "Leave one blank line between the last body paragraph and 'Yours sincerely,'.")];
    }
    return [];
  },

  // R10.5 — "since" requires present perfect
  since_requires_present_perfect(rule, _input, structure) {
    const re = /\b(?:she|he|they|the patient|[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\s+(had|has)\s+([a-z]+)\s+since\b/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(structure.body))) {
      if (m[1].toLowerCase() === 'had') {
        findings.push(
          ruleFinding(rule, "With 'since', use present perfect ('has had X since ...'), not past simple.", {
            quote: m[0],
            start: m.index,
            end: m.index + m[0].length,
          }),
        );
      }
    }
    return findings;
  },

  // R10.6 — "for" + duration requires present perfect (heuristic)
  for_duration_requires_present_perfect(rule, _input, structure) {
    const re = /\b(she|he|they)\s+had\s+([a-z ]+?)\s+for\s+(\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(years?|months?|weeks?|days?)\b/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(structure.body))) {
      findings.push(
        ruleFinding(rule, "With 'for [duration]', use present perfect ('has had X for Y').", {
          quote: m[0],
          start: m.index,
          end: m.index + m[0].length,
        }),
      );
    }
    return findings;
  },

  // R10.8 — surgery past simple, never present perfect
  surgery_past_simple(rule, _input, structure) {
    const re = /\bhas\s+had\s+(a\s+|an\s+)?([a-z]+(?:ectomy|otomy|ostomy|plasty)|surgery|operation)\b/gi;
    const m = re.exec(structure.body);
    if (m) return [ruleFinding(rule, 'Surgery uses past simple: "had a cholecystectomy in 2018". Do not use present perfect for surgery.', { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R10.10 — "X ago" requires past simple
  ago_requires_past_simple(rule, _input, structure) {
    const re = /\bhas\s+(\w+ed)\s+(\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(years?|months?|weeks?|days?)\s+ago\b/gi;
    const m = re.exec(structure.body);
    if (m) return [ruleFinding(rule, '"X ago" always takes past simple, not present perfect. Write "She presented 3 weeks ago."', { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R11.1 — Latin abbreviations translated
  latin_abbreviations_translated(rule, _input, structure) {
    const map = (rule.params as { map?: Record<string, string> } | undefined)?.map ?? {};
    const findings: LintFinding[] = [];
    for (const abbr of Object.keys(map)) {
      const re = new RegExp(`\\b${abbr}\\b`, 'gi');
      const m = re.exec(structure.body);
      if (m) {
        findings.push(
          ruleFinding(rule, `Translate Latin abbreviation "${abbr}" to plain English ("${map[abbr]}").`, {
            quote: m[0],
            start: m.index,
            end: m.index + m[0].length,
            fixSuggestion: map[abbr],
          }),
        );
      }
    }
    return findings;
  },

  // R12.1 — no contractions
  no_contractions(rule, _input, structure) {
    const re = /\b(?:don't|can't|won't|isn't|aren't|doesn't|didn't|wasn't|weren't|hasn't|haven't|hadn't|I'm|I've|I'll|she's|he's|it's|we're|they're|you're|you'd|we'd|they'd|I'd|we've|they've|you've|couldn't|wouldn't|shouldn't)\b/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(structure.body))) {
      findings.push(
        ruleFinding(rule, `Contraction "${m[0]}" is not allowed in OET letters. Expand it.`, {
          quote: m[0],
          start: m.index,
          end: m.index + m[0].length,
        }),
      );
      if (findings.length >= 5) break;
    }
    return findings;
  },

  // R12.5 — medical conditions lowercase (sample common ones)
  conditions_lowercase(rule, _input, structure) {
    const forbidden = ['Hypertension', 'Asthma', 'Diabetes Mellitus', 'Gastroenteritis', 'Bronchial Asthma', 'Myocardial Infarction'];
    const findings: LintFinding[] = [];
    for (const term of forbidden) {
      const re = new RegExp(`(?<![A-Za-z])${term}(?![a-z])`, 'g');
      const m = re.exec(structure.body);
      if (m) {
        findings.push(
          ruleFinding(rule, `Medical condition "${m[0]}" should be lowercase in running text.`, {
            quote: m[0],
            start: m.index,
            end: m.index + m[0].length,
            fixSuggestion: m[0].toLowerCase(),
          }),
        );
      }
    }
    return findings;
  },

  // R12.9 — 'however' punctuation: ; however,
  linker_however_punctuation(rule, _input, structure) {
    const hits = structure.body.match(/\bhowever\b/gi) ?? [];
    if (hits.length === 0) return [];
    // Flag 'however,' without preceding ';' within a reasonable window
    const re = /([^\n;]{5,})\bhowever\b/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(structure.body))) {
      if (!m[1].trimEnd().endsWith(';')) {
        findings.push(
          ruleFinding(rule, "Precede 'however' with a semicolon: '[clause]; however, [clause].'", {
            quote: m[0].trim(),
            start: m.index,
            end: m.index + m[0].length,
          }),
        );
      }
    }
    return findings;
  },

  // R12.10 — 'therefore' / 'thus'
  linker_therefore_punctuation(rule, _input, structure) {
    const re = /([^\n;]{5,})\b(therefore|thus)\b/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(structure.body))) {
      if (!m[1].trimEnd().endsWith(';')) {
        findings.push(
          ruleFinding(rule, "Precede 'therefore'/'thus' with a semicolon: '[clause]; therefore, [clause].'", {
            quote: m[0].trim(),
            start: m.index,
            end: m.index + m[0].length,
          }),
        );
      }
    }
    return findings;
  },

  // R12.11 — 'in addition' (as clause joiner)
  linker_in_addition_punctuation(rule, _input, structure) {
    const re = /([^\n;]{5,})\bin addition\b(?!\s+to\b)/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = re.exec(structure.body))) {
      if (!m[1].trimEnd().endsWith(';')) {
        findings.push(
          ruleFinding(rule, "Precede 'in addition' (as a clause joiner) with a semicolon.", {
            quote: m[0].trim(),
            start: m.index,
            end: m.index + m[0].length,
          }),
        );
      }
    }
    return findings;
  },

  // R12.19 — sentence length 15–25 (flag >30)
  sentence_length_guard(rule, _input, structure) {
    const params = (rule.params as { maxWords?: number } | undefined) ?? {};
    const max = params.maxWords ?? 30;
    const findings: LintFinding[] = [];
    const sentences = structure.body.split(/(?<=[.!?])\s+/);
    for (const s of sentences) {
      const wc = (s.match(WORD_RE) ?? []).length;
      if (wc > max) {
        findings.push(
          ruleFinding(rule, `Sentence is ${wc} words — aim for 15–25. Split to improve clarity.`, {
            quote: s.slice(0, 80) + (s.length > 80 ? '…' : ''),
          }),
        );
        if (findings.length >= 3) break;
      }
    }
    return findings;
  },

  // R12.20 — linker density
  linker_density(rule, _input, structure) {
    const params = (rule.params as { maxPerParagraph?: number } | undefined) ?? {};
    const max = params.maxPerParagraph ?? 2;
    const linkerRe = /\b(however|therefore|thus|in addition|moreover|furthermore|consequently|nonetheless|nevertheless|subsequently)\b/gi;
    const findings: LintFinding[] = [];
    for (const p of structure.bodyParagraphs) {
      const count = (p.match(linkerRe) ?? []).length;
      if (count > max) {
        findings.push(
          ruleFinding(rule, `Paragraph uses ${count} linkers. Max ${max} per paragraph — excess reduces the Conciseness & Clarity score.`),
        );
      }
    }
    return findings;
  },

  // R13.10 — never 'ASAP'
  no_asap_in_letter(rule, _input, structure) {
    const m = /\bASAP\b/.exec(structure.body);
    if (m) return [ruleFinding(rule, "Never write 'ASAP' in the letter. Use 'at your earliest convenience' instead.", { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R08.5 / R13.4 — urgent body starts today
  urgent_body_starts_today(rule, input, structure) {
    if (input.letterType !== 'urgent_referral') return [];
    // First body paragraph is intro. Second paragraph should reference today.
    const firstVisitPara = structure.bodyParagraphs[1] ?? '';
    if (!/\b(today|on today's|presented today|on this visit)/i.test(firstVisitPara)) {
      return [ruleFinding(rule, "Urgent referrals must start the body with today's visit (the acute presentation) before background.")];
    }
    return [];
  },

  // R14.2 — discharge intro template
  discharge_intro_template(rule, input, structure) {
    if (input.letterType !== 'discharge') return [];
    const intro = structure.bodyParagraphs[0] ?? '';
    if (!/I am writing to update you regarding/i.test(intro)) {
      return [ruleFinding(rule, 'Discharge intro must start "I am writing to update you regarding..." — do not use routine-referral phrasing.')];
    }
    return [];
  },

  // R14.4 — discharge omits FH/SH/smoking/PMH/occupation
  discharge_omits_knownto_gp(rule, input) {
    if (input.letterType !== 'discharge') return [];
    const findings: LintFinding[] = [];
    if (/\bfamily history\b/i.test(input.letterText)) findings.push(ruleFinding(rule, 'Discharge must not include family history — GP already knows it.'));
    if (/\bsocial history\b/i.test(input.letterText)) findings.push(ruleFinding(rule, 'Discharge must not include social history.'));
    if (/\bpast medical history\b/i.test(input.letterText)) findings.push(ruleFinding(rule, 'Discharge must not include past medical history.'));
    if (/\bsmok(ing|es|er)\b/i.test(input.letterText)) findings.push(ruleFinding(rule, 'Discharge must not include smoking status.'));
    return findings;
  },

  // R14.6 — admission phrasing ("was admitted with" not "was presented with")
  discharge_admitted_with_past_simple(rule, input) {
    if (input.letterType !== 'discharge') return [];
    const m = /\bwas presented\b/i.exec(input.letterText);
    if (m) return [ruleFinding(rule, '"Was presented" is incorrect. Use "was admitted to [hospital] with [condition]".', { quote: m[0], start: m.index, end: m.index + m[0].length })];
    return [];
  },

  // R14.7 — discharge plan paragraph must list medications + post-discharge instructions
  discharge_plan_present(rule, input, structure) {
    if (input.letterType !== 'discharge') return [];
    const hasMeds = /\b(mg|mcg|tablet|capsule|ml|prescribed|discharged? with|dose)\b/i.test(structure.body);
    const hasInstr = /\b(follow[- ]up|review|advised|should|must|recommend)/i.test(structure.body);
    if (!hasMeds || !hasInstr) {
      return [ruleFinding(rule, 'Discharge plan paragraph must contain medications with doses AND post-discharge instructions.')];
    }
    return [];
  },

  // R14.12 — treatment FOR not FROM
  treatment_for_not_from(rule, input) {
    const re = /\b(treated|admitted|referred|managed)\s+from\b/gi;
    const m = re.exec(input.letterText);
    if (m) return [ruleFinding(rule, `Use 'treatment/admission/referral for ...', not 'from ...'.`, { quote: m[0], start: m.index, end: m.index + m[0].length, fixSuggestion: m[0].replace(/from/i, 'for') })];
    return [];
  },

  // R15.2 — non-medical: no medical jargon
  non_medical_no_jargon(rule, input) {
    if (input.letterType !== 'non_medical_referral') return [];
    const jargon = /\b(hypertension|hypoglycaemia|hyperglycaemia|myocardial infarction|tachycardia|bradycardia|BP|ECG|MRI|CT scan|paediatric|gynaecologic|endocrine)\b/gi;
    const findings: LintFinding[] = [];
    let m: RegExpExecArray | null;
    while ((m = jargon.exec(input.letterText))) {
      findings.push(
        ruleFinding(rule, `Non-medical referral must avoid medical jargon "${m[0]}". Use plain English.`, {
          quote: m[0],
          start: m.index,
          end: m.index + m[0].length,
        }),
      );
      if (findings.length >= 3) break;
    }
    return findings;
  },

  // R05.5 — date format consistency (basic heuristic)
  date_format_consistent(rule, input) {
    const formats = new Set<string>();
    const re = /\b(\d{1,2}\/\d{1,2}\/\d{4}|\d{1,2}\s+[A-Za-z]+\s+\d{4}|[A-Za-z]+\s+\d{1,2},?\s+\d{4})\b/g;
    let m: RegExpExecArray | null;
    while ((m = re.exec(input.letterText))) {
      formats.add(m[0].match(/^\d/) ? 'numeric' : 'long');
    }
    if (formats.size > 1) {
      return [ruleFinding(rule, 'Date format must be consistent throughout the letter.')];
    }
    return [];
  },

  // R05.6 — year not abbreviated
  year_not_abbreviated(rule, input) {
    const m = /\b\d{1,2}\/\d{1,2}\/'?\d{2}\b/.exec(input.letterText);
    if (m && !m[0].includes('19') && !m[0].includes('20')) {
      return [ruleFinding(rule, "Do not abbreviate the year (write 2024, not '24).", { quote: m[0], start: m.index, end: m.index + m[0].length })];
    }
    return [];
  },
};

// ---------------------------------------------------------------------------
// Generic detectors: forbidden patterns baked into rule JSON
// ---------------------------------------------------------------------------

function runForbiddenPatternChecks(rule: Rule, input: WritingLintInput): LintFinding[] {
  const findings: LintFinding[] = [];
  if (!rule.forbiddenPatterns?.length) return findings;
  for (const pat of rule.forbiddenPatterns) {
    const re = new RegExp(pat, 'gi');
    const hit = findQuote(input.letterText, re);
    if (hit) {
      findings.push(ruleFinding(rule, `Rule ${rule.id}: "${hit.quote}" violates the pattern.`, hit));
    }
  }
  return findings;
}

// ---------------------------------------------------------------------------
// Public entry point
// ---------------------------------------------------------------------------

export function lintWritingLetter(input: WritingLintInput): LintFinding[] {
  const profession = input.profession ?? 'medicine';
  const book = loadRulebook('writing', profession);
  const structure = parseLetter(input.letterText);
  const applicable = rulesApplicableTo(book, input.letterType);

  const findings: LintFinding[] = [];
  for (const rule of applicable) {
    if (rule.checkId && DETECTORS[rule.checkId]) {
      findings.push(...DETECTORS[rule.checkId](rule, input, structure));
    }
    if (rule.forbiddenPatterns?.length) {
      findings.push(...runForbiddenPatternChecks(rule, input));
    }
  }

  // Deduplicate by (ruleId + quote)
  const seen = new Set<string>();
  const unique: LintFinding[] = [];
  for (const f of findings) {
    const key = `${f.ruleId}|${f.quote ?? ''}|${f.message}`;
    if (seen.has(key)) continue;
    seen.add(key);
    unique.push(f);
  }

  // Sort: critical > major > minor > info, then by start offset
  const sevRank: Record<string, number> = { critical: 0, major: 1, minor: 2, info: 3 };
  unique.sort((a, b) => {
    const sa = sevRank[a.severity] ?? 99;
    const sb = sevRank[b.severity] ?? 99;
    if (sa !== sb) return sa - sb;
    return (a.start ?? 0) - (b.start ?? 0);
  });
  return unique;
}

/** Coverage summary for UIs and analytics. */
export function writingCoverageSummary(input: WritingLintInput): {
  totalRules: number;
  critical: { total: number; violated: number };
  major: { total: number; violated: number };
  minor: { total: number; violated: number };
  findings: LintFinding[];
} {
  const profession = input.profession ?? 'medicine';
  const book = loadRulebook('writing', profession);
  const applicable = rulesApplicableTo(book, input.letterType);
  const findings = lintWritingLetter(input);
  const byRule = new Set(findings.map((f) => f.ruleId));

  const tally = (sev: string) => {
    const total = applicable.filter((r) => r.severity === sev).length;
    const violated = applicable.filter((r) => r.severity === sev && byRule.has(r.id)).length;
    return { total, violated };
  };

  return {
    totalRules: applicable.length,
    critical: tally('critical'),
    major: tally('major'),
    minor: tally('minor'),
    findings,
  };
}
