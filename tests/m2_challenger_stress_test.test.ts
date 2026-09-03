import { describe, it, expect } from 'vitest';
import {
  oetRawToScaled,
  oetGradeFromScaled,
  oetGradeLabel,
  isListeningReadingPassByRaw,
  isListeningReadingPassByScaled,
  gradeListeningReading,
  WRITING_CRITERION_CODES,
  WRITING_CRITERION_MAX_SCORES,
  WRITING_RAW_MAX,
  writingRawTotalFromCriterionScores,
  writingRawToScaled,
  WRITING_GRADE_B_COUNTRIES,
  WRITING_GRADE_C_PLUS_COUNTRIES,
  SUPPORTED_WRITING_COUNTRIES,
  normalizeWritingCountry,
  getWritingPassThreshold,
  gradeWriting,
  deriveWritingResultFromCriteria,
  isSpeakingPass,
  gradeSpeaking,
  SPEAKING_RUBRIC_MAX,
  speakingProjectedScaledFromPercentage,
  speakingProjectedScaled,
  speakingProjectedBand,
  speakingReadinessBandFromScaled,
  speakingReadinessBandLabel,
  formatScaledScore,
  formatRawLrScore,
  formatListeningReadingDisplay,
  OET_LR_RAW_MAX,
  OET_LR_RAW_PASS,
  OET_SCALED_PASS_B,
  OET_SCALED_PASS_C_PLUS,
  OET_SCALED_MIN,
  OET_SCALED_MAX,
} from '@/lib/scoring';

import { loadRulebook, listRulebooks } from '@/lib/rulebook/loader';

describe('CHALLENGER 1 M2 EMPIRICAL AUDIT: OBJECTIVE SCORING (43 INTEGER POINTS 0..42)', () => {
  it('Verify exact invariant anchors: 0->0, 30->350, 42->500', () => {
    expect(oetRawToScaled(0)).toBe(0);
    expect(oetRawToScaled(30)).toBe(350);
    expect(oetRawToScaled(42)).toBe(500);

    expect(isListeningReadingPassByRaw(0)).toBe(false);
    expect(isListeningReadingPassByRaw(30)).toBe(true);
    expect(isListeningReadingPassByRaw(42)).toBe(true);

    expect(isListeningReadingPassByScaled(0)).toBe(false);
    expect(isListeningReadingPassByScaled(350)).toBe(true);
    expect(isListeningReadingPassByScaled(500)).toBe(true);
  });

  it('Exhaustive empirical check of all 43 raw score points (0..42) against piecewise linear formula', () => {
    const rawToScaledTable: Array<{ raw: number; scaled: number; grade: string; passed: boolean }> = [];
    for (let r = 0; r <= 42; r++) {
      const scaled = oetRawToScaled(r);
      let expectedScaled: number;
      if (r === 0) {
        expectedScaled = 0;
      } else if (r === 30) {
        expectedScaled = 350;
      } else if (r === 42) {
        expectedScaled = 500;
      } else if (r < 30) {
        expectedScaled = Math.round((r * 350) / 30);
      } else {
        expectedScaled = Math.round(350 + ((r - 30) * 150) / 12);
      }

      expect(scaled).toBe(expectedScaled);
      const grade = oetGradeFromScaled(scaled);
      const passed = r >= 30;
      rawToScaledTable.push({ raw: r, scaled, grade, passed });
    }

    expect(rawToScaledTable.length).toBe(43);
  });

  it('Exhaustive check: Strict monotonic non-decreasing progression across all 43 points', () => {
    let prev = -1;
    for (let r = 0; r <= 42; r++) {
      const s = oetRawToScaled(r);
      expect(s).toBeGreaterThanOrEqual(prev);
      prev = s;
    }
  });

  it('Boundary delta transitions: 29->338 (Grade C+, fail), 30->350 (Grade B, pass), 31->363 (Grade B, pass)', () => {
    const s29 = oetRawToScaled(29);
    const s30 = oetRawToScaled(30);
    const s31 = oetRawToScaled(31);

    expect(s29).toBe(338);
    expect(s30).toBe(350);
    expect(s31).toBe(363);

    expect(isListeningReadingPassByRaw(29)).toBe(false);
    expect(isListeningReadingPassByRaw(30)).toBe(true);
    expect(isListeningReadingPassByRaw(31)).toBe(true);

    expect(isListeningReadingPassByScaled(s29)).toBe(false);
    expect(isListeningReadingPassByScaled(s30)).toBe(true);
    expect(isListeningReadingPassByScaled(s31)).toBe(true);
  });

  it('Exhaustive grade band mapping across all 43 points', () => {
    for (let r = 0; r <= 42; r++) {
      const scaled = oetRawToScaled(r);
      const grade = oetGradeFromScaled(scaled);

      if (scaled >= 450) {
        expect(grade).toBe('A');
      } else if (scaled >= 350) {
        expect(grade).toBe('B');
      } else if (scaled >= 300) {
        expect(grade).toBe('C+');
      } else if (scaled >= 200) {
        expect(grade).toBe('C');
      } else if (scaled >= 100) {
        expect(grade).toBe('D');
      } else {
        expect(grade).toBe('E');
      }

      const rResult = gradeListeningReading('reading', r);
      expect(rResult.rawCorrect).toBe(r);
      expect(rResult.rawMax).toBe(42);
      expect(rResult.scaledScore).toBe(scaled);
      expect(rResult.grade).toBe(grade);
      expect(rResult.passed).toBe(r >= 30);
    }
  });

  it('Clamping and extreme edge cases for objective scoring', () => {
    expect(oetRawToScaled(-100)).toBe(0);
    expect(oetRawToScaled(-1)).toBe(0);
    expect(oetRawToScaled(43)).toBe(500);
    expect(oetRawToScaled(9999)).toBe(500);

    expect(oetRawToScaled(29.4)).toBe(338);
    expect(oetRawToScaled(29.6)).toBe(350);

    expect(() => oetRawToScaled(NaN)).toThrow(RangeError);
    expect(() => oetRawToScaled(Infinity)).toThrow(RangeError);
  });
});

