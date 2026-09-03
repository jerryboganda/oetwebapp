import { describe, it, expect } from 'vitest';

import {
  oetRawToScaled,
  oetGradeFromScaled,
  oetGradeLabel,
  isListeningReadingPassByRaw,
  isListeningReadingPassByScaled,
  gradeListeningReading,
  gradeWriting,
  deriveWritingResultFromCriteria,
  gradeSpeaking,
  speakingProjectedScaled,
  speakingProjectedBand,
  speakingReadinessBandFromScaled,
  normalizeWritingCountry,
  getWritingPassThreshold,
  writingRawTotalFromCriterionScores,
  writingRawToScaled,
  formatScaledScore,
  formatRawLrScore,
  formatListeningReadingDisplay,
  OET_LR_RAW_MAX,
  OET_LR_RAW_PASS,
  OET_SCALED_PASS_B,
  OET_SCALED_PASS_C_PLUS,
  OET_SCALED_MAX,
  OET_SCALED_MIN,
  WRITING_RAW_MAX,
  WRITING_CRITERION_CODES,
  WRITING_CRITERION_MAX_SCORES,
  WRITING_GRADE_B_COUNTRIES,
  WRITING_GRADE_C_PLUS_COUNTRIES,
  SUPPORTED_WRITING_COUNTRIES,
  type WritingCriterionCode,
  type SpeakingCriterionScores,
  type SupportedWritingCountry,
} from '@/lib/scoring';

import {
  getExamScoringStrategy,
  registerExamScoringStrategy,
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
  type IExamScoringStrategy,
  type SharedReadinessBand,
} from '@/lib/exam-family-scoring';

import {
  ieltsRoundBand,
  ieltsBandDisplay,
  ieltsListeningBandFromRaw,
  ieltsWritingBand,
  ieltsSpeakingBand,
  ieltsOverallBand,
  isValidIeltsBand,
  sanitizeIeltsBand,
  IELTS_BAND_MIN,
  IELTS_BAND_MAX,
  IELTS_DEFAULT_TARGET_BAND,
} from '@/lib/ielts-scoring';

import {
  clampPteScore,
  isValidPteScore,
  pteReadinessBand,
  pteReadinessBandLabel,
  PTE_SCORE_MIN,
  PTE_SCORE_MAX,
  PTE_DEFAULT_TARGET_SCORE,
} from '@/lib/pte-scoring';

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
  TOEFL_DEFAULT_TARGET_SCORE,
  TOEFL_SECTION_THRESHOLDS,
  type ToeflSubtest,
} from '@/lib/toefl-scoring';

import {
  isMockReportStatementOfResultsReady,
  mockReportToStatementOfResults,
} from '@/lib/adapters/oet-sor-adapter';
import type { MockReport } from '@/lib/mock-data';

// ============================================================================
// MILESTONE M4: TIER 5 ADVERSARIAL COVERAGE HARDENING SUITE
// EMPIRICAL CHALLENGER 1 (Critic & Specialist)
// ============================================================================

