import { describe, it, expect } from 'vitest';
import {
  SPEAKING_MAPPING_VERSION,
  SPEAKING_RAW_TO_REPORTED,
  SPEAKING_RUBRIC_MAX,
  oetGradeFromScaled,
  oetReportedScoreFromScaled,
  speakingProjectedScaled,
  speakingProjectedScaledFromPercentage,
  speakingProjectedBand,
  speakingRawTotal,
  speakingReadinessBandFromScaled,
  speakingReadinessBandLabel,
  speakingReportedScaled,
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

describe('speakingReportedScaled — the one candidate-facing number', () => {
  it('has one entry per raw total, never decreases, and every entry is a multiple of 10', () => {
    expect(SPEAKING_RAW_TO_REPORTED).toHaveLength(SPEAKING_RUBRIC_MAX + 1);
    expect(SPEAKING_RAW_TO_REPORTED[0]).toBe(0);
    expect(SPEAKING_RAW_TO_REPORTED[SPEAKING_RUBRIC_MAX]).toBe(500);
    SPEAKING_RAW_TO_REPORTED.forEach((value, raw) => {
      expect(value % 10).toBe(0);
      if (raw > 0) expect(value).toBeGreaterThanOrEqual(SPEAKING_RAW_TO_REPORTED[raw - 1]);
    });
  });

  it('v0 is exactly the former heuristic rounded to 10 (no displayed number moves)', () => {
    for (let raw = 0; raw <= SPEAKING_RUBRIC_MAX; raw++) {
      const heuristic = speakingProjectedScaledFromPercentage((raw * 100) / SPEAKING_RUBRIC_MAX);
      expect(SPEAKING_RAW_TO_REPORTED[raw]).toBe(oetReportedScoreFromScaled(heuristic));
    }
    expect(SPEAKING_MAPPING_VERSION).toBe('speaking-map.v0-heuristic');
  });

  it('maps the two production results (308, 231) onto ten-point scores', () => {
    // 16/24 + 8/15 = 24/39 → was shown as 308
    const a: SpeakingCriterionScores = {
      intelligibility: 4, fluency: 4, appropriateness: 4, grammarExpression: 4,
      relationshipBuilding: 2, patientPerspective: 2, structure: 1, informationGathering: 2, informationGiving: 1,
    };
    expect(speakingRawTotal(a)).toBe(24);
    expect(speakingReportedScaled(a)).toBe(310);
    // 13/24 + 5/15 = 18/39 → was shown as 231
    const b: SpeakingCriterionScores = {
      intelligibility: 4, fluency: 3, appropriateness: 3, grammarExpression: 3,
      relationshipBuilding: 1, patientPerspective: 1, structure: 1, informationGathering: 1, informationGiving: 1,
    };
    expect(speakingRawTotal(b)).toBe(18);
    expect(speakingReportedScaled(b)).toBe(230);
  });

  it('clamps out-of-range criterion values instead of trusting them', () => {
    const overrange: SpeakingCriterionScores = {
      intelligibility: 99, fluency: 99, appropriateness: 99, grammarExpression: 99,
      relationshipBuilding: 99, patientPerspective: 99, structure: 99, informationGathering: 99, informationGiving: 99,
    };
    expect(speakingReportedScaled(overrange)).toBe(500);
    expect(speakingReportedScaled(ZERO)).toBe(0);
  });

  it('every reported score maps to one of the six official letters, never B+', () => {
    const letters = new Set(['A', 'B', 'C+', 'C', 'D', 'E']);
    for (const reported of SPEAKING_RAW_TO_REPORTED) {
      expect(letters.has(oetGradeFromScaled(reported))).toBe(true);
    }
  });

  it.each([
    [500, 'A'], [450, 'A'], [440, 'B'], [430, 'B'], [350, 'B'],
    [340, 'C+'], [300, 'C+'], [290, 'C'], [200, 'C'], [190, 'D'], [100, 'D'], [90, 'E'], [0, 'E'],
  ] as const)('reported %i → Grade %s', (reported, grade) => {
    expect(oetGradeFromScaled(reported)).toBe(grade);
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
