'use client';

import { listeningV2Api } from '@/lib/listening/v2-api';
import {
  useAttemptAnnotations,
  type AttemptAnnotationsState,
  type AttemptQuestionAnnotation,
} from '@/hooks/use-attempt-annotations';

/**
 * Per-question highlight / strikethrough payload persisted to the backend
 * via `PUT /v1/listening/v2/attempts/{id}/annotations`. Aliased from the
 * shared {@link AttemptQuestionAnnotation} so existing
 * `ListeningQuestionAnnotation` imports keep working — the persistence
 * contract is identical across subtests.
 */
export type ListeningQuestionAnnotation = AttemptQuestionAnnotation;
export type ListeningAnnotationsState = AttemptAnnotationsState;

/**
 * `useListeningAnnotations` — thin delegate over {@link useAttemptAnnotations}
 * bound to the Listening V2 API. Debounced 400 ms auto-save of highlight +
 * strikethrough state; all behaviour (debounce, 64 KB cap, last-write-wins,
 * hydrate-on-mount, empty→null clear) lives in the shared hook.
 */
export function useListeningAnnotations(options: {
  attemptId: string | null;
  initialAnnotationsJson?: string | null;
  disabled?: boolean;
}) {
  return useAttemptAnnotations({ ...options, api: listeningV2Api });
}
