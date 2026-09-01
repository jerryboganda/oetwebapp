/**
 * Offline Reading import contract.
 *
 * Derived from verified runtime code, not docs:
 * - backend/src/OetLearner.Api/Domain/ReadingEntities.cs
 * - backend/src/OetLearner.Api/Services/Reading/ReadingStructureService.cs
 * - backend/src/OetLearner.Api/Services/Reading/ReadingGradingService.cs
 * - lib/reading-authoring-api.ts
 *
 * This validator does not write data. It is the dry-run gate for future
 * Reading ingestion. The backend ValidatePaperAsync remains authoritative
 * after a write.
 */

import {
  describePartALayout,
  detectPartALayoutFromQuestions,
  expectedPartAQuestionType,
  type PartALayout,
} from './reading-part-a-layout';

export {
  CLASSIC_PART_A_LAYOUT,
  describePartALayout,
  detectPartALayoutFromBookletText,
  detectPartALayoutFromQuestions,
  expectedPartAQuestionType,
  partALayoutId,
  resolvePartALayout,
  suggestPartALayout,
  type PartALayout,
} from './reading-part-a-layout';

export const READING_PUBLISH_TYPES = [
  'MatchingTextReference',
  'ShortAnswer',
  'SentenceCompletion',
  'MultipleChoice3',
  'MultipleChoice4',
] as const;

export const READING_PRACTICE_ONLY_TYPES = [
  'FillInBlank',
  'ShortAnswerLabeled',
  'MultipleChoiceFlexible',
] as const;

export const READING_QUESTION_TYPES = [
  ...READING_PUBLISH_TYPES,
  ...READING_PRACTICE_ONLY_TYPES,
] as const;

export type ReadingPublishQuestionType = (typeof READING_PUBLISH_TYPES)[number];
export type ReadingQuestionTypeName = (typeof READING_QUESTION_TYPES)[number];
export type ReadingPartCodeName = 'A' | 'B' | 'C';
export type ReadingIssueSeverity = 'error' | 'warning';

export interface ReadingValidationIssue {
  code: string;
  severity: ReadingIssueSeverity;
  message: string;
  targetId: string | null;
}

export interface ReadingValidationCounts {
  partACount: number;
  partBCount: number;
  partCCount: number;
  totalPoints: number;
  textACount: number;
  textBCount: number;
  textCCount: number;
}

export interface ReadingManifestValidationReport {
  isPublishReady: boolean;
  issues: ReadingValidationIssue[];
  counts: ReadingValidationCounts;
  detected: {
    parts: ReadingPartCodeName[];
    questionTypes: string[];
    answersPresent: number;
    missingAnswers: number;
    reviewPublished: number;
    reviewNotPublished: number;
    partALayout: string | null;
  };
  importGaps: ReadingValidationIssue[];
}

export interface ReadingQuestionManifestLike {
  displayOrder: number;
  points?: number;
  questionType: string;
  stem?: string;
  optionsJson?: string;
  correctAnswerJson?: string;
  acceptedSynonymsJson?: string | null;
  caseSensitive?: boolean;
  explanationMarkdown?: string | null;
  evidenceSentence?: string | null;
  skillTag?: string | null;
  readingTextDisplayOrder?: number | null;
  reviewState?: string;
}

export interface ReadingTextManifestLike {
  displayOrder: number;
  title?: string;
  source?: string | null;
  bodyHtml?: string;
  wordCount?: number;
  topicTag?: string | null;
}

export interface ReadingPartManifestLike {
  partCode: string;
  timeLimitMinutes?: number | null;
  instructions?: string | null;
  texts?: ReadingTextManifestLike[];
  questions?: ReadingQuestionManifestLike[];
}

export interface ReadingStructureManifestLike {
  parts?: ReadingPartManifestLike[];
}

export interface ReadingImportPaperLike {
  paper?: {
    slug?: string;
    title?: string;
    subtestCode?: string;
    sourceProvenance?: string;
  };
  assets?: Array<{ role?: string; part?: string | null; sourcePath?: string; makePrimary?: boolean }>;
  manifest?: ReadingStructureManifestLike;
}

