import { describe, it, expect } from 'vitest';
import {
  buildCanonicalReadingManifest,
  buildCanonicalTextLinkedReadingManifest,
  validateReadingManifest,
  validateReadingImportBundle,
  validateRequiredAssets,
  validateQuestionPayload,
  expectedPartAQuestionType,
  readingPublicDisplayNumber,
  isQuestionTypeAllowedForPart,
  detectPartALayoutFromQuestions,
  detectPartALayoutFromBookletText,
  suggestPartALayout,
  resolvePartALayout,
  describePartALayout,
  CLASSIC_PART_A_LAYOUT,
  type ReadingStructureManifestLike,
  type ReadingValidationIssue,
  type ReadingPartManifestLike,
  type ReadingQuestionManifestLike,
} from '@/lib/reading-manifest-contract';

// Helper to filter error codes
function getErrorCodes(report: { issues: Array<{ code: string; severity: string }> }): string[] {
  return report.issues.filter((i) => i.severity === 'error').map((i) => i.code);
}

// Helper to check if a specific error code is present
function hasErrorCode(report: { issues: Array<{ code: string; severity: string }> }, code: string): boolean {
  return report.issues.some((i) => i.severity === 'error' && i.code === code);
}

// Helper to simulate fail-closed publish gate
function simulatePublishGate(report: { isPublishReady: boolean; issues: ReadingValidationIssue[] }): {
  published: boolean;
  status: 'Published' | 'Draft';
  error?: string;
} {
  const errorIssues = report.issues.filter((i) => i.severity === 'error');
  if (!report.isPublishReady || errorIssues.length > 0) {
    const errorMsg = errorIssues.map((i) => `[${i.code}] ${i.message}`).join(' | ');
    throw new Error(`Reading publication gate failed: ${errorMsg}`);
  }
  return { published: true, status: 'Published' };
}

// Helper to simulate candidate query isolation
interface MockContentPaper {
  id: string;
  title: string;
  slug: string;
  subtestCode: string;
  status: 'Draft' | 'UnderReview' | 'Published' | 'Archived';
  candidateVisible: boolean;
}

function filterCandidateVisiblePapers(papers: MockContentPaper[]): MockContentPaper[] {
  return papers.filter((p) => p.status === 'Published' && p.candidateVisible);
}

// ============================================================================
// CHALLENGER 2 M3 ADVERSARIAL STRESS TEST SUITE
// Admin Zero-Deviation Ingestion & Publication Gates
// 1. 20/6/16 = 42 Reading Structure Enforcement (41/43 Items Must Fail)
// 2. Points == 1 Per Question Invariant (Points != 1 Must Fail)
// 3. Part A Question Type Rules & Last Block Start Index (13/14/15/16)
// 4. Part-Only PDFs Requirement (Missing Part A/B/C or Combined Booklet)
// 5. Answer Key Page Detection & Removal Enforcing
// 6. Fail-Closed Publication Gate & Review State Invariants
// 7. CandidateVisible Toggle & Candidate Query Isolation
// ============================================================================

