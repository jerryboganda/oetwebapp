/**
 * Official OET Reading Part A always has 20 items, but the three task
 * blocks are not fixed at 1-7 / 8-14 / 15-20.
 *
 * Verified from live booklets (Jayden Book + Sample 5):
 * - Matching (which text A-D) is always first and is either 1-7 or 1-8.
 * - The last heading ends at 20 and starts at 15 or 16.
 * - The middle block fills the gap (8-14, 9-14, 8-15, or 9-15).
 * - Middle and last swap between "answer the questions" (ShortAnswer)
 *   and "complete the sentences" (SentenceCompletion).
 */

export const PART_A_LAST_QUESTION = 20;
export const PART_A_LAST_BLOCK_STARTS = [15, 16] as const;

export type PartAMatchingEnd = 7 | 8;
export type PartALastStart = 15 | 16;
export type PartAGapQuestionType = 'ShortAnswer' | 'SentenceCompletion';

export interface PartALayout {
  matchingEnd: PartAMatchingEnd;
  lastStart: PartALastStart;
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
  lastStart: 15,
  middleType: 'ShortAnswer',
  lastType: 'SentenceCompletion',
};

/** @deprecated Use layout.lastStart. Kept for callers that still assume 15-20. */
export const PART_A_LAST_BLOCK_START = CLASSIC_PART_A_LAYOUT.lastStart;

function allLayouts(): PartALayout[] {
  const layouts: PartALayout[] = [];
  for (const matchingEnd of [7, 8] as const) {
    for (const lastStart of PART_A_LAST_BLOCK_STARTS) {
      layouts.push(
        { matchingEnd, lastStart, middleType: 'ShortAnswer', lastType: 'SentenceCompletion' },
        { matchingEnd, lastStart, middleType: 'SentenceCompletion', lastType: 'ShortAnswer' },
      );
    }
  }
  return layouts;
}

export const PART_A_LAYOUTS: readonly PartALayout[] = allLayouts();

export function normalizePartALayout(layout: Partial<PartALayout> | null | undefined): PartALayout {
  return {
    matchingEnd: layout?.matchingEnd === 8 ? 8 : 7,
    lastStart: layout?.lastStart === 16 ? 16 : 15,
    middleType: layout?.middleType === 'SentenceCompletion' ? 'SentenceCompletion' : 'ShortAnswer',
    lastType: layout?.lastType === 'ShortAnswer' ? 'ShortAnswer' : 'SentenceCompletion',
  };
}

export function partALayoutId(layout: PartALayout): string {
  const normalized = normalizePartALayout(layout);
  return `1-${normalized.matchingEnd}-matching/${normalized.matchingEnd + 1}-${normalized.lastStart - 1}-${normalized.middleType}/${normalized.lastStart}-20-${normalized.lastType}`;
}

export function describePartALayout(layout: PartALayout): string {
  const normalized = normalizePartALayout(layout);
  const middleLabel = normalized.middleType === 'ShortAnswer' ? 'answer the questions' : 'complete the sentences';
  const lastLabel = normalized.lastType === 'ShortAnswer' ? 'answer the questions' : 'complete the sentences';
  return `Questions 1-${normalized.matchingEnd} matching A–D; ${normalized.matchingEnd + 1}–${normalized.lastStart - 1} ${middleLabel}; ${normalized.lastStart}–20 ${lastLabel}`;
}

export function expectedPartAQuestionType(
  displayOrder: number,
  layout: PartALayout = CLASSIC_PART_A_LAYOUT,
): 'MatchingTextReference' | PartAGapQuestionType | null {
  const normalized = normalizePartALayout(layout);
  if (displayOrder < 1 || displayOrder > PART_A_LAST_QUESTION) return null;
  if (displayOrder <= normalized.matchingEnd) return 'MatchingTextReference';
  if (displayOrder < normalized.lastStart) return normalized.middleType;
  return normalized.lastType;
}

export function layoutsEqual(left: PartALayout, right: PartALayout): boolean {
  const a = normalizePartALayout(left);
  const b = normalizePartALayout(right);
  return a.matchingEnd === b.matchingEnd
    && a.lastStart === b.lastStart
    && a.middleType === b.middleType
    && a.lastType === b.lastType;
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
  return { ok: true, layout: normalizePartALayout(layout), source, error: null };
}

