import {
  detectPartALayoutFromBookletText,
  detectPartALayoutFromQuestions,
  expectedPartAQuestionType,
  suggestPartALayout,
} from '../../lib/reading-part-a-layout.ts';

function assert(condition: unknown, message: string): void {
  if (!condition) throw new Error(message);
}

function typed(
  matchingEnd: 7 | 8,
  middle: 'ShortAnswer' | 'SentenceCompletion',
  last: 'ShortAnswer' | 'SentenceCompletion',
) {
  return Array.from({ length: 20 }, (_, index) => {
    const displayOrder = index + 1;
    return {
      displayOrder,
      questionType: displayOrder <= matchingEnd
        ? 'MatchingTextReference'
        : displayOrder <= 14
          ? middle
          : last,
    };
  });
}

const sample5 = detectPartALayoutFromBookletText(`
Questions 1- 8
For each question, 1-8, decide which text (A, B, C or D) the information comes from.
Questions 9-14
Answer each of the questions, 9-14, with a word or short phrase from one of the texts.
Questions 15-20
Complete each of the sentences, 15-20, with a word or short phrase from one of the texts.
`);
assert(sample5.ok && sample5.layout?.matchingEnd === 8, `Sample 5 should be 1-8: ${sample5.error}`);
assert(expectedPartAQuestionType(8, sample5.layout!) === 'MatchingTextReference', 'Q8 matching');
assert(expectedPartAQuestionType(9, sample5.layout!) === 'ShortAnswer', 'Q9 short answer');
assert(expectedPartAQuestionType(15, sample5.layout!) === 'SentenceCompletion', 'Q15 complete');

const swapped = detectPartALayoutFromBookletText(`
Questions 1-7
decide which text (A, B, C or D)
Questions 8-14
Complete the following sentences
Questions 15-20
Answer the following
`);
assert(swapped.ok && swapped.layout?.middleType === 'SentenceCompletion', `swapped middle: ${swapped.error}`);
assert(swapped.layout?.lastType === 'ShortAnswer', 'swapped last should be answer');

assert(detectPartALayoutFromQuestions(typed(7, 'ShortAnswer', 'SentenceCompletion')).ok, 'classic typed');
assert(detectPartALayoutFromQuestions(typed(7, 'SentenceCompletion', 'ShortAnswer')).ok, 'swapped typed');
assert(detectPartALayoutFromQuestions(typed(8, 'ShortAnswer', 'SentenceCompletion')).ok, '1-8 SA typed');
assert(detectPartALayoutFromQuestions(typed(8, 'SentenceCompletion', 'ShortAnswer')).ok, '1-8 SC typed');

const mixed = typed(7, 'ShortAnswer', 'SentenceCompletion');
mixed[7] = { displayOrder: 8, questionType: 'SentenceCompletion' };
assert(!detectPartALayoutFromQuestions(mixed).ok, 'mixed 8-14 must fail');

assert(suggestPartALayout([{ displayOrder: 8, questionType: 'MatchingTextReference' }]).matchingEnd === 8, 'Q8 matching hint');

const kaplan = detectPartALayoutFromBookletText(`
Questions 1-6
For each question, 1-6, decide which text (A, B, C or D) the information comes from.
Questions 7-12
Complete each of the sentences, 7-12, with a word or short phrase from one of the texts.
Questions 13-20
Answer each of the questions, 13-20, with a word or short phrase from one of the texts.
`);
assert(kaplan.ok && kaplan.layout?.lastStart === 13, `Kaplan lastStart=13: ${kaplan.error}`);
assert(kaplan.layout?.matchingEnd === 6, 'Kaplan matching 1-6');
assert(kaplan.layout?.middleType === 'SentenceCompletion', 'Kaplan middle SC');
assert(kaplan.layout?.lastType === 'ShortAnswer', 'Kaplan last SA');

console.log('reading-part-a-layout smoke: Sample 5 1-8, swapped 8-14, lastStart=13, and all four typed layouts passed');