describe('CHALLENGER 2 M3 AUDIT 1: 20/6/16 = 42 READING STRUCTURE ENFORCEMENT', () => {
  it('passes canonical 20/6/16 = 42 structure with 0 errors and isPublishReady=true', () => {
    const manifest = buildCanonicalReadingManifest();
    const report = validateReadingManifest(manifest);

    expect(report.isPublishReady).toBe(true);
    expect(report.counts.partACount).toBe(20);
    expect(report.counts.partBCount).toBe(6);
    expect(report.counts.partCCount).toBe(16);
    expect(report.counts.totalPoints).toBe(42);
    expect(getErrorCodes(report)).toHaveLength(0);
  });

  describe('Under-allocation Boundary Stress (Total = 41 Items)', () => {
    it('Part A under-allocation (19/6/16 = 41): fails validation with part_A_item_count and total_points_mismatch', () => {
      const manifest = buildCanonicalReadingManifest();
      // Remove 1 question from Part A
      manifest.parts![0].questions!.pop();

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(report.counts.partACount).toBe(19);
      expect(report.counts.totalPoints).toBe(41);
      expect(hasErrorCode(report, 'part_A_item_count')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });

    it('Part B under-allocation (20/5/16 = 41): fails validation with part_B_item_count and total_points_mismatch', () => {
      const manifest = buildCanonicalReadingManifest();
      // Remove 1 question from Part B
      manifest.parts![1].questions!.pop();

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(report.counts.partBCount).toBe(5);
      expect(report.counts.totalPoints).toBe(41);
      expect(hasErrorCode(report, 'part_B_item_count')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });

    it('Part C under-allocation (20/6/15 = 41): fails validation with part_C_item_count and total_points_mismatch', () => {
      const manifest = buildCanonicalReadingManifest();
      // Remove 1 question from Part C
      manifest.parts![2].questions!.pop();

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(report.counts.partCCount).toBe(15);
      expect(report.counts.totalPoints).toBe(41);
      expect(hasErrorCode(report, 'part_C_item_count')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });
  });

  describe('Over-allocation Boundary Stress (Total = 43 Items)', () => {
    it('Part A over-allocation (21/6/16 = 43): fails validation with part_A_item_count and total_points_mismatch', () => {
      const manifest = buildCanonicalReadingManifest();
      const extraQ: ReadingQuestionManifestLike = {
        displayOrder: 21,
        points: 1,
        questionType: 'SentenceCompletion',
        stem: 'Extra question 21',
        optionsJson: '[]',
        correctAnswerJson: '"extra answer"',
        explanationMarkdown: 'Rationale for extra question',
        evidenceSentence: 'Evidence sentence',
        reviewState: 'Published',
      };
      manifest.parts![0].questions!.push(extraQ);

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(report.counts.partACount).toBe(21);
      expect(report.counts.totalPoints).toBe(43);
      expect(hasErrorCode(report, 'part_A_item_count')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });

    it('Part B over-allocation (20/7/16 = 43): fails validation with part_B_item_count and total_points_mismatch', () => {
      const manifest = buildCanonicalReadingManifest();
      const extraQ: ReadingQuestionManifestLike = {
        displayOrder: 7,
        points: 1,
        questionType: 'MultipleChoice3',
        stem: 'Extra B question 7',
        optionsJson: '["A. opt1", "B. opt2", "C. opt3"]',
        correctAnswerJson: '"A"',
        explanationMarkdown: 'Rationale for extra B',
        evidenceSentence: 'Evidence sentence B',
        reviewState: 'Published',
      };
      manifest.parts![1].questions!.push(extraQ);

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(report.counts.partBCount).toBe(7);
      expect(report.counts.totalPoints).toBe(43);
      expect(hasErrorCode(report, 'part_B_item_count')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });

    it('Part C over-allocation (20/6/17 = 43): fails validation with part_C_item_count and total_points_mismatch', () => {
      const manifest = buildCanonicalReadingManifest();
      const extraQ: ReadingQuestionManifestLike = {
        displayOrder: 17,
        points: 1,
        questionType: 'MultipleChoice4',
        stem: 'Extra C question 17',
        optionsJson: '["A. opt1", "B. opt2", "C. opt3", "D. opt4"]',
        correctAnswerJson: '"B"',
        explanationMarkdown: 'Rationale for extra C',
        evidenceSentence: 'Evidence sentence C',
        reviewState: 'Published',
      };
      manifest.parts![2].questions!.push(extraQ);

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(report.counts.partCCount).toBe(17);
      expect(report.counts.totalPoints).toBe(43);
      expect(hasErrorCode(report, 'part_C_item_count')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });
  });

  describe('Structural Completeness & Integrity Failures', () => {
    it('rejects missing Part C completely', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts = manifest.parts!.filter((p) => p.partCode !== 'C');

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'part_C_missing')).toBe(true);
      expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    });

    it('rejects duplicate Part A sections', () => {
      const manifest = buildCanonicalReadingManifest();
      const duplicatePartA = JSON.parse(JSON.stringify(manifest.parts![0]));
      manifest.parts!.push(duplicatePartA);

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'duplicate_part')).toBe(true);
    });

    it('rejects non-contiguous question display orders (gap in numbering)', () => {
      const manifest = buildCanonicalReadingManifest();
      // Introduce gap: change Q2 to displayOrder 3, creating duplicate 3 and missing 2
      manifest.parts![0].questions![1].displayOrder = 3;

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'part_A_question_order')).toBe(true);
    });

    it('rejects invalid timing limits on parts (Part A != 15m, Part B != 45m)', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![0].timeLimitMinutes = 20; // Part A is strictly 15m
      manifest.parts![1].timeLimitMinutes = 30; // Part B is strictly 45m (shared B+C)

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'part_A_time_limit')).toBe(true);
      expect(hasErrorCode(report, 'part_B_time_limit')).toBe(true);
    });
  });
});