describe('CHALLENGER 1 M2 EMPIRICAL AUDIT: WRITING RUBRIC & 12 PROFESSIONS & COUNTRY DESTINATION', () => {
  const ALL_12_PROFESSIONS = [
    'medicine',
    'nursing',
    'pharmacy',
    'dentistry',
    'physiotherapy',
    'veterinary',
    'optometry',
    'radiography',
    'occupational-therapy',
    'speech-pathology',
    'podiatry',
    'dietetics',
    'other-allied-health',
  ] as const;

  it('Rulebook integrity: All 12 professions exist, load without error, and contain 172-rule baseline', () => {
    const registered = listRulebooks()
      .filter((b) => b.kind === 'writing')
      .map((b) => b.profession);

    for (const p of ALL_12_PROFESSIONS) {
      expect(registered).toContain(p);
      const book = loadRulebook('writing', p);
      expect(book.kind).toBe('writing');
      expect(book.profession).toBe(p);
      expect(book.rules.length).toBe(172);
      expect(book.sections.length).toBeGreaterThan(0);
    }
  });

  it('Writing criteria max scores: Total raw max is 38, Purpose clamped to 0..3, others 0..7', () => {
    expect(WRITING_RAW_MAX).toBe(38);
    expect(WRITING_CRITERION_MAX_SCORES.purpose).toBe(3);
    expect(WRITING_CRITERION_MAX_SCORES.content).toBe(7);
    expect(WRITING_CRITERION_MAX_SCORES.conciseness_clarity).toBe(7);
    expect(WRITING_CRITERION_MAX_SCORES.genre_style).toBe(7);
    expect(WRITING_CRITERION_MAX_SCORES.organisation_layout).toBe(7);
    expect(WRITING_CRITERION_MAX_SCORES.language).toBe(7);

    const inflated = {
      purpose: 10,
      content: 10,
      conciseness_clarity: 10,
      genre_style: 10,
      organisation_layout: 10,
      language: 10,
    };
    expect(writingRawTotalFromCriterionScores(inflated)).toBe(38);
    expect(writingRawToScaled(38)).toBe(500);

    const zero = {
      purpose: 0,
      content: 0,
      conciseness_clarity: 0,
      genre_style: 0,
      organisation_layout: 0,
      language: 0,
    };
    expect(writingRawTotalFromCriterionScores(zero)).toBe(0);
    expect(writingRawToScaled(0)).toBe(0);
  });

  it('Writing raw-to-scaled conversion monotonicity across all 39 points (0..38)', () => {
    let prev = -1;
    for (let r = 0; r <= 38; r++) {
      const s = writingRawToScaled(r);
      const expected = Math.round((r * 500) / 38);
      expect(s).toBe(expected);
      expect(s).toBeGreaterThanOrEqual(prev);
      prev = s;
    }
  });

  it('Country Destination Matrix: Grade B countries require >= 350', () => {
    const gradeBList = [
      'GB', 'UK', 'United Kingdom', 'Great Britain', 'England', 'Scotland', 'Wales', 'Northern Ireland',
      'IE', 'Ireland', 'Republic of Ireland',
      'AU', 'Australia',
      'NZ', 'New Zealand',
      'CA', 'Canada',
      'Gulf Countries', 'Other Countries',
    ];

    for (const country of gradeBList) {
      const threshold = getWritingPassThreshold(country);
      expect(threshold).not.toBeNull();
      expect(threshold!.threshold).toBe(350);
      expect(threshold!.grade).toBe('B');

      const pass350 = gradeWriting(350, country);
      expect(pass350.passed).toBe(true);
      if (pass350.passed !== null) {
        expect(pass350.requiredScaled).toBe(350);
        expect(pass350.requiredGrade).toBe('B');
      }

      const fail349 = gradeWriting(349, country);
      expect(fail349.passed).toBe(false);
      if (fail349.passed !== null) {
        expect(fail349.requiredScaled).toBe(350);
      }

      const fail300 = gradeWriting(300, country);
      expect(fail300.passed).toBe(false);
      if (fail300.passed !== null) {
        expect(fail300.grade).toBe('C+');
      }
    }
  });

  it('Country Destination Matrix: Grade C+ countries require >= 300', () => {
    const gradeCPlusList = [
      'US', 'USA', 'United States', 'United States of America', 'America',
      'QA', 'Qatar',
    ];

    for (const country of gradeCPlusList) {
      const threshold = getWritingPassThreshold(country);
      expect(threshold).not.toBeNull();
      expect(threshold!.threshold).toBe(300);
      expect(threshold!.grade).toBe('C+');

      const pass300 = gradeWriting(300, country);
      expect(pass300.passed).toBe(true);
      if (pass300.passed !== null) {
        expect(pass300.requiredScaled).toBe(300);
        expect(pass300.requiredGrade).toBe('C+');
      }

      const fail299 = gradeWriting(299, country);
      expect(fail299.passed).toBe(false);
      if (fail299.passed !== null) {
        expect(fail299.requiredScaled).toBe(300);
      }

      const pass350 = gradeWriting(350, country);
      expect(pass350.passed).toBe(true);
    }
  });

  it('Cross-country divergence test: Score in [300, 349] passes for US/QA but fails for GB/IE/AU/NZ/CA', () => {
    for (const s of [300, 310, 320, 330, 340, 349]) {
      const uk = gradeWriting(s, 'UK');
      const ie = gradeWriting(s, 'Ireland');
      const au = gradeWriting(s, 'AU');
      const nz = gradeWriting(s, 'NZ');
      const ca = gradeWriting(s, 'Canada');

      const us = gradeWriting(s, 'USA');
      const qa = gradeWriting(s, 'Qatar');

      expect(uk.passed).toBe(false);
      expect(ie.passed).toBe(false);
      expect(au.passed).toBe(false);
      expect(nz.passed).toBe(false);
      expect(ca.passed).toBe(false);

      expect(us.passed).toBe(true);
      expect(qa.passed).toBe(true);
    }
  });

  it('Fail-closed on missing, empty, or unsupported countries', () => {
    for (const c of [null, undefined, '', '   ']) {
      const res = gradeWriting(400, c);
      expect(res.passed).toBeNull();
      if (res.passed === null) {
        expect(res.reason).toBe('country_required');
        expect(res.subtest).toBe('writing');
      }
    }

    for (const c of ['FR', 'DE', 'Japan', 'China', 'India', 'Pakistan', 'Atlantis']) {
      const res = gradeWriting(400, c);
      expect(res.passed).toBeNull();
      if (res.passed === null) {
        expect(res.reason).toBe('country_unsupported');
        expect(res.providedCountry).toBe(c);
        expect(res.subtest).toBe('writing');
      }
    }
  });

  it('deriveWritingResultFromCriteria across arbitrary score vectors', () => {
    const vector1 = {
      purpose: 2,
      content: 5,
      conciseness_clarity: 5,
      genre_style: 5,
      organisation_layout: 5,
      language: 5,
    }; // 2 + 25 = 27 raw -> round(27 * 500 / 38) = 355
    const res1 = deriveWritingResultFromCriteria(vector1, 'UK');
    expect(res1.rawTotal).toBe(27);
    expect(res1.scaled).toBe(355);
    expect(res1.grade).toBe('B');
    expect(res1.result.passed).toBe(true);

    const vector2 = {
      purpose: 2,
      content: 4,
      conciseness_clarity: 4,
      genre_style: 5,
      organisation_layout: 5,
      language: 5,
    }; // 2 + 23 = 25 raw -> round(25 * 500 / 38) = 329
    const res2UK = deriveWritingResultFromCriteria(vector2, 'UK');
    const res2US = deriveWritingResultFromCriteria(vector2, 'USA');
    expect(res2UK.rawTotal).toBe(25);
    expect(res2UK.scaled).toBe(329);
    expect(res2UK.grade).toBe('C+');
    expect(res2UK.result.passed).toBe(false);
    expect(res2US.result.passed).toBe(true);
  });
});

