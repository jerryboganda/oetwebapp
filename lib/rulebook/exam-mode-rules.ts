/**
 * ============================================================================
 * Exam-Mode Rule Enforcement Registry
 * ============================================================================
 *
 * The listening-exam-mode and reading-exam-mode rulebooks encode every
 * candidate-facing UX rule from the PDFs as JSON. Each rule has a `checkId`
 * (or an `enforcement` marker if it cannot be machine-enforced). This file is
 * the single, canonical map between those checkIds and the code that actually
 * enforces them — modePolicy flags, FSM transitions, grading services, and
 * Playwright tests.
 *
 * Why this exists: the rulebook JSON tells US which rules are deterministic,
 * but until each `checkId` is matched with an enforcer, the compliance
 * promise is paper-only. This registry makes the matching explicit and
 * runtime-checkable: `findUnimplementedExamModeRules()` returns any rule whose
 * `checkId` is not yet wired, so CI can fail when a future rulebook edit adds
 * a rule but forgets the enforcer.
 *
 * The registry is profession-agnostic because the underlying exam-mode rules
 * are profession-agnostic (Listening R01.10, Reading R01.6).
 * ============================================================================
 */

import { loadRulebook } from './loader';
import type { Rule, Rulebook } from './types';

/**
 * Surface where a rule's `checkId` is enforced. Multiple surfaces per rule
 * are allowed (e.g. a rule with both a server-side policy flag AND a
 * frontend assertion in a Playwright test).
 */
export type EnforcementSurface =
  | { kind: 'mode-policy-flag'; flag: string; expected: unknown }
  | { kind: 'mode-policy-derived'; description: string }
  | { kind: 'fsm-transition'; from: string; to: string; trigger: string }
  | { kind: 'fsm-lock-on-exit'; states: string[] }
  | { kind: 'session-service'; service: string; method: string }
  | { kind: 'grading-service'; service: string; description: string }
  | { kind: 'tech-readiness-probe'; field: string; description: string }
  | { kind: 'ui-component'; component: string; description: string }
  | { kind: 'playwright-spec'; spec: string };

export interface ExamModeEnforcerEntry {
  checkId: string;
  surfaces: EnforcementSurface[];
  notes?: string;
}

// ---------------------------------------------------------------------------
// Listening exam-mode enforcers
// ---------------------------------------------------------------------------