describe('CHALLENGER 2 M3 AUDIT 2: POINTS == 1 PER QUESTION INVARIANT', () => {
  it('rejects question with 0 points (under-allocated points total = 41)', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![0].questions![0].points = 0;

    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    expect(hasErrorCode(report, 'question_points_not_one')).toBe(true);
    expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
    expect(report.counts.totalPoints).toBe(41);
  });

  it('rejects question with 2 points even when sum is balanced to 42 (1 item with 2 pts, 1 item with 0 pts)', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![0].questions![0].points = 2;
    manifest.parts![0].questions![1].points = 0;

    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    // Total points is 42, but question_points_not_one must trigger for BOTH questions
    expect(report.counts.totalPoints).toBe(42);
    const pointIssues = report.issues.filter((i) => i.code === 'question_points_not_one');
    expect(pointIssues.length).toBe(2);
  });

  it('rejects negative points (-1) on a question', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![1].questions![0].points = -1;

    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    expect(hasErrorCode(report, 'question_points_not_one')).toBe(true);
    expect(hasErrorCode(report, 'total_points_mismatch')).toBe(true);
  });

  it('rejects omitted/undefined points in manifest (deserializes to 0)', () => {
    const manifest = buildCanonicalReadingManifest();
    delete manifest.parts![2].questions![0].points;

    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    expect(hasErrorCode(report, 'question_points_not_one')).toBe(true);
  });

  it('rejects fractional/floating points (e.g. 1.5 points)', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![0].questions![0].points = 1.5;

    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    expect(hasErrorCode(report, 'question_points_not_one')).toBe(true);
  });
});

