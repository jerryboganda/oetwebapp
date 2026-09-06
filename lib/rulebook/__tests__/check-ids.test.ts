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
    expect(WRITING_CHECK_IDS.size).toBe(59);
    expect(WRITING_CHECK_IDS.has('letter_body_length')).toBe(true);
    expect(WRITING_CHECK_IDS.has('no_contractions')).toBe(true);
    expect(WRITING_CHECK_IDS.has('urgent_intro_contains_urgent')).toBe(true);
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
