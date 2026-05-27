/**
 * Writing Module V2 — Zod schemas for client-side form validation.
 *
 * Server-side validation is duplicated (see backend Contracts/Writing*.cs);
 * never trust client. These schemas exist to give immediate inline feedback
 * in forms and to coerce/sanitize values before sending to the API.
 */

import { z } from 'zod';

const writingProfessionEnum = z.enum(['medicine', 'pharmacy', 'nursing', 'other']);
const writingLetterTypeEnum = z.enum(['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-NM', 'LT-RP']);
const writingEditorModeEnum = z.enum([
  'practice',
  'coached',
  'timed',
  'diagnostic',
  'mock',
  'revision',
]);

// ─────────────────────────────────────────────────────────────────────────────
// Profile / onboarding
// ─────────────────────────────────────────────────────────────────────────────

export const writingProfileSchema = z.object({
  profession: writingProfessionEnum,
  subDiscipline: z.string().max(120).nullable().optional(),
  yearsExperience: z.number().int().min(0).max(60).nullable().optional(),
  targetBand: z.enum(['A', 'B', 'B+', 'C+', 'C']),
  examDate: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, 'Use YYYY-MM-DD')
    .nullable(),
  daysPerWeek: z.number().int().min(1).max(7),
  minutesPerDay: z.number().int().min(15).max(240),
  targetCountry: z.string().min(2).max(80),
  letterTypeFocus: z.array(writingLetterTypeEnum).min(1).max(6),
  optInCommunity: z.boolean().optional().default(false),
  optInLeaderboard: z.boolean().optional().default(false),
  optInDataForTraining: z.boolean().optional().default(false),
});

export type WritingProfileFormValues = z.infer<typeof writingProfileSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Diagnostic submit
// ─────────────────────────────────────────────────────────────────────────────

export const writingDiagnosticSubmitSchema = z.object({
  letterContent: z.string().min(50, 'Letter is too short to grade').max(10000),
  wordCount: z.number().int().min(50).max(2000),
  timeSpentSeconds: z.number().int().min(60).max(60 * 90),
});

export type WritingDiagnosticSubmitValues = z.infer<typeof writingDiagnosticSubmitSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Submission (general practice)
// ─────────────────────────────────────────────────────────────────────────────

export const writingSubmissionSchema = z.object({
  scenarioId: z.string().min(1),
  mode: writingEditorModeEnum,
  letterContent: z.string().min(50, 'Letter is too short to grade').max(10000),
  wordCount: z.number().int().min(50).max(2000),
  timeSpentSeconds: z.number().int().min(0).max(60 * 90),
  inputSource: z.enum(['editor', 'paper-ocr', 'voice-draft']).optional(),
});

export type WritingSubmissionValues = z.infer<typeof writingSubmissionSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Drill response
// ─────────────────────────────────────────────────────────────────────────────

export const writingDrillResponseSchema = z.object({
  drillId: z.string().min(1),
  responseText: z.string().max(2000).default(''),
  selectedOptionIndex: z.number().int().min(0).optional(),
  orderedItems: z.array(z.string()).optional(),
});

export type WritingDrillResponseValues = z.infer<typeof writingDrillResponseSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Mock submission
// ─────────────────────────────────────────────────────────────────────────────

export const writingMockSubmissionSchema = z.object({
  letterContent: z.string().min(50).max(10000),
  wordCount: z.number().int().min(50).max(2000),
  timeSpentSeconds: z.number().int().min(60).max(60 * 90),
});

export type WritingMockSubmissionValues = z.infer<typeof writingMockSubmissionSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Dispute a canon violation
// ─────────────────────────────────────────────────────────────────────────────

export const writingDisputeViolationSchema = z.object({
  ruleId: z.string().min(1),
  violationId: z.string().min(1),
  reason: z.string().min(10, 'Tell us why this detection is incorrect (10+ chars)').max(1000),
});

export type WritingDisputeViolationValues = z.infer<typeof writingDisputeViolationSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Appeal request
// ─────────────────────────────────────────────────────────────────────────────

export const writingAppealRequestSchema = z.object({
  reason: z.string().min(20, 'Briefly explain why you believe the grade is wrong (20+ chars)').max(2000),
});

export type WritingAppealRequestValues = z.infer<typeof writingAppealRequestSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Case-note drill response
// ─────────────────────────────────────────────────────────────────────────────

export const writingCaseNoteDrillResponseSchema = z.object({
  selectedIndices: z.array(z.number().int().min(0)).min(0).max(200),
});

export type WritingCaseNoteDrillResponseValues = z.infer<typeof writingCaseNoteDrillResponseSchema>;

// ─────────────────────────────────────────────────────────────────────────────
// Lesson quiz submission
// ─────────────────────────────────────────────────────────────────────────────

export const writingLessonQuizSubmissionSchema = z.object({
  lessonId: z.string().min(1),
  quizAnswers: z.array(z.number().int().min(0)).length(5),
});

export type WritingLessonQuizSubmissionValues = z.infer<typeof writingLessonQuizSubmissionSchema>;