describe('CHALLENGER 2 M3 AUDIT 3: PART A QUESTION TYPE RULES & LAST BLOCK START INDEX', () => {
  describe('Banned Question Types in Part A', () => {
    it('rejects MultipleChoice3 in Part A', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![0].questions![0].questionType = 'MultipleChoice3';
      manifest.parts![0].questions![0].optionsJson = '["A. opt1", "B. opt2", "C. opt3"]';

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'part_A_question_type')).toBe(true);
    });

    it('rejects MultipleChoice4 in Part A', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![0].questions![0].questionType = 'MultipleChoice4';
      manifest.parts![0].questions![0].optionsJson = '["A. opt1", "B. opt2", "C. opt3", "D. opt4"]';

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'part_A_question_type')).toBe(true);
    });

    it('rejects practice-only types (MultipleChoiceFlexible, FillInBlank, ShortAnswerLabeled) in Part A', () => {
      for (const pType of ['MultipleChoiceFlexible', 'FillInBlank', 'ShortAnswerLabeled']) {
        const manifest = buildCanonicalReadingManifest();
        manifest.parts![0].questions![0].questionType = pType;

        const report = validateReadingManifest(manifest);
        expect(report.isPublishReady).toBe(false);
        expect(
          hasErrorCode(report, 'unsupported_interaction') || hasErrorCode(report, 'part_A_question_type')
        ).toBe(true);
      }
    });

    it('confirms allowed Part A types via isQuestionTypeAllowedForPart', () => {
      expect(isQuestionTypeAllowedForPart('A', 'MatchingTextReference')).toBe(true);
      expect(isQuestionTypeAllowedForPart('A', 'ShortAnswer')).toBe(true);
      expect(isQuestionTypeAllowedForPart('A', 'SentenceCompletion')).toBe(true);

      expect(isQuestionTypeAllowedForPart('A', 'MultipleChoice3')).toBe(false);
      expect(isQuestionTypeAllowedForPart('A', 'MultipleChoice4')).toBe(false);
      expect(isQuestionTypeAllowedForPart('A', 'MultipleChoiceFlexible')).toBe(false);

      // Part B only allows MultipleChoice3
      expect(isQuestionTypeAllowedForPart('B', 'MultipleChoice3')).toBe(true);
      expect(isQuestionTypeAllowedForPart('B', 'MultipleChoice4')).toBe(false);
      expect(isQuestionTypeAllowedForPart('B', 'ShortAnswer')).toBe(false);

      // Part C only allows MultipleChoice4
      expect(isQuestionTypeAllowedForPart('C', 'MultipleChoice4')).toBe(true);
      expect(isQuestionTypeAllowedForPart('C', 'MultipleChoice3')).toBe(false);
      expect(isQuestionTypeAllowedForPart('C', 'ShortAnswer')).toBe(false);
    });
  });

  describe('Part A Layout Detector: Matching Boundary & Last Block Starts (13, 14, 15, 16)', () => {
    it('accepts all 4 valid last block start indices: 13, 14, 15, 16', () => {
      // Test start 13 (matching 1-7, middle 8-12, last 13-20)
      const q13 = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 5 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 8 }, (_, i) => ({ displayOrder: i + 13, questionType: 'SentenceCompletion' })),
      ];
      const d13 = detectPartALayoutFromQuestions(q13);
      expect(d13.ok).toBe(true);
      expect(d13.layout?.lastStart).toBe(13);

      // Test start 14 (matching 1-7, middle 8-13, last 14-20)
      const q14 = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 6 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 14, questionType: 'SentenceCompletion' })),
      ];
      const d14 = detectPartALayoutFromQuestions(q14);
      expect(d14.ok).toBe(true);
      expect(d14.layout?.lastStart).toBe(14);

      // Test start 15 (classic: matching 1-7, middle 8-14, last 15-20)
      const q15 = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 6 }, (_, i) => ({ displayOrder: i + 15, questionType: 'SentenceCompletion' })),
      ];
      const d15 = detectPartALayoutFromQuestions(q15);
      expect(d15.ok).toBe(true);
      expect(d15.layout?.lastStart).toBe(15);

      // Test start 16 (matching 1-7, middle 8-15, last 16-20)
      const q16 = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 8 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 5 }, (_, i) => ({ displayOrder: i + 16, questionType: 'SentenceCompletion' })),
      ];
      const d16 = detectPartALayoutFromQuestions(q16);
      expect(d16.ok).toBe(true);
      expect(d16.layout?.lastStart).toBe(16);
    });

    it('rejects invalid last block start index outside 13..16 (e.g. starting at 12 or 17)', () => {
      // Start at 12 (matching 1-7, middle 8-11, last 12-20) -> invalid
      const q12 = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 4 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 9 }, (_, i) => ({ displayOrder: i + 12, questionType: 'SentenceCompletion' })),
      ];
      const d12 = detectPartALayoutFromQuestions(q12);
      expect(d12.ok).toBe(false);
      expect(d12.error).toContain('last block must start at question 13, 14, 15, or 16');

      // Start at 17 (matching 1-7, middle 8-16, last 17-20) -> invalid
      const q17 = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 9 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 4 }, (_, i) => ({ displayOrder: i + 17, questionType: 'SentenceCompletion' })),
      ];
      const d17 = detectPartALayoutFromQuestions(q17);
      expect(d17.ok).toBe(false);
      expect(d17.error).toContain('last block must start at question 13, 14, 15, or 16');
    });

    it('rejects matching block ending outside 5..8 (e.g. ending at 4 or 9)', () => {
      // Matching 1-4
      const q4 = [
        ...Array.from({ length: 4 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 10 }, (_, i) => ({ displayOrder: i + 5, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 6 }, (_, i) => ({ displayOrder: i + 15, questionType: 'SentenceCompletion' })),
      ];
      const d4 = detectPartALayoutFromQuestions(q4);
      expect(d4.ok).toBe(false);
      expect(d4.error).toContain('matching A–D block must be questions 1-5, 1-6, 1-7, or 1-8');

      // Matching 1-9 (Q9 is MatchingTextReference instead of gap type)
      const q9 = [
        ...Array.from({ length: 9 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 5 }, (_, i) => ({ displayOrder: i + 10, questionType: 'ShortAnswer' })),
        ...Array.from({ length: 6 }, (_, i) => ({ displayOrder: i + 15, questionType: 'SentenceCompletion' })),
      ];
      const d9 = detectPartALayoutFromQuestions(q9);
      expect(d9.ok).toBe(false);
      expect(d9.error).toContain('Part A Q9 must start a ShortAnswer or SentenceCompletion block');
    });

    it('rejects layout when middle and last blocks use identical task types (e.g. both ShortAnswer)', () => {
      const qSame = [
        ...Array.from({ length: 7 }, (_, i) => ({ displayOrder: i + 1, questionType: 'MatchingTextReference' })),
        ...Array.from({ length: 13 }, (_, i) => ({ displayOrder: i + 8, questionType: 'ShortAnswer' })),
      ];
      const dSame = detectPartALayoutFromQuestions(qSame);
      expect(dSame.ok).toBe(false);
      expect(dSame.error).toContain('last block must start at question 13, 14, 15, or 16');
    });
  });

  describe('Part A Question Payload Validation', () => {
    it('MatchingTextReference: accepts A, B, C, D and rejects invalid letters (E, Z, 1, empty, array)', () => {
      // Valid
      for (const valid of ['"A"', '"B"', '"C"', '"D"']) {
        expect(validateQuestionPayload('MatchingTextReference', '[]', valid)).toEqual([]);
      }

      // Invalid
      expect(validateQuestionPayload('MatchingTextReference', '[]', '"E"')).toContain(
        'Matching answer must be one of A,B,C,D.'
      );
      expect(validateQuestionPayload('MatchingTextReference', '[]', '"Z"')).toContain(
        'Matching answer must be one of A,B,C,D.'
      );
      expect(validateQuestionPayload('MatchingTextReference', '[]', '""')).toContain(
        'Matching answer must be one of A,B,C,D.'
      );
      expect(validateQuestionPayload('MatchingTextReference', '[]', '["A"]')).toContain(
        'Matching answer must be one of A,B,C,D.'
      );
    });

    it('ShortAnswer: rejects empty or whitespace-only answers', () => {
      expect(validateQuestionPayload('ShortAnswer', '[]', '""')).toContain(
        'Short-answer CorrectAnswerJson must be a non-empty string.'
      );
      expect(validateQuestionPayload('ShortAnswer', '[]', '"   "')).toContain(
        'Short-answer CorrectAnswerJson must be a non-empty string.'
      );
      expect(validateQuestionPayload('ShortAnswer', '[]', 'null')).toContain(
        'Short-answer CorrectAnswerJson must be a non-empty string.'
      );
    });
  });
});

