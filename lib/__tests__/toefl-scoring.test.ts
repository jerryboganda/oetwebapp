import { describe, it, expect } from 'vitest';
import {
  clampToeflScore,
  clampToeflSectionScore,
  isValidToeflScore,
  isValidToeflSectionScore,
  toeflOverallScore,
  toeflSectionBand,
  toeflSectionBandLabel,
  toeflReadinessBand,
  toeflReadinessBandLabel,
  formatToeflScoreDisplay,
  formatToeflGradeDisplay,
  TOEFL_SCORE_MIN,
  TOEFL_SCORE_MAX,
  TOEFL_SECTION_MIN,
  TOEFL_SECTION_MAX,
} from '../toefl-scoring';

describe('TOEFL iBT Scoring Engine (lib/toefl-scoring.ts)', () => {
  describe('Bounds and Clamping', () => {
    it('clamps total score to 0..120 range', () => {
      expect(clampToeflScore(-10)).toBe(TOEFL_SCORE_MIN);
      expect(clampToeflScore(0)).toBe(0);
      expect(clampToeflScore(85.4)).toBe(85);
      expect(clampToeflScore(85.6)).toBe(86);
      expect(clampToeflScore(120)).toBe(120);
      expect(clampToeflScore(150)).toBe(TOEFL_SCORE_MAX);
      expect(clampToeflScore(NaN)).toBe(TOEFL_SCORE_MIN);
      expect(clampToeflScore(Infinity)).toBe(TOEFL_SCORE_MIN);
    });

    it('clamps section score to 0..30 range', () => {
      expect(clampToeflSectionScore(-5)).toBe(TOEFL_SECTION_MIN);
      expect(clampToeflSectionScore(0)).toBe(0);
      expect(clampToeflSectionScore(24.2)).toBe(24);
      expect(clampToeflSectionScore(30)).toBe(30);
      expect(clampToeflSectionScore(35)).toBe(TOEFL_SECTION_MAX);
      expect(clampToeflSectionScore(NaN)).toBe(TOEFL_SECTION_MIN);
    });
  });

  describe('Validation', () => {
    it('validates total scores correctly', () => {
      expect(isValidToeflScore(0)).toBe(true);
      expect(isValidToeflScore(120)).toBe(true);
      expect(isValidToeflScore(80)).toBe(true);
      expect(isValidToeflScore('85')).toBe(true);
      expect(isValidToeflScore(-1)).toBe(false);
      expect(isValidToeflScore(121)).toBe(false);
      expect(isValidToeflScore(85.5)).toBe(false);
      expect(isValidToeflScore('abc')).toBe(false);
      expect(isValidToeflScore(null)).toBe(false);
      expect(isValidToeflScore(undefined)).toBe(false);
      expect(isValidToeflScore('')).toBe(false);
    });

    it('validates section scores correctly', () => {
      expect(isValidToeflSectionScore(0)).toBe(true);
      expect(isValidToeflSectionScore(30)).toBe(true);
      expect(isValidToeflSectionScore(25)).toBe(true);
      expect(isValidToeflSectionScore('22')).toBe(true);
      expect(isValidToeflSectionScore(-1)).toBe(false);
      expect(isValidToeflSectionScore(31)).toBe(false);
      expect(isValidToeflSectionScore(20.5)).toBe(false);
      expect(isValidToeflSectionScore(null)).toBe(false);
    });
  });

  describe('Section Band Mapping (CEFR Proficiency Levels)', () => {
    it('evaluates Reading section levels', () => {
      expect(toeflSectionBand('reading', 24)).toBe('advanced');
      expect(toeflSectionBand('reading', 30)).toBe('advanced');
      expect(toeflSectionBand('reading', 18)).toBe('high_intermediate');
      expect(toeflSectionBand('reading', 23)).toBe('high_intermediate');
      expect(toeflSectionBand('reading', 10)).toBe('low_intermediate');
      expect(toeflSectionBand('reading', 17)).toBe('low_intermediate');
      expect(toeflSectionBand('reading', 9)).toBe('below_low_intermediate');
      expect(toeflSectionBand('reading', 0)).toBe('below_low_intermediate');
    });

    it('evaluates Listening section levels', () => {
      expect(toeflSectionBand('listening', 22)).toBe('advanced');
      expect(toeflSectionBand('listening', 17)).toBe('high_intermediate');
      expect(toeflSectionBand('listening', 9)).toBe('low_intermediate');
      expect(toeflSectionBand('listening', 8)).toBe('below_low_intermediate');
    });

    it('evaluates Speaking section levels', () => {
      expect(toeflSectionBand('speaking', 25)).toBe('advanced');
      expect(toeflSectionBand('speaking', 20)).toBe('high_intermediate');
      expect(toeflSectionBand('speaking', 16)).toBe('low_intermediate');
      expect(toeflSectionBand('speaking', 15)).toBe('below_low_intermediate');
    });

    it('evaluates Writing section levels', () => {
      expect(toeflSectionBand('writing', 24)).toBe('advanced');
      expect(toeflSectionBand('writing', 17)).toBe('high_intermediate');
      expect(toeflSectionBand('writing', 13)).toBe('low_intermediate');
      expect(toeflSectionBand('writing', 12)).toBe('below_low_intermediate');
    });

    it('formats section band labels accurately', () => {
      expect(toeflSectionBandLabel('advanced')).toBe('Advanced');
      expect(toeflSectionBandLabel('high_intermediate')).toBe('High-Intermediate');
      expect(toeflSectionBandLabel('low_intermediate')).toBe('Low-Intermediate');
      expect(toeflSectionBandLabel('below_low_intermediate')).toBe('Below Low-Intermediate');
    });
  });

  describe('Readiness Band Mapping & Formatting', () => {
    it('maps total score to shared readiness bands', () => {
      expect(toeflReadinessBand(50)).toBe('not_ready');
      expect(toeflReadinessBand(59)).toBe('not_ready');
      expect(toeflReadinessBand(60)).toBe('developing');
      expect(toeflReadinessBand(69)).toBe('developing');
      expect(toeflReadinessBand(70)).toBe('borderline');
      expect(toeflReadinessBand(79)).toBe('borderline');
      expect(toeflReadinessBand(80)).toBe('exam_ready');
      expect(toeflReadinessBand(94)).toBe('exam_ready');
      expect(toeflReadinessBand(95)).toBe('strong');
      expect(toeflReadinessBand(120)).toBe('strong');
    });

    it('returns human-readable readiness labels', () => {
      expect(toeflReadinessBandLabel('not_ready')).toBe('Not ready');
      expect(toeflReadinessBandLabel('developing')).toBe('Developing');
      expect(toeflReadinessBandLabel('borderline')).toBe('Borderline');
      expect(toeflReadinessBandLabel('exam_ready')).toBe('Exam-ready');
      expect(toeflReadinessBandLabel('strong')).toBe('Strong');
    });

    it('formats display strings accurately', () => {
      expect(formatToeflScoreDisplay(92)).toBe('92/120');
      expect(formatToeflGradeDisplay(92)).toBe('Score 92');
    });
  });

  describe('Overall Score Aggregation', () => {
    it('sums all four section scores with proper clamping', () => {
      expect(toeflOverallScore({ reading: 25, listening: 20, speaking: 22, writing: 23 })).toBe(90);
      expect(toeflOverallScore({ reading: 30, listening: 30, speaking: 30, writing: 30 })).toBe(120);
      expect(toeflOverallScore({ reading: 0, listening: 0, speaking: 0, writing: 0 })).toBe(0);
      expect(toeflOverallScore({ reading: 35, listening: 35, speaking: 35, writing: 35 })).toBe(120);
      expect(toeflOverallScore({ reading: -5, listening: -5, speaking: -5, writing: -5 })).toBe(0);
    });
  });
});
