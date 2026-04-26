import { describe, it, expect } from 'vitest';
import {
  OET_LR_RAW_MAX,
  OET_LR_RAW_PASS,
  OET_SCALED_PASS_B,
  OET_SCALED_PASS_C_PLUS,
  OET_SCALED_MAX,
  WRITING_RAW_MAX,
  WRITING_CRITERION_MAX_SCORES,
  writingRawTotalFromCriterionScores,
  writingRawToScaled,
  normalizeWritingCountry,
  oetRawToScaled,
  oetGradeFromScaled,
  oetGradeLabel,
  isListeningReadingPassByRaw,
  isListeningReadingPassByScaled,
  gradeListeningReading,
  getWritingPassThreshold,
  gradeWriting,
  isWritingPassFailResult,
  isSpeakingPass,
  gradeSpeaking,
  pronunciationProjectedScaled,
  pronunciationProjectedBand,
  pronunciationScoreTier,
  conversationProjectedScaled,
  conversationProjectedBand,
  formatScaledScore,
  formatRawLrScore,
  formatListeningReadingDisplay,
} from '../scoring';

describe('OET L/R raw → scaled (mission-critical anchors)', () => {
  it('anchors 0/42 → 0', () => {
    expect(oetRawToScaled(0)).toBe(0);
  });
  it('anchors 30/42 → 350 (the pass anchor)', () => {
    expect(oetRawToScaled(30)).toBe(OET_SCALED_PASS_B);
  });
  it('anchors 42/42 → 500', () => {
    expect(oetRawToScaled(42)).toBe(OET_SCALED_MAX);
  });
  it('rounds fractional input', () => {
    expect(oetRawToScaled(29.6)).toBe(oetRawToScaled(30));
  });
  it('clamps negative input to 0', () => {
    expect(oetRawToScaled(-5)).toBe(0);
  });
  it('clamps over-max input to 500', () => {
    expect(oetRawToScaled(99)).toBe(OET_SCALED_MAX);
  });
  it('throws RangeError on NaN / Infinity', () => {
    expect(() => oetRawToScaled(NaN)).toThrow(RangeError);
    expect(() => oetRawToScaled(Infinity)).toThrow(RangeError);
  });
  it('is monotonic non-decreasing across 0..42', () => {
    let prev = -1;
    for (let r = 0; r <= OET_LR_RAW_MAX; r++) {
      const s = oetRawToScaled(r);
      expect(s).toBeGreaterThanOrEqual(prev);
      prev = s;
    }
  });
  it('keeps 29/42 strictly below 350 and 31/42 strictly above', () => {
    expect(oetRawToScaled(29)).toBeLessThan(OET_SCALED_PASS_B);
    expect(oetRawToScaled(31)).toBeGreaterThan(OET_SCALED_PASS_B);
  });
});

describe('oetGradeFromScaled bands', () => {
  it.each([
    [500, 'A'],
    [450, 'A'],
    [449, 'B'],
    [350, 'B'],
    [349, 'C+'],
    [300, 'C+'],
    [299, 'C'],
    [200, 'C'],
    [199, 'D'],
    [100, 'D'],
    [99, 'E'],
    [0, 'E'],
  ])('scaled %i → %s', (scaled, grade) => {
    expect(oetGradeFromScaled(scaled)).toBe(grade);
  });
  it('clamps out-of-range scaled scores', () => {
    expect(oetGradeFromScaled(-50)).toBe('E');
    expect(oetGradeFromScaled(9999)).toBe('A');
  });
  it('formats grade labels', () => {
    expect(oetGradeLabel('B')).toBe('Grade B');
    expect(oetGradeLabel('C+')).toBe('Grade C+');
  });
});

describe('Listening/Reading pass logic', () => {
  it('passes at exactly raw 30', () => {
    expect(isListeningReadingPassByRaw(30)).toBe(true);
  });
  it('fails at raw 29', () => {
    expect(isListeningReadingPassByRaw(29)).toBe(false);
  });
  it('passes at exactly scaled 350', () => {
    expect(isListeningReadingPassByScaled(OET_SCALED_PASS_B)).toBe(true);
  });
  it('fails at scaled 349', () => {
    expect(isListeningReadingPassByScaled(349)).toBe(false);
  });
  it('gradeListeningReading exposes raw, scaled, grade and pass=true at 30/42', () => {
    const r = gradeListeningReading('listening', 30);
    expect(r).toMatchObject({
      subtest: 'listening',
      rawCorrect: 30,
      rawMax: OET_LR_RAW_MAX,
      scaledScore: 350,
      requiredScaled: 350,
      requiredGrade: 'B',
      grade: 'B',
      passed: true,
    });
  });
  it('gradeListeningReading at 29/42 fails', () => {
    const r = gradeListeningReading('reading', 29);
    expect(r.passed).toBe(false);
    expect(r.grade).toBe('C+');
  });
});