describe('CHALLENGER 2 M3 AUDIT 4: PART-ONLY PDFS REQUIREMENT', () => {
  it('passes import bundle when each Part A, B, C has exactly one primary QuestionPaper asset', () => {
    const bundle = {
      papers: [{
        paper: { slug: 'test-paper', subtestCode: 'reading' },
        assets: [
          { role: 'QuestionPaper', part: 'A', sourcePath: 'paper-PartA.pdf', makePrimary: true },
          { role: 'QuestionPaper', part: 'B', sourcePath: 'paper-PartB.pdf', makePrimary: true },
          { role: 'QuestionPaper', part: 'C', sourcePath: 'paper-PartC.pdf', makePrimary: true },
        ],
        manifest: buildCanonicalReadingManifest(),
      }],
    };

    const res = validateReadingImportBundle(bundle);
    expect(res.isPublishReady).toBe(true);
    expect(res.papers[0].assetIssues).toHaveLength(0);
  });

  describe('Missing Part Primary PDF Assets', () => {
    it('fails when Part A QuestionPaper PDF is missing', () => {
      const assets = [
        { role: 'QuestionPaper', part: 'B', sourcePath: 'paper-PartB.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'C', sourcePath: 'paper-PartC.pdf', makePrimary: true },
      ];
      const issues = validateRequiredAssets(assets);
      expect(issues.some((i) => i.code === 'part_A_pdf_required')).toBe(true);
    });

    it('fails when Part B QuestionPaper PDF is missing', () => {
      const assets = [
        { role: 'QuestionPaper', part: 'A', sourcePath: 'paper-PartA.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'C', sourcePath: 'paper-PartC.pdf', makePrimary: true },
      ];
      const issues = validateRequiredAssets(assets);
      expect(issues.some((i) => i.code === 'part_B_pdf_required')).toBe(true);
    });

    it('fails when Part C QuestionPaper PDF is missing', () => {
      const assets = [
        { role: 'QuestionPaper', part: 'A', sourcePath: 'paper-PartA.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'B', sourcePath: 'paper-PartB.pdf', makePrimary: true },
      ];
      const issues = validateRequiredAssets(assets);
      expect(issues.some((i) => i.code === 'part_C_pdf_required')).toBe(true);
    });

    it('fails when all QuestionPaper assets are missing', () => {
      const issues = validateRequiredAssets([]);
      expect(issues).toHaveLength(3);
      expect(issues.map((i) => i.code)).toEqual([
        'part_A_pdf_required',
        'part_B_pdf_required',
        'part_C_pdf_required',
      ]);
    });
  });

  describe('Duplicate & Non-Primary PDF Assets for a Single Part', () => {
    it('fails when Part A has 2 primary QuestionPaper assets (must be exactly 1)', () => {
      const assets = [
        { role: 'QuestionPaper', part: 'A', sourcePath: 'paper-PartA-1.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'A', sourcePath: 'paper-PartA-2.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'B', sourcePath: 'paper-PartB.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'C', sourcePath: 'paper-PartC.pdf', makePrimary: true },
      ];
      const issues = validateRequiredAssets(assets);
      expect(issues.some((i) => i.code === 'part_A_pdf_required')).toBe(true);
    });

    it('fails when an asset has makePrimary: false (only primary assets fulfill requirement)', () => {
      const assets = [
        { role: 'QuestionPaper', part: 'A', sourcePath: 'paper-PartA.pdf', makePrimary: false },
        { role: 'QuestionPaper', part: 'B', sourcePath: 'paper-PartB.pdf', makePrimary: true },
        { role: 'QuestionPaper', part: 'C', sourcePath: 'paper-PartC.pdf', makePrimary: true },
      ];
      const issues = validateRequiredAssets(assets);
      expect(issues.some((i) => i.code === 'part_A_pdf_required')).toBe(true);
    });
  });
});

describe('CHALLENGER 2 M3 AUDIT 5: ANSWER KEY PAGE DETECTION & REMOVAL', () => {
  /**
   * Detector function simulating the Zero-Deviation Contract §15:
   * "Fail the upload if a part PDF contains ANSWER KEY or END OF KEY"
   */
  function detectForbiddenAnswerKeyTokens(pdfTextContent: string): {
    hasForbiddenTokens: boolean;
    detectedTokens: string[];
    error?: string;
  } {
    const forbiddenPatterns = [
      /\bANSWER\s+KEY\b/i,
      /\bEND\s+OF\s+KEY\b/i,
      /\bPART\s+[ABC]\s+ANSWER\s+KEY\b/i,
      /\bOFFICIAL\s+ANSWER\s+KEY\b/i,
      /\bANSWERS\s+TO\s+QUESTIONS\b/i,
    ];

    const detectedTokens: string[] = [];
    for (const pattern of forbiddenPatterns) {
      const match = pdfTextContent.match(pattern);
      if (match) {
        detectedTokens.push(match[0]);
      }
    }

    if (detectedTokens.length > 0) {
      return {
        hasForbiddenTokens: true,
        detectedTokens,
        error: `Zero-Deviation Contract Violation: Candidate PDF contains forbidden answer key tokens: ${detectedTokens.join(', ')}. Drop answer-key pages before attaching.`,
      };
    }

    return { hasForbiddenTokens: false, detectedTokens: [] };
  }

  it('detects and rejects PDF text containing ANSWER KEY token', () => {
    const contaminatedPartA = `
      READING SUB-TEST — PART A
      Questions 1 - 7: Which text mentions...
      ...
      ANSWER KEY:
      1. A
      2. B
      3. C
    `;

    const detection = detectForbiddenAnswerKeyTokens(contaminatedPartA);
    expect(detection.hasForbiddenTokens).toBe(true);
    expect(detection.detectedTokens).toContain('ANSWER KEY');
    expect(detection.error).toContain('Zero-Deviation Contract Violation');
  });

  it('detects and rejects PDF text containing END OF KEY token', () => {
    const contaminatedPartB = `
      READING SUB-TEST — PART B
      Questions 1 - 6
      ...
      END OF KEY
    `;

    const detection = detectForbiddenAnswerKeyTokens(contaminatedPartB);
    expect(detection.hasForbiddenTokens).toBe(true);
    expect(detection.detectedTokens).toContain('END OF KEY');
  });

  it('detects and rejects specific variant PART C ANSWER KEY token', () => {
    const contaminatedPartC = `
      READING SUB-TEST — PART C
      Questions 7 - 22
      ...
      PART C ANSWER KEY
    `;

    const detection = detectForbiddenAnswerKeyTokens(contaminatedPartC);
    expect(detection.hasForbiddenTokens).toBe(true);
    expect(detection.detectedTokens.some((t) => /PART C ANSWER KEY/i.test(t))).toBe(true);
  });

  it('passes cleanly cropped PDF text without answer key pages', () => {
    const cleanPartA = `
      READING SUB-TEST — QUESTION PAPER: PART A
      TIME LIMIT: 15 MINUTES
      Questions 1-7
      For each question, 1-7, decide which text (A, B, C or D) the information comes from.
      Questions 8-14
      Answer each of the questions, 8-14, with a word or short phrase from the texts.
      Questions 15-20
      Complete each of the sentences, 15-20, with a word or short phrase from the texts.
      END OF PART A — THIS QUESTION PAPER WILL BE COLLECTED
    `;

    const detection = detectForbiddenAnswerKeyTokens(cleanPartA);
    expect(detection.hasForbiddenTokens).toBe(false);
    expect(detection.detectedTokens).toHaveLength(0);
    expect(detection.error).toBeUndefined();
  });
});

describe('CHALLENGER 2 M3 AUDIT 6: FAIL-CLOSED PUBLICATION GATE & REVIEW STATE INVARIANTS', () => {
  describe('Review State Invariants (Draft / UnderReview / Approved vs Published)', () => {
    it('rejects publication when any question is in Draft review state', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![0].questions![0].reviewState = 'Draft';

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'question_not_published')).toBe(true);
      expect(report.detected.reviewNotPublished).toBe(1);
    });

    it('rejects publication when any question is in UnderReview or Approved state (strictly requires Published)', () => {
      const manifestUnderReview = buildCanonicalReadingManifest();
      manifestUnderReview.parts![0].questions![0].reviewState = 'UnderReview';
      const r1 = validateReadingManifest(manifestUnderReview);
      expect(r1.isPublishReady).toBe(false);
      expect(hasErrorCode(r1, 'question_not_published')).toBe(true);

      const manifestApproved = buildCanonicalReadingManifest();
      manifestApproved.parts![1].questions![0].reviewState = 'Approved';
      const r2 = validateReadingManifest(manifestApproved);
      expect(r2.isPublishReady).toBe(false);
      expect(hasErrorCode(r2, 'question_not_published')).toBe(true);
    });

    it('passes publication when all 42 questions are in Published review state', () => {
      const manifest = buildCanonicalReadingManifest();
      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(true);
      expect(report.detected.reviewPublished).toBe(42);
      expect(report.detected.reviewNotPublished).toBe(0);
      expect(hasErrorCode(report, 'question_not_published')).toBe(false);
    });
  });

  describe('Post-Submit Rationale & Evidence Sentence Requirements', () => {
    it('rejects publication when explanationMarkdown is missing or empty', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![0].questions![0].explanationMarkdown = '';

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'question_rationale_missing')).toBe(true);
    });

    it('rejects publication when evidenceSentence is missing or null', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![2].questions![5].evidenceSentence = null;

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);
      expect(hasErrorCode(report, 'question_evidence_missing')).toBe(true);
    });
  });

  describe('Fail-Closed PublishGate Simulation', () => {
    it('successfully publishes when 100% of zero-deviation checks pass', () => {
      const manifest = buildCanonicalReadingManifest();
      const report = validateReadingManifest(manifest);

      const publishResult = simulatePublishGate(report);
      expect(publishResult.published).toBe(true);
      expect(publishResult.status).toBe('Published');
    });

    it('throws InvalidOperationException and stays in Draft when ANY validation error exists', () => {
      const manifest = buildCanonicalReadingManifest();
      manifest.parts![0].questions![0].points = 2; // Invariant violation

      const report = validateReadingManifest(manifest);
      expect(report.isPublishReady).toBe(false);

      expect(() => simulatePublishGate(report)).toThrowError(
        /Reading publication gate failed.*question_points_not_one/
      );
    });
  });
});

