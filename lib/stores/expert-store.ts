import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AnchoredComment, TimestampComment } from '@/lib/types/expert';

export interface LocalReviewDraft {
  reviewId: string;
  scores: Record<string, number>;
  criterionComments: Record<string, string>;
  finalComment: string;
  anchoredComments: AnchoredComment[];
  timestampComments: TimestampComment[];
  version?: number;
  updatedAt: string;
}

interface ExpertState {
  playbackSpeed: number;
  setPlaybackSpeed: (speed: number) => void;
  currentTimestamp: number;
  setCurrentTimestamp: (time: number) => void;
  isPlaying: boolean;
  setIsPlaying: (playing: boolean) => void;

  reviewDrafts: Record<string, LocalReviewDraft>;
  upsertReviewDraft: (reviewId: string, draft: Omit<LocalReviewDraft, 'reviewId' | 'updatedAt'> & { updatedAt?: string }) => void;
  getReviewDraft: (reviewId: string) => LocalReviewDraft | null;
  clearReviewDraft: (reviewId: string) => void;
}

export const useExpertStore = create<ExpertState>()(
  persist(
    (set, get) => ({
      playbackSpeed: 1,
      setPlaybackSpeed: (speed) => set({ playbackSpeed: speed }),
      currentTimestamp: 0,
      setCurrentTimestamp: (time) => set({ currentTimestamp: time }),
      isPlaying: false,
      setIsPlaying: (playing) => set({ isPlaying: playing }),

      reviewDrafts: {},
      upsertReviewDraft: (reviewId, draft) => set((state) => ({
        reviewDrafts: {
          ...state.reviewDrafts,
          [reviewId]: {
            reviewId,
            scores: draft.scores,
            criterionComments: draft.criterionComments,
            finalComment: draft.finalComment,
            anchoredComments: draft.anchoredComments,
            timestampComments: draft.timestampComments,
            version: draft.version,
            updatedAt: draft.updatedAt ?? new Date().toISOString(),
          },
        },
      })),
      getReviewDraft: (reviewId) => get().reviewDrafts[reviewId] ?? null,
      clearReviewDraft: (reviewId) => set((state) => {
        const { [reviewId]: _removed, ...rest } = state.reviewDrafts;
        return { reviewDrafts: rest };
      }),
    }),
    {
      name: 'expert-console-store',
      partialize: (state) => ({ reviewDrafts: state.reviewDrafts }),
    },
  ),
);
