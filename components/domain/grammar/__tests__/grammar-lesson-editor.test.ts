import { describe, it, expect } from 'vitest';
import { draftToApi, emptyDraft, type LessonDraft } from '../grammar-lesson-editor';

describe('grammar-lesson-editor / draftToApi', () => {
  it('produces a well-formed GrammarLessonUpsertPayload from emptyDraft', () => {
    const payload = draftToApi(emptyDraft());

    expect(payload.examTypeCode).toBe('oet');
    expect(payload.topicId).toBeNull();
    expect(payload.title).toBe('');
    expect(payload.description).toBe('');
    expect(payload.level).toBe('intermediate');
    expect(Array.isArray(payload.contentBlocks)).toBe(true);
    expect(payload.contentBlocks.length).toBeGreaterThan(0);
    expect(payload.exercises).toEqual([]);
    expect(Array.isArray(payload.prerequisiteLessonIds)).toBe(true);
  });

  it('normalises content block / exercise sort order if zero', () => {
    const draft: LessonDraft = {
      ...emptyDraft(),
      title: 'Passive voice',
      description: 'Clinical passive patterns',
      category: 'passive_voice',
      sourceProvenance: 'Dr Hesham grammar rulebook',
      contentBlocks: [
        { sortOrder: 0, type: 'prose', contentMarkdown: 'Overview' },
        { sortOrder: 0, type: 'example', contentMarkdown: 'Example' },
      ],
      exercises: [
        {
          sortOrder: 0,
          type: 'mcq',
          promptMarkdown: 'Which is passive?',
          options: [
            { id: 'a', label: 'The doctor examined the patient.' },
            { id: 'b', label: 'The patient was examined.' },
          ],
          correctAnswer: 'b',
          acceptedAnswers: [],
          explanationMarkdown: 'Option b uses was + past participle.',
          difficulty: 'intermediate',
          points: 1,
        },
      ],
    };

    const payload = draftToApi(draft);

    expect(payload.contentBlocks[0].sortOrder).toBe(1);
    expect(payload.contentBlocks[1].sortOrder).toBe(2);
    expect(payload.exercises[0].sortOrder).toBe(1);
    // id on blocks/exercises is nullable at payload boundary
    expect(payload.contentBlocks[0].id).toBeNull();
    expect(payload.exercises[0].id).toBeNull();
  });

  it('preserves MCQ options and correctAnswer verbatim', () => {
    const draft: LessonDraft = {
      ...emptyDraft(),
      title: 't',
      description: 'd',
      category: 'c',
      sourceProvenance: 's',
      exercises: [
        {
          sortOrder: 1,
          type: 'mcq',
          promptMarkdown: 'p',
          options: [
            { id: 'a', label: 'A' },
            { id: 'b', label: 'B' },
          ],
          correctAnswer: 'a',
          acceptedAnswers: [],
          explanationMarkdown: 'e',
          difficulty: 'beginner',
          points: 1,
        },
      ],
    };
    const payload = draftToApi(draft);
    expect(payload.exercises[0].correctAnswer).toBe('a');
    expect(payload.exercises[0].options).toEqual([
      { id: 'a', label: 'A' },
      { id: 'b', label: 'B' },
    ]);
  });

  it('preserves matching-pair correctAnswer as array', () => {
    const pairs = [
      { left: 'The patient was admitted', right: 'after assessment' },
      { left: 'The chart was updated', right: 'by the RN' },
    ];
    const draft: LessonDraft = {
      ...emptyDraft(),
      title: 't',
      description: 'd',
      category: 'c',
      sourceProvenance: 's',
      exercises: [
        {
          sortOrder: 1,
          type: 'matching',
          promptMarkdown: 'Match',
          options: pairs,
          correctAnswer: pairs,
          acceptedAnswers: [],
          explanationMarkdown: 'e',
          difficulty: 'intermediate',
          points: 2,
        },
      ],
    };
    const payload = draftToApi(draft);
    expect(payload.exercises[0].options).toEqual(pairs);
    expect(payload.exercises[0].correctAnswer).toEqual(pairs);
  });

  it('emits empty array for exercise options if source is null/undefined', () => {
    const draft: LessonDraft = {
      ...emptyDraft(),
      title: 't',
      description: 'd',
      category: 'c',
      sourceProvenance: 's',
      exercises: [
        {
          sortOrder: 1,
          type: 'fill_blank',
          promptMarkdown: 'The nurse ___ the chart.',
          options: undefined,
          correctAnswer: 'updated',
          acceptedAnswers: ['updated', 'revised'],
          explanationMarkdown: 'Simple past in clinical narrative.',
          difficulty: 'beginner',
          points: 1,
        },
      ],
    };
    const payload = draftToApi(draft);
    expect(payload.exercises[0].options).toEqual([]);
    expect(payload.exercises[0].acceptedAnswers).toEqual(['updated', 'revised']);
  });
});
