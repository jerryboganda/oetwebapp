import { describe, expect, it } from 'vitest';
import {
  LISTENING_FSM_STATES,
  LISTENING_FORWARD_PATH,
  listeningPositionForState,
  listeningStateForPosition,
  listeningWindowSeconds,
  nextListeningState,
  listeningPartFor,
  isPreviewState,
} from '@/lib/listening/transitions';

/**
 * Listening V2 FSM parity test. The TypeScript transitions table MUST stay
 * byte-for-byte identical to `backend/src/OetLearner.Api/Services/Listening/
 * ListeningFsmTransitions.cs`. If the backend table changes you MUST update
 * `lib/listening/transitions.ts` in the same PR — drift breaks the two-step
 * confirm-token protocol because the client computes the destination state
 * locally before posting.
 */
describe('Listening FSM transitions — TS/C# parity', () => {
  it('forward path order matches the canonical CBT sequence', () => {
    expect(LISTENING_FORWARD_PATH).toEqual([
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
    ]);
  });

  it('LISTENING_FSM_STATES is a superset of LISTENING_FORWARD_PATH (plus paywalled)', () => {
    for (const s of LISTENING_FORWARD_PATH) {
      expect(LISTENING_FSM_STATES).toContain(s);
    }
  });

  it('nextListeningState walks the forward path', () => {
    expect(nextListeningState('intro')).toBe('a1_preview');
    expect(nextListeningState('a1_review')).toBe('a2_preview');
    expect(nextListeningState('a2_review')).toBe('b_intro');
    expect(nextListeningState('b_intro')).toBe('b_audio');
    expect(nextListeningState('b_audio')).toBe('c1_preview');
    expect(nextListeningState('c2_final_review')).toBe('submitted');
    expect(nextListeningState('submitted')).toBeNull();
  });

  it('listeningPartFor returns canonical part labels', () => {
    expect(listeningPartFor('a1_preview')).toBe('A1');
    expect(listeningPartFor('a2_audio')).toBe('A2');
    expect(listeningPartFor('b_intro')).toBe('B');
    expect(listeningPartFor('c1_review')).toBe('C1');
    expect(listeningPartFor('c2_final_review')).toBe('C2');
    expect(listeningPartFor('intro')).toBeNull();
    expect(listeningPartFor('submitted')).toBeNull();
  });

  it('maps FSM states to the active player section/phase model', () => {
    expect(listeningPositionForState('a1_preview')).toEqual({ section: 'A1', phase: 'preview' });
    expect(listeningPositionForState('a2_audio')).toEqual({ section: 'A2', phase: 'audio' });
    expect(listeningPositionForState('b_intro')).toEqual({ section: 'B', phase: 'preview' });
    expect(listeningPositionForState('c1_review')).toEqual({ section: 'C1', phase: 'review' });
    expect(listeningPositionForState('c2_final_review')).toEqual({ section: 'C2', phase: 'review' });
    expect(listeningPositionForState('intro')).toBeNull();
  });

  it('maps active player positions back to client-reachable FSM states', () => {
    expect(listeningStateForPosition('A1', 'preview')).toBe('a1_preview');
    expect(listeningStateForPosition('A2', 'audio')).toBe('a2_audio');
    expect(listeningStateForPosition('B', 'preview')).toBe('b_intro');
    expect(listeningStateForPosition('B', 'review')).toBeNull();
    expect(listeningStateForPosition('C2', 'review')).toBe('c2_review');
  });

  it('normalizes server window milliseconds to display seconds', () => {
    expect(listeningWindowSeconds(30_000)).toBe(30);
    expect(listeningWindowSeconds(30_001)).toBe(31);
    expect(listeningWindowSeconds(-1)).toBe(0);
    expect(listeningWindowSeconds(null)).toBe(0);
  });

  it('keeps preview-state helper aligned with the backend semantics', () => {
    expect(isPreviewState('a1_preview')).toBe(true);
    expect(isPreviewState('b_intro')).toBe(true);
    expect(isPreviewState('intro')).toBe(false);
    expect(isPreviewState('a1_audio')).toBe(false);
  });
});