describe('CHALLENGER 1 M2 EMPIRICAL AUDIT: SPEAKING RUBRIC & UNIVERSAL PASS POLICY', () => {
  it('Speaking rubric max score is 39 (24 linguistic + 15 clinical)', () => {
    expect(SPEAKING_RUBRIC_MAX).toBe(39);
  });

  it('Speaking percentage anchors: 0->0, 50->250, 70->350, 80->400, 90->450, 100->500', () => {
    expect(speakingProjectedScaledFromPercentage(0)).toBe(0);
    expect(speakingProjectedScaledFromPercentage(50)).toBe(250);
    expect(speakingProjectedScaledFromPercentage(70)).toBe(350);
    expect(speakingProjectedScaledFromPercentage(80)).toBe(400);
    expect(speakingProjectedScaledFromPercentage(90)).toBe(450);
    expect(speakingProjectedScaledFromPercentage(100)).toBe(500);
  });

  it('Speaking percentage projection monotonicity across all 101 points (0..100)', () => {
    let prev = -1;
    for (let p = 0; p <= 100; p++) {
      const s = speakingProjectedScaledFromPercentage(p);
      expect(s).toBeGreaterThanOrEqual(prev);
      prev = s;
    }
  });

  it('Speaking full criterion vector calculations and readiness band mapping', () => {
    const perfect = {
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
    expect(speakingProjectedScaled(perfect)).toBe(500);
    const bandPerfect = speakingProjectedBand(perfect);
    expect(bandPerfect.passed).toBe(true);
    expect(bandPerfect.grade).toBe('A');

    const zero = {
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
    expect(speakingProjectedScaled(zero)).toBe(0);
    const bandZero = speakingProjectedBand(zero);
    expect(bandZero.passed).toBe(false);
    expect(bandZero.grade).toBe('E');

    // Universal Speaking pass is strictly 350
    expect(isSpeakingPass(350)).toBe(true);
    expect(isSpeakingPass(349)).toBe(false);
    expect(isSpeakingPass(300)).toBe(false);

    const grade350 = gradeSpeaking(350);
    expect(grade350.passed).toBe(true);
    expect(grade350.requiredScaled).toBe(350);
    expect(grade350.grade).toBe('B');

    const grade349 = gradeSpeaking(349);
    expect(grade349.passed).toBe(false);
    expect(grade349.grade).toBe('C+');

    // Readiness bands
    expect(speakingReadinessBandFromScaled(200)).toBe('not_ready');
    expect(speakingReadinessBandFromScaled(250)).toBe('developing');
    expect(speakingReadinessBandFromScaled(300)).toBe('borderline');
    expect(speakingReadinessBandFromScaled(350)).toBe('exam_ready');
    expect(speakingReadinessBandFromScaled(420)).toBe('strong');
    expect(speakingReadinessBandFromScaled(500)).toBe('strong');

    expect(speakingReadinessBandLabel('not_ready')).toBe('Not ready');
    expect(speakingReadinessBandLabel('developing')).toBe('Developing');
    expect(speakingReadinessBandLabel('borderline')).toBe('Borderline');
    expect(speakingReadinessBandLabel('exam_ready')).toBe('Exam-ready');
    expect(speakingReadinessBandLabel('strong')).toBe('Strong');
  });
});