export const LISTENING_EXAM_MODE_ENFORCERS: Readonly<Record<string, ExamModeEnforcerEntry>> = Object.freeze({
  listening_paper_shape: {
    checkId: 'listening_paper_shape',
    surfaces: [
      { kind: 'grading-service', service: 'ListeningAuthoringAdminEndpoints', description: 'Publish gate rejects papers whose 24/6/12 split, A1/A2 ranges, or Q numbering is non-canonical.' },
    ],
  },
  listening_question_type_per_part: {
    checkId: 'listening_question_type_per_part',
    surfaces: [
      { kind: 'grading-service', service: 'ListeningAuthoringAdminEndpoints', description: 'Publish gate enforces Part A = short_answer; Part B = mcq3; Part C = mcq3.' },
    ],
  },
  listening_audio_one_play_only: {
    checkId: 'listening_audio_one_play_only',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'onePlayOnly', expected: true },
      { kind: 'mode-policy-flag', flag: 'canPause', expected: false },
      { kind: 'mode-policy-flag', flag: 'canScrub', expected: false },
      { kind: 'ui-component', component: 'app/listening/player/[id]/page.tsx', description: 'Player blocks rewind via shouldResumeAfterBlockedPause + resolveBlockedSeekTarget when these flags are set.' },
    ],
  },
  listening_pass_threshold: {
    checkId: 'listening_pass_threshold',
    surfaces: [
      { kind: 'grading-service', service: 'OetScoring', description: 'Canonical 30/42 ≡ 350/500 anchor — set by Listening grading service post-submit.' },
    ],
  },
  listening_part_a_strict_spelling: {
    checkId: 'listening_part_a_strict_spelling',
    surfaces: [
      { kind: 'grading-service', service: 'ListeningGradingService', description: 'Whole-word-wrong policy: any spelling error in a Part A answer marks the entire answer wrong, with limited examiner-discretion variants accepted (see R02.5).' },
    ],
  },
  listening_part_a_variant_acceptance: {
    checkId: 'listening_part_a_variant_acceptance',
    surfaces: [
      { kind: 'grading-service', service: 'ListeningGradingService', description: 'Accepted-variants list per ListeningAnswerKey row; UK/US spelling and abbreviation variants resolve to the canonical answer.' },
    ],
  },
  listening_no_score_clipping: {
    checkId: 'listening_no_score_clipping',
    surfaces: [
      { kind: 'grading-service', service: 'OetScoring', description: 'Medicine pathway requires single-sitting pass; clipping disabled.' },
    ],
  },
  listening_part_a1_pre_read_window: {
    checkId: 'listening_part_a1_pre_read_window',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'reviewMsA1', expected: { type: 'number', min: 30000 } },
      { kind: 'fsm-transition', from: 'a1_intro', to: 'a1_audio', trigger: 'pre_read_done' },
    ],
  },
  listening_a1_review_window: {
    checkId: 'listening_a1_review_window',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'reviewMsA1', expected: { type: 'number', min: 60000, max: 90000 } },
    ],
  },
  listening_a2_pre_read_window: {
    checkId: 'listening_a2_pre_read_window',
    surfaces: [
      { kind: 'fsm-transition', from: 'a2_pre', to: 'a2_audio', trigger: 'pre_read_done' },
    ],
  },
  listening_a2_review_window: {
    checkId: 'listening_a2_review_window',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'reviewMsA2', expected: { type: 'number', min: 60000, max: 90000 } },
    ],
  },
  listening_part_b_pre_question_window: {
    checkId: 'listening_part_b_pre_question_window',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'partBQuestionWindowMs', expected: { type: 'number', min: 15000 } },
    ],
  },
  listening_part_b_one_per_screen_cbt: {
    checkId: 'listening_part_b_one_per_screen_cbt',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'oneWayLocks', expected: true },
      { kind: 'ui-component', component: 'app/listening/player/[id]/page.tsx', description: 'In exam/home mode the player renders one Part B question per screen and advances on Next.' },
    ],
  },
  listening_a_to_b_transition: {
    checkId: 'listening_a_to_b_transition',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'betweenSectionTransitionMs', expected: { type: 'number', min: 40000 } },
    ],
  },
  listening_part_c_continuous_audio: {
    checkId: 'listening_part_c_continuous_audio',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'canPause', expected: false },
      { kind: 'ui-component', component: 'app/listening/player/[id]/page.tsx', description: 'Part C audio plays continuously; question navigation does not pause playback.' },
    ],
  },
  listening_b_to_c1_transition: {
    checkId: 'listening_b_to_c1_transition',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'betweenSectionTransitionMs', expected: { type: 'number', min: 60000 } },
    ],
  },
  listening_c1_review_window: {
    checkId: 'listening_c1_review_window',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'reviewMsC1', expected: { type: 'number', min: 30000 } },
    ],
  },
  listening_c2_pre_read_window: {
    checkId: 'listening_c2_pre_read_window',
    surfaces: [
      { kind: 'fsm-transition', from: 'c2_pre', to: 'c2_audio', trigger: 'pre_read_done' },
    ],
  },
  listening_c2_final_review_scope: {
    checkId: 'listening_c2_final_review_scope',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'reviewWindowMsC2FinalCbt', expected: { type: 'number', min: 120000 } },
      { kind: 'ui-component', component: 'app/listening/player/[id]/page.tsx', description: 'When state == c2_final_review, visible question set is filtered to partC2Range (Q37–42) only.' },
      { kind: 'playwright-spec', spec: 'tests/e2e/listening/c2-only-final-review.spec.ts' },
    ],
  },
  listening_section_lock_one_way: {
    checkId: 'listening_section_lock_one_way',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'oneWayLocks', expected: true },
      { kind: 'session-service', service: 'ListeningSessionService', method: 'AdvanceStateAsync — appends prior state to nav.Locks; subsequent transitions to a locked state are rejected.' },
      { kind: 'fsm-lock-on-exit', states: ['a1_audio', 'a1_review', 'a2_audio', 'a2_review', 'b_q', 'c1_audio', 'c1_review', 'c2_audio'] },
    ],
  },
  listening_within_part_c_free_nav: {
    checkId: 'listening_within_part_c_free_nav',
    surfaces: [
      { kind: 'mode-policy-derived', description: 'Within a Part C extract, the left-side question tabs are not part of the Locks set — free navigation within section is allowed.' },
    ],
  },
  listening_section_boundary_confirm_dialog: {
    checkId: 'listening_section_boundary_confirm_dialog',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'confirmDialogRequired', expected: true },
      { kind: 'session-service', service: 'ListeningSessionService', method: 'RequestConfirmTokenAsync — issues a confirm-token + TTL the client must present before AdvanceStateAsync accepts a boundary cross.' },
    ],
  },
  listening_unanswered_warning: {
    checkId: 'listening_unanswered_warning',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'unansweredWarningRequired', expected: true },
    ],
  },
  listening_part_c_split_layout: {
    checkId: 'listening_part_c_split_layout',
    surfaces: [
      { kind: 'ui-component', component: 'app/listening/player/[id]/page.tsx', description: 'Part C view renders audio on the left panel and questions on the right.' },
    ],
  },
  listening_part_a_no_highlight: {
    checkId: 'listening_part_a_no_highlight',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'highlightingEnabledPartA', expected: false },
      { kind: 'ui-component', component: 'components/domain/listening/PartARenderer.tsx', description: 'highlightingEnabled prop is wired to the policy; when false the prompt has user-select: none.' },
    ],
  },
  listening_bc_choices_no_highlight: {
    checkId: 'listening_bc_choices_no_highlight',
    surfaces: [
      { kind: 'ui-component', component: 'components/domain/listening/BCQuestionRenderer.tsx', description: 'Choice text containers have user-select: none — highlight tooling is only on the stem.' },
    ],
  },
  listening_bc_choice_strikethrough: {
    checkId: 'listening_bc_choice_strikethrough',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'optionStrikethroughEnabled', expected: true },
      { kind: 'ui-component', component: 'components/domain/listening/BCQuestionRenderer.tsx', description: 'Right-click handler toggles strikethrough on a choice without selecting it.' },
    ],
  },
  listening_zoom_app_only: {
    checkId: 'listening_zoom_app_only',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'inAppZoomEnabled', expected: true },
      { kind: 'mode-policy-flag', flag: 'ctrlZoomBlocked', expected: true },
    ],
  },
  listening_part_c_audio_panel_visible: {
    checkId: 'listening_part_c_audio_panel_visible',
    surfaces: [
      { kind: 'ui-component', component: 'app/listening/player/[id]/page.tsx', description: 'Part C view keeps the audio panel docked to the left for the duration of the extract.' },
    ],
  },
  listening_tech_min_screen_resolution: {
    checkId: 'listening_tech_min_screen_resolution',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'screenWidth/screenHeight', description: 'Pre-exam probe asserts at least 1920×1080.' },
    ],
  },
  listening_tech_display_scale: {
    checkId: 'listening_tech_display_scale',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'displayScalePercent', description: 'Pre-exam probe asserts ≤ 125%.' },
    ],
  },
  listening_tech_wired_audio_device: {
    checkId: 'listening_tech_wired_audio_device',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'audioDeviceLabel', description: 'Pre-exam probe enumerates input/output devices and rejects bluetooth/wireless devices in exam mode.' },
    ],
  },
  listening_tech_stable_internet: {
    checkId: 'listening_tech_stable_internet',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'networkStability', description: 'Pre-exam probe runs a short ping/latency check.' },
    ],
  },
  listening_tech_no_vpn_vm: {
    checkId: 'listening_tech_no_vpn_vm',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'vpnDetected/vmDetected', description: 'Pre-exam probe checks for known VPN/VM signals (best-effort; ProProctor is the authoritative gate at exam-day).' },
    ],
  },
});

