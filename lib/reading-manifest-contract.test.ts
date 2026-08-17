import {
  buildCanonicalReadingManifest,
  buildCanonicalTextLinkedReadingManifest,
  expectedPartAQuestionType,
  readingPublicDisplayNumber,
  validateQuestionPayload,
  validateReadingImportBundle,
  validateReadingManifest,
} from '@/lib/reading-manifest-contract';

function errorCodes(report: { issues: Array<{ code: string; severity: string }> }): string[] {
  return report.issues.filter((issue) => issue.severity === 'error').map((issue) => issue.code);
}

describe('reading-manifest-contract', () => {
  it('accepts the official PDF-only 20/6/16 publish shape', () => {
    const report = validateReadingManifest(buildCanonicalReadingManifest());
    expect(report.isPublishReady).toBe(true);
    expect(report.counts).toEqual({
      partACount: 20,
      partBCount: 6,
      partCCount: 16,
      totalPoints: 42,
      textACount: 0,
      textBCount: 0,
      textCCount: 0,
    });
    expect(errorCodes(report)).toEqual([]);
    expect(report.importGaps.some((issue) => issue.code === 'evidence_not_imported')).toBe(true);
  });

  it('rejects omitted texts arrays because ImportManifestAsync requires them', () => {
    const manifest = buildCanonicalReadingManifest();
    for (const part of manifest.parts ?? []) {
      delete part.texts;
    }
    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    expect(errorCodes(report)).toEqual(expect.arrayContaining([
      'part_A_texts_missing',
      'part_B_texts_missing',
      'part_C_texts_missing',
    ]));
  });

  it('rejects B/C texts that are not linked to the official question counts', () => {
    const report = validateReadingManifest(buildCanonicalReadingManifest({
      B: {
        texts: [{ displayOrder: 1, title: 'Extract 1', source: 'fixture', bodyHtml: '<p>x</p>' }],
      },
    }));
    expect(errorCodes(report)).toEqual(expect.arrayContaining([
      'part_B_text_count',
      'part_B_questions_per_text',
    ]));
  });

  it('accepts a fully text-linked 4/6/2 paper', () => {
    const report = validateReadingManifest(buildCanonicalTextLinkedReadingManifest());
    expect(report.isPublishReady).toBe(true);
    expect(report.counts).toMatchObject({
      textACount: 4,
      textBCount: 6,
      textCCount: 2,
      totalPoints: 42,
    });
    expect(errorCodes(report)).toEqual([]);
  });

  it('allows PDF-only papers with empty texts', () => {
    const report = validateReadingManifest(buildCanonicalReadingManifest({
      A: { texts: [] },
      B: { texts: [] },
      C: { texts: [] },
    }));
    expect(report.isPublishReady).toBe(true);
    expect(report.counts.textACount).toBe(0);
  });

  it('rejects Part A type sequence drift', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![0].questions![0].questionType = 'ShortAnswer';
    manifest.parts![0].questions![0].correctAnswerJson = '"ORT"';
    const report = validateReadingManifest(manifest);
    expect(report.isPublishReady).toBe(false);
    expect(report.issues.some((issue) => issue.code === 'part_A_question_sequence')).toBe(true);
  });

  it('rejects practice-only types as published exam items', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![1].questions![0].questionType = 'MultipleChoiceFlexible';
    const report = validateReadingManifest(manifest);
    expect(report.issues.some((issue) => issue.code === 'unsupported_interaction')).toBe(true);
  });

  it('rejects matching answers that are not a single A-D letter', () => {
    expect(validateQuestionPayload('MatchingTextReference', '[]', '["A","B"]')).toContain(
      'Matching answer must be one of A,B,C,D.',
    );
  });

  it('rejects MCQ3 with four options', () => {
    const errors = validateQuestionPayload(
      'MultipleChoice3',
      '["A","B","C","D"]',
      '"A"',
    );
    expect(errors).toContain('MCQ3 must have exactly 3 options.');
  });

  it('requires explanation and evidence for publish-ready papers', () => {
    const manifest = buildCanonicalReadingManifest();
    manifest.parts![0].questions![0].explanationMarkdown = '';
    manifest.parts![0].questions![0].evidenceSentence = null;
    const report = validateReadingManifest(manifest);
    expect(report.issues.some((issue) => issue.code === 'question_rationale_missing')).toBe(true);
    expect(report.issues.some((issue) => issue.code === 'question_evidence_missing')).toBe(true);
  });

  it('flags missing primary QuestionPaper assets on import bundles', () => {
    const result = validateReadingImportBundle({
      papers: [{
        paper: { slug: 'sample-reading', subtestCode: 'reading' },
        assets: [{ role: 'AnswerKey', sourcePath: 'answers.pdf' }],
        manifest: buildCanonicalReadingManifest(),
      }],
    });
    expect(result.isPublishReady).toBe(false);
    expect(result.papers[0].assetIssues.map((issue) => issue.code)).toEqual([
      'part_A_pdf_required',
      'part_B_pdf_required',
      'part_C_pdf_required',
    ]);
  });

  it('maps official Part A types and public C numbers from live player rules', () => {
    expect(expectedPartAQuestionType(1)).toBe('MatchingTextReference');
    expect(expectedPartAQuestionType(8)).toBe('ShortAnswer');
    expect(expectedPartAQuestionType(15)).toBe('SentenceCompletion');
    expect(readingPublicDisplayNumber('C', 1)).toBe(7);
    expect(readingPublicDisplayNumber('C', 16)).toBe(22);
  });
});
