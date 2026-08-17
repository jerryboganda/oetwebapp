/**
 * Official OET Reading Part A always has 20 items, but the three task
 * blocks are not fixed at 1-7 / 8-14 / 15-20.
 *
 * Verified from live booklets:
 * - Matching (which text A-D) is always first and is either 1-7 or 1-8.
 * - The last heading is always Questions 15-20.
 * - The middle block fills the gap (8-14 or 9-14).
 * - Middle and last swap between "answer the questions" (ShortAnswer)
 *   and "complete the sentences" (SentenceCompletion).
 */

export const PART_A_LAST_BLOCK_START = 15;
export const PART_A_LAST_QUESTION = 20;

export type PartAMatchingEnd = 7 | 8;
export type PartAGapQuestionType = 'ShortAnswer' | 'SentenceCompletion';

export interface PartALayout {
  matchingEnd: PartAMatchingEnd;
  middleType: PartAGapQuestionType;
  lastType: PartAGapQuestionType;
}

export interface PartALayoutDetection {
  ok: boolean;
  layout: PartALayout | null;
  source: 'questions' | 'booklet' | 'default';
  error: string | null;
}

export const CLASSIC_PART_A_LAYOUT: PartALayout = {
  matchingEnd: 7,
  middleType: 'ShortAnswer',
  lastType: 'SentenceCompletion',
};

export const PART_A_LAYOUTS: readonly PartALayout[] = [
  { matchingEnd: 7, middleType: 'ShortAnswer', lastType: 'SentenceCompletion' },
  { matchingEnd: 7, middleType: 'SentenceCompletion', lastType: 'ShortAnswer' },
  { matchingEnd: 8, middleType: 'ShortAnswer', lastType: 'SentenceCompletion' },
  { matchingEnd: 8, middleType: 'SentenceCompletion', lastType: 'ShortAnswer' },
] as const;

export function partALayoutId(layout: PartALayout): string {
  return `1-${layout.matchingEnd}-matching/${layout.matchingEnd + 1}-14-${layout.middleType}/15-20-${layout.lastType}`;
}

export function describePartALayout(layout: PartALayout): string {
  const middleLabel = layout.middleType === 'ShortAnswer' ? 'answer the questions' : 'complete the sentences';
  const lastLabel = layout.lastType === 'ShortAnswer' ? 'answer the questions' : 'complete the sentences';
  return `Questions 1-${layout.matchingEnd} matching A–D; ${layout.matchingEnd + 1}–14 ${middleLabel}; 15–20 ${lastLabel}`;
}

export function expectedPartAQuestionType(
  displayOrder: number,
  layout: PartALayout = CLASSIC_PART_A_LAYOUT,
): 'MatchingTextReference' | PartAGapQuestionType | null {
  if (displayOrder < 1 || displayOrder > PART_A_LAST_QUESTION) return null;
  if (displayOrder <= layout.matchingEnd) return 'MatchingTextReference';
  if (displayOrder < PART_A_LAST_BLOCK_START) return layout.middleType;
  return layout.lastType;
}

export function layoutsEqual(left: PartALayout, right: PartALayout): boolean {
  return left.matchingEnd === right.matchingEnd
    && left.middleType === right.middleType
    && left.lastType === right.lastType;
}

function isGapType(type: string | undefined): type is PartAGapQuestionType {
  return type === 'ShortAnswer' || type === 'SentenceCompletion';
}

function otherGapType(type: PartAGapQuestionType): PartAGapQuestionType {
  return type === 'ShortAnswer' ? 'SentenceCompletion' : 'ShortAnswer';
}

function uniqueTypes(types: Array<string | undefined>): string[] {
  return [...new Set(types.filter((type): type is string => Boolean(type)))];
}

function fail(error: string): PartALayoutDetection {
  return { ok: false, layout: null, source: 'questions', error };
}

function ok(layout: PartALayout, source: PartALayoutDetection['source']): PartALayoutDetection {
  return { ok: true, layout, source, error: null };
}

/**
 * Strict publish-time detection from typed questions.
 * Requires a contiguous 1-20 set that matches one of the four official layouts.
 */