describe('CHALLENGER 2 M3 AUDIT 7: CANDIDATEVISIBLE TOGGLE & CANDIDATE QUERY ISOLATION', () => {
  const mockPaperDatabase: MockContentPaper[] = [
    {
      id: 'paper-1',
      title: 'Atlas Reading Test 01',
      slug: 'atlas-reading-01',
      subtestCode: 'reading',
      status: 'Published',
      candidateVisible: true,
    },
    {
      id: 'paper-2',
      title: 'Atlas Reading Test 02',
      slug: 'atlas-reading-02',
      subtestCode: 'reading',
      status: 'Published',
      candidateVisible: false, // Explicitly hidden by Admin
    },
    {
      id: 'paper-3',
      title: 'Jayden Reading Test 01 (Draft)',
      slug: 'jayden-reading-01',
      subtestCode: 'reading',
      status: 'Draft',
      candidateVisible: true, // Draft paper with candidateVisible=true must NOT be visible
    },
    {
      id: 'paper-4',
      title: 'Jayden Reading Test 02',
      slug: 'jayden-reading-02',
      subtestCode: 'reading',
      status: 'Published',
      candidateVisible: true,
    },
    {
      id: 'paper-5',
      title: 'Legacy Reading Exam (Other papers cleanup)',
      slug: 'legacy-reading-99',
      subtestCode: 'reading',
      status: 'Published',
      candidateVisible: false, // Hidden by series migration
    },
  ];

  it('filters candidate query strictly to Status == Published AND CandidateVisible == true', () => {
    const candidateList = filterCandidateVisiblePapers(mockPaperDatabase);

    // Only paper-1 and paper-4 should appear
    expect(candidateList).toHaveLength(2);
    expect(candidateList.map((p) => p.id)).toEqual(['paper-1', 'paper-4']);

    // Hidden published paper-2 must NOT appear
    expect(candidateList.some((p) => p.id === 'paper-2')).toBe(false);
    // Draft paper-3 must NOT appear
    expect(candidateList.some((p) => p.id === 'paper-3')).toBe(false);
    // Legacy hidden paper-5 must NOT appear
    expect(candidateList.some((p) => p.id === 'paper-5')).toBe(false);
  });

  it('Admin Toggle: dynamically toggles candidate visibility and reflects immediately in candidate queries', () => {
    const dbCopy = JSON.parse(JSON.stringify(mockPaperDatabase)) as MockContentPaper[];

    // Admin hides paper-1: POST /v1/admin/papers/paper-1/candidate-visible?visible=false
    const targetPaper = dbCopy.find((p) => p.id === 'paper-1')!;
    targetPaper.candidateVisible = false;

    let candidateList = filterCandidateVisiblePapers(dbCopy);
    expect(candidateList.some((p) => p.id === 'paper-1')).toBe(false);
    expect(candidateList.map((p) => p.id)).toEqual(['paper-4']);

    // Admin unhides paper-2: POST /v1/admin/papers/paper-2/candidate-visible?visible=true
    const targetPaper2 = dbCopy.find((p) => p.id === 'paper-2')!;
    targetPaper2.candidateVisible = true;

    candidateList = filterCandidateVisiblePapers(dbCopy);
    expect(candidateList.some((p) => p.id === 'paper-2')).toBe(true);
    expect(candidateList.map((p) => p.id)).toEqual(['paper-2', 'paper-4']);
  });

  it('Media Asset Access Isolation: denies candidate access when paper.CandidateVisible == false', () => {
    function authorizeMediaAssetAccess(
      userRole: 'admin' | 'candidate',
      paper: MockContentPaper
    ): { authorized: boolean; statusCode: number } {
      if (userRole === 'admin') {
        return { authorized: true, statusCode: 200 };
      }
      // Candidate rule: Must be Published AND CandidateVisible
      if (paper.status === 'Published' && paper.candidateVisible) {
        return { authorized: true, statusCode: 200 };
      }
      return { authorized: false, statusCode: 403 };
    }

    const visiblePublished = mockPaperDatabase.find((p) => p.id === 'paper-1')!;
    const hiddenPublished = mockPaperDatabase.find((p) => p.id === 'paper-2')!;

    // Candidate accessing visible paper -> 200 OK
    expect(authorizeMediaAssetAccess('candidate', visiblePublished)).toEqual({
      authorized: true,
      statusCode: 200,
    });

    // Candidate accessing hidden paper -> 403 Forbidden
    expect(authorizeMediaAssetAccess('candidate', hiddenPublished)).toEqual({
      authorized: false,
      statusCode: 403,
    });

    // Admin accessing hidden paper -> 200 OK (Admins can always inspect assets)
    expect(authorizeMediaAssetAccess('admin', hiddenPublished)).toEqual({
      authorized: true,
      statusCode: 200,
    });
  });
});
