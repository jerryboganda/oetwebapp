'use client';

import { readingAnnotationsApi } from '@/lib/reading-authoring-api';
import {
  useAttemptAnnotations,
  type AttemptAnnotationsState,
  type AttemptQuestionAnnotation,
} from '@/hooks/use-attempt-annotations';

/**
 * Per-question rule-out / highlight payload for a Reading attempt. Aliased
 * from the shared {@link AttemptQuestionAnnotation} — the persistence contract
 * is identical to Listening.
 */
export type ReadingQuestionAnnotation = AttemptQuestionAnnotation;
export type ReadingAnnotationsState = AttemptAnnotationsState;

/**
 * `useReadingAnnotations` — thin delegate over {@link useAttemptAnnotations}
 * bound to the Reading API (`PUT/GET /v1/reading-papers/attempts/{id}/annotations`).
 * Identical behaviour to `useListeningAnnotations`; only the endpoint differs.
 */
export function useReadingAnnotations(options: {
  attemptId: string | null;
  initialAnnotationsJson?: string | null;
  disabled?: boolean;
}) {
  return useAttemptAnnotations({ ...options, api: readingAnnotationsApi });
}
