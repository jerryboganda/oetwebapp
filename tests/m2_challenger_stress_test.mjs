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
} from '../lib/scoring.ts';

import { loadRulebook, listRulebooks } from '../lib/rulebook/loader.ts';

console.log('================================================================');
console.log('CHALLENGER 1 COMPREHENSIVE EMPIRICAL STRESS TEST HARNESS — M2');
console.log('================================================================\n');

let passCount = 0;
let failCount = 0;

function test(description, fn) {
  try {
    fn();
    console.log(`[PASS] ${description}`);
    passCount++;
  } catch (err) {
    console.error(`[FAIL] ${description}`);
    console.error(`       Error: ${err.message}`);
    console.error(err.stack);
    failCount++;
  }
}

// =============================================================================
// TEST SUITE 1: ALL 43 INTEGER RAW SCORE POINTS (0..42) FOR READING & LISTENING
// =============================================================================
console.log('--- TEST SUITE 1: OBJECTIVE SCORING (43 INTEGER POINTS 0..42) ---');

test('Verify OET Invariants: 0->0, 30->350, 42->500', () => {
  assert.equal(oetRawToScaled(0), 0, '0 raw must map to 0 scaled');
  assert.equal(oetRawToScaled(30), 350, '30 raw must map to 350 scaled (Grade B anchor)');
  assert.equal(oetRawToScaled(42), 500, '42 raw must map to 500 scaled (Grade A anchor)');
});

test('Verify exact piecewise mathematical formula for all 43 raw score points (0..42)', () => {
  const table = [];
  for (let r = 0; r <= 42; r++) {
    const scaled = oetRawToScaled(r);
    let expectedScaled;
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

    assert.equal(scaled, expectedScaled, `Mismatch at raw score ${r}: actual ${scaled} vs expected ${expectedScaled}`);
    table.push({ raw: r, scaled, expected: expectedScaled });
  }
  assert.equal(table.length, 43, 'Must test exactly 43 score points');
});

test('Verify strict monotonic non-decreasing progression across all 43 points', () => {
  let prevScaled = -1;
  for (let r = 0; r <= 42; r++) {
    const scaled = oetRawToScaled(r);
    assert.ok(scaled >= prevScaled, `Monotonicity violated at raw score ${r}: ${scaled} < ${prevScaled}`);
    prevScaled = scaled;
  }
});

test('Verify boundary transitions around 30/42 pass mark (29->338 fail, 30->350 pass, 31->363 pass)', () => {
  const s29 = oetRawToScaled(29);
  const s30 = oetRawToScaled(30);
  const s31 = oetRawToScaled(31);

  assert.equal(s29, 338, '29 raw must map to 338 scaled');
  assert.equal(s30, 350, '30 raw must map to 350 scaled');
  assert.equal(s31, 363, '31 raw must map to 363 scaled');

  assert.equal(isListeningReadingPassByRaw(29), false, '29 raw must fail');
  assert.equal(isListeningReadingPassByRaw(30), true, '30 raw must pass');
  assert.equal(isListeningReadingPassByRaw(31), true, '31 raw must pass');

  assert.equal(isListeningReadingPassByScaled(s29), false, '338 scaled must fail');
  assert.equal(isListeningReadingPassByScaled(s30), true, '350 scaled must pass');
  assert.equal(isListeningReadingPassByScaled(s31), true, '363 scaled must pass');
});

test('Verify grade letter assignments across all 43 points', () => {
  for (let r = 0; r <= 42; r++) {
    const scaled = oetRawToScaled(r);
    const grade = oetGradeFromScaled(scaled);

    if (scaled >= 450) {
      assert.equal(grade, 'A', `Raw ${r} (${scaled}) expected Grade A`);
    } else if (scaled >= 350) {
      assert.equal(grade, 'B', `Raw ${r} (${scaled}) expected Grade B`);
    } else if (scaled >= 300) {
      assert.equal(grade, 'C+', `Raw ${r} (${scaled}) expected Grade C+`);
    } else if (scaled >= 200) {
      assert.equal(grade, 'C', `Raw ${r} (${scaled}) expected Grade C`);
    } else if (scaled >= 100) {
      assert.equal(grade, 'D', `Raw ${r} (${scaled}) expected Grade D`);
    } else {
      assert.equal(grade, 'E', `Raw ${r} (${scaled}) expected Grade E`);
    }

    const rResult = gradeListeningReading('reading', r);
    const lResult = gradeListeningReading('listening', r);

    assert.equal(rResult.scaledScore, scaled);
    assert.equal(rResult.grade, grade);
    assert.equal(rResult.passed, r >= 30);

    assert.equal(lResult.scaledScore, scaled);
    assert.equal(lResult.grade, grade);
    assert.equal(lResult.passed, r >= 30);
  }
});

