import assert from 'node:assert/strict';
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
} from '../lib/scoring.ts';

import {
  isMockReportStatementOfResultsReady,
  mockReportToStatementOfResults,
} from '../lib/adapters/oet-sor-adapter.ts';

console.log('================================================================');
console.log('CHALLENGER 1 EMPIRICAL ADVERSARIAL STRESS HARNESS — MILESTONE 2');
console.log('================================================================\n');

let passCount = 0;
let failCount = 0;

function check(description, fn) {
  try {
    fn();
    console.log(`[PASS] ${description}`);
    passCount++;
  } catch (err) {
    console.error(`[FAIL] ${description}`);
    console.error(`       Error: ${err.message}`);
    failCount++;
  }
}

// -----------------------------------------------------------------------------
// SUITE 1: OBJECTIVE SCORING [0, 42] DOMAIN & MONOTONICITY ORACLE
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 1: OBJECTIVE SCORING [0, 42] DOMAIN & PIECEWISE LINEARITY ---');

check('Exact invariant anchor: r=0 -> 0', () => {
  assert.equal(oetRawToScaled(0), 0);
  assert.equal(isListeningReadingPassByRaw(0), false);
  assert.equal(isListeningReadingPassByScaled(0), false);
  assert.equal(oetGradeFromScaled(0), 'E');
});

check('Exact invariant anchor: r=30 -> 350 (Grade B pass line)', () => {
  assert.equal(oetRawToScaled(30), 350);
  assert.equal(isListeningReadingPassByRaw(30), true);
  assert.equal(isListeningReadingPassByScaled(350), true);
  assert.equal(oetGradeFromScaled(350), 'B');
});

check('Exact boundary threshold: r=29 -> 338 (< 350, Grade C+, fail)', () => {
  const scaled = oetRawToScaled(29);
  assert.equal(scaled, Math.round((29 * 350) / 30));
  assert.equal(scaled, 338);
  assert.equal(scaled < 350, true);
  assert.equal(isListeningReadingPassByRaw(29), false);
  assert.equal(isListeningReadingPassByScaled(scaled), false);
  assert.equal(oetGradeFromScaled(scaled), 'C+');
});

check('Exact boundary threshold: r=31 -> 363 (> 350, Grade B, pass)', () => {
  const scaled = oetRawToScaled(31);
  assert.equal(scaled, 350 + Math.round((1 * 150) / 12));
  assert.equal(scaled, 363);
  assert.equal(scaled >= 350, true);
  assert.equal(isListeningReadingPassByRaw(31), true);
  assert.equal(isListeningReadingPassByScaled(scaled), true);
  assert.equal(oetGradeFromScaled(scaled), 'B');
});

check('Exact invariant anchor: r=42 -> 500 (Grade A max)', () => {
  assert.equal(oetRawToScaled(42), 500);
  assert.equal(isListeningReadingPassByRaw(42), true);
  assert.equal(isListeningReadingPassByScaled(500), true);
  assert.equal(oetGradeFromScaled(500), 'A');
});

check('Exhaustive piecewise formula verification across all r in [0, 42]', () => {
  for (let r = 0; r <= 42; r++) {
    const actual = oetRawToScaled(r);
    let expected;
    if (r === 0) expected = 0;
    else if (r === 30) expected = 350;
    else if (r === 42) expected = 500;
    else if (r < 30) expected = Math.round((r * 350) / 30);
    else expected = Math.round(350 + ((r - 30) * 150) / 12);

    assert.equal(actual, expected, `Mismatch at raw score ${r}: actual=${actual}, expected=${expected}`);
  }
});

check('Strict monotonicity check across all r in [0, 42]', () => {
  let prev = -1;
  for (let r = 0; r <= 42; r++) {
    const current = oetRawToScaled(r);
    assert.ok(current >= prev, `Monotonicity violated at r=${r}: current=${current} < prev=${prev}`);
    prev = current;
  }
});

check('Grade band distribution across all r in [0, 42]', () => {
  for (let r = 0; r <= 42; r++) {
    const scaled = oetRawToScaled(r);
    const grade = oetGradeFromScaled(scaled);
    if (scaled >= 450) assert.equal(grade, 'A');
    else if (scaled >= 350) assert.equal(grade, 'B');
    else if (scaled >= 300) assert.equal(grade, 'C+');
    else if (scaled >= 200) assert.equal(grade, 'C');
    else if (scaled >= 100) assert.equal(grade, 'D');
    else assert.equal(grade, 'E');

    const result = gradeListeningReading('reading', r);
    assert.equal(result.rawCorrect, r);
    assert.equal(result.rawMax, 42);
    assert.equal(result.scaledScore, scaled);
    assert.equal(result.grade, grade);
    assert.equal(result.passed, r >= 30);
  }
});

