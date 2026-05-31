import { describe, expect, it } from 'vitest';

import {
  writingAppealRequestSchema,
  writingLessonQuizSubmissionSchema,
  writingProfileSchema,
  writingSubmissionSchema,
} from './zod';

describe('writing zod schemas', () => {
  it('accepts a valid profile and applies the expected defaults', () => {
    const parsed = writingProfileSchema.parse({
      profession: 'medicine',
      subDiscipline: null,
      yearsExperience: 12,
      targetBand: 'B',
      examDate: '2026-06-15',
      daysPerWeek: 5,
      minutesPerDay: 60,
      targetCountry: 'UK',
      letterTypeFocus: ['LT-RR', 'LT-DG'],
    });

    expect(parsed).toMatchObject({
      profession: 'medicine',
      targetBand: 'B',
      examDate: '2026-06-15',
      optInCommunity: false,
      optInLeaderboard: false,
      optInDataForTraining: false,
    });
  });

  it('validates writing submissions and accepts the optional input source', () => {
    const parsed = writingSubmissionSchema.parse({
      scenarioId: 'scenario-1',
      mode: 'practice',
      letterContent: 'This is a long enough response for the submission schema to accept it.',
      wordCount: 123,
      timeSpentSeconds: 600,
      inputSource: 'paper-ocr',
    });

    expect(parsed.inputSource).toBe('paper-ocr');
    expect(parsed.wordCount).toBe(123);
  });

  it('rejects too-short appeals and enforces the five-answer quiz shape', () => {
    expect(
      writingAppealRequestSchema.safeParse({ reason: 'Too short' }).success,
    ).toBe(false);

    expect(
      writingLessonQuizSubmissionSchema.safeParse({
        lessonId: 'lesson-1',
        quizAnswers: [0, 1, 2, 3, 4],
      }).success,
    ).toBe(true);

    expect(
      writingLessonQuizSubmissionSchema.safeParse({
        lessonId: 'lesson-1',
        quizAnswers: [0, 1, 2],
      }).success,
    ).toBe(false);
  });
});