// ---------------------------------------------------------------------------
// Reading exam-mode enforcers
// ---------------------------------------------------------------------------

export const READING_EXAM_MODE_ENFORCERS: Readonly<Record<string, ExamModeEnforcerEntry>> = Object.freeze({
  reading_paper_shape: {
    checkId: 'reading_paper_shape',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingAuthoringAdminEndpoints', description: 'Publish gate enforces 20/6/16 split with 4 texts in Part A and 2 long texts in Part C.' },
    ],
  },
  reading_session_timing: {
    checkId: 'reading_session_timing',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'partATimerMinutes', expected: 15 },
      { kind: 'mode-policy-flag', flag: 'fullPaperTimerMinutes', expected: 60 },
    ],
  },
  reading_pass_threshold: {
    checkId: 'reading_pass_threshold',
    surfaces: [
      { kind: 'grading-service', service: 'OetScoring', description: 'Canonical 30/42 ≡ 350/500 anchor — set by Reading grading service post-submit.' },
    ],
  },
  reading_no_score_clipping: {
    checkId: 'reading_no_score_clipping',
    surfaces: [
      { kind: 'grading-service', service: 'OetScoring', description: 'Medicine pathway requires single-sitting pass; clipping disabled.' },
    ],
  },
  reading_part_a_four_texts: {
    checkId: 'reading_part_a_four_texts',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingAuthoringAdminEndpoints', description: 'Publish gate enforces exactly 4 source texts in Part A and that all 20 items reference one of them.' },
    ],
  },
  reading_part_a_question_types: {
    checkId: 'reading_part_a_question_types',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingAuthoringAdminEndpoints', description: 'Publish gate enforces Q1–7 matching, Q8–14 short_answer, Q15–20 sentence_completion.' },
    ],
  },
  reading_part_a_source_grounded: {
    checkId: 'reading_part_a_source_grounded',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'Acceptance: answers must appear verbatim in the cited source text (R05.1 paired check).' },
    ],
  },
  reading_part_a_lock_at_15_min: {
    checkId: 'reading_part_a_lock_at_15_min',
    surfaces: [
      { kind: 'session-service', service: 'ReadingSessionService', method: 'TickPartATimer + AdvanceStateAsync — Part A text panel becomes inaccessible after 15 minutes or part submission, mirroring the Listening Locks pattern.' },
      { kind: 'ui-component', component: 'app/reading/player/[id]/page.tsx', description: 'When sessionState.partALocked is true the text panel is unmounted and answers become read-only.' },
      { kind: 'playwright-spec', spec: 'tests/e2e/reading/part-a-15-min-lock.spec.ts' },
    ],
  },
  reading_part_a_strict_spelling: {
    checkId: 'reading_part_a_strict_spelling',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'shortAnswerNormalisation=trim_collapse but no synonym tolerance; any letter mismatch marks the answer wrong.' },
    ],
  },
  reading_part_a_strict_number_form: {
    checkId: 'reading_part_a_strict_number_form',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'Singular/plural mismatch fails the acceptance check unless the canonical answer explicitly lists the alternate form.' },
    ],
  },
  reading_part_a_strict_form: {
    checkId: 'reading_part_a_strict_form',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'Form check: range-vs-max and dose-per-kg-vs-total are detected via canonical answer metadata and reject mismatched forms.' },
    ],
  },
  reading_part_a_numeric_verbatim: {
    checkId: 'reading_part_a_numeric_verbatim',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'Numbers compared verbatim including unit, decimal point, and grouping.' },
    ],
  },
  reading_part_a_verbatim_match: {
    checkId: 'reading_part_a_verbatim_match',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'Q8–14 and Q15–20 require exact-text match; paraphrase / synonym alternates are NOT accepted.' },
    ],
  },
  reading_part_a_copy_paste_warning: {
    checkId: 'reading_part_a_copy_paste_warning',
    surfaces: [
      { kind: 'ui-component', component: 'app/reading/player/[id]/page.tsx', description: 'One-time banner in Part A warns the candidate that copy/paste is unreliable across exam centres.' },
    ],
  },
  reading_part_b_three_options: {
    checkId: 'reading_part_b_three_options',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingAuthoringAdminEndpoints', description: 'Publish gate rejects Part B items whose questionType ≠ MultipleChoice3 or option count ≠ 3.' },
    ],
  },
  reading_part_c_four_options: {
    checkId: 'reading_part_c_four_options',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingAuthoringAdminEndpoints', description: 'Publish gate rejects Part C items whose questionType ≠ MultipleChoice4 or option count ≠ 4.' },
    ],
  },
  reading_part_c_paragraph_order: {
    checkId: 'reading_part_c_paragraph_order',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingAuthoringAdminEndpoints', description: 'Authoring lint: warns when Part C question indexes do not monotonically follow paragraph order.' },
    ],
  },
  reading_no_autofill: {
    checkId: 'reading_no_autofill',
    surfaces: [
      { kind: 'grading-service', service: 'ReadingGradingService', description: 'Blank answers score 0; no auto-fill is applied at grading time.' },
    ],
  },
  reading_bc_choice_strikethrough: {
    checkId: 'reading_bc_choice_strikethrough',
    surfaces: [
      { kind: 'ui-component', component: 'components/domain/reading/PartBcQuestionRenderer.tsx', description: 'Right-click handler toggles strikethrough without selecting.' },
    ],
  },
  reading_bc_strikethrough_not_selection: {
    checkId: 'reading_bc_strikethrough_not_selection',
    surfaces: [
      { kind: 'ui-component', component: 'components/domain/reading/PartBcQuestionRenderer.tsx', description: 'Strikethrough does not write the option index into selectedAnswer — separate state.' },
    ],
  },
  reading_bc_choices_no_highlight: {
    checkId: 'reading_bc_choices_no_highlight',
    surfaces: [
      { kind: 'ui-component', component: 'components/domain/reading/PartBcQuestionRenderer.tsx', description: 'Choice text containers have user-select: none — only the question stem and source text accept selection.' },
    ],
  },
  reading_no_break_after_listening_cbt: {
    checkId: 'reading_no_break_after_listening_cbt',
    surfaces: [
      { kind: 'session-service', service: 'ReadingSessionService', method: 'StartSessionAsync — when triggered from the Listening completion handoff, Reading begins immediately with no break screen.' },
    ],
  },
  reading_optional_break_between_a_and_bc: {
    checkId: 'reading_optional_break_between_a_and_bc',
    surfaces: [
      { kind: 'session-service', service: 'ReadingSessionService', method: 'OptionalBreakState — 10-minute optional break, skippable via Resume Test.' },
    ],
  },
  reading_section_boundary_confirm_dialog: {
    checkId: 'reading_section_boundary_confirm_dialog',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'confirmDialogRequired', expected: true },
    ],
  },
  reading_bc_lock_at_45_min: {
    checkId: 'reading_bc_lock_at_45_min',
    surfaces: [
      { kind: 'session-service', service: 'ReadingSessionService', method: 'TickFullPaperTimer — after 45 min in B+C the screen locks and submission auto-closes.' },
    ],
  },
  reading_zoom_app_only: {
    checkId: 'reading_zoom_app_only',
    surfaces: [
      { kind: 'mode-policy-flag', flag: 'inAppZoomEnabled', expected: true },
      { kind: 'mode-policy-flag', flag: 'ctrlZoomBlocked', expected: true },
    ],
  },
  reading_tech_min_screen_resolution: {
    checkId: 'reading_tech_min_screen_resolution',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'screenWidth/screenHeight', description: 'Pre-exam probe asserts at least 1920×1080.' },
    ],
  },
  reading_tech_display_scale: {
    checkId: 'reading_tech_display_scale',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'displayScalePercent', description: 'Pre-exam probe asserts ≤ 125%.' },
    ],
  },
  reading_tech_at_home_pre_check: {
    checkId: 'reading_tech_at_home_pre_check',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'preExamChecklist', description: 'At-home candidates run an end-to-end tech-readiness probe at least 24h before exam day.' },
    ],
  },
  reading_tech_stable_internet: {
    checkId: 'reading_tech_stable_internet',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'networkStability', description: 'Pre-exam probe runs a short ping/latency check.' },
    ],
  },
  reading_tech_no_vpn_vm: {
    checkId: 'reading_tech_no_vpn_vm',
    surfaces: [
      { kind: 'tech-readiness-probe', field: 'vpnDetected/vmDetected', description: 'Pre-exam probe checks for known VPN/VM signals.' },
    ],
  },
});

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export function getExamModeEnforcers(kind: 'listening-exam-mode' | 'reading-exam-mode'): Readonly<Record<string, ExamModeEnforcerEntry>> {
  return kind === 'listening-exam-mode' ? LISTENING_EXAM_MODE_ENFORCERS : READING_EXAM_MODE_ENFORCERS;
}