describe('Writing country normalization', () => {
  it.each([
    ['gb', 'GB'],
    ['UK', 'GB'],
    ['United Kingdom', 'GB'],
    ['england', 'GB'],
    ['ireland', 'IE'],
    ['australia', 'AU'],
    ['NZ', 'NZ'],
    ['ca', 'CA'],
    ['usa', 'US'],
    ['America', 'US'],
    ['Qatar', 'QA'],
  ])('maps %s → %s', (input, expected) => {
    expect(normalizeWritingCountry(input)).toBe(expected);
  });
  it('returns null for empty / null / unknown', () => {
    expect(normalizeWritingCountry(null)).toBeNull();
    expect(normalizeWritingCountry(undefined)).toBeNull();
    expect(normalizeWritingCountry('')).toBeNull();
    expect(normalizeWritingCountry('   ')).toBeNull();
    expect(normalizeWritingCountry('FR')).toBeNull();
    expect(normalizeWritingCountry('Atlantis')).toBeNull();
  });
});

describe('Writing pass threshold + gradeWriting', () => {
  it('UK/IE/AU/NZ/CA → grade B / 350', () => {
    for (const c of ['GB', 'IE', 'AU', 'NZ', 'CA'] as const) {
      const t = getWritingPassThreshold(c);
      expect(t?.threshold).toBe(OET_SCALED_PASS_B);
      expect(t?.grade).toBe('B');
    }
  });
  it('US/QA → grade C+ / 300', () => {
    for (const c of ['US', 'QA'] as const) {
      const t = getWritingPassThreshold(c);
      expect(t?.threshold).toBe(OET_SCALED_PASS_C_PLUS);
      expect(t?.grade).toBe('C+');
    }
  });
  it('returns country_required when country missing', () => {
    const r = gradeWriting(360, null);
    expect(isWritingPassFailResult(r)).toBe(false);
    if (!isWritingPassFailResult(r)) {
      expect(r.reason).toBe('country_required');
      expect(r.passed).toBeNull();
      expect(r.providedCountry).toBeNull();
    }
  });
  it('returns country_unsupported when country given but unknown', () => {
    const r = gradeWriting(360, 'France');
    expect(isWritingPassFailResult(r)).toBe(false);
    if (!isWritingPassFailResult(r)) {
      expect(r.reason).toBe('country_unsupported');
      expect(r.providedCountry).toBe('France');
    }
  });
  it('US: 300 passes, 299 fails', () => {
    const r1 = gradeWriting(300, 'US');
    const r2 = gradeWriting(299, 'US');
    expect(isWritingPassFailResult(r1) && r1.passed).toBe(true);
    expect(isWritingPassFailResult(r2) && r2.passed).toBe(false);
  });
  it('UK: 349 fails, 350 passes', () => {
    const r1 = gradeWriting(349, 'UK');
    const r2 = gradeWriting(350, 'UK');
    expect(isWritingPassFailResult(r1) && r1.passed).toBe(false);
    expect(isWritingPassFailResult(r2) && r2.passed).toBe(true);
  });
  it('does NOT silently default to UK when country missing', () => {
    const r = gradeWriting(450, undefined);
    expect(isWritingPassFailResult(r)).toBe(false);
  });
});

describe('Writing raw scoring', () => {
  it('writingRawToScaled anchors 0 → 0 and 38 → 500', () => {
    expect(writingRawToScaled(0)).toBe(0);
    expect(writingRawToScaled(WRITING_RAW_MAX)).toBe(OET_SCALED_MAX);
  });
  it('writingRawToScaled clamps and rounds', () => {
    expect(writingRawToScaled(-5)).toBe(0);
    expect(writingRawToScaled(99)).toBe(OET_SCALED_MAX);
  });
  it('writingRawTotalFromCriterionScores caps each criterion at its max', () => {
    const inflated = Object.fromEntries(
      Object.entries(WRITING_CRITERION_MAX_SCORES).map(([k, v]) => [k, v + 99]),
    );
    expect(writingRawTotalFromCriterionScores(inflated)).toBe(WRITING_RAW_MAX);
  });
  it('treats missing / negative / non-finite as 0', () => {
    expect(
      writingRawTotalFromCriterionScores({ purpose: -2, content: NaN }),
    ).toBe(0);
  });
  it('ignores unknown keys', () => {
    expect(
      writingRawTotalFromCriterionScores({ unknown_criterion: 99 } as Record<string, number>),
    ).toBe(0);
  });
});