check('Clamping and extreme edge cases for objective scoring', () => {
  assert.equal(oetRawToScaled(-100), 0);
  assert.equal(oetRawToScaled(-1), 0);
  assert.equal(oetRawToScaled(43), 500);
  assert.equal(oetRawToScaled(1000), 500);
  assert.equal(oetRawToScaled(29.4), 338); // rounded to 29
  assert.equal(oetRawToScaled(29.6), 350); // rounded to 30
  assert.throws(() => oetRawToScaled(Number.NaN), RangeError);
  assert.throws(() => oetRawToScaled(Number.POSITIVE_INFINITY), RangeError);
});

// -----------------------------------------------------------------------------
// SUITE 2: WRITING RUBRIC & DESTINATION COUNTRY PERMUTATIONS
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 2: WRITING RUBRIC & DESTINATION COUNTRY PERMUTATIONS ---');

check('Writing criterion max scores & Purpose 0-3 clamp', () => {
  assert.equal(WRITING_RAW_MAX, 38);
  assert.equal(WRITING_CRITERION_MAX_SCORES.purpose, 3);
  assert.equal(WRITING_CRITERION_MAX_SCORES.content, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.conciseness_clarity, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.genre_style, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.organisation_layout, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.language, 7);

  // Purpose capped at 3 even if 7 is provided
  const overInflated = {
    purpose: 7, // must clamp to 3
    content: 7,
    conciseness_clarity: 7,
    genre_style: 7,
    organisation_layout: 7,
    language: 7,
  };
  assert.equal(writingRawTotalFromCriterionScores(overInflated), 38);

  const perfect = {
    purpose: 3,
    content: 7,
    conciseness_clarity: 7,
    genre_style: 7,
    organisation_layout: 7,
    language: 7,
  };
  assert.equal(writingRawTotalFromCriterionScores(perfect), 38);
  assert.equal(writingRawToScaled(38), 500);

  const zero = {
    purpose: 0,
    content: 0,
    conciseness_clarity: 0,
    genre_style: 0,
    organisation_layout: 0,
    language: 0,
  };
  assert.equal(writingRawTotalFromCriterionScores(zero), 0);
  assert.equal(writingRawToScaled(0), 0);
});

check('Writing raw [0, 38] to scaled [0, 500] monotonicity', () => {
  let prev = -1;
  for (let r = 0; r <= 38; r++) {
    const s = writingRawToScaled(r);
    const expected = Math.round((r * 500) / 38);
    assert.equal(s, expected);
    assert.ok(s >= prev, `Writing monotonicity failure at r=${r}`);
    prev = s;
  }
});

check('Destination Country Permutations: Grade B countries (GB, IE, AU, NZ, CA)', () => {
  const gradeBCountries = ['GB', 'IE', 'AU', 'NZ', 'CA', 'gb', 'uk', 'UNITED KINGDOM', 'ireland', 'australia', 'new zealand', 'canada', 'scotland', 'wales', 'england', 'northern ireland', 'gulf countries', 'other countries'];
  
  for (const country of gradeBCountries) {
    const threshold = getWritingPassThreshold(country);
    assert.ok(threshold !== null, `Failed to resolve country: ${country}`);
    assert.equal(threshold.threshold, 350);
    assert.equal(threshold.grade, 'B');

    // Test passing scaled score (e.g. 350)
    const passResult = gradeWriting(350, country);
    assert.equal(passResult.passed, true);
    assert.equal(passResult.requiredScaled, 350);
    assert.equal(passResult.requiredGrade, 'B');

    // Test failing scaled score (e.g. 349)
    const failResult = gradeWriting(349, country);
    assert.equal(failResult.passed, false);
    assert.equal(failResult.requiredScaled, 350);
    assert.equal(failResult.requiredGrade, 'B');

    // Test score of 300 (which passes US/QA but fails Grade B)
    const cPlusResult = gradeWriting(300, country);
    assert.equal(cPlusResult.passed, false);
    assert.equal(cPlusResult.grade, 'C+');
  }
});

check('Destination Country Permutations: Grade C+ countries (US, QA)', () => {
  const gradeCPlusCountries = ['US', 'QA', 'us', 'usa', 'united states', 'united states of america', 'america', 'qatar', 'QA'];
  
  for (const country of gradeCPlusCountries) {
    const threshold = getWritingPassThreshold(country);
    assert.ok(threshold !== null, `Failed to resolve country: ${country}`);
    assert.equal(threshold.threshold, 300);
    assert.equal(threshold.grade, 'C+');

    // Test passing scaled score (e.g. 300)
    const pass300 = gradeWriting(300, country);
    assert.equal(pass300.passed, true);
    assert.equal(pass300.requiredScaled, 300);
    assert.equal(pass300.requiredGrade, 'C+');

    // Test failing scaled score (e.g. 299)
    const fail299 = gradeWriting(299, country);
    assert.equal(fail299.passed, false);
    assert.equal(fail299.requiredScaled, 300);
    assert.equal(fail299.requiredGrade, 'C+');

    // Test high score (e.g. 450)
    const pass450 = gradeWriting(450, country);
    assert.equal(pass450.passed, true);
    assert.equal(pass450.grade, 'A');
  }
});