test('Verify edge case clamping and non-finite error handling', () => {
  assert.equal(oetRawToScaled(-10), 0);
  assert.equal(oetRawToScaled(-1), 0);
  assert.equal(oetRawToScaled(43), 500);
  assert.equal(oetRawToScaled(9999), 500);
  assert.throws(() => oetRawToScaled(NaN), RangeError);
  assert.throws(() => oetRawToScaled(Infinity), RangeError);
  assert.throws(() => oetRawToScaled(-Infinity), RangeError);
});

// =============================================================================
// TEST SUITE 2: WRITING RUBRIC & COUNTRY DESTINATION ACROSS 12 PROFESSIONS
// =============================================================================
console.log('\n--- TEST SUITE 2: WRITING RUBRIC & COUNTRY DESTINATION ACROSS 12 PROFESSIONS ---');

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
];

test('Verify rulebook registration and 172-rule baseline for all 12 professions', () => {
  const registered = listRulebooks()
    .filter((b) => b.kind === 'writing')
    .map((b) => b.profession);

  for (const p of ALL_12_PROFESSIONS) {
    assert.ok(registered.includes(p), `Writing rulebook missing for profession: ${p}`);
    const book = loadRulebook('writing', p);
    assert.equal(book.kind, 'writing');
    assert.equal(book.profession, p);
    assert.equal(book.rules.length, 172, `Profession ${p} must have canonical 172 rules baseline`);
    assert.ok(book.sections.length > 0, `Profession ${p} sections must not be empty`);
  }
});

