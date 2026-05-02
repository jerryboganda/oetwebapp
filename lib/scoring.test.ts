import {
  // constants
  OET_LR_RAW_MAX,
  OET_LR_RAW_PASS,
  OET_SCALED_MAX,
  OET_SCALED_MIN,
  OET_SCALED_PASS_B,
  OET_SCALED_PASS_C_PLUS,
  SUPPORTED_WRITING_COUNTRIES,
  WRITING_GRADE_B_COUNTRIES,
  WRITING_GRADE_C_PLUS_COUNTRIES,
  // funcs
  formatListeningReadingDisplay,
  formatRawLrScore,
  formatScaledScore,
  getWritingPassThreshold,
  gradeListeningReading,
  gradeSpeaking,
  gradeWriting,
  isListeningReadingPassByRaw,
  isListeningReadingPassByScaled,
  isSpeakingPass,
  isWritingPassFailResult,
  normalizeWritingCountry,
  oetGradeFromScaled,
  oetGradeLabel,
  oetRawToScaled,
} from './scoring';

describe('OET scoring — constants', () => {
  it('fixes the mission-critical invariants', () => {
    expect(OET_LR_RAW_MAX).toBe(42);
    expect(OET_LR_RAW_PASS).toBe(30);
    expect(OET_SCALED_PASS_B).toBe(350);
    expect(OET_SCALED_PASS_C_PLUS).toBe(300);
    expect(OET_SCALED_MIN).toBe(0);
    expect(OET_SCALED_MAX).toBe(500);
  });

  it('lists exactly the correct Grade-B countries', () => {
    expect([...WRITING_GRADE_B_COUNTRIES].sort()).toEqual(
      ['GB', 'IE', 'AU', 'NZ', 'CA'].sort(),
    );
  });

  it('lists exactly the correct Grade-C+ countries', () => {
    expect([...WRITING_GRADE_C_PLUS_COUNTRIES].sort()).toEqual(['US', 'QA'].sort());
  });

  it('does not overlap Grade-B and Grade-C+ country sets', () => {
    for (const c of WRITING_GRADE_B_COUNTRIES) {
      expect(WRITING_GRADE_C_PLUS_COUNTRIES).not.toContain(c);
    }
  });

  it('supported set is the union of both', () => {
    expect([...SUPPORTED_WRITING_COUNTRIES].sort()).toEqual(
      ['GB', 'IE', 'AU', 'NZ', 'CA', 'US', 'QA'].sort(),
    );
  });
});

describe('OET scoring — oetRawToScaled (Listening/Reading anchors)', () => {
  it('maps 30/42 EXACTLY to 350/500 (mission-critical anchor)', () => {
    expect(oetRawToScaled(30)).toBe(350);
  });

  it('maps 0/42 to 0/500', () => {
    expect(oetRawToScaled(0)).toBe(0);
  });

  it('maps 42/42 to 500/500', () => {
    expect(oetRawToScaled(42)).toBe(500);
  });

  it('29/42 is strictly below 350 (fail territory)', () => {
    expect(oetRawToScaled(29)).toBeLessThan(350);
  });

  it('31/42 is strictly above 350 (pass territory)', () => {
    expect(oetRawToScaled(31)).toBeGreaterThan(350);
  });

  it('is monotonically non-decreasing across the full raw range', () => {
    let prev = -1;
    for (let r = 0; r <= 42; r++) {
      const s = oetRawToScaled(r);
      expect(s).toBeGreaterThanOrEqual(prev);
      prev = s;
    }
  });

  it('clamps raw scores above 42 to 500', () => {
    expect(oetRawToScaled(100)).toBe(500);
  });

  it('clamps negative raw scores to 0', () => {
    expect(oetRawToScaled(-5)).toBe(0);
  });

  it('rounds fractional raw values', () => {
    expect(oetRawToScaled(29.6)).toBe(350); // rounds to 30 → 350
    expect(oetRawToScaled(30.4)).toBe(350);
  });

  it('throws on non-finite input', () => {
    expect(() => oetRawToScaled(Number.NaN)).toThrow();
    expect(() => oetRawToScaled(Number.POSITIVE_INFINITY)).toThrow();
  });
});