check('Destination Country Permutations: Missing / Null / Invalid countries fail closed', () => {
  const nullCases = [null, undefined, '', '   '];
  for (const c of nullCases) {
    const threshold = getWritingPassThreshold(c);
    assert.equal(threshold, null);
    const result = gradeWriting(400, c);
    assert.equal(result.passed, null);
    assert.equal(result.reason, 'country_required');
    assert.equal(result.subtest, 'writing');
  }

  const unsupportedCases = ['INVALID', 'FR', 'DE', 'JP', 'ES', 'IT', 'CH', 'XYZ'];
  for (const c of unsupportedCases) {
    const threshold = getWritingPassThreshold(c);
    assert.equal(threshold, null);
    const result = gradeWriting(400, c);
    assert.equal(result.passed, null);
    assert.equal(result.reason, 'country_unsupported');
    assert.equal(result.subtest, 'writing');
  }
});

// -----------------------------------------------------------------------------
// SUITE 3: SPEAKING RUBRIC & UNIVERSAL PASS POLICY
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 3: SPEAKING RUBRIC & UNIVERSAL PASS POLICY ---');

check('Speaking rubric max scores & canonical percentage anchors', () => {
  assert.equal(SPEAKING_RUBRIC_MAX, 39);

  // Exact anchor checks for percentage projection
  assert.equal(speakingProjectedScaledFromPercentage(0), 0);
  assert.equal(speakingProjectedScaledFromPercentage(50), 250);
  assert.equal(speakingProjectedScaledFromPercentage(70), 350); // Canonical B pass anchor
  assert.equal(speakingProjectedScaledFromPercentage(80), 400);
  assert.equal(speakingProjectedScaledFromPercentage(90), 450);
  assert.equal(speakingProjectedScaledFromPercentage(100), 500);

  // Monotonicity of speaking percentage projection
  let prev = -1;
  for (let p = 0; p <= 100; p++) {
    const s = speakingProjectedScaledFromPercentage(p);
    assert.ok(s >= prev, `Speaking percentage projection monotonicity failure at p=${p}`);
    prev = s;
  }
});

