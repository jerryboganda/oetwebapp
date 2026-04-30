import { describe, it, expect } from 'vitest';
import {
  SPEAKING_RUBRIC_MAX,
  speakingProjectedScaled,
  speakingProjectedScaledFromPercentage,
  speakingProjectedBand,
  speakingReadinessBandFromScaled,
  speakingReadinessBandLabel,
  type SpeakingCriterionScores,
} from '@/lib/scoring';

const ZERO: SpeakingCriterionScores = {
  intelligibility: 0,
  fluency: 0,
  appropriateness: 0,
  grammarExpression: 0,
  relationshipBuilding: 0,
  patientPerspective: 0,
  structure: 0,
  informationGathering: 0,
  informationGiving: 0,
};

const FULL: SpeakingCriterionScores = {
  intelligibility: 6,
  fluency: 6,
  appropriateness: 6,
  grammarExpression: 6,
  relationshipBuilding: 3,
  patientPerspective: 3,
  structure: 3,
  informationGathering: 3,
  informationGiving: 3,
};

describe('SPEAKING_RUBRIC_MAX', () => {
  it('matches official rubric (24 linguistic + 15 clinical)', () => {
    expect(SPEAKING_RUBRIC_MAX).toBe(39);
  });
});

describe('speakingProjectedScaledFromPercentage', () => {
  it('maps 0% to 0', () => {
    expect(speakingProjectedScaledFromPercentage(0)).toBe(0);
  });

  it('maps 70% exactly to 350 (B pass anchor)', () => {
    expect(speakingProjectedScaledFromPercentage(70)).toBe(350);
  });

  it('maps 50% to 250 (developing anchor)', () => {
    expect(speakingProjectedScaledFromPercentage(50)).toBe(250);
  });

  it('maps 100% to 500', () => {
    expect(speakingProjectedScaledFromPercentage(100)).toBe(500);
  });

  it('clamps negatives to 0', () => {
    expect(speakingProjectedScaledFromPercentage(-10)).toBe(0);
  });

  it('clamps over-100 to 500', () => {
    expect(speakingProjectedScaledFromPercentage(150)).toBe(500);
  });

  it('returns 0 for NaN', () => {
    expect(speakingProjectedScaledFromPercentage(Number.NaN)).toBe(0);
  });

  it('interpolates linearly between 70 and 80 anchors', () => {
    // 75% → midway between 350 and 400 = 375
    expect(speakingProjectedScaledFromPercentage(75)).toBe(375);
  });
});

describe('speakingProjectedScaled', () => {
  it('zero scores → 0 scaled', () => {
    expect(speakingProjectedScaled(ZERO)).toBe(0);
  });

  it('full scores → 500 scaled', () => {
    expect(speakingProjectedScaled(FULL)).toBe(500);
  });

  it('clamps out-of-range criterion values', () => {
    const overrange: SpeakingCriterionScores = { ...FULL, intelligibility: 99, relationshipBuilding: 99 };
    expect(speakingProjectedScaled(overrange)).toBe(500);
  });

  it('respects the 70% anchor — exactly 350 when total is 27.3/39', () => {
    // 6 + 6 + 6 + 3 + 3 + 3 + 0 + 0 + 0 = 27 of 39 ≈ 69.23%
    // 6 + 6 + 6 + 3 + 3 + 3 + 0 + 0 + 1 = 28 of 39 ≈ 71.79%
    // The exact 70% pass anchor is between these — verify both sides.
    const justBelow: SpeakingCriterionScores = {
      intelligibility: 6, fluency: 6, appropriateness: 6, grammarExpression: 0,
      relationshipBuilding: 3, patientPerspective: 3, structure: 3,
      informationGathering: 0, informationGiving: 0,
    };
    // 6+6+6+0 + 3+3+3+0+0 = 27 → 69.23% → < 350
    expect(speakingProjectedScaled(justBelow)).toBeLessThan(350);

    const justAbove: SpeakingCriterionScores = {
      intelligibility: 6, fluency: 6, appropriateness: 6, grammarExpression: 0,
      relationshipBuilding: 3, patientPerspective: 3, structure: 3,
      informationGathering: 1, informationGiving: 0,
    };
    // 6+6+6+0 + 3+3+3+1+0 = 28 → 71.79% → > 350
    expect(speakingProjectedScaled(justAbove)).toBeGreaterThan(350);
  });

  it('matches percentage helper', () => {
    const half: SpeakingCriterionScores = {
      intelligibility: 3, fluency: 3, appropriateness: 3, grammarExpression: 3,
      relationshipBuilding: 1, patientPerspective: 2, structure: 2,
      informationGathering: 2, informationGiving: 1,
    };
    // 12 + 8 = 20 of 39 ≈ 51.28%
    const direct = speakingProjectedScaled(half);
    const viaPct = speakingProjectedScaledFromPercentage((20 * 100) / 39);
    expect(direct).toBe(viaPct);
  });
});

describe('speakingProjectedBand', () => {
  it('full scores → grade A passed', () => {
    const r = speakingProjectedBand(FULL);
    expect(r.passed).toBe(true);
    expect(r.scaledScore).toBe(500);
    expect(r.subtest).toBe('speaking');
  });

  it('zero scores → not passed', () => {
    expect(speakingProjectedBand(ZERO).passed).toBe(false);
  });
});

describe('speakingReadinessBandFromScaled', () => {
  it.each([
    [0, 'not_ready'],
    [200, 'not_ready'],
    [249, 'not_ready'],
    [250, 'developing'],
    [299, 'developing'],
    [300, 'borderline'],
    [349, 'borderline'],
    [350, 'exam_ready'],
    [419, 'exam_ready'],
    [420, 'strong'],
    [500, 'strong'],
  ] as const)('scaled %i → %s', (scaled, expected) => {
    expect(speakingReadinessBandFromScaled(scaled)).toBe(expected);
  });

  it('clamps negatives to not_ready', () => {
    expect(speakingReadinessBandFromScaled(-100)).toBe('not_ready');
  });

  it('clamps over-500 to strong', () => {
    expect(speakingReadinessBandFromScaled(9999)).toBe('strong');
  });
});

describe('speakingReadinessBandLabel', () => {
  it('returns human labels for every band', () => {
    expect(speakingReadinessBandLabel('not_ready')).toBe('Not ready');
    expect(speakingReadinessBandLabel('developing')).toBe('Developing');
    expect(speakingReadinessBandLabel('borderline')).toBe('Borderline');
    expect(speakingReadinessBandLabel('exam_ready')).toBe('Exam-ready');
    expect(speakingReadinessBandLabel('strong')).toBe('Strong');
  });
});