describe('OET scoring — oetGradeFromScaled', () => {
  it('assigns the correct grade at each band boundary', () => {
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

  it('clamps out-of-range input', () => {
    expect(oetGradeFromScaled(9999)).toBe('A');
    expect(oetGradeFromScaled(-100)).toBe('E');
  });

  it('oetGradeLabel prefixes "Grade "', () => {
    expect(oetGradeLabel('B')).toBe('Grade B');
    expect(oetGradeLabel('C+')).toBe('Grade C+');
  });
});

describe('OET scoring — Listening/Reading pass logic', () => {
  it('isListeningReadingPassByRaw: 30 passes, 29 fails', () => {
    expect(isListeningReadingPassByRaw(30)).toBe(true);
    expect(isListeningReadingPassByRaw(29)).toBe(false);
    expect(isListeningReadingPassByRaw(42)).toBe(true);
    expect(isListeningReadingPassByRaw(0)).toBe(false);
  });

  it('isListeningReadingPassByScaled: 350 passes, 349 fails', () => {
    expect(isListeningReadingPassByScaled(350)).toBe(true);
    expect(isListeningReadingPassByScaled(349)).toBe(false);
    expect(isListeningReadingPassByScaled(500)).toBe(true);
    expect(isListeningReadingPassByScaled(0)).toBe(false);
  });

  it('gradeListeningReading returns a consistent full result', () => {
    const r = gradeListeningReading('listening', 30);
    expect(r).toEqual({
      subtest: 'listening',
      rawCorrect: 30,
      rawMax: 42,
      scaledScore: 350,
      requiredScaled: 350,
      requiredGrade: 'B',
      grade: 'B',
      passed: true,
    });
  });

  it('gradeListeningReading flags 29/42 as fail with correct scaled', () => {
    const r = gradeListeningReading('reading', 29);
    expect(r.passed).toBe(false);
    expect(r.scaledScore).toBeLessThan(350);
    expect(r.grade === 'C+' || r.grade === 'C' || r.grade === 'D' || r.grade === 'E').toBe(true);
  });

  it('gradeListeningReading: perfect score is A', () => {
    const r = gradeListeningReading('reading', 42);
    expect(r.scaledScore).toBe(500);
    expect(r.grade).toBe('A');
    expect(r.passed).toBe(true);
  });
});

describe('OET scoring — normalizeWritingCountry', () => {
  it.each([
    ['GB', 'GB'],
    ['UK', 'GB'],
    ['United Kingdom', 'GB'],
    ['Great Britain', 'GB'],
    ['england', 'GB'],
    ['IE', 'IE'],
    ['Ireland', 'IE'],
    ['AU', 'AU'],
    ['Australia', 'AU'],
    ['NZ', 'NZ'],
    ['New Zealand', 'NZ'],
    ['CA', 'CA'],
    ['Canada', 'CA'],
    ['US', 'US'],
    ['USA', 'US'],
    ['United States', 'US'],
    ['united states of america', 'US'],
    ['America', 'US'],
    ['QA', 'QA'],
    ['Qatar', 'QA'],
    ['  qatar  ', 'QA'],
    ['Gulf Countries', 'GB'],
    ['Other Countries', 'GB'],
  ])('normalizes %s to %s', (input, expected) => {
    expect(normalizeWritingCountry(input)).toBe(expected);
  });

  it('returns null for null/undefined/empty', () => {
    expect(normalizeWritingCountry(null)).toBeNull();
    expect(normalizeWritingCountry(undefined)).toBeNull();
    expect(normalizeWritingCountry('')).toBeNull();
    expect(normalizeWritingCountry('   ')).toBeNull();
  });

  it('returns null for unsupported countries', () => {
    expect(normalizeWritingCountry('DE')).toBeNull();
    expect(normalizeWritingCountry('France')).toBeNull();
    expect(normalizeWritingCountry('India')).toBeNull();
  });
});

describe('OET scoring — Writing thresholds (country-aware)', () => {
  it.each(['GB', 'UK', 'IE', 'AU', 'NZ', 'CA', 'Gulf Countries', 'Other Countries'])(
    'returns 350/Grade B for %s',
    (country) => {
      const t = getWritingPassThreshold(country);
      expect(t).not.toBeNull();
      expect(t!.threshold).toBe(350);
      expect(t!.grade).toBe('B');
    },
  );

  it.each(['US', 'USA', 'QA', 'Qatar'])('returns 300/Grade C+ for %s', (country) => {
    const t = getWritingPassThreshold(country);
    expect(t).not.toBeNull();
    expect(t!.threshold).toBe(300);
    expect(t!.grade).toBe('C+');
  });

  it('returns null for missing/unsupported country', () => {
    expect(getWritingPassThreshold(null)).toBeNull();
    expect(getWritingPassThreshold(undefined)).toBeNull();
    expect(getWritingPassThreshold('')).toBeNull();
    expect(getWritingPassThreshold('DE')).toBeNull();
  });
});

describe('OET scoring — gradeWriting', () => {
  it('UK candidate scoring 350 → pass at Grade B', () => {
    const r = gradeWriting(350, 'UK');
    expect(isWritingPassFailResult(r)).toBe(true);
    if (!isWritingPassFailResult(r)) return;
    expect(r.passed).toBe(true);
    expect(r.requiredScaled).toBe(350);
    expect(r.requiredGrade).toBe('B');
    expect(r.grade).toBe('B');
  });

  it('UK candidate scoring 349 → fail despite being at Grade C+', () => {
    const r = gradeWriting(349, 'UK');
    expect(isWritingPassFailResult(r)).toBe(true);
    if (!isWritingPassFailResult(r)) return;
    expect(r.passed).toBe(false);
    expect(r.grade).toBe('C+');
    expect(r.requiredGrade).toBe('B');
  });

  it('USA candidate scoring 300 → pass at Grade C+', () => {
    const r = gradeWriting(300, 'USA');
    expect(isWritingPassFailResult(r)).toBe(true);
    if (!isWritingPassFailResult(r)) return;
    expect(r.passed).toBe(true);
    expect(r.requiredScaled).toBe(300);
    expect(r.requiredGrade).toBe('C+');
  });

  it('USA candidate scoring 299 → fail', () => {
    const r = gradeWriting(299, 'US');
    expect(isWritingPassFailResult(r)).toBe(true);
    if (!isWritingPassFailResult(r)) return;
    expect(r.passed).toBe(false);
  });

  it('Qatar candidate scoring 310 → pass', () => {
    const r = gradeWriting(310, 'Qatar');
    expect(isWritingPassFailResult(r)).toBe(true);
    if (!isWritingPassFailResult(r)) return;
    expect(r.passed).toBe(true);
    expect(r.requiredScaled).toBe(300);
  });

  it('CRITICAL: the SAME 320 score is FAIL for UK but PASS for USA', () => {
    const uk = gradeWriting(320, 'UK');
    const us = gradeWriting(320, 'USA');
    expect(isWritingPassFailResult(uk)).toBe(true);
    expect(isWritingPassFailResult(us)).toBe(true);
    if (!isWritingPassFailResult(uk) || !isWritingPassFailResult(us)) return;
    expect(uk.passed).toBe(false);
    expect(us.passed).toBe(true);
  });

  it('returns country_required when country is missing/empty', () => {
    const r1 = gradeWriting(400, null);
    const r2 = gradeWriting(400, undefined);
    const r3 = gradeWriting(400, '');
    for (const r of [r1, r2, r3]) {
      expect(isWritingPassFailResult(r)).toBe(false);
      if (isWritingPassFailResult(r)) continue;
      expect(r.passed).toBeNull();
      expect(r.reason).toBe('country_required');
      expect(r.subtest).toBe('writing');
      expect(r.supportedCountries.length).toBeGreaterThan(0);
    }
  });

  it('returns country_unsupported for unknown countries', () => {
    const r = gradeWriting(400, 'Germany');
    expect(isWritingPassFailResult(r)).toBe(false);
    if (isWritingPassFailResult(r)) return;
    expect(r.passed).toBeNull();
    expect(r.reason).toBe('country_unsupported');
    expect(r.providedCountry).toBe('Germany');
  });
});

describe('OET scoring — Speaking (universal, country-independent)', () => {
  it('isSpeakingPass: 350 passes, 349 fails', () => {
    expect(isSpeakingPass(350)).toBe(true);
    expect(isSpeakingPass(349)).toBe(false);
    expect(isSpeakingPass(500)).toBe(true);
  });

  it('gradeSpeaking produces a Grade B pass at 350 regardless of country context', () => {
    const r = gradeSpeaking(350);
    expect(r.passed).toBe(true);
    expect(r.requiredScaled).toBe(350);
    expect(r.requiredGrade).toBe('B');
    expect(r.grade).toBe('B');
    expect(r.subtest).toBe('speaking');
  });

  it('gradeSpeaking fails 349', () => {
    expect(gradeSpeaking(349).passed).toBe(false);
  });
});

describe('OET scoring — formatters', () => {
  it('formatRawLrScore formats correctly', () => {
    expect(formatRawLrScore(35)).toBe('35/42');
    expect(formatRawLrScore(0)).toBe('0/42');
    expect(formatRawLrScore(42)).toBe('42/42');
  });

  it('formatScaledScore formats correctly', () => {
    expect(formatScaledScore(350)).toBe('350/500');
    expect(formatScaledScore(0)).toBe('0/500');
    expect(formatScaledScore(500)).toBe('500/500');
  });

  it('formatListeningReadingDisplay shows raw, scaled, and grade together', () => {
    const out = formatListeningReadingDisplay(30);
    expect(out).toContain('30/42');
    expect(out).toContain('350/500');
    expect(out).toContain('Grade B');
  });

  it('formatListeningReadingDisplay honours 30/42≡350/500 exactly', () => {
    expect(formatListeningReadingDisplay(30)).toBe('30/42 \u2022 350/500 \u2022 Grade B');
  });
});
