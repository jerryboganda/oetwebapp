import { describe, it, expect } from 'vitest';
import {
  getExamScoringStrategy,
  OetScoringStrategy,
  IeltsScoringStrategy,
  PteScoringStrategy,
  ToeflScoringStrategy,
  formatScoreDisplay,
  formatGradeDisplay,
  normalizeTargetScore,
  sharedReadinessBand,
  sharedReadinessBandLabel,
  examFamilyLabel,
  examFamilyScoreHint,
} from '../exam-family-scoring';

describe('Exam Family Scoring Strategy Pattern (lib/exam-family-scoring.ts)', () => {
  describe('Strategy Resolution & Polymorphism', () => {
    it('resolves OetScoringStrategy for "oet"', () => {
      const strategy = getExamScoringStrategy('oet');
      expect(strategy).toBeInstanceOf(OetScoringStrategy);
      expect(strategy.examFamily).toBe('oet');
      expect(strategy.label).toBe('OET');
      expect(strategy.minScore).toBe(0);
      expect(strategy.maxScore).toBe(500);
      expect(strategy.defaultTarget).toBe(350);
    });

    it('resolves IeltsScoringStrategy for "ielts"', () => {
      const strategy = getExamScoringStrategy('ielts');
      expect(strategy).toBeInstanceOf(IeltsScoringStrategy);
      expect(strategy.examFamily).toBe('ielts');
      expect(strategy.label).toBe('IELTS');
      expect(strategy.minScore).toBe(0);
      expect(strategy.maxScore).toBe(9);
      expect(strategy.defaultTarget).toBe(7.0);
    });

    it('resolves PteScoringStrategy for "pte"', () => {
      const strategy = getExamScoringStrategy('pte');
      expect(strategy).toBeInstanceOf(PteScoringStrategy);
      expect(strategy.examFamily).toBe('pte');
      expect(strategy.label).toBe('PTE');
      expect(strategy.minScore).toBe(10);
      expect(strategy.maxScore).toBe(90);
      expect(strategy.defaultTarget).toBe(65);
    });

    it('resolves ToeflScoringStrategy for "toefl"', () => {
      const strategy = getExamScoringStrategy('toefl');
      expect(strategy).toBeInstanceOf(ToeflScoringStrategy);
      expect(strategy.examFamily).toBe('toefl');
      expect(strategy.label).toBe('TOEFL');
      expect(strategy.minScore).toBe(0);
      expect(strategy.maxScore).toBe(120);
      expect(strategy.defaultTarget).toBe(80);
    });

    it('defaults to OET strategy when unknown code or null is provided', () => {
      expect(getExamScoringStrategy('unknown')).toBeInstanceOf(OetScoringStrategy);
      expect(getExamScoringStrategy('')).toBeInstanceOf(OetScoringStrategy);
      expect(getExamScoringStrategy(null as unknown as string)).toBeInstanceOf(OetScoringStrategy);
    });
  });

  describe('OET Strategy Execution', () => {
    const oet = new OetScoringStrategy();

    it('formats score display on 0-500 scale', () => {
      expect(oet.formatScore(380)).toBe('380/500');
      expect(oet.formatScore(350)).toBe('350/500');
    });

    it('formats grade display with OET letter grades', () => {
      expect(oet.formatGrade(460)).toBe('Grade A');
      expect(oet.formatGrade(380)).toBe('Grade B');
      expect(oet.formatGrade(320)).toBe('Grade C+');
      expect(oet.formatGrade(250)).toBe('Grade C');
      expect(oet.formatGrade(150)).toBe('Grade D');
      expect(oet.formatGrade(50)).toBe('Grade E');
    });

    it('normalizes target scores', () => {
      expect(oet.normalizeTargetScore('350')).toBe(350);
      expect(oet.normalizeTargetScore(400)).toBe(400);
      expect(oet.normalizeTargetScore(-10)).toBe(null);
      expect(oet.normalizeTargetScore(550)).toBe(null);
      expect(oet.normalizeTargetScore('invalid')).toBe(null);
    });

    it('evaluates pass determinations with country awareness', () => {
      expect(oet.isPass(350, 'GB')).toBe(true);
      expect(oet.isPass(340, 'GB')).toBe(false);
      expect(oet.isPass(300, 'US')).toBe(true);
      expect(oet.isPass(300, 'QA')).toBe(true);
      expect(oet.isPass(300, 'AU')).toBe(false);
    });
  });

  describe('IELTS Strategy Execution', () => {
    const ielts = new IeltsScoringStrategy();

    it('formats score display with 0.5 half-band precision', () => {
      expect(ielts.formatScore(7.0)).toBe('7.0');
      expect(ielts.formatScore(7.25)).toBe('7.5');
      expect(ielts.formatScore(6.75)).toBe('7.0');
    });

    it('formats grade display with Band prefix', () => {
      expect(ielts.formatGrade(7.0)).toBe('Band 7.0');
      expect(ielts.formatGrade(8.5)).toBe('Band 8.5');
    });

    it('normalizes target scores in 0..9 range', () => {
      expect(ielts.normalizeTargetScore('7.5')).toBe(7.5);
      expect(ielts.normalizeTargetScore(8)).toBe(8);
      expect(ielts.normalizeTargetScore(-1)).toBe(null);
      expect(ielts.normalizeTargetScore(10)).toBe(null);
    });

    it('evaluates pass determinations against standard 7.0 benchmark', () => {
      expect(ielts.isPass(7.0)).toBe(true);
      expect(ielts.isPass(7.5)).toBe(true);
      expect(ielts.isPass(6.5)).toBe(false);
    });
  });

  describe('PTE Strategy Execution', () => {
    const pte = new PteScoringStrategy();

    it('formats score display on 10-90 scale', () => {
      expect(pte.formatScore(65)).toBe('65');
      expect(pte.formatScore(5)).toBe('10');
      expect(pte.formatScore(95)).toBe('90');
    });

    it('formats grade display with Score prefix', () => {
      expect(pte.formatGrade(65)).toBe('Score 65');
    });

    it('normalizes target scores in 10..90 range', () => {
      expect(pte.normalizeTargetScore('65')).toBe(65);
      expect(pte.normalizeTargetScore(5)).toBe(null);
      expect(pte.normalizeTargetScore(95)).toBe(null);
    });

    it('evaluates pass determinations against 65 target', () => {
      expect(pte.isPass(65)).toBe(true);
      expect(pte.isPass(79)).toBe(true);
      expect(pte.isPass(64)).toBe(false);
    });
  });

  describe('TOEFL Strategy Execution', () => {
    const toefl = new ToeflScoringStrategy();

    it('formats score display on 0-120 scale', () => {
      expect(toefl.formatScore(92)).toBe('92/120');
      expect(toefl.formatScore(120)).toBe('120/120');
    });

    it('formats grade display with Score prefix', () => {
      expect(toefl.formatGrade(92)).toBe('Score 92');
    });

    it('normalizes target scores in 0..120 range', () => {
      expect(toefl.normalizeTargetScore('80')).toBe(80);
      expect(toefl.normalizeTargetScore(-5)).toBe(null);
      expect(toefl.normalizeTargetScore(130)).toBe(null);
    });

    it('evaluates pass determinations against 80 target', () => {
      expect(toefl.isPass(80)).toBe(true);
      expect(toefl.isPass(100)).toBe(true);
      expect(toefl.isPass(79)).toBe(false);
    });
  });

  describe('Backward Compatibility Helper Functions', () => {
    it('dispatches formatScoreDisplay correctly across all 4 exam families', () => {
      expect(formatScoreDisplay('oet', 380)).toBe('380/500');
      expect(formatScoreDisplay('ielts', 7.0)).toBe('7.0');
      expect(formatScoreDisplay('pte', 65)).toBe('65');
      expect(formatScoreDisplay('toefl', 90)).toBe('90/120');
    });

    it('dispatches formatGradeDisplay correctly across all 4 exam families', () => {
      expect(formatGradeDisplay('oet', 380)).toBe('Grade B');
      expect(formatGradeDisplay('ielts', 7.0)).toBe('Band 7.0');
      expect(formatGradeDisplay('pte', 65)).toBe('Score 65');
      expect(formatGradeDisplay('toefl', 90)).toBe('Score 90');
    });

    it('dispatches normalizeTargetScore correctly', () => {
      expect(normalizeTargetScore('oet', '350')).toBe(350);
      expect(normalizeTargetScore('ielts', '7.0')).toBe(7.0);
      expect(normalizeTargetScore('pte', '65')).toBe(65);
      expect(normalizeTargetScore('toefl', '80')).toBe(80);
    });

    it('dispatches sharedReadinessBand and labels', () => {
      expect(sharedReadinessBand('oet', 380)).toBe('exam_ready');
      expect(sharedReadinessBand('ielts', 7.0)).toBe('exam_ready');
      expect(sharedReadinessBand('pte', 70)).toBe('exam_ready');
      expect(sharedReadinessBand('toefl', 85)).toBe('exam_ready');

      expect(sharedReadinessBandLabel('exam_ready')).toBe('Exam-ready');
    });

    it('provides labels and score hints for all 4 exam families', () => {
      expect(examFamilyLabel('oet')).toBe('OET');
      expect(examFamilyLabel('ielts')).toBe('IELTS');
      expect(examFamilyLabel('pte')).toBe('PTE');
      expect(examFamilyLabel('toefl')).toBe('TOEFL');

      expect(examFamilyScoreHint('toefl').placeholder).toBe('e.g. 80');
    });
  });
});