export function detectPartALayoutFromQuestions(
  questions: Array<{ displayOrder: number; questionType: string }>,
): PartALayoutDetection {
  const byOrder = new Map<number, string>();
  for (const question of questions) {
    if (question.displayOrder < 1 || question.displayOrder > PART_A_LAST_QUESTION) {
      return fail(`Part A question ${question.displayOrder} is outside the official 1-20 range.`);
    }
    const existing = byOrder.get(question.displayOrder);
    if (existing && existing !== question.questionType) {
      return fail(`Part A Q${question.displayOrder} has conflicting types.`);
    }
    byOrder.set(question.displayOrder, question.questionType);
  }

  if (byOrder.size !== PART_A_LAST_QUESTION) {
    return fail(`Part A must have questions 1-20 before a layout can be confirmed (found ${byOrder.size}).`);
  }

  const q8 = byOrder.get(8);
  const matchingEnd: PartAMatchingEnd = q8 === 'MatchingTextReference' ? 8 : 7;

  for (let order = 1; order <= matchingEnd; order += 1) {
    if (byOrder.get(order) !== 'MatchingTextReference') {
      return fail(`Part A Q${order}: expected MatchingTextReference in the opening A–D block (1-${matchingEnd}).`);
    }
  }

  if (matchingEnd === 7 && q8 === 'MatchingTextReference') {
    return fail('Part A matching block cannot stop at 7 if Q8 is also matching.');
  }

  const middleTypes = uniqueTypes(
    Array.from({ length: 14 - matchingEnd }, (_, index) => byOrder.get(matchingEnd + 1 + index)),
  );
  const lastTypes = uniqueTypes(
    Array.from({ length: 6 }, (_, index) => byOrder.get(PART_A_LAST_BLOCK_START + index)),
  );

  if (middleTypes.length !== 1 || !isGapType(middleTypes[0])) {
    return fail(`Part A Q${matchingEnd + 1}-14 must be one block of ShortAnswer or SentenceCompletion.`);
  }
  if (lastTypes.length !== 1 || !isGapType(lastTypes[0])) {
    return fail('Part A Q15-20 must be one block of ShortAnswer or SentenceCompletion.');
  }
  if (middleTypes[0] === lastTypes[0]) {
    return fail('Part A middle and last blocks must be different tasks (answer vs complete the sentences).');
  }

  return ok({
    matchingEnd,
    middleType: middleTypes[0],
    lastType: lastTypes[0],
  }, 'questions');
}

/**
 * Soft authoring hint while questions are still being entered.
 * Falls back to the classic 1-7 / 8-14 / 15-20 layout.
 */
export function suggestPartALayout(
  questions: Array<{ displayOrder: number; questionType: string }> = [],
): PartALayout {
  const byOrder = new Map(questions.map((question) => [question.displayOrder, question.questionType]));
  const q8 = byOrder.get(8);
  let matchingEnd: PartAMatchingEnd = 7;
  if (q8 === 'MatchingTextReference') matchingEnd = 8;
  else if (!q8) {
    const leadingMatch = [1, 2, 3, 4, 5, 6, 7, 8]
      .every((order) => !byOrder.has(order) || byOrder.get(order) === 'MatchingTextReference');
    if (leadingMatch && byOrder.get(8) === 'MatchingTextReference') matchingEnd = 8;
  }

  const lastPresent = [15, 16, 17, 18, 19, 20]
    .map((order) => byOrder.get(order))
    .find(isGapType);
  const middlePresent = Array.from({ length: 14 - matchingEnd }, (_, index) => byOrder.get(matchingEnd + 1 + index))
    .find(isGapType);

  const lastType: PartAGapQuestionType = lastPresent
    ?? (middlePresent ? otherGapType(middlePresent) : CLASSIC_PART_A_LAYOUT.lastType);
  const middleType: PartAGapQuestionType = middlePresent
    ?? otherGapType(lastType);

  return { matchingEnd, middleType, lastType };
}

function normalizeBookletText(raw: string): string {
  return raw
    .replace(/\u00a0/g, ' ')
    .replace(/[\u2010-\u2015]/g, '-')
    .replace(/\s+/g, ' ')
    .trim();
}

