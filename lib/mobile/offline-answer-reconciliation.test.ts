import { describe, expect, it } from 'vitest';
import { reconcileOfflineAnswer } from './offline-answer-reconciliation';

describe('offline answer reconciliation', () => {
  const queued = { questionId: 'q1', value: 'new', baseValue: 'old' };

  it('replays only when the server still has the confirmed base value', () => {
    expect(reconcileOfflineAnswer('old', queued)).toBe('safe-to-replay');
  });

  it('recognises a request that reached the server before the response was lost', () => {
    expect(reconcileOfflineAnswer('new', queued)).toBe('already-synced');
  });

  it('keeps a different server answer authoritative', () => {
    expect(reconcileOfflineAnswer('another-answer', queued)).toBe('conflict');
  });
});