describe('TIER 5 AUDIT 1: OBJECTIVE SCORING & 30/42 ANCHOR ADVERSARIAL STRESS', () => {
  describe('Exhaustive 43-Point Piecewise Interpolation Mapping ($r \\in [0..42]$)', () => {
    it('verifies all 43 raw score points adhere to official mathematical formula', () => {
      for (let r = 0; r <= OET_LR_RAW_MAX; r++) {
        const scaled = oetRawToScaled(r);

        if (r === 0) {
          expect(scaled).toBe(0);
        } else if (r === OET_LR_RAW_PASS) {
          expect(scaled).toBe(OET_SCALED_PASS_B); // Exactly 350
        } else if (r === OET_LR_RAW_MAX) {
          expect(scaled).toBe(OET_SCALED_MAX); // Exactly 500
        } else if (r < OET_LR_RAW_PASS) {
          const expected = Math.round((r * 350) / 30);
          expect(scaled).toBe(expected);
          expect(scaled).toBeLessThan(350);
        } else {
          const expected = Math.round(350 + ((r - 30) * 150) / 12);
          expect(scaled).toBe(expected);
          expect(scaled).toBeGreaterThan(350);
          expect(scaled).toBeLessThanOrEqual(500);
        }
      }
    });

    it('rigorously tests the critical 29 vs 30 pass/fail boundary', () => {
      const raw29 = gradeListeningReading('reading', 29);
      expect(raw29.rawCorrect).toBe(29);
      expect(raw29.scaledScore).toBe(338);
      expect(raw29.grade).toBe('C+');
      expect(raw29.passed).toBe(false);
      expect(isListeningReadingPassByRaw(29)).toBe(false);
      expect(isListeningReadingPassByScaled(338)).toBe(false);

      const raw30 = gradeListeningReading('reading', 30);
      expect(raw30.rawCorrect).toBe(30);
      expect(raw30.scaledScore).toBe(350);
      expect(raw30.grade).toBe('B');
      expect(raw30.passed).toBe(true);
      expect(isListeningReadingPassByRaw(30)).toBe(true);
      expect(isListeningReadingPassByScaled(350)).toBe(true);

      const raw31 = gradeListeningReading('reading', 31);
      expect(raw31.rawCorrect).toBe(31);
      expect(raw31.scaledScore).toBe(363);
      expect(raw31.grade).toBe('B');
      expect(raw31.passed).toBe(true);
    });

    it('proves strict monotonicity across the full range [0..42]', () => {
      let prevScaled = -1;
      for (let r = 0; r <= OET_LR_RAW_MAX; r++) {
        const scaled = oetRawToScaled(r);
        expect(scaled).toBeGreaterThan(prevScaled);
        prevScaled = scaled;
      }
    });

    it('proves grade band consistency across scaled scores', () => {
      expect(oetGradeFromScaled(500)).toBe('A');
      expect(oetGradeFromScaled(450)).toBe('A');
      expect(oetGradeFromScaled(449)).toBe('B');
      expect(oetGradeFromScaled(350)).toBe('B');
      expect(oetGradeFromScaled(349)).toBe('C+');
      expect(oetGradeFromScaled(300)).toBe('C+');
      expect(oetGradeFromScaled(299)).toBe('C');
      expect(oetGradeFromScaled(200)).toBe('C');
      expect(oetGradeFromScaled(199)).toBe('D');
      expect(oetGradeFromScaled(100)).toBe('D');
      expect(oetGradeFromScaled(99)).toBe('E');
      expect(oetGradeFromScaled(0)).toBe('E');
    });
  });

  describe('Adversarial Boundary & Fault Injection', () => {
    it('clamps negative inputs and extreme overflows safely', () => {
      expect(oetRawToScaled(-1)).toBe(0);
      expect(oetRawToScaled(-999)).toBe(0);
      expect(oetRawToScaled(43)).toBe(500);
      expect(oetRawToScaled(10000)).toBe(500);

      expect(isListeningReadingPassByRaw(-5)).toBe(false);
      expect(isListeningReadingPassByRaw(100)).toBe(true);
    });

    it('throws RangeError for non-finite inputs to prevent corrupt grading', () => {
      expect(() => oetRawToScaled(NaN)).toThrow(RangeError);
      expect(() => oetRawToScaled(Infinity)).toThrow(RangeError);
      expect(() => oetRawToScaled(-Infinity)).toThrow(RangeError);
    });

    it('handles fractional raw scores with standard mathematical rounding', () => {
      expect(oetRawToScaled(29.4)).toBe(338); // rounds to 29
      expect(oetRawToScaled(29.5)).toBe(350); // rounds to 30
      expect(oetRawToScaled(29.9)).toBe(350); // rounds to 30
      expect(oetRawToScaled(30.4)).toBe(350); // rounds to 30
      expect(oetRawToScaled(30.6)).toBe(363); // rounds to 31
    });

    it('formats score display strings with strict boundary verification', () => {
      expect(formatScaledScore(350)).toBe('350/500');
      expect(formatScaledScore(0)).toBe('0/500');
      expect(formatScaledScore(500)).toBe('500/500');
      expect(formatScaledScore(600)).toBe('500/500');
      expect(formatScaledScore(-50)).toBe('0/500');

      expect(formatRawLrScore(30)).toBe('30/42');
      expect(formatRawLrScore(42)).toBe('42/42');
      expect(formatRawLrScore(50)).toBe('42/42');
      expect(formatRawLrScore(-10)).toBe('0/42');

      expect(formatListeningReadingDisplay(30)).toBe('30/42 • 350/500 • Grade B');
      expect(formatListeningReadingDisplay(29)).toBe('29/42 • 338/500 • Grade C+');
    });
  });
});