/**
 * For every rule in an exam-mode rulebook with severity ∈ {critical, major}:
 *   - If the rule has a `checkId`, verify there is an enforcer entry for it.
 *   - Otherwise the rule must declare `enforcement: 'ai-grounded' | 'human-review-only'`.
 *
 * Returns rules that violate this contract. Used by the CI contract test
 * `lib/rulebook/__tests__/exam-mode-enforcer-coverage.test.ts`.
 */
export function findUnimplementedExamModeRules(): Array<{ kind: string; rule: Rule; reason: string }> {
  const out: Array<{ kind: string; rule: Rule; reason: string }> = [];
  for (const kind of ['listening-exam-mode', 'reading-exam-mode'] as const) {
    const book: Rulebook = loadRulebook(kind, 'medicine');
    const enforcers = getExamModeEnforcers(kind);
    for (const rule of book.rules) {
      if (rule.severity !== 'critical' && rule.severity !== 'major') continue;
      if (rule.enforcement === 'ai-grounded' || rule.enforcement === 'human-review-only') continue;
      if (!rule.checkId) {
        out.push({ kind, rule, reason: `No checkId and no explicit enforcement marker on critical/major rule.` });
        continue;
      }
      if (!enforcers[rule.checkId]) {
        out.push({ kind, rule, reason: `checkId "${rule.checkId}" has no enforcer entry in exam-mode-rules.ts.` });
      }
    }
  }
  return out;
}
