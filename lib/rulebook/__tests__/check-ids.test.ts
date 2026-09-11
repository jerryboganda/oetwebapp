import { describe, expect, it } from 'vitest';
import {
  WRITING_CHECK_IDS,
  SPEAKING_CHECK_IDS,
  LISTENING_EXAM_MODE_CHECK_IDS,
  READING_EXAM_MODE_CHECK_IDS,
  supportedCheckIds,
  isSupportedCheckId,
} from '../check-ids';
import { SUPPORTED_SPEAKING_CHECK_IDS } from '../speaking-rules';
import { LISTENING_EXAM_MODE_ENFORCERS, READING_EXAM_MODE_ENFORCERS } from '../exam-mode-rules';

describe('rulebook check-id registry', () => {
  it('pins the frozen writing detector check-ids (R-a reviewed truth)', () => {
    // 59 original + 4 from the 2026-09-06/07 Global Model Answer Formatting
    // addendum (blank_line_after_re_line, dob_age_forbidden_phrase,
    // no_brackets_in_letter, signoff_no_invented_name) + 6 from the Writing
    // Rule Enforcement Addendum Rev5 (2026-09-10: age_not_duplicated_in_intro,
    // emotional_wording, judgmental_labels, linker_avoid_words,
    // no_duplicated_request, number_style_words_vs_digits) + 12 from the
    // owner Rev8 addendum (2026-09-11, WritingRuleEngine.Rev8.cs; registry
    // rows OWN-W-001..038) = 81. Keep this in lockstep with
    // WritingRuleEngine.SupportedCheckIdSet (C#) — see that file's own
    // header comment.
    expect(WRITING_CHECK_IDS.size).toBe(81);
    expect(WRITING_CHECK_IDS.has('letter_body_length')).toBe(true);
    expect(WRITING_CHECK_IDS.has('no_contractions')).toBe(true);
    expect(WRITING_CHECK_IDS.has('urgent_intro_contains_urgent')).toBe(true);
    expect(WRITING_CHECK_IDS.has('blank_line_after_re_line')).toBe(true);
    expect(WRITING_CHECK_IDS.has('no_duplicated_request')).toBe(true);
  });

  it('includes every owner Rev8 (2026-09-11) detector check-id', () => {
    const rev8 = [
      'closure_contact_offer',
      'closure_contains_management',
      'dob_colon_format',
      'intro_opens_i_am_writing_to',
      'linker_comma_and_case',
      'medication_list_punctuation',
      'model_answer_layout',
      'paragraph_start_patient_name',
      'register_colloquial',
      'relationship_label_patient_reference',
      'signoff_designation_present',
      'value_unit_spacing',
    ];
    expect(rev8.filter((id) => !WRITING_CHECK_IDS.has(id))).toEqual([]);
  });

  it('exposes the speaking detector check-ids', () => {
    expect([...SPEAKING_CHECK_IDS].sort()).toEqual([...SUPPORTED_SPEAKING_CHECK_IDS].sort());
    expect(SPEAKING_CHECK_IDS.has('speaking_jargon_detector')).toBe(true);
  });

  it('derives exam-mode check-ids from the enforcer registries', () => {
    expect([...LISTENING_EXAM_MODE_CHECK_IDS].sort()).toEqual(Object.keys(LISTENING_EXAM_MODE_ENFORCERS).sort());
    expect([...READING_EXAM_MODE_CHECK_IDS].sort()).toEqual(Object.keys(READING_EXAM_MODE_ENFORCERS).sort());
  });

  it('answers isSupportedCheckId for a known writing detector and rejects unknowns', () => {
    const known = [...WRITING_CHECK_IDS][0];
    expect(isSupportedCheckId('writing', known)).toBe(true);
    expect(isSupportedCheckId('writing', '__definitely_not_a_check__')).toBe(false);
  });

  it('returns an empty set for kinds that have no deterministic detectors yet', () => {
    expect(supportedCheckIds('grammar').size).toBe(0);
    expect(supportedCheckIds('vocabulary').size).toBe(0);
  });

  it('registers the listening and reading authoring detector sets', () => {
    expect(supportedCheckIds('listening').size).toBeGreaterThan(0);
    expect(supportedCheckIds('reading').size).toBeGreaterThan(0);
  });
});