describe('TIER 5 AUDIT 2: MULTI-EXAM STRATEGY POLYMORPHISM & DYNAMIC SWITCHING', () => {
  describe('Strategy Contract Adherence & Default Resolvers', () => {
    it('resolves concrete strategy for all supported exam family codes', () => {
      const oetStrat = getExamScoringStrategy('oet');
      expect(oetStrat.examFamily).toBe('oet');
      expect(oetStrat.minScore).toBe(0);
      expect(oetStrat.maxScore).toBe(500);
      expect(oetStrat.defaultTarget).toBe(350);

      const ieltsStrat = getExamScoringStrategy('ielts');
      expect(ieltsStrat.examFamily).toBe('ielts');
      expect(ieltsStrat.minScore).toBe(0);
      expect(ieltsStrat.maxScore).toBe(9);
      expect(ieltsStrat.defaultTarget).toBe(7.0);

      const pteStrat = getExamScoringStrategy('pte');
      expect(pteStrat.examFamily).toBe('pte');
      expect(pteStrat.minScore).toBe(10);
      expect(pteStrat.maxScore).toBe(90);
      expect(pteStrat.defaultTarget).toBe(65);

      const toeflStrat = getExamScoringStrategy('toefl');
      expect(toeflStrat.examFamily).toBe('toefl');
      expect(toeflStrat.minScore).toBe(0);
      expect(toeflStrat.maxScore).toBe(120);
      expect(toeflStrat.defaultTarget).toBe(80);
    });

    it('falls back safely to OET strategy when exam family is null, undefined, or unknown', () => {
      expect(getExamScoringStrategy(null).examFamily).toBe('oet');
      expect(getExamScoringStrategy(undefined).examFamily).toBe('oet');
      expect(getExamScoringStrategy('').examFamily).toBe('oet');
      expect(getExamScoringStrategy('unknown_exam_xyz').examFamily).toBe('oet');
      expect(getExamScoringStrategy('  OET  ').examFamily).toBe('oet');
    });

    it('supports dynamic registration of custom / extension exam scoring strategies', () => {
      class CambridgeC1Strategy implements IExamScoringStrategy {
        readonly examFamily = 'c1_advanced' as any;
        readonly label = 'Cambridge C1 Advanced';
        readonly scoreHint = { hint: 'Cambridge scale 160-210.', placeholder: 'e.g. 180' };
        readonly minScore = 160;
        readonly maxScore = 210;
        readonly defaultTarget = 180;

        formatScore(score: number): string {
          return `${Math.round(score)}/210`;
        }
        formatGrade(score: number): string {
          return score >= 180 ? 'Grade C1' : 'Below C1';
        }
        normalizeTargetScore(value: string | number | null | undefined): number | null {
          const n = Number(value);
          return Number.isFinite(n) && n >= 160 && n <= 210 ? Math.round(n) : null;
        }
        getReadinessBand(score: number): SharedReadinessBand {
          return score >= 180 ? 'exam_ready' : 'developing';
        }
        isPass(score: number): boolean {
          return score >= 180;
        }
      }

      registerExamScoringStrategy(new CambridgeC1Strategy());

      const resolved = getExamScoringStrategy('c1_advanced');
      expect(resolved.label).toBe('Cambridge C1 Advanced');
      expect(resolved.isPass(185)).toBe(true);
      expect(resolved.isPass(175)).toBe(false);
      expect(resolved.formatScore(190)).toBe('190/210');
    });

    it('executes rapid interleaved multi-exam polymorphic score formatting without cross-talk', () => {
      const exams = ['oet', 'ielts', 'pte', 'toefl'] as const;
      const testCases = [
        { exam: 'oet', score: 380, expectedScore: '380/500', expectedGrade: 'Grade B' },
        { exam: 'ielts', score: 7.5, expectedScore: '7.5', expectedGrade: 'Band 7.5' },
        { exam: 'pte', score: 68, expectedScore: '68', expectedGrade: 'Score 68' },
        { exam: 'toefl', score: 92, expectedScore: '92/120', expectedGrade: 'Score 92' },
      ];

      for (let i = 0; i < 50; i++) {
        for (const tc of testCases) {
          expect(formatScoreDisplay(tc.exam as any, tc.score)).toBe(tc.expectedScore);
          expect(formatGradeDisplay(tc.exam as any, tc.score)).toBe(tc.expectedGrade);
        }
      }
    });
  });

  describe('Shared Readiness Band & Target Score Normalization Across Exam Families', () => {
    it('normalizes target scores correctly across all 4 exam types', () => {
      // OET (0..500)
      expect(normalizeTargetScore('oet', '350')).toBe(350);
      expect(normalizeTargetScore('oet', ' 400 ')).toBe(400);
      expect(normalizeTargetScore('oet', '550')).toBeNull();
      expect(normalizeTargetScore('oet', '-10')).toBeNull();
      expect(normalizeTargetScore('oet', null)).toBeNull();

      // IELTS (0..9 in 0.5 increments)
      expect(normalizeTargetScore('ielts', '7.0')).toBe(7.0);
      expect(normalizeTargetScore('ielts', '7.5')).toBe(7.5);
      expect(normalizeTargetScore('ielts', '7.25')).toBe(7.5);
      expect(normalizeTargetScore('ielts', '10.0')).toBeNull();

      // PTE (10..90 integer)
      expect(normalizeTargetScore('pte', '65')).toBe(65);
      expect(normalizeTargetScore('pte', '65.4')).toBe(65);
      expect(normalizeTargetScore('pte', '95')).toBeNull();
      expect(normalizeTargetScore('pte', '5')).toBeNull();

      // TOEFL (0..120 integer)
      expect(normalizeTargetScore('toefl', '80')).toBe(80);
      expect(normalizeTargetScore('toefl', '80.2')).toBe(80);
      expect(normalizeTargetScore('toefl', '130')).toBeNull();
      expect(normalizeTargetScore('toefl', '-5')).toBeNull();
    });

    it('maps readiness bands consistently across all exam types', () => {
      expect(sharedReadinessBand('oet', 450)).toBe('strong');
      expect(sharedReadinessBand('oet', 360)).toBe('exam_ready');
      expect(sharedReadinessBand('oet', 320)).toBe('borderline');
      expect(sharedReadinessBand('oet', 270)).toBe('developing');
      expect(sharedReadinessBand('oet', 200)).toBe('not_ready');

      expect(sharedReadinessBand('ielts', 8.0)).toBe('strong');
      expect(sharedReadinessBand('ielts', 7.0)).toBe('exam_ready');
      expect(sharedReadinessBand('ielts', 6.0)).toBe('borderline');
      expect(sharedReadinessBand('ielts', 5.0)).toBe('developing');
      expect(sharedReadinessBand('ielts', 4.0)).toBe('not_ready');

      expect(sharedReadinessBand('pte', 80)).toBe('strong');
      expect(sharedReadinessBand('pte', 68)).toBe('exam_ready');
      expect(sharedReadinessBand('pte', 60)).toBe('borderline');
      expect(sharedReadinessBand('pte', 52)).toBe('developing');
      expect(sharedReadinessBand('pte', 40)).toBe('not_ready');

      expect(sharedReadinessBand('toefl', 100)).toBe('strong');
      expect(sharedReadinessBand('toefl', 85)).toBe('exam_ready');
      expect(sharedReadinessBand('toefl', 75)).toBe('borderline');
      expect(sharedReadinessBand('toefl', 65)).toBe('developing');
      expect(sharedReadinessBand('toefl', 50)).toBe('not_ready');
    });
  });
});