describe('Speaking pass', () => {
  it('passes at exactly 350, fails at 349, regardless of country', () => {
    expect(isSpeakingPass(350)).toBe(true);
    expect(isSpeakingPass(349)).toBe(false);
    const r = gradeSpeaking(350);
    expect(r.subtest).toBe('speaking');
    expect(r.passed).toBe(true);
    expect(r.requiredScaled).toBe(OET_SCALED_PASS_B);
  });
});

describe('Pronunciation projection', () => {
  it.each([
    [0, 0],
    [60, 300],
    [70, 350],
    [80, 400],
    [90, 450],
    [100, 500],
  ])('anchor %i → %i', (overall, scaled) => {
    expect(pronunciationProjectedScaled(overall)).toBe(scaled);
  });
  it('interpolates linearly between anchors', () => {
    const mid = pronunciationProjectedScaled(75);
    expect(mid).toBeGreaterThan(350);
    expect(mid).toBeLessThan(400);
  });
  it('clamps and handles non-finite', () => {
    expect(pronunciationProjectedScaled(-10)).toBe(0);
    expect(pronunciationProjectedScaled(150)).toBe(500);
    expect(pronunciationProjectedScaled(NaN)).toBe(0);
  });
  it('projected band passes at 70', () => {
    expect(pronunciationProjectedBand(70).passed).toBe(true);
    expect(pronunciationProjectedBand(69).passed).toBe(false);
  });
  it('tiers bucket correctly', () => {
    expect(pronunciationScoreTier(95)).toBe('excellent');
    expect(pronunciationScoreTier(85)).toBe('excellent');
    expect(pronunciationScoreTier(84)).toBe('passing');
    expect(pronunciationScoreTier(70)).toBe('passing');
    expect(pronunciationScoreTier(50)).toBe('below');
    expect(pronunciationScoreTier(0)).toBe('empty');
    expect(pronunciationScoreTier(NaN)).toBe('empty');
  });
});

describe('Conversation projection', () => {
  it.each([
    [0, 0],
    [3, 250],
    [4.2, 350],
    [5, 417],
    [6, 500],
  ])('anchor mean %s → %i', (mean, scaled) => {
    expect(conversationProjectedScaled(mean)).toBe(scaled);
  });
  it('clamps NaN / out of range', () => {
    expect(conversationProjectedScaled(NaN)).toBe(0);
    expect(conversationProjectedScaled(-1)).toBe(0);
    expect(conversationProjectedScaled(99)).toBe(500);
  });
  it('projected band averages four criteria and applies 350 threshold', () => {
    const pass = conversationProjectedBand(4.2, 4.2, 4.2, 4.2);
    expect(pass.passed).toBe(true);
    const fail = conversationProjectedBand(3, 3, 3, 3);
    expect(fail.passed).toBe(false);
  });
  it('treats non-finite criteria as 0', () => {
    const r = conversationProjectedBand(NaN, Infinity, 6, 6);
    // mean of (0, 0, 6, 6) = 3 → 250
    expect(r.scaledScore).toBe(250);
  });
});

describe('Display formatters', () => {
  it('formatScaledScore', () => {
    expect(formatScaledScore(380)).toBe('380/500');
    expect(formatScaledScore(-5)).toBe('0/500');
    expect(formatScaledScore(9999)).toBe('500/500');
  });
  it('formatRawLrScore', () => {
    expect(formatRawLrScore(35)).toBe('35/42');
    expect(formatRawLrScore(99)).toBe('42/42');
  });
  it('formatListeningReadingDisplay shows raw • scaled • grade with bullet separator', () => {
    const out = formatListeningReadingDisplay(30);
    expect(out).toBe('30/42 \u2022 350/500 \u2022 Grade B');
  });
});

describe('Scoring constants sanity', () => {
  it('OET_LR_RAW_MAX is 42, OET_LR_RAW_PASS is 30', () => {
    expect(OET_LR_RAW_MAX).toBe(42);
    expect(OET_LR_RAW_PASS).toBe(30);
  });
  it('Pass thresholds: B=350, C+=300, max=500', () => {
    expect(OET_SCALED_PASS_B).toBe(350);
    expect(OET_SCALED_PASS_C_PLUS).toBe(300);
    expect(OET_SCALED_MAX).toBe(500);
  });
});