test('Verify Writing rubric max points (38 raw) and Purpose 0-3 clamping', () => {
  assert.equal(WRITING_RAW_MAX, 38);
  assert.equal(WRITING_CRITERION_MAX_SCORES.purpose, 3);
  assert.equal(WRITING_CRITERION_MAX_SCORES.content, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.conciseness_clarity, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.genre_style, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.organisation_layout, 7);
  assert.equal(WRITING_CRITERION_MAX_SCORES.language, 7);

  // Inflated score test
  const inflated = {
    purpose: 10, // Clamped to 3
    content: 9, // Clamped to 7
    conciseness_clarity: 8, // Clamped to 7
    genre_style: 7,
    organisation_layout: 7,
    language: 7,
  };
  assert.equal(writingRawTotalFromCriterionScores(inflated), 38);
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

test('Verify Writing raw to scaled conversion monotonicity across all 39 points (0..38)', () => {
  let prev = -1;
  for (let r = 0; r <= 38; r++) {
    const s = writingRawToScaled(r);
    const expected = Math.round((r * 500) / 38);
    assert.equal(s, expected);
    assert.ok(s >= prev, `Writing monotonicity failure at raw ${r}`);
    prev = s;
  }
});

test('Verify Country Destination Matrix for Grade B countries (GB, IE, AU, NZ, CA)', () => {
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
    assert.ok(threshold !== null, `Threshold resolution failed for ${country}`);
    assert.equal(threshold.threshold, 350);
    assert.equal(threshold.grade, 'B');

    // 350 must pass
    const res350 = gradeWriting(350, country);
    assert.equal(res350.passed, true);
    assert.equal(res350.requiredScaled, 350);
    assert.equal(res350.requiredGrade, 'B');

    // 349 must fail
    const res349 = gradeWriting(349, country);
    assert.equal(res349.passed, false);
    assert.equal(res349.requiredScaled, 350);
    assert.equal(res349.requiredGrade, 'B');

    // 300 must fail
    const res300 = gradeWriting(300, country);
    assert.equal(res300.passed, false);
    assert.equal(res300.grade, 'C+');
  }
});

test('Verify Country Destination Matrix for Grade C+ countries (US, QA)', () => {
  const gradeCPlusList = [
    'US', 'USA', 'United States', 'United States of America', 'America',
    'QA', 'Qatar',
  ];

  for (const country of gradeCPlusList) {
    const threshold = getWritingPassThreshold(country);
    assert.ok(threshold !== null, `Threshold resolution failed for ${country}`);
    assert.equal(threshold.threshold, 300);
    assert.equal(threshold.grade, 'C+');

    // 300 must pass
    const res300 = gradeWriting(300, country);
    assert.equal(res300.passed, true);
    assert.equal(res300.requiredScaled, 300);
    assert.equal(res300.requiredGrade, 'C+');

    // 299 must fail
    const res299 = gradeWriting(299, country);
    assert.equal(res299.passed, false);
    assert.equal(res299.requiredScaled, 300);

    // 350 must pass
    const res350 = gradeWriting(350, country);
    assert.equal(res350.passed, true);
  }
});

test('Verify critical cross-country divergence: Same score in [300, 349] fails Grade B and passes Grade C+', () => {
  const testScores = [300, 310, 320, 330, 340, 349];
  for (const s of testScores) {
    const uk = gradeWriting(s, 'UK');
    const ie = gradeWriting(s, 'Ireland');
    const au = gradeWriting(s, 'AU');
    const nz = gradeWriting(s, 'NZ');
    const ca = gradeWriting(s, 'Canada');

    const us = gradeWriting(s, 'USA');
    const qa = gradeWriting(s, 'Qatar');

    assert.equal(uk.passed, false, `Score ${s} should FAIL for UK`);
    assert.equal(ie.passed, false, `Score ${s} should FAIL for IE`);
    assert.equal(au.passed, false, `Score ${s} should FAIL for AU`);
    assert.equal(nz.passed, false, `Score ${s} should FAIL for NZ`);
    assert.equal(ca.passed, false, `Score ${s} should FAIL for CA`);

    assert.equal(us.passed, true, `Score ${s} should PASS for US`);
    assert.equal(qa.passed, true, `Score ${s} should PASS for QA`);
  }
});

test('Verify missing/null/invalid countries fail closed with structured reasons', () => {
  const missing = [null, undefined, '', '   '];
  for (const c of missing) {
    const res = gradeWriting(400, c);
    assert.equal(res.passed, null);
    assert.equal(res.reason, 'country_required');
    assert.equal(res.subtest, 'writing');
  }

  const unsupported = ['FR', 'DE', 'India', 'Pakistan', 'Nigeria', 'Atlantis', '12345'];
  for (const c of unsupported) {
    const res = gradeWriting(400, c);
    assert.equal(res.passed, null);
    assert.equal(res.reason, 'country_unsupported');
    assert.equal(res.providedCountry, c);
    assert.equal(res.subtest, 'writing');
  }
});

test('Verify deriveWritingResultFromCriteria across arbitrary score vectors', () => {
  const vector1 = {
    purpose: 2,
    content: 5,
    conciseness_clarity: 5,
    genre_style: 5,
    organisation_layout: 5,
    language: 5,
  }; // 2 + 25 = 27 raw -> round(27 * 500 / 38) = round(355.26) = 355
  const res1 = deriveWritingResultFromCriteria(vector1, 'UK');
  assert.equal(res1.rawTotal, 27);
  assert.equal(res1.scaled, 355);
  assert.equal(res1.grade, 'B');
  assert.equal(res1.result.passed, true);

  const vector2 = {
    purpose: 2,
    content: 4,
    conciseness_clarity: 4,
    genre_style: 5,
    organisation_layout: 5,
    language: 5,
  }; // 2 + 23 = 25 raw -> round(25 * 500 / 38) = round(328.95) = 329
  const res2UK = deriveWritingResultFromCriteria(vector2, 'UK');
  const res2US = deriveWritingResultFromCriteria(vector2, 'USA');
  assert.equal(res2UK.rawTotal, 25);
  assert.equal(res2UK.scaled, 329);
  assert.equal(res2UK.grade, 'C+');
  assert.equal(res2UK.result.passed, false);
  assert.equal(res2US.result.passed, true);
});

// =============================================================================
// TEST SUITE 3: SPEAKING RUBRIC & UNIVERSAL PASS POLICY
// =============================================================================
console.log('\n--- TEST SUITE 3: SPEAKING RUBRIC & UNIVERSAL PASS POLICY ---');

test('Verify Speaking rubric max score (39 raw) and percentage anchors', () => {
  assert.equal(SPEAKING_RUBRIC_MAX, 39);

  // Exact anchor points: 0->0, 50->250, 70->350, 80->400, 90->450, 100->500
  assert.equal(speakingProjectedScaledFromPercentage(0), 0);
  assert.equal(speakingProjectedScaledFromPercentage(50), 250);
  assert.equal(speakingProjectedScaledFromPercentage(70), 350); // Canonical B pass anchor
  assert.equal(speakingProjectedScaledFromPercentage(80), 400);
  assert.equal(speakingProjectedScaledFromPercentage(90), 450);
  assert.equal(speakingProjectedScaledFromPercentage(100), 500);
});

test('Verify Speaking percentage projection monotonicity across all 101 points (0..100)', () => {
  let prev = -1;
  for (let p = 0; p <= 100; p++) {
    const s = speakingProjectedScaledFromPercentage(p);
    assert.ok(s >= prev, `Speaking percentage projection monotonicity failure at p=${p}`);
    prev = s;
  }
});

test('Verify Speaking full criterion vector calculations and readiness band mapping', () => {
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
  assert.equal(speakingProjectedScaled(perfect), 500);
  const bandPerfect = speakingProjectedBand(perfect);
  assert.equal(bandPerfect.passed, true);
  assert.equal(bandPerfect.grade, 'A');

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
  assert.equal(speakingProjectedScaled(zero), 0);
  const bandZero = speakingProjectedBand(zero);
  assert.equal(bandZero.passed, false);
  assert.equal(bandZero.grade, 'E');

  // Universal Speaking: 350 pass mark strictly applied regardless of country
  assert.equal(isSpeakingPass(350), true);
  assert.equal(isSpeakingPass(349), false);
  assert.equal(isSpeakingPass(300), false);

  const grade350 = gradeSpeaking(350);
  assert.equal(grade350.passed, true);
  assert.equal(grade350.requiredScaled, 350);
  assert.equal(grade350.grade, 'B');

  const grade349 = gradeSpeaking(349);
  assert.equal(grade349.passed, false);
  assert.equal(grade349.grade, 'C+');

  // Readiness bands
  assert.equal(speakingReadinessBandFromScaled(200), 'not_ready');
  assert.equal(speakingReadinessBandFromScaled(250), 'developing');
  assert.equal(speakingReadinessBandFromScaled(300), 'borderline');
  assert.equal(speakingReadinessBandFromScaled(350), 'exam_ready');
  assert.equal(speakingReadinessBandFromScaled(420), 'strong');
  assert.equal(speakingReadinessBandFromScaled(500), 'strong');

  assert.equal(speakingReadinessBandLabel('not_ready'), 'Not ready');
  assert.equal(speakingReadinessBandLabel('developing'), 'Developing');
  assert.equal(speakingReadinessBandLabel('borderline'), 'Borderline');
  assert.equal(speakingReadinessBandLabel('exam_ready'), 'Exam-ready');
  assert.equal(speakingReadinessBandLabel('strong'), 'Strong');
});

// =============================================================================
// SUMMARY
// =============================================================================
console.log('\n================================================================');
console.log(`TOTAL TESTS: ${passCount + failCount} | PASSED: ${passCount} | FAILED: ${failCount}`);
console.log('================================================================');

if (failCount > 0) {
  process.exit(1);
} else {
  process.exit(0);
}