describe('TIER 5 AUDIT 3: SUBJECTIVE RUBRICS & DESTINATION COUNTRY POLICIES', () => {
  describe('Writing Purpose 0..3 Clamping & Criterion Score Integrity', () => {
    it('strictly clamps Purpose criterion to 3 even under inflated grader input (0..7 mistreatment)', () => {
      const inflatedCriteria = {
        purpose: 7, // Grader mistakenly entered 7 instead of 3
        content: 7,
        conciseness_clarity: 7,
        genre_style: 7,
        organisation_layout: 7,
        language: 7,
      };

      const rawTotal = writingRawTotalFromCriterionScores(inflatedCriteria);
      expect(rawTotal).toBe(38); // 3 + 5*7 = 38 (NOT 42)
      expect(writingRawToScaled(rawTotal)).toBe(500);

      const derived = deriveWritingResultFromCriteria(inflatedCriteria, 'GB');
      expect(derived.rawTotal).toBe(38);
      expect(derived.scaled).toBe(500);
      expect(derived.grade).toBe('A');
    });

    it('clamps negative or NaN criterion scores to 0', () => {
      const dirtyCriteria = {
        purpose: -3,
        content: NaN,
        conciseness_clarity: 5,
        genre_style: 6,
        organisation_layout: 4,
        language: 5,
      };
      const rawTotal = writingRawTotalFromCriterionScores(dirtyCriteria);
      expect(rawTotal).toBe(0 + 0 + 5 + 6 + 4 + 5); // 20
      expect(writingRawToScaled(rawTotal)).toBe(Math.round((20 * 500) / 38)); // 263
    });

    it('verifies all canonical writing criterion codes and their exact max values', () => {
      expect(WRITING_CRITERION_CODES).toHaveLength(6);
      expect(WRITING_CRITERION_MAX_SCORES.purpose).toBe(3);
      expect(WRITING_CRITERION_MAX_SCORES.content).toBe(7);
      expect(WRITING_CRITERION_MAX_SCORES.conciseness_clarity).toBe(7);
      expect(WRITING_CRITERION_MAX_SCORES.genre_style).toBe(7);
      expect(WRITING_CRITERION_MAX_SCORES.organisation_layout).toBe(7);
      expect(WRITING_CRITERION_MAX_SCORES.language).toBe(7);
      expect(WRITING_RAW_MAX).toBe(38);
    });
  });

  describe('Destination Country Writing Resolution Matrix', () => {
    it('correctly classifies Grade B countries (GB, IE, AU, NZ, CA) with 350 threshold', () => {
      for (const country of WRITING_GRADE_B_COUNTRIES) {
        const thresholdInfo = getWritingPassThreshold(country);
        expect(thresholdInfo).not.toBeNull();
        expect(thresholdInfo?.threshold).toBe(350);
        expect(thresholdInfo?.grade).toBe('B');

        const passRes = gradeWriting(350, country);
        expect(passRes.passed).toBe(true);

        const failRes = gradeWriting(349, country);
        expect(failRes.passed).toBe(false);

        const cPlusRes = gradeWriting(300, country);
        expect(cPlusRes.passed).toBe(false);
      }
    });

    it('correctly classifies Grade C+ countries (US, QA) with 300 threshold', () => {
      for (const country of WRITING_GRADE_C_PLUS_COUNTRIES) {
        const thresholdInfo = getWritingPassThreshold(country);
        expect(thresholdInfo).not.toBeNull();
        expect(thresholdInfo?.threshold).toBe(300);
        expect(thresholdInfo?.grade).toBe('C+');

        const passRes = gradeWriting(300, country);
        expect(passRes.passed).toBe(true);

        const failRes = gradeWriting(299, country);
        expect(failRes.passed).toBe(false);
      }
    });

    it('normalizes common country aliases correctly', () => {
      expect(normalizeWritingCountry('UK')).toBe('GB');
      expect(normalizeWritingCountry('United Kingdom')).toBe('GB');
      expect(normalizeWritingCountry('Scotland')).toBe('GB');
      expect(normalizeWritingCountry('Northern Ireland')).toBe('GB');
      expect(normalizeWritingCountry('Republic of Ireland')).toBe('IE');
      expect(normalizeWritingCountry('Australia')).toBe('AU');
      expect(normalizeWritingCountry('New Zealand')).toBe('NZ');
      expect(normalizeWritingCountry('Canada')).toBe('CA');
      expect(normalizeWritingCountry('USA')).toBe('US');
      expect(normalizeWritingCountry('United States of America')).toBe('US');
      expect(normalizeWritingCountry('Qatar')).toBe('QA');
      expect(normalizeWritingCountry('Gulf Countries')).toBe('GB'); // Conservative alias
    });

    it('returns explicit discriminated CountryRequiredResult for missing or invalid countries', () => {
      const nullRes = gradeWriting(350, null);
      expect(nullRes.passed).toBeNull();
      if (nullRes.passed === null) {
        expect(nullRes.reason).toBe('country_required');
      }

      const emptyRes = gradeWriting(350, '');
      expect(emptyRes.passed).toBeNull();
      if (emptyRes.passed === null) {
        expect(emptyRes.reason).toBe('country_required');
      }

      const invalidRes = gradeWriting(350, 'FRANCE');
      expect(invalidRes.passed).toBeNull();
      if (invalidRes.passed === null) {
        expect(invalidRes.reason).toBe('country_unsupported');
        expect(invalidRes.providedCountry).toBe('FRANCE');
      }
    });
  });

  describe('Speaking 2-Card Rubric Clamping & Universal 350 Grade B Pass', () => {
    it('calculates full speaking rubric composite and 70% ≡ 350 anchor correctly', () => {
      // 4 Linguistic (0..6 max 24) + 5 Clinical (0..3 max 15) = 39
      const perfectScores: SpeakingCriterionScores = {
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
      expect(speakingProjectedScaled(perfectScores)).toBe(500);

      // Exact 70% score: (27.3 / 39) * 100 = 70%
      // 4 * 4.2 + 5 * 2.1 = 16.8 + 10.5 = 27.3
      const passScores: SpeakingCriterionScores = {
        intelligibility: 4.2,
        fluency: 4.2,
        appropriateness: 4.2,
        grammarExpression: 4.2,
        relationshipBuilding: 2.1,
        patientPerspective: 2.1,
        structure: 2.1,
        informationGathering: 2.1,
        informationGiving: 2.1,
      };
      const projectedPass = speakingProjectedScaled(passScores);
      expect(projectedPass).toBe(350);

      const passBand = speakingProjectedBand(passScores);
      expect(passBand.passed).toBe(true);
      expect(passBand.grade).toBe('B');
      expect(passBand.requiredScaled).toBe(350);
    });

    it('clamps speaking rubric criterion overflows and underflows', () => {
      const overflowScores: SpeakingCriterionScores = {
        intelligibility: 10, // max 6
        fluency: 99,        // max 6
        appropriateness: -5, // min 0
        grammarExpression: 6,
        relationshipBuilding: 5, // max 3
        patientPerspective: -1, // min 0
        structure: 3,
        informationGathering: 3,
        informationGiving: 3,
      };
      // Clamped: 6 + 6 + 0 + 6 + 3 + 0 + 3 + 3 + 3 = 30 / 39 = 76.92%
      const scaled = speakingProjectedScaled(overflowScores);
      expect(scaled).toBeGreaterThanOrEqual(350);
      expect(scaled).toBeLessThanOrEqual(500);
    });

    it('verifies Speaking readiness bands across scaled spectrum', () => {
      expect(speakingReadinessBandFromScaled(450)).toBe('strong');
      expect(speakingReadinessBandFromScaled(380)).toBe('exam_ready');
      expect(speakingReadinessBandFromScaled(320)).toBe('borderline');
      expect(speakingReadinessBandFromScaled(280)).toBe('developing');
      expect(speakingReadinessBandFromScaled(200)).toBe('not_ready');
    });
  });
});

describe('TIER 5 AUDIT 4: ENTITLEMENT WATERFALL, SHARED CREDITS & ROLLBACK INTEGRITY', () => {
  interface CandidateCreditPool {
    writingOnlyCredits: number;
    speakingOnlyCredits: number;
    flexibleCredits: number;
    sharedCredits: number;
  }

  function drainCreditsForSubtest(
    pool: CandidateCreditPool,
    subtest: 'reading' | 'listening' | 'writing' | 'speaking',
  ): { success: boolean; drainedFrom: string; error?: string } {
    if (subtest === 'writing') {
      if (pool.writingOnlyCredits >= 1) {
        pool.writingOnlyCredits -= 1;
        return { success: true, drainedFrom: 'writing_only' };
      }
      if (pool.flexibleCredits >= 1) {
        pool.flexibleCredits -= 1;
        return { success: true, drainedFrom: 'flexible_ws' };
      }
      if (pool.sharedCredits >= 2) {
        pool.sharedCredits -= 2;
        return { success: true, drainedFrom: 'shared' };
      }
      return { success: false, drainedFrom: 'none', error: 'INSUFFICIENT_CREDITS_WRITING' };
    }

    if (subtest === 'speaking') {
      if (pool.speakingOnlyCredits >= 1) {
        pool.speakingOnlyCredits -= 1;
        return { success: true, drainedFrom: 'speaking_only' };
      }
      if (pool.flexibleCredits >= 1) {
        pool.flexibleCredits -= 1;
        return { success: true, drainedFrom: 'flexible_ws' };
      }
      if (pool.sharedCredits >= 2) {
        pool.sharedCredits -= 2;
        return { success: true, drainedFrom: 'shared' };
      }
      return { success: false, drainedFrom: 'none', error: 'INSUFFICIENT_CREDITS_SPEAKING' };
    }

    // Reading or Listening: requires 1 shared credit
    if (pool.sharedCredits >= 1) {
      pool.sharedCredits -= 1;
      return { success: true, drainedFrom: 'shared' };
    }
    return { success: false, drainedFrom: 'none', error: `INSUFFICIENT_CREDITS_${subtest.toUpperCase()}` };
  }

  it('verifies strict 3-tier waterfall drainage order (Specific -> Flexible -> Shared)', () => {
    const pool: CandidateCreditPool = {
      writingOnlyCredits: 1,
      speakingOnlyCredits: 1,
      flexibleCredits: 2,
      sharedCredits: 4,
    };

    // Step 1: Writing uses writing-only credit
    const w1 = drainCreditsForSubtest(pool, 'writing');
    expect(w1.success).toBe(true);
    expect(w1.drainedFrom).toBe('writing_only');
    expect(pool.writingOnlyCredits).toBe(0);
    expect(pool.flexibleCredits).toBe(2);
    expect(pool.sharedCredits).toBe(4);

    // Step 2: Writing uses flexible credit
    const w2 = drainCreditsForSubtest(pool, 'writing');
    expect(w2.success).toBe(true);
    expect(w2.drainedFrom).toBe('flexible_ws');
    expect(pool.flexibleCredits).toBe(1);

    // Step 3: Speaking uses speaking-only credit
    const s1 = drainCreditsForSubtest(pool, 'speaking');
    expect(s1.success).toBe(true);
    expect(s1.drainedFrom).toBe('speaking_only');
    expect(pool.speakingOnlyCredits).toBe(0);

    // Step 4: Speaking uses remaining flexible credit
    const s2 = drainCreditsForSubtest(pool, 'speaking');
    expect(s2.success).toBe(true);
    expect(s2.drainedFrom).toBe('flexible_ws');
    expect(pool.flexibleCredits).toBe(0);

    // Step 5: Writing uses 2 shared credits
    const w3 = drainCreditsForSubtest(pool, 'writing');
    expect(w3.success).toBe(true);
    expect(w3.drainedFrom).toBe('shared');
    expect(pool.sharedCredits).toBe(2);

    // Step 6: Reading uses 1 shared credit
    const r1 = drainCreditsForSubtest(pool, 'reading');
    expect(r1.success).toBe(true);
    expect(r1.drainedFrom).toBe('shared');
    expect(pool.sharedCredits).toBe(1);

    // Step 7: Writing needs 2 shared credits but only 1 remains -> fails safely
    const w4 = drainCreditsForSubtest(pool, 'writing');
    expect(w4.success).toBe(false);
    expect(w4.error).toBe('INSUFFICIENT_CREDITS_WRITING');
    expect(pool.sharedCredits).toBe(1); // Not leaked or decremented

    // Step 8: Listening uses the last 1 shared credit
    const l1 = drainCreditsForSubtest(pool, 'listening');
    expect(l1.success).toBe(true);
    expect(pool.sharedCredits).toBe(0);

    // Step 9: All subsequent drains fail
    expect(drainCreditsForSubtest(pool, 'reading').success).toBe(false);
    expect(drainCreditsForSubtest(pool, 'listening').success).toBe(false);
    expect(drainCreditsForSubtest(pool, 'writing').success).toBe(false);
    expect(drainCreditsForSubtest(pool, 'speaking').success).toBe(false);
  });

  it('guarantees 2-phase reservation rollback restoration with zero credit leakage', () => {
    class CreditReservationManager {
      private state: CandidateCreditPool;
      private reservations = new Map<string, { bucket: string; amount: number }>();

      constructor(initial: CandidateCreditPool) {
        this.state = { ...initial };
      }

      reserve(id: string, subtest: 'writing' | 'reading'): boolean {
        if (subtest === 'writing') {
          if (this.state.writingOnlyCredits >= 1) {
            this.state.writingOnlyCredits -= 1;
            this.reservations.set(id, { bucket: 'writing_only', amount: 1 });
            return true;
          }
          if (this.state.sharedCredits >= 2) {
            this.state.sharedCredits -= 2;
            this.reservations.set(id, { bucket: 'shared', amount: 2 });
            return true;
          }
        }
        return false;
      }

      commit(id: string): void {
        this.reservations.delete(id);
      }

      rollback(id: string): boolean {
        const res = this.reservations.get(id);
        if (!res) return false;
        if (res.bucket === 'writing_only') {
          this.state.writingOnlyCredits += res.amount;
        } else if (res.bucket === 'shared') {
          this.state.sharedCredits += res.amount;
        }
        this.reservations.delete(id);
        return true;
      }

      getState(): CandidateCreditPool {
        return { ...this.state };
      }
    }

    const mgr = new CreditReservationManager({
      writingOnlyCredits: 1,
      speakingOnlyCredits: 0,
      flexibleCredits: 0,
      sharedCredits: 4,
    });

    // Reserve for attempt 1 (writing-only)
    expect(mgr.reserve('res-1', 'writing')).toBe(true);
    expect(mgr.getState().writingOnlyCredits).toBe(0);

    // Reserve for attempt 2 (shared 2 credits)
    expect(mgr.reserve('res-2', 'writing')).toBe(true);
    expect(mgr.getState().sharedCredits).toBe(2);

    // Attempt 2 fails mid-stream -> rollback
    expect(mgr.rollback('res-2')).toBe(true);
    expect(mgr.getState().sharedCredits).toBe(4); // Restored exactly

    // Rollback idempotency check
    expect(mgr.rollback('res-2')).toBe(false); // Already rolled back
    expect(mgr.getState().sharedCredits).toBe(4); // Not double-credited

    // Commit attempt 1
    mgr.commit('res-1');
    expect(mgr.getState().writingOnlyCredits).toBe(0);
  });
});

describe('TIER 5 AUDIT 5: STATEMENT OF RESULTS ADAPTER ROBUSTNESS', () => {
  const baseMockReport: MockReport = {
    id: 'mock-report-full-001',
    date: '2026-08-30T10:00:00Z',
    overallScore: 'B',
    passed: true,
    subTests: [
      {
        id: 'reading',
        name: 'Reading',
        score: 380,
        scaledScore: 380,
        rawScore: '33/42',
        grade: 'B',
        passed: true,
        reviewState: 'completed',
        scoreConversionTableVersionKey: 'v2026_canonical',
        scoreConversionPassed: true,
      },
      {
        id: 'listening',
        name: 'Listening',
        score: 360,
        scaledScore: 360,
        rawScore: '31/42',
        grade: 'B',
        passed: true,
        reviewState: 'completed',
        scoreConversionTableVersionKey: 'v2026_canonical',
        scoreConversionPassed: true,
      },
      {
        id: 'writing',
        name: 'Writing',
        score: 350,
        scaledScore: 350,
        rawScore: '27/38',
        grade: 'B',
        passed: true,
        reviewState: 'completed',
      },
      {
        id: 'speaking',
        name: 'Speaking',
        score: 390,
        scaledScore: 390,
        rawScore: '30/39',
        grade: 'B',
        passed: true,
        reviewState: 'completed',
      },
    ],
  };

  it('validates a complete, approved mock report generates pixel-ready SoR card data', () => {
    expect(isMockReportStatementOfResultsReady(baseMockReport)).toBe(true);

    const sor = mockReportToStatementOfResults({
      report: baseMockReport,
      candidate: {
        name: 'Dr. Sarah Jenkins',
        candidateNumber: 'OET-987654-321',
        dateOfBirth: '1990-05-15',
        gender: 'Female',
      },
      country: 'United Kingdom',
      profession: 'Medicine',
    });

    expect(sor.candidate.name).toBe('Dr. Sarah Jenkins');
    expect(sor.candidate.candidateNumber).toBe('OET-987654-321');
    expect(sor.scores.reading).toBe(380);
    expect(sor.scores.listening).toBe(360);
    expect(sor.scores.writing).toBe(350);
    expect(sor.scores.speaking).toBe(390);
    expect(sor.isPractice).toBe(true);
  });

  it('rejects reports with uncompleted or un-governed subtests', () => {
    // Incomplete review state
    const incompleteReport: MockReport = {
      ...baseMockReport,
      subTests: baseMockReport.subTests.map((st) =>
        st.id === 'writing' ? { ...st, reviewState: 'in_review' } : st,
      ),
    };
    expect(isMockReportStatementOfResultsReady(incompleteReport)).toBe(false);

    // Missing governed conversion table on Reading
    const ungovernedReport: MockReport = {
      ...baseMockReport,
      subTests: baseMockReport.subTests.map((st) =>
        st.id === 'reading' ? { ...st, scoreConversionPassed: false } : st,
      ),
    };
    expect(isMockReportStatementOfResultsReady(ungovernedReport)).toBe(false);
  });

  it('generates a stable pseudo-candidate number when none is provided', () => {
    const sor1 = mockReportToStatementOfResults({
      report: baseMockReport,
    });
    const sor2 = mockReportToStatementOfResults({
      report: baseMockReport,
    });

    expect(sor1.candidate.candidateNumber).toMatch(/^OET-\d{6}-\d{6}$/);
    expect(sor1.candidate.candidateNumber).toBe(sor2.candidate.candidateNumber);
  });

  it('clamps and rounds odd/fractional scores to nearest 10 on the 0..500 scale', () => {
    const rawScoresReport: MockReport = {
      ...baseMockReport,
      subTests: [
        { ...baseMockReport.subTests[0], scaledScore: 384 },
        { ...baseMockReport.subTests[1], scaledScore: 366 },
        { ...baseMockReport.subTests[2], scaledScore: 352 },
        { ...baseMockReport.subTests[3], scaledScore: 395 },
      ],
    };

    const sor = mockReportToStatementOfResults({ report: rawScoresReport });
    expect(sor.scores.reading).toBe(380); // 384 -> 380
    expect(sor.scores.listening).toBe(370); // 366 -> 370
    expect(sor.scores.writing).toBe(350); // 352 -> 350
    expect(sor.scores.speaking).toBe(400); // 395 -> 400
  });
});