/**
 * Strict publish-time detection from typed questions.
 * Requires a contiguous 1-20 set that matches one official layout.
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

  const firstGap = byOrder.get(matchingEnd + 1);
  if (!isGapType(firstGap)) {
    return fail(`Part A Q${matchingEnd + 1} must start a ShortAnswer or SentenceCompletion block.`);
  }

  let lastStart: number | null = null;
  for (let order = matchingEnd + 2; order <= PART_A_LAST_QUESTION; order += 1) {
    const type = byOrder.get(order);
    if (type !== firstGap) {
      lastStart = order;
      break;
    }
  }

  if (lastStart !== 15 && lastStart !== 16) {
    return fail('Part A last block must start at question 15 or 16.');
  }

  const lastType = byOrder.get(lastStart);
  if (!isGapType(lastType) || lastType === firstGap) {
    return fail('Part A middle and last blocks must be different tasks (answer vs complete the sentences).');
  }

  const middleTypes = uniqueTypes(
    Array.from({ length: lastStart - matchingEnd - 1 }, (_, index) => byOrder.get(matchingEnd + 1 + index)),
  );
  const lastTypes = uniqueTypes(
    Array.from({ length: PART_A_LAST_QUESTION - lastStart + 1 }, (_, index) => byOrder.get(lastStart + index)),
  );

  if (middleTypes.length !== 1 || middleTypes[0] !== firstGap) {
    return fail(`Part A Q${matchingEnd + 1}-${lastStart - 1} must be one block of ShortAnswer or SentenceCompletion.`);
  }
  if (lastTypes.length !== 1 || lastTypes[0] !== lastType) {
    return fail(`Part A Q${lastStart}-20 must be one block of ShortAnswer or SentenceCompletion.`);
  }

  return ok({
    matchingEnd,
    lastStart,
    middleType: firstGap,
    lastType,
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
  const matchingEnd: PartAMatchingEnd = q8 === 'MatchingTextReference' ? 8 : 7;

  const firstGap = [matchingEnd + 1, matchingEnd + 2, matchingEnd + 3]
    .map((order) => byOrder.get(order))
    .find(isGapType);

  let lastStart: PartALastStart = 15;
  if (byOrder.get(15) && byOrder.get(15) === firstGap && isGapType(byOrder.get(16))) {
    lastStart = 16;
  } else if (isGapType(byOrder.get(16)) && byOrder.get(15) && byOrder.get(15) !== byOrder.get(16)) {
    lastStart = 16;
  } else if (isGapType(byOrder.get(15)) && firstGap && byOrder.get(15) !== firstGap) {
    lastStart = 15;
  }

  const lastPresent = Array.from({ length: PART_A_LAST_QUESTION - lastStart + 1 }, (_, index) => byOrder.get(lastStart + index))
    .find(isGapType);
  const middlePresent = Array.from({ length: lastStart - matchingEnd - 1 }, (_, index) => byOrder.get(matchingEnd + 1 + index))
    .find(isGapType);

  const lastType: PartAGapQuestionType = lastPresent
    ?? (middlePresent ? otherGapType(middlePresent) : CLASSIC_PART_A_LAYOUT.lastType);
  const middleType: PartAGapQuestionType = middlePresent
    ?? otherGapType(lastType);

  return normalizePartALayout({ matchingEnd, lastStart, middleType, lastType });
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
 * Works with "Questions 1- 8", "Questions 8-15", and swapped answer/complete blocks.
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
  const last = blocks.find((block) => (
    (block.start === 15 || block.start === 16)
    && block.end === PART_A_LAST_QUESTION
    && isGapType(block.kind)
  ));
  if (!matching) {
    return { ok: false, layout: null, source: 'booklet', error: 'Could not find a Questions 1-7 or 1-8 matching A–D heading.' };
  }
  if (!last || last.kind === 'matching') {
    return { ok: false, layout: null, source: 'booklet', error: 'Could not find a Questions 15-20 or 16-20 answer/complete heading.' };
  }

  const matchingEnd = matching.end as PartAMatchingEnd;
  const lastStart = last.start as PartALastStart;
  const expectedMiddleStart = matchingEnd + 1;
  const expectedMiddleEnd = lastStart - 1;
  const middle = blocks.find((block) => (
    block.start === expectedMiddleStart
    && block.end === expectedMiddleEnd
    && isGapType(block.kind)
  )) ?? (
    isGapType(last.kind)
      ? { start: expectedMiddleStart, end: expectedMiddleEnd, kind: otherGapType(last.kind) }
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
    lastStart,
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