const CANONICAL_SHAPE: Record<ReadingPartCodeName, { items: number; minutes: number; texts: number }> = {
  A: { items: 20, minutes: 15, texts: 4 },
  B: { items: 6, minutes: 45, texts: 6 },
  C: { items: 16, minutes: 45, texts: 2 },
};

const CANONICAL_MAX_RAW = 42;
const MATCHING_LETTERS = ['A', 'B', 'C', 'D'] as const;
const MCQ_LETTERS = ['A', 'B', 'C', 'D', 'E', 'F'] as const;
const LEARNER_SAFE_OPTION_KEYS = new Set(['id', 'value', 'label', 'text', 'title', 'letter']);

export function isQuestionTypeAllowedForPart(partCode: ReadingPartCodeName, questionType: string): boolean {
  if (partCode === 'A') {
    return questionType === 'MatchingTextReference'
      || questionType === 'ShortAnswer'
      || questionType === 'SentenceCompletion';
  }
  if (partCode === 'B') return questionType === 'MultipleChoice3';
  if (partCode === 'C') return questionType === 'MultipleChoice4';
  return false;
}

export function readingPublicDisplayNumber(partCode: ReadingPartCodeName, internalDisplayOrder: number): number {
  return partCode === 'C' ? internalDisplayOrder + 6 : internalDisplayOrder;
}

function issue(
  code: string,
  severity: ReadingIssueSeverity,
  message: string,
  targetId: string | null = null,
): ReadingValidationIssue {
  return { code, severity, message, targetId };
}

function parseJson(raw: string | undefined, label: string): { ok: true; value: unknown } | { ok: false; error: string } {
  if (raw === undefined) return { ok: false, error: `${label} is missing.` };
  try {
    return { ok: true, value: JSON.parse(raw) };
  } catch {
    return { ok: false, error: `${label} is not valid JSON.` };
  }
}

