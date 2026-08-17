import {
  CLASSIC_PART_A_LAYOUT,
  describePartALayout,
  detectPartALayoutFromBookletText,
  detectPartALayoutFromQuestions,
  expectedPartAQuestionType,
  resolvePartALayout,
  suggestPartALayout,
} from '@/lib/reading-part-a-layout';

const SAMPLE5_HEADINGS = `
Questions 1- 8
For each question, 1-8, decide which text (A, B, C or D) the information comes from. Write the letter A, B, C or D
in the space provided. You may use any letter more than once.
In which text can you find information about
1 treatment for cellulitis caused by less common bacteria?
Questions 9-14
Answer each of the questions, 9-14, with a word or short phrase from one of the texts. Each answer may include
words, numbers or both. You should not write full sentences.
9 Which antibiotic formulation is not suitable for treatment of cellulitis?
Questions 15-20
Complete each of the sentences, 15-20, with a word or short phrase from one of the texts. Each answer may
include words, numbers or both.
15 It may be necessary to carry out of blisters caused by cellulitis.
`;

const CLASSIC_HEADINGS = `
Questions 1-7
For each question, 1-7, decide which text (A, B, C or D) the information comes from.
Questions 8-14
Answer each of the questions, 8-14, with a word or short phrase from one of the texts.
Questions 15-20
Complete each of the sentences, 15-20, with a word or short phrase from one of the texts.
`;

const SWAPPED_HEADINGS = `
Questions 1–7
For each of the questions, 1-7, decide which text (A, B, C or D) the information comes from.
Questions 8-14
Complete each of the sentences, 8-14, with a word or short phrase from one of the texts.
Questions 15-20
Answer each of the questions, 15-20, with a word or short phrase from one of the texts.
`;

function typedPartA(
  matchingEnd: 7 | 8,
  middle: 'ShortAnswer' | 'SentenceCompletion',
  last: 'ShortAnswer' | 'SentenceCompletion',
  lastStart: 15 | 16 = 15,
) {
  return Array.from({ length: 20 }, (_, index) => {
    const displayOrder = index + 1;
    const questionType = displayOrder <= matchingEnd
      ? 'MatchingTextReference'
      : displayOrder < lastStart
        ? middle
        : last;
    return { displayOrder, questionType };
  });
}

describe('reading-part-a-layout', () => {
  it('keeps the classic 1-7 / 8-14 / 15-20 mapping by default', () => {
    expect(expectedPartAQuestionType(1)).toBe('MatchingTextReference');
    expect(expectedPartAQuestionType(7)).toBe('MatchingTextReference');
    expect(expectedPartAQuestionType(8)).toBe('ShortAnswer');
    expect(expectedPartAQuestionType(14)).toBe('ShortAnswer');
    expect(expectedPartAQuestionType(15)).toBe('SentenceCompletion');
    expect(expectedPartAQuestionType(20)).toBe('SentenceCompletion');
  });

  it('detects Sample 5 as 1-8 matching, 9-14 short answer, 15-20 sentence completion', () => {
    const detected = detectPartALayoutFromBookletText(SAMPLE5_HEADINGS);
    expect(detected.ok).toBe(true);
    expect(detected.layout).toEqual({
      matchingEnd: 8,
      lastStart: 15,
      middleType: 'ShortAnswer',
      lastType: 'SentenceCompletion',
    });
    expect(expectedPartAQuestionType(8, detected.layout!)).toBe('MatchingTextReference');
    expect(expectedPartAQuestionType(9, detected.layout!)).toBe('ShortAnswer');
    expect(expectedPartAQuestionType(15, detected.layout!)).toBe('SentenceCompletion');
  });

  it('detects classic headings and the swapped 8-14 complete / 15-20 answer variant', () => {
    expect(detectPartALayoutFromBookletText(CLASSIC_HEADINGS).layout).toEqual(CLASSIC_PART_A_LAYOUT);
    expect(detectPartALayoutFromBookletText(SWAPPED_HEADINGS).layout).toEqual({
      matchingEnd: 7,
      lastStart: 15,
      middleType: 'SentenceCompletion',
      lastType: 'ShortAnswer',
    });
  });

  it('detects Jayden 8-15 answer / 16-20 complete layout', () => {
    const headings = `
Questions 1-7
For each question, 1-7, decide which text (A, B, C or D) the information comes from.
Questions 8-15
Answer each of the questions, 8-15, with a word or short phrase from one of the texts.
Questions 16-20
Complete each of the sentences, 16-20, with a word or short phrase from one of the texts.
`;
    const detected = detectPartALayoutFromBookletText(headings);
    expect(detected.ok).toBe(true);
    expect(detected.layout).toEqual({
      matchingEnd: 7,
      lastStart: 16,
      middleType: 'ShortAnswer',
      lastType: 'SentenceCompletion',
    });
    expect(expectedPartAQuestionType(15, detected.layout!)).toBe('ShortAnswer');
    expect(expectedPartAQuestionType(16, detected.layout!)).toBe('SentenceCompletion');
    expect(detectPartALayoutFromQuestions(typedPartA(7, 'ShortAnswer', 'SentenceCompletion', 16)).ok).toBe(true);
  });

  it('accepts all four official typed layouts and rejects mixed blocks', () => {
    expect(detectPartALayoutFromQuestions(typedPartA(7, 'ShortAnswer', 'SentenceCompletion')).ok).toBe(true);
    expect(detectPartALayoutFromQuestions(typedPartA(7, 'SentenceCompletion', 'ShortAnswer')).ok).toBe(true);
    expect(detectPartALayoutFromQuestions(typedPartA(8, 'ShortAnswer', 'SentenceCompletion')).ok).toBe(true);
    expect(detectPartALayoutFromQuestions(typedPartA(8, 'SentenceCompletion', 'ShortAnswer')).ok).toBe(true);

    const mixed = typedPartA(7, 'ShortAnswer', 'SentenceCompletion');
    mixed[7] = { displayOrder: 8, questionType: 'SentenceCompletion' };
    expect(detectPartALayoutFromQuestions(mixed).ok).toBe(false);
  });

  it('suggests 1-8 matching as soon as Q8 is entered as matching', () => {
    const layout = suggestPartALayout([
      { displayOrder: 8, questionType: 'MatchingTextReference' },
    ]);
    expect(layout.matchingEnd).toBe(8);
    expect(expectedPartAQuestionType(8, layout)).toBe('MatchingTextReference');
    expect(expectedPartAQuestionType(9, layout)).toBe('ShortAnswer');
  });

  it('prefers booklet text over the classic default when resolving a layout', () => {
    const resolved = resolvePartALayout({ bookletText: SAMPLE5_HEADINGS, questions: [] });
    expect(resolved.source).toBe('booklet');
    expect(describePartALayout(resolved.layout!)).toContain('Questions 1-8 matching');
  });
});
