import type { ListeningSectionCode } from '@/lib/listening-sections';

/**
 * Listening V2 — FSM transition table. Mirror of the backend
 * `ListeningFsmTransitions.cs`. Kept in sync by the
 * `transitions.parity.test.ts` Vitest meta-test.
 */

export const LISTENING_FSM_STATES = [
  'intro',
  'a1_preview',
  'a1_audio',
  'a1_review',
  'a2_preview',
  'a2_audio',
  'a2_review',
  'b_intro',
  'b_audio',
  'c1_preview',
  'c1_audio',
  'c1_review',
  'c2_preview',
  'c2_audio',
  'c2_review',
  'c2_final_review',
  'submitted',
] as const;

export type ListeningFsmState = (typeof LISTENING_FSM_STATES)[number] | 'paywalled';

/** Linear forward path (CBT / OET-Home strict modes). */
export const LISTENING_FORWARD_PATH: ListeningFsmState[] = [...LISTENING_FSM_STATES];

export function nextListeningState(
  current: ListeningFsmState,
): ListeningFsmState | null {
  const idx = LISTENING_FORWARD_PATH.indexOf(current);
  if (idx < 0 || idx >= LISTENING_FORWARD_PATH.length - 1) return null;
  return LISTENING_FORWARD_PATH[idx + 1];
}

export function listeningPartFor(state: ListeningFsmState): string | null {
  switch (state) {
    case 'a1_preview':
    case 'a1_audio':
    case 'a1_review':
      return 'A1';
    case 'a2_preview':
    case 'a2_audio':
    case 'a2_review':
      return 'A2';
    case 'b_intro':
    case 'b_audio':
      return 'B';
    case 'c1_preview':
    case 'c1_audio':
    case 'c1_review':
      return 'C1';
    case 'c2_preview':
    case 'c2_audio':
    case 'c2_review':
    case 'c2_final_review':
      return 'C2';
    default:
      return null;
  }
}

export function isAudioState(state: ListeningFsmState): boolean {
  return state.endsWith('_audio');
}

export function isReviewState(state: ListeningFsmState): boolean {
  return state.endsWith('_review') || state === 'c2_final_review';
}

export function isPreviewState(state: ListeningFsmState): boolean {
  return state.endsWith('_preview') || state === 'b_intro';
}

export type ListeningPlayerPhase = 'preview' | 'audio' | 'review';

export interface ListeningPlayerPosition {
  section: ListeningSectionCode;
  phase: ListeningPlayerPhase;
}

export function listeningPositionForState(state: ListeningFsmState): ListeningPlayerPosition | null {
  switch (state) {
    case 'a1_preview':
      return { section: 'A1', phase: 'preview' };
    case 'a1_audio':
      return { section: 'A1', phase: 'audio' };
    case 'a1_review':
      return { section: 'A1', phase: 'review' };
    case 'a2_preview':
      return { section: 'A2', phase: 'preview' };
    case 'a2_audio':
      return { section: 'A2', phase: 'audio' };
    case 'a2_review':
      return { section: 'A2', phase: 'review' };
    case 'b_intro':
      return { section: 'B', phase: 'preview' };
    case 'b_audio':
      return { section: 'B', phase: 'audio' };
    case 'c1_preview':
      return { section: 'C1', phase: 'preview' };
    case 'c1_audio':
      return { section: 'C1', phase: 'audio' };
    case 'c1_review':
      return { section: 'C1', phase: 'review' };
    case 'c2_preview':
      return { section: 'C2', phase: 'preview' };
    case 'c2_audio':
      return { section: 'C2', phase: 'audio' };
    case 'c2_review':
    case 'c2_final_review':
      return { section: 'C2', phase: 'review' };
    default:
      return null;
  }
}

export function listeningStateForPosition(
  section: ListeningSectionCode,
  phase: ListeningPlayerPhase,
): ListeningFsmState | null {
  switch (`${section}:${phase}`) {
    case 'A1:preview':
      return 'a1_preview';
    case 'A1:audio':
      return 'a1_audio';
    case 'A1:review':
      return 'a1_review';
    case 'A2:preview':
      return 'a2_preview';
    case 'A2:audio':
      return 'a2_audio';
    case 'A2:review':
      return 'a2_review';
    case 'B:preview':
      return 'b_intro';
    case 'B:audio':
      return 'b_audio';
    case 'C1:preview':
      return 'c1_preview';
    case 'C1:audio':
      return 'c1_audio';
    case 'C1:review':
      return 'c1_review';
    case 'C2:preview':
      return 'c2_preview';
    case 'C2:audio':
      return 'c2_audio';
    case 'C2:review':
      return 'c2_review';
    default:
      return null;
  }
}

export function listeningWindowSeconds(windowRemainingMs: number | null | undefined): number {
  if (windowRemainingMs == null || !Number.isFinite(windowRemainingMs)) return 0;
  return Math.max(0, Math.ceil(windowRemainingMs / 1000));
}