function hasContiguousDisplayOrders(orders: number[]): boolean {
  if (orders.length === 0) return true;
  const list = [...orders].sort((a, b) => a - b);
  return list.every((value, index) => value === index + 1);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

export function validateQuestionPayload(
  type: string,
  optionsJson: string | undefined,
  correctAnswerJson: string | undefined,
  synonymsJson?: string | null,
): string[] {
  const errors: string[] = [];
  const options = parseJson(optionsJson, 'OptionsJson');
  const correct = parseJson(correctAnswerJson, 'CorrectAnswerJson');
  if (!options.ok) errors.push(options.error);
  if (!correct.ok) errors.push(correct.error);
  if (!options.ok || !correct.ok) return errors;

  switch (type) {
    case 'MultipleChoice3':
    case 'MultipleChoice4':
    case 'MultipleChoiceFlexible': {
      if (!Array.isArray(options.value)) {
        errors.push('MCQ OptionsJson must be a JSON array.');
        break;
      }
      const optionCount = options.value.length;
      if (type === 'MultipleChoice3' && optionCount !== 3) errors.push('MCQ3 must have exactly 3 options.');
      if (type === 'MultipleChoice4' && optionCount !== 4) errors.push('MCQ4 must have exactly 4 options.');
      if (type === 'MultipleChoiceFlexible' && (optionCount < 2 || optionCount > MCQ_LETTERS.length)) {
        errors.push(`Flexible MCQ must have between 2 and ${MCQ_LETTERS.length} options.`);
      }
      errors.push(...validateMcqOptions(options.value));
      if (typeof correct.value !== 'string') {
        errors.push('MCQ CorrectAnswerJson must be a single string letter.');
      } else {
        const correctValue = correct.value;
        const valid = MCQ_LETTERS.slice(0, optionCount);
        if (!valid.some((letter) => letter.toLowerCase() === correctValue.toLowerCase())) {
          errors.push(`MCQ answer must be one of ${valid.join(',')}.`);
        }
      }
      break;
    }
    case 'MatchingTextReference': {
      if (typeof correct.value !== 'string' || !MATCHING_LETTERS.includes(correct.value as typeof MATCHING_LETTERS[number])) {
        errors.push('Matching answer must be one of A,B,C,D.');
      }
      break;
    }
    case 'ShortAnswer':
    case 'SentenceCompletion':
    case 'FillInBlank': {
      if (!isNonEmptyString(correct.value)) {
        errors.push('Short-answer CorrectAnswerJson must be a non-empty string.');
      }
      errors.push(...validateSynonyms(synonymsJson, false));
      break;
    }
    case 'ShortAnswerLabeled': {
      if (!correct.value || typeof correct.value !== 'object' || Array.isArray(correct.value)) {
        errors.push('Labeled short-answer CorrectAnswerJson must be a JSON object.');
        break;
      }
      const entries = Object.entries(correct.value as Record<string, unknown>);
      if (entries.length === 0) errors.push('Labeled short-answer CorrectAnswerJson must contain at least one answer.');
      for (const [key, value] of entries) {
        if (!isNonEmptyString(key) || !isNonEmptyString(value)) {
          errors.push('Labeled short-answer answers must be non-empty string values keyed by label.');
        }
      }
      errors.push(...validateSynonyms(synonymsJson, true));
      break;
    }
    default:
      errors.push(`Unknown question type ${type}.`);
  }

  return errors;
}

function validateMcqOptions(options: unknown[]): string[] {
  const errors: string[] = [];
  const labels = new Set<string>();
  const values = new Set<string>();

  for (const option of options) {
    if (typeof option === 'string') {
      const label = option.trim();
      if (!label || labels.has(label.toLowerCase())) {
        errors.push('MCQ options must be non-empty and unique.');
      } else {
        labels.add(label.toLowerCase());
      }
      continue;
    }
    if (!option || typeof option !== 'object' || Array.isArray(option)) {
      errors.push('MCQ OptionsJson entries must be strings or learner-safe option objects.');
      continue;
    }
    const record = option as Record<string, unknown>;
    for (const [key, value] of Object.entries(record)) {
      if (!LEARNER_SAFE_OPTION_KEYS.has(key) || typeof value !== 'string') {
        errors.push('MCQ option objects may only contain string id, value, label, text, title, or letter fields.');
      }
    }
    const label = [record.label, record.text, record.title, record.value]
      .find((item) => typeof item === 'string' && item.trim()) as string | undefined;
    const value = typeof record.value === 'string'
      ? record.value
      : typeof record.letter === 'string'
        ? record.letter
        : undefined;
    const id = typeof record.id === 'string' ? record.id : undefined;
    if (!label?.trim()) errors.push('MCQ options must be non-empty and unique.');
    else if (labels.has(label.trim().toLowerCase())) errors.push('MCQ options must be non-empty and unique.');
    else labels.add(label.trim().toLowerCase());
    if (value && values.has(value.trim().toLowerCase())) errors.push('MCQ option values must be unique.');
    else if (value) values.add(value.trim().toLowerCase());
    if (id && values.has(`id:${id}`)) errors.push('MCQ option IDs must be unique.');
    else if (id) values.add(`id:${id}`);
  }

  return errors;
}

function validateSynonyms(synonymsJson: string | null | undefined, allowObject: boolean): string[] {
  if (!synonymsJson) return [];
  const parsed = parseJson(synonymsJson, 'AcceptedSynonymsJson');
  if (!parsed.ok) return [parsed.error];
  if (Array.isArray(parsed.value)) {
    return parsed.value.every((item) => isNonEmptyString(item))
      ? []
      : ['AcceptedSynonymsJson must contain only non-empty strings.'];
  }
  if (!allowObject || !parsed.value || typeof parsed.value !== 'object') {
    return [allowObject
      ? 'AcceptedSynonymsJson must be a JSON array or object.'
      : 'AcceptedSynonymsJson must be a JSON array of strings.'];
  }
  for (const [key, value] of Object.entries(parsed.value as Record<string, unknown>)) {
    if (!isNonEmptyString(key) || !Array.isArray(value) || !value.every((item) => typeof item === 'string')) {
      return ['AcceptedSynonymsJson labeled entries must map labels to arrays of strings.'];
    }
  }
  return [];
}

export function validateReadingManifest(
  manifest: ReadingStructureManifestLike | null | undefined,
  options: { requirePublishMetadata?: boolean } = {},
): ReadingManifestValidationReport {
  const requirePublishMetadata = options.requirePublishMetadata !== false;
  const issues: ReadingValidationIssue[] = [];
  const parts = manifest?.parts ?? [];
  const partCodes = parts.map((part) => part.partCode);
  const questionTypes = new Set<string>();
  let partACount = 0;
  let partBCount = 0;
  let partCCount = 0;
  let textACount = 0;
  let textBCount = 0;
  let textCCount = 0;
  let totalPoints = 0;
  let answersPresent = 0;
  let missingAnswers = 0;
  let reviewPublished = 0;
  let reviewNotPublished = 0;
  let detectedPartALayout: string | null = null;

  if (parts.length === 0) {
    issues.push(issue('manifest_empty', 'error', 'Reading manifest must contain at least one part.'));
  }

  const duplicatePart = partCodes.find((code, index) => partCodes.indexOf(code) !== index);
  if (duplicatePart) {
    issues.push(issue('duplicate_part', 'error', `Reading manifest contains duplicate Part ${duplicatePart} sections.`));
  }

  for (const code of Object.keys(CANONICAL_SHAPE) as ReadingPartCodeName[]) {
    if (!parts.some((part) => part.partCode === code)) {
      issues.push(issue(`part_${code}_missing`, 'error', `Part ${code} is missing.`));
    }
  }

  for (const part of parts) {
    const partCode = part.partCode as ReadingPartCodeName;
    if (!(partCode in CANONICAL_SHAPE)) {
      issues.push(issue('unsupported_part', 'error', `Unsupported Reading part code ${part.partCode}.`));
      continue;
    }
    const expected = CANONICAL_SHAPE[partCode];
    const texts = part.texts ?? [];
    const questions = part.questions ?? [];
    if (!part.texts) {
      issues.push(issue(
        `part_${partCode}_texts_missing`,
        'error',
        `Part ${partCode} must include a texts array. Use [] for official PDF-only papers.`,
      ));
    }
    if (!part.questions) {
      issues.push(issue(`part_${partCode}_questions_missing`, 'error', `Part ${partCode} must include a questions array.`));
    }

    if (partCode === 'A') { partACount = questions.length; textACount = texts.length; }
    if (partCode === 'B') { partBCount = questions.length; textBCount = texts.length; }
    if (partCode === 'C') { partCCount = questions.length; textCCount = texts.length; }

    if (part.timeLimitMinutes != null && part.timeLimitMinutes !== expected.minutes) {
      issues.push(issue(
        `part_${partCode}_time_limit`,
        'error',
        `Part ${partCode} has a ${part.timeLimitMinutes}-minute limit, expected ${expected.minutes} minute(s).`,
      ));
    }
    if (questions.length !== expected.items) {
      issues.push(issue(
        `part_${partCode}_item_count`,
        'error',
        `Part ${partCode} has ${questions.length} item(s), expected ${expected.items}.`,
      ));
    }
    if (texts.length > 0 && texts.length !== expected.texts) {
      issues.push(issue(
        `part_${partCode}_text_count`,
        'error',
        `Reading Part ${partCode} has ${texts.length} text row(s), expected exactly ${expected.texts} when text-linked content is present.`,
      ));
    }
    if (texts.length > 0 && !hasContiguousDisplayOrders(texts.map((text) => text.displayOrder))) {
      issues.push(issue(
        `part_${partCode}_text_order`,
        'warning',
        `Legacy Part ${partCode} text display orders should be unique and contiguous from 1.`,
      ));
    }
    if (!hasContiguousDisplayOrders(questions.map((question) => question.displayOrder))) {
      issues.push(issue(
        `part_${partCode}_question_order`,
        'error',
        `Part ${partCode} question display orders must be unique and contiguous from 1.`,
      ));
    }

    const textOrders = new Set(texts.map((text) => text.displayOrder));
    const partAHasAnyTextLink = partCode === 'A'
      && questions.some((question) => question.readingTextDisplayOrder != null && textOrders.has(question.readingTextDisplayOrder));

    let partALayout: PartALayout | null = null;
    if (partCode === 'A' && questions.length > 0) {
      const detectedLayout = detectPartALayoutFromQuestions(
        questions.map((question) => ({ displayOrder: question.displayOrder, questionType: question.questionType })),
      );
      if (detectedLayout.ok && detectedLayout.layout) {
        partALayout = detectedLayout.layout;
        detectedPartALayout = describePartALayout(detectedLayout.layout);
      } else if (questions.length === 20) {
        issues.push(issue(
          'part_A_layout_invalid',
          'error',
          detectedLayout.error ?? 'Part A does not match an official 1-7/1-8 matching layout.',
          'Part A',
        ));
      }
    }

    for (const question of questions) {
      const target = `Part ${partCode} Q${question.displayOrder}`;
      questionTypes.add(question.questionType);
      const points = question.points ?? 0;
      totalPoints += points;
      if (points !== 1) {
        issues.push(issue(
          'question_points_not_one',
          'error',
          `${target} is worth ${points} point(s), expected 1. Import deserializes omitted points as 0.`,
          target,
        ));
      }
      if (!isQuestionTypeAllowedForPart(partCode, question.questionType)) {
        const practiceOnly = READING_PRACTICE_ONLY_TYPES.includes(question.questionType as typeof READING_PRACTICE_ONLY_TYPES[number]);
        issues.push(issue(
          practiceOnly ? 'unsupported_interaction' : `part_${partCode}_question_type`,
          'error',
          practiceOnly
            ? `${target} uses practice-only type ${question.questionType}. Do not approximate it as a published exam item.`
            : `${target} uses ${question.questionType}, which is not valid for this part.`,
          target,
        ));
      }
      if (partCode === 'A' && partALayout) {
        const expectedType = expectedPartAQuestionType(question.displayOrder, partALayout);
        if (!expectedType) {
          issues.push(issue('part_A_question_order', 'error', `${target} is outside the official 1-20 range.`, target));
        } else if (expectedType !== question.questionType) {
          issues.push(issue(
            'part_A_question_sequence',
            'error',
            `${target}: expected ${expectedType}, got ${question.questionType}.`,
            target,
          ));
        }
      }
      if (question.readingTextDisplayOrder != null && !textOrders.has(question.readingTextDisplayOrder)) {
        issues.push(issue(
          `part_${partCode}_question_text_invalid`,
          'error',
          `${target} references missing text display order ${question.readingTextDisplayOrder}. ImportManifestAsync rejects this.`,
          target,
        ));
      }
      if (partAHasAnyTextLink && question.readingTextDisplayOrder == null) {
        issues.push(issue(
          'part_A_question_text_required',
          'error',
          `${target} is missing a text link. Either all Part A questions must reference a text passage or none of them should.`,
          target,
        ));
      }

      const payloadErrors = validateQuestionPayload(
        question.questionType,
        question.optionsJson,
        question.correctAnswerJson,
        question.acceptedSynonymsJson,
      );
      if (payloadErrors.length > 0) {
        missingAnswers += 1;
        for (const payloadError of payloadErrors) {
          issues.push(issue('question_payload_invalid', 'error', `${target}: ${payloadError}`, target));
        }
      } else {
        answersPresent += 1;
      }

      if (requirePublishMetadata && !isNonEmptyString(question.explanationMarkdown)) {
        issues.push(issue('question_rationale_missing', 'error', `${target} must include a post-submit rationale.`, target));
      }
      if (requirePublishMetadata && !isNonEmptyString(question.evidenceSentence)) {
        issues.push(issue(
          'question_evidence_missing',
          'error',
          `${target} must include the source sentence that supports the correct answer.`,
          target,
        ));
      }
      if ((question.reviewState ?? 'Draft') === 'Published') reviewPublished += 1;
      else {
        reviewNotPublished += 1;
        if (requirePublishMetadata) {
          issues.push(issue(
            'question_not_published',
            'error',
            `${target} is in ${question.reviewState ?? 'Draft'}; advance to Published before paper publish.`,
            target,
          ));
        }
      }
    }

    if (texts.length > 0 && (partCode === 'B' || partCode === 'C')) {
      const expectedPerText = partCode === 'B' ? 1 : 8;
      const questionsByText = new Map<number, number>();
      for (const question of questions) {
        if (question.readingTextDisplayOrder == null) continue;
        questionsByText.set(
          question.readingTextDisplayOrder,
          (questionsByText.get(question.readingTextDisplayOrder) ?? 0) + 1,
        );
      }
      for (const text of texts) {
        const count = questionsByText.get(text.displayOrder) ?? 0;
        if (count !== expectedPerText) {
          issues.push(issue(
            `part_${partCode}_questions_per_text`,
            'error',
            `Part ${partCode} text ${text.displayOrder} has ${count} linked question(s), expected ${expectedPerText}.`,
            `Part ${partCode} text ${text.displayOrder}`,
          ));
        }
      }
    }

    for (const text of texts) {
      if (!isNonEmptyString(text.source)) {
        issues.push(issue(
          'legacy_text_missing_source',
          'warning',
          `Legacy text '${text.title ?? `displayOrder ${text.displayOrder}`}' is missing a Source attribution; PDF-only learners will not see it.`,
        ));
      }
    }
  }

  if (totalPoints !== CANONICAL_MAX_RAW && parts.length > 0) {
    issues.push(issue('total_points_mismatch', 'error', `Total Reading points = ${totalPoints}, must equal ${CANONICAL_MAX_RAW}.`));
  }

  const importGaps = [
    issue(
      'evidence_imported',
      'warning',
      'ImportManifestAsync now persists EvidenceSentence and ReviewState from the manifest. Confirm the live API includes that patch before publishing.',
    ),
    issue(
      'replace_import_wipes_pdfs',
      'warning',
      'The local write importer posts replaceExisting=true on /reading/manifest. It now re-injects uploaded QuestionPaper mediaAssetIds into the manifest so Part A/B/C PDFs are recreated in the same request.',
    ),
  ];
  issues.push(...importGaps);

  return {
    isPublishReady: issues.every((item) => item.severity !== 'error'),
    issues,
    counts: { partACount, partBCount, partCCount, totalPoints, textACount, textBCount, textCCount },
    detected: {
      parts: parts.map((part) => part.partCode).filter((code): code is ReadingPartCodeName => code === 'A' || code === 'B' || code === 'C'),
      questionTypes: [...questionTypes],
      answersPresent,
      missingAnswers,
      reviewPublished,
      reviewNotPublished,
      partALayout: detectedPartALayout,
    },
    importGaps,
  };
}

export function extractManifestFromBundle(input: unknown): {
  papers: Array<{ label: string; paper?: ReadingImportPaperLike['paper']; assets?: ReadingImportPaperLike['assets']; manifest: ReadingStructureManifestLike }>;
} {
  if (!input || typeof input !== 'object') {
    return { papers: [] };
  }
  const root = input as Record<string, unknown>;
  if (Array.isArray(root.papers)) {
    return {
      papers: root.papers.map((entry, index) => {
        const paper = (entry ?? {}) as ReadingImportPaperLike;
        return {
          label: paper.paper?.slug ?? `paper[${index}]`,
          paper: paper.paper,
          assets: paper.assets,
          manifest: paper.manifest ?? { parts: [] },
        };
      }),
    };
  }
  if (root.manifest && typeof root.manifest === 'object') {
    const paper = root as ReadingImportPaperLike;
    return {
      papers: [{
        label: paper.paper?.slug ?? 'paper',
        paper: paper.paper,
        assets: paper.assets,
        manifest: paper.manifest ?? { parts: [] },
      }],
    };
  }
  if (Array.isArray(root.parts)) {
    return { papers: [{ label: 'manifest', manifest: root as ReadingStructureManifestLike }] };
  }
  return { papers: [] };
}

export function validateReadingImportBundle(
  input: unknown,
  options: { requirePublishMetadata?: boolean } = {},
): {
  isPublishReady: boolean;
  papers: Array<{
    label: string;
    paper?: ReadingImportPaperLike['paper'];
    assets?: ReadingImportPaperLike['assets'];
    report: ReadingManifestValidationReport;
    assetIssues: ReadingValidationIssue[];
  }>;
} {
  const extracted = extractManifestFromBundle(input);
  const papers = extracted.papers.map((paper) => {
    const report = validateReadingManifest(paper.manifest, options);
    const assetIssues = validateRequiredAssets(paper.assets);
    return { ...paper, report, assetIssues };
  });
  return {
    isPublishReady: papers.length > 0
      && papers.every((paper) => paper.report.isPublishReady && paper.assetIssues.every((item) => item.severity !== 'error')),
    papers,
  };
}

export function validateRequiredAssets(
  assets: ReadingImportPaperLike['assets'],
): ReadingValidationIssue[] {
  const issues: ReadingValidationIssue[] = [];
  const list = assets ?? [];
  for (const part of ['A', 'B', 'C'] as const) {
    const matches = list.filter((asset) =>
      asset.role === 'QuestionPaper'
      && asset.part === part
      && asset.makePrimary !== false);
    if (matches.length !== 1) {
      issues.push(issue(
        `part_${part}_pdf_required`,
        'error',
        `Reading Part ${part} requires exactly one primary QuestionPaper PDF or image asset.`,
      ));
    }
  }
  return issues;
}

function cloneTemplate(type: ReadingQuestionTypeName, displayOrder: number): ReadingQuestionManifestLike {
  return { ...READING_QUESTION_TEMPLATES[type], displayOrder };
}

function emptyTexts(): ReadingTextManifestLike[] {
  return [];
}

function linkedTexts(partCode: ReadingPartCodeName): ReadingTextManifestLike[] {
  const count = CANONICAL_SHAPE[partCode].texts;
  return Array.from({ length: count }, (_, index) => ({
    displayOrder: index + 1,
    title: partCode === 'A' ? `Text ${String.fromCharCode(65 + index)}` : `${partCode} ${index + 1}`,
    source: 'synthetic-fixture',
    bodyHtml: `<p>${partCode} passage ${index + 1}</p>`,
    wordCount: partCode === 'C' ? 700 : 120,
  }));
}

export function buildCanonicalReadingManifest(
  overrides: Partial<Record<ReadingPartCodeName, Partial<ReadingPartManifestLike>>> & {
    partALayout?: PartALayout;
  } = {},
): ReadingStructureManifestLike {
  const layout = overrides.partALayout;
  const matchingCount = layout?.matchingEnd ?? 7;
  const middleType = layout?.middleType ?? 'ShortAnswer';
  const lastType = layout?.lastType ?? 'SentenceCompletion';
  const partAQuestions = [
    ...Array.from({ length: matchingCount }, (_, index) => cloneTemplate('MatchingTextReference', index + 1)),
    ...Array.from({ length: 14 - matchingCount }, (_, index) => cloneTemplate(middleType, matchingCount + 1 + index)),
    ...Array.from({ length: 6 }, (_, index) => cloneTemplate(lastType, index + 15)),
  ];
  const partBQuestions = Array.from({ length: 6 }, (_, index) => cloneTemplate('MultipleChoice3', index + 1));
  const partCQuestions = Array.from({ length: 16 }, (_, index) => cloneTemplate('MultipleChoice4', index + 1));

  return {
    parts: [
      {
        partCode: 'A',
        timeLimitMinutes: 15,
        instructions: 'Answer Questions 1-20.',
        texts: emptyTexts(),
        questions: partAQuestions,
        ...overrides.A,
      },
      {
        partCode: 'B',
        timeLimitMinutes: 45,
        instructions: 'Answer Questions 1-6.',
        texts: emptyTexts(),
        questions: partBQuestions,
        ...overrides.B,
      },
      {
        partCode: 'C',
        timeLimitMinutes: 45,
        instructions: 'Answer Questions 7-22.',
        texts: emptyTexts(),
        questions: partCQuestions,
        ...overrides.C,
      },
    ],
  };
}

export function buildCanonicalTextLinkedReadingManifest(): ReadingStructureManifestLike {
  const manifest = buildCanonicalReadingManifest({
    A: { texts: linkedTexts('A') },
    B: { texts: linkedTexts('B') },
    C: { texts: linkedTexts('C') },
  });
  const [partA, partB, partC] = manifest.parts ?? [];
  for (const question of partA.questions ?? []) {
    question.readingTextDisplayOrder = ((question.displayOrder - 1) % 4) + 1;
  }
  for (const question of partB.questions ?? []) {
    question.readingTextDisplayOrder = question.displayOrder;
  }
  for (const question of partC.questions ?? []) {
    question.readingTextDisplayOrder = question.displayOrder <= 8 ? 1 : 2;
  }
  return manifest;
}

export const READING_QUESTION_TEMPLATES: Record<ReadingQuestionTypeName, ReadingQuestionManifestLike> = {
  MatchingTextReference: {
    displayOrder: 1,
    points: 1,
    questionType: 'MatchingTextReference',
    stem: 'Which text mentions ...',
    optionsJson: '[]',
    correctAnswerJson: '"A"',
    acceptedSynonymsJson: null,
    caseSensitive: false,
    explanationMarkdown: 'Text A states the matching fact.',
    evidenceSentence: 'Verbatim sentence from Text A.',
    reviewState: 'Published',
  },
  ShortAnswer: {
    displayOrder: 8,
    points: 1,
    questionType: 'ShortAnswer',
    stem: 'What is the recommended first-line treatment?',
    optionsJson: '[]',
    correctAnswerJson: '"ORT"',
    acceptedSynonymsJson: null,
    caseSensitive: false,
    explanationMarkdown: 'Copied word-for-word from the source text.',
    evidenceSentence: 'Oral rehydration therapy is recommended.',
    reviewState: 'Published',
  },
  SentenceCompletion: {
    displayOrder: 15,
    points: 1,
    questionType: 'SentenceCompletion',
    stem: 'Patients should be advised to ______.',
    optionsJson: '[]',
    correctAnswerJson: '"rest in bed"',
    acceptedSynonymsJson: null,
    caseSensitive: false,
    explanationMarkdown: 'The completion is taken verbatim from the phrase bank / text.',
    evidenceSentence: 'Patients should be advised to rest in bed.',
    reviewState: 'Published',
  },
  MultipleChoice3: {
    displayOrder: 1,
    points: 1,
    questionType: 'MultipleChoice3',
    stem: 'The notice is advising staff to',
    optionsJson: '["wash their hands","reuse gloves","skip documentation"]',
    correctAnswerJson: '"A"',
    acceptedSynonymsJson: null,
    caseSensitive: false,
    explanationMarkdown: 'Option A restates the extract.',
    evidenceSentence: 'Staff must wash their hands.',
    reviewState: 'Published',
  },
  MultipleChoice4: {
    displayOrder: 1,
    points: 1,
    questionType: 'MultipleChoice4',
    stem: 'The writer suggests that',
    optionsJson: '["option A","option B","option C","option D"]',
    correctAnswerJson: '"C"',
    acceptedSynonymsJson: null,
    caseSensitive: false,
    explanationMarkdown: 'Option C is supported by the paragraph.',
    evidenceSentence: 'Supporting sentence from the article.',
    reviewState: 'Published',
  },
  FillInBlank: {
    displayOrder: 1,
    points: 1,
    questionType: 'FillInBlank',
    stem: 'Practice-only blank.',
    optionsJson: '[]',
    correctAnswerJson: '"sample"',
    reviewState: 'Draft',
  },
  ShortAnswerLabeled: {
    displayOrder: 1,
    points: 1,
    questionType: 'ShortAnswerLabeled',
    stem: 'Practice-only labeled boxes.',
    optionsJson: '[{"value":"box1","label":"Box 1"},{"value":"box2","label":"Box 2"}]',
    correctAnswerJson: '{"box1":"one","box2":"two"}',
    reviewState: 'Draft',
  },
  MultipleChoiceFlexible: {
    displayOrder: 1,
    points: 1,
    questionType: 'MultipleChoiceFlexible',
    stem: 'Practice-only flexible MCQ.',
    optionsJson: '["one","two"]',
    correctAnswerJson: '"A"',
    reviewState: 'Draft',
  },
};