check('Speaking full criterion score projection & universal pass mark', () => {
  // Max score across all criteria
  const maxScore = {
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
  assert.equal(speakingProjectedScaled(maxScore), 500);
  const maxBand = speakingProjectedBand(maxScore);
  assert.equal(maxBand.passed, true);
  assert.equal(maxBand.grade, 'A');
  assert.equal(maxBand.requiredScaled, 350);

  // Zero score across all criteria
  const zeroScore = {
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
  assert.equal(speakingProjectedScaled(zeroScore), 0);
  const zeroBand = speakingProjectedBand(zeroScore);
  assert.equal(zeroBand.passed, false);
  assert.equal(zeroBand.grade, 'E');

  // Exact 70% threshold: 27.3 / 39 = 70% -> 350 (Grade B)
  // Let's test a score vector that yields exactly >= 70%
  // Linguistic 4*4.5=18, Clinical 5*2=10 -> Total 28 / 39 = 71.79% -> > 350
  const passScore = {
    intelligibility: 4,
    fluency: 5,
    appropriateness: 5,
    grammarExpression: 4,
    relationshipBuilding: 2,
    patientPerspective: 2,
    structure: 2,
    informationGathering: 2,
    informationGiving: 2,
  };
  const passScaled = speakingProjectedScaled(passScore);
  assert.ok(passScaled >= 350);
  assert.equal(isSpeakingPass(passScaled), true);

  // Universal Speaking: isSpeakingPass is strictly based on 350
  assert.equal(isSpeakingPass(350), true);
  assert.equal(isSpeakingPass(349), false);
  assert.equal(isSpeakingPass(300), false);
});

check('Speaking readiness bands', () => {
  assert.equal(speakingReadinessBandFromScaled(200), 'not_ready');
  assert.equal(speakingReadinessBandFromScaled(250), 'developing');
  assert.equal(speakingReadinessBandFromScaled(300), 'borderline');
  assert.equal(speakingReadinessBandFromScaled(350), 'exam_ready');
  assert.equal(speakingReadinessBandFromScaled(420), 'strong');
  assert.equal(speakingReadinessBandFromScaled(500), 'strong');
});

// -----------------------------------------------------------------------------
// SUITE 4: STATEMENT OF RESULTS ADAPTER FAIL-CLOSED INTEGRITY
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 4: STATEMENT OF RESULTS ADAPTER FAIL-CLOSED INTEGRITY ---');

const baseValidMockReport = {
  id: 'mock-report-001',
  date: '2026-08-15T10:00:00Z',
  subTests: [
    {
      id: 'listening',
      name: 'Listening',
      rawScore: '35/42',
      scaledScore: 380,
      score: '380',
      grade: 'B',
      reviewState: 'completed',
      scoreConversionTableVersionKey: 'tbl_v1_listening_active',
      scoreConversionPassed: true,
    },
    {
      id: 'reading',
      name: 'Reading',
      rawScore: '38/42',
      scaledScore: 420,
      score: '420',
      grade: 'B',
      reviewState: 'completed',
      scoreConversionTableVersionKey: 'tbl_v1_reading_active',
      scoreConversionPassed: true,
    },
    {
      id: 'writing',
      name: 'Writing',
      rawScore: '30/38',
      scaledScore: 390,
      score: '390',
      grade: 'B',
      reviewState: 'completed',
    },
    {
      id: 'speaking',
      name: 'Speaking',
      rawScore: '32/39',
      scaledScore: 410,
      score: '410',
      grade: 'B',
      reviewState: 'completed',
    },
  ],
};

check('Statement of Results ready when all 4 subtests are approved', () => {
  assert.equal(isMockReportStatementOfResultsReady(baseValidMockReport), true);
  const sor = mockReportToStatementOfResults({
    report: baseValidMockReport,
    candidate: { name: 'Dr. Sarah Smith', candidateNumber: 'OET-123-456' },
    profession: 'medicine',
    country: 'United Kingdom',
  });
  assert.equal(sor.candidate.name, 'Dr. Sarah Smith');
  assert.equal(sor.candidate.candidateNumber, 'OET-123-456');
  assert.equal(sor.scores.listening, 380);
  assert.equal(sor.scores.reading, 420);
  assert.equal(sor.scores.writing, 390);
  assert.equal(sor.scores.speaking, 410);
  assert.equal(sor.isPractice, true);
});

check('Fail closed: Missing speaking subtest', () => {
  const missingSpeaking = {
    ...baseValidMockReport,
    subTests: baseValidMockReport.subTests.filter((s) => s.id !== 'speaking'),
  };
  assert.equal(isMockReportStatementOfResultsReady(missingSpeaking), false);
});

check('Fail closed: Missing reading subtest', () => {
  const missingReading = {
    ...baseValidMockReport,
    subTests: baseValidMockReport.subTests.filter((s) => s.id !== 'reading'),
  };
  assert.equal(isMockReportStatementOfResultsReady(missingReading), false);
});

check('Fail closed: Incomplete subtest reviewState (in_progress / in_review)', () => {
  const incompleteState = {
    ...baseValidMockReport,
    subTests: baseValidMockReport.subTests.map((s) =>
      s.id === 'writing' ? { ...s, reviewState: 'in_progress' } : s
    ),
  };
  assert.equal(isMockReportStatementOfResultsReady(incompleteState), false);

  const pendingState = {
    ...baseValidMockReport,
    subTests: baseValidMockReport.subTests.map((s) =>
      s.id === 'writing' ? { ...s, state: 'in_review' } : s
    ),
  };
  assert.equal(isMockReportStatementOfResultsReady(pendingState), false);
});

check('Fail closed: Listening subtest missing approved score conversion table version key', () => {
  const unapprovedListening = {
    ...baseValidMockReport,
    subTests: baseValidMockReport.subTests.map((s) =>
      s.id === 'listening' ? { ...s, scoreConversionTableVersionKey: '' } : s
    ),
  };
  assert.equal(isMockReportStatementOfResultsReady(unapprovedListening), false);
  const sor = mockReportToStatementOfResults({ report: unapprovedListening });
  assert.equal(sor.scores.listening, 0); // Forces score to 0 when conversion is not approved
});

check('Fail closed: Reading subtest missing scoreConversionPassed boolean flag', () => {
  const unapprovedReading = {
    ...baseValidMockReport,
    subTests: baseValidMockReport.subTests.map((s) =>
      s.id === 'reading' ? { ...s, scoreConversionPassed: undefined } : s
    ),
  };
  assert.equal(isMockReportStatementOfResultsReady(unapprovedReading), false);
  const sor = mockReportToStatementOfResults({ report: unapprovedReading });
  assert.equal(sor.scores.reading, 0); // Forces score to 0
});

// -----------------------------------------------------------------------------
// SUMMARY
// -----------------------------------------------------------------------------
console.log('\n================================================================');
console.log(`TEST EXECUTION COMPLETE: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================');

if (failCount > 0) {
  process.exit(1);
} else {
  process.exit(0);
}
