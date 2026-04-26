import { describe, it, expect, beforeEach } from 'vitest';
import { useExpertStore, type LocalReviewDraft } from '../expert-store';
import type { AnchoredComment, ExpertChecklistItem, TimestampComment } from '@/lib/types/expert';

function makeDraft(overrides: Partial<Omit<LocalReviewDraft, 'reviewId' | 'updatedAt'>> = {}) {
  return {
    scores: { content: 5, fluency: 4 },
    criterionComments: { content: 'good' },
    finalComment: 'overall good',
    anchoredComments: [] as AnchoredComment[],
    timestampComments: [] as TimestampComment[],
    scratchpad: '',
    checklistItems: [] as ExpertChecklistItem[],
    version: 1,
    ...overrides,
  };
}

describe('useExpertStore', () => {
  beforeEach(() => {
    // Reset store between tests.
    useExpertStore.setState({
      playbackSpeed: 1,
      currentTimestamp: 0,
      isPlaying: false,
      reviewDrafts: {},
    });
    if (typeof localStorage !== 'undefined') localStorage.clear();
  });

  describe('playback state', () => {
    it('has expected defaults', () => {
      const s = useExpertStore.getState();
      expect(s.playbackSpeed).toBe(1);
      expect(s.currentTimestamp).toBe(0);
      expect(s.isPlaying).toBe(false);
    });

    it('setPlaybackSpeed updates value', () => {
      useExpertStore.getState().setPlaybackSpeed(1.5);
      expect(useExpertStore.getState().playbackSpeed).toBe(1.5);
    });

    it('setCurrentTimestamp updates value', () => {
      useExpertStore.getState().setCurrentTimestamp(42.5);
      expect(useExpertStore.getState().currentTimestamp).toBe(42.5);
    });

    it('setIsPlaying toggles flag', () => {
      useExpertStore.getState().setIsPlaying(true);
      expect(useExpertStore.getState().isPlaying).toBe(true);
      useExpertStore.getState().setIsPlaying(false);
      expect(useExpertStore.getState().isPlaying).toBe(false);
    });
  });

  describe('reviewDrafts', () => {
    it('upsertReviewDraft creates a new entry and stamps reviewId', () => {
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft());
      const draft = useExpertStore.getState().getReviewDraft('R-1');
      expect(draft).not.toBeNull();
      expect(draft!.reviewId).toBe('R-1');
      expect(draft!.scores.content).toBe(5);
      expect(draft!.version).toBe(1);
    });

    it('autostamps updatedAt as ISO string when omitted', () => {
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft());
      const draft = useExpertStore.getState().getReviewDraft('R-1')!;
      expect(typeof draft.updatedAt).toBe('string');
      expect(() => new Date(draft.updatedAt).toISOString()).not.toThrow();
    });

    it('preserves a caller-provided updatedAt', () => {
      const explicit = '2026-01-01T00:00:00.000Z';
      useExpertStore.getState().upsertReviewDraft('R-1', { ...makeDraft(), updatedAt: explicit });
      expect(useExpertStore.getState().getReviewDraft('R-1')!.updatedAt).toBe(explicit);
    });

    it('upsert replaces an existing draft for the same reviewId', () => {
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft({ finalComment: 'v1' }));
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft({ finalComment: 'v2', version: 2 }));
      const draft = useExpertStore.getState().getReviewDraft('R-1')!;
      expect(draft.finalComment).toBe('v2');
      expect(draft.version).toBe(2);
    });

    it('upsert isolates drafts by reviewId', () => {
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft({ finalComment: 'a' }));
      useExpertStore.getState().upsertReviewDraft('R-2', makeDraft({ finalComment: 'b' }));
      expect(useExpertStore.getState().getReviewDraft('R-1')!.finalComment).toBe('a');
      expect(useExpertStore.getState().getReviewDraft('R-2')!.finalComment).toBe('b');
    });

    it('getReviewDraft returns null for unknown reviewId', () => {
      expect(useExpertStore.getState().getReviewDraft('missing')).toBeNull();
    });

    it('clearReviewDraft removes only the targeted draft', () => {
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft());
      useExpertStore.getState().upsertReviewDraft('R-2', makeDraft());
      useExpertStore.getState().clearReviewDraft('R-1');
      expect(useExpertStore.getState().getReviewDraft('R-1')).toBeNull();
      expect(useExpertStore.getState().getReviewDraft('R-2')).not.toBeNull();
    });

    it('clearReviewDraft is a no-op for unknown reviewId', () => {
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft());
      useExpertStore.getState().clearReviewDraft('does-not-exist');
      expect(useExpertStore.getState().getReviewDraft('R-1')).not.toBeNull();
    });

    it('upsertReviewDraft does NOT mutate the input object', () => {
      const input = makeDraft({ finalComment: 'orig' });
      useExpertStore.getState().upsertReviewDraft('R-1', input);
      input.finalComment = 'mutated-after';
      // Stored copy should still reflect the original value at the time of upsert
      // because the store does its own field-by-field copy.
      expect(useExpertStore.getState().getReviewDraft('R-1')!.finalComment).toBe('orig');
    });
  });

  describe('persistence partialization', () => {
    it('persists ONLY reviewDrafts, not playback state', () => {
      useExpertStore.getState().setPlaybackSpeed(2);
      useExpertStore.getState().setCurrentTimestamp(99);
      useExpertStore.getState().setIsPlaying(true);
      useExpertStore.getState().upsertReviewDraft('R-1', makeDraft());

      const persisted = localStorage.getItem('expert-console-store');
      expect(persisted).not.toBeNull();
      const parsed = JSON.parse(persisted!) as { state: Record<string, unknown> };
      expect(parsed.state).toHaveProperty('reviewDrafts');
      expect(parsed.state).not.toHaveProperty('playbackSpeed');
      expect(parsed.state).not.toHaveProperty('currentTimestamp');
      expect(parsed.state).not.toHaveProperty('isPlaying');
    });
  });
});