function classifyBookletInstruction(text: string): 'matching' | PartAGapQuestionType | null {
  const normalized = normalizeBookletText(text).toLowerCase();
  if (
    /which text/.test(normalized)
    || /a,\s*b,\s*c or d/.test(normalized)
    || /decide which text/.test(normalized)
    || /information comes from/.test(normalized)
  ) {
    return 'matching';
  }
  if (
    /complete each of the sentences/.test(normalized)
    || /complete the following sentences/.test(normalized)
    || /complete each of the sentence/.test(normalized)
  ) {
    return 'SentenceCompletion';
  }
  if (
    /answer each of the questions/.test(normalized)
    || /answer the following/.test(normalized)
    || /with a word or short phrase/.test(normalized)
  ) {
    return 'ShortAnswer';
  }
  return null;
}

const BOOKLET_HEADING = /questions\s+(\d+)\s*(?:-|to)\s*(\d+)/gi;

/**
 * Detect layout from official Part A question-paper headings.
 * Works with "Questions 1- 8", en-dashes, and swapped answer/complete blocks.
 */
export function detectPartALayoutFromBookletText(raw: string): PartALayoutDetection {
  const text = normalizeBookletText(raw ?? '');
  if (!text) {
    return { ok: false, layout: null, source: 'booklet', error: 'No question-paper text was provided.' };
  }

  const blocks: Array<{ start: number; end: number; kind: 'matching' | PartAGapQuestionType }> = [];
  const matches = [...text.matchAll(BOOKLET_HEADING)];
  for (let index = 0; index < matches.length; index += 1) {
    const match = matches[index];
    const start = Number(match[1]);
    const end = Number(match[2]);
    if (!Number.isInteger(start) || !Number.isInteger(end) || start < 1 || end > 20 || start > end) {
      continue;
    }
    const chunkStart = (match.index ?? 0) + match[0].length;
    const nextIndex = matches[index + 1]?.index ?? Math.min(text.length, chunkStart + 500);
    const kind = classifyBookletInstruction(text.slice(chunkStart, nextIndex));
    if (!kind) continue;
    blocks.push({ start, end, kind });
  }

  const matching = blocks.find((block) => block.kind === 'matching' && block.start === 1 && (block.end === 7 || block.end === 8));
  const last = blocks.find((block) => block.start === PART_A_LAST_BLOCK_START && block.end === PART_A_LAST_QUESTION && isGapType(block.kind));
  if (!matching) {
    return { ok: false, layout: null, source: 'booklet', error: 'Could not find a Questions 1-7 or 1-8 matching A–D heading.' };
  }
  if (!last || last.kind === 'matching') {
    return { ok: false, layout: null, source: 'booklet', error: 'Could not find a Questions 15-20 answer/complete heading.' };
  }

  const matchingEnd = matching.end as PartAMatchingEnd;
  const expectedMiddleStart = matchingEnd + 1;
  const middle = blocks.find((block) => (
    block.start === expectedMiddleStart
    && block.end === 14
    && isGapType(block.kind)
  )) ?? (
    isGapType(last.kind)
      ? { start: expectedMiddleStart, end: 14, kind: otherGapType(last.kind) }
      : null
  );

  if (!middle || !isGapType(middle.kind) || middle.kind === last.kind) {
    return {
      ok: false,
      layout: null,
      source: 'booklet',
      error: 'The middle and last Part A blocks must be different (answer vs complete the sentences).',
    };
  }

  return ok({
    matchingEnd,
    middleType: middle.kind,
    lastType: last.kind,
  }, 'booklet');
}

export function resolvePartALayout(input: {
  questions?: Array<{ displayOrder: number; questionType: string }>;
  bookletText?: string | null;
}): PartALayoutDetection {
  const booklet = input.bookletText?.trim()
    ? detectPartALayoutFromBookletText(input.bookletText)
    : null;
  if (booklet?.ok && booklet.layout) return booklet;

  if (input.questions && input.questions.length === PART_A_LAST_QUESTION) {
    const fromQuestions = detectPartALayoutFromQuestions(input.questions);
    if (fromQuestions.ok) return fromQuestions;
    if (booklet && !booklet.ok) return booklet;
    return fromQuestions;
  }

  if (booklet && !booklet.ok && input.bookletText?.trim()) return booklet;
  return ok(suggestPartALayout(input.questions ?? []), 'default');
}
