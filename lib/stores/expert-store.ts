import { create } from 'zustand';
import type { AnchoredComment, TimestampComment } from '@/lib/types/expert';

interface ExpertState {
  activeReviewId: string | null;
  setActiveReviewId: (id: string | null) => void;
  
  // Speaking Review specific
  playbackSpeed: number;
  setPlaybackSpeed: (speed: number) => void;
  currentTimestamp: number;
  setCurrentTimestamp: (time: number) => void;
  isPlaying: boolean;
  setIsPlaying: (playing: boolean) => void;

  // Draft persistence (local only)
  draftScores: Record<string, number>;
  setDraftScores: (scores: Record<string, number>) => void;
  draftCriterionComments: Record<string, string>;
  setDraftCriterionComments: (comments: Record<string, string>) => void;
  draftFinalComment: string;
  setDraftFinalComment: (comment: string) => void;
  draftComments: (AnchoredComment | TimestampComment)[];
  setDraftComments: (comments: (AnchoredComment | TimestampComment)[]) => void;
  clearDraft: () => void;
}

export const useExpertStore = create<ExpertState>((set) => ({
  activeReviewId: null,
  setActiveReviewId: (id) => set({ activeReviewId: id }),
  
  playbackSpeed: 1,
  setPlaybackSpeed: (speed) => set({ playbackSpeed: speed }),
  currentTimestamp: 0,
  setCurrentTimestamp: (time) => set({ currentTimestamp: time }),
  isPlaying: false,
  setIsPlaying: (playing) => set({ isPlaying: playing }),

  draftScores: {},
  setDraftScores: (scores) => set({ draftScores: scores }),
  draftCriterionComments: {},
  setDraftCriterionComments: (comments) => set({ draftCriterionComments: comments }),
  draftFinalComment: '',
  setDraftFinalComment: (comment) => set({ draftFinalComment: comment }),
  draftComments: [],
  setDraftComments: (comments) => set({ draftComments: comments }),
  clearDraft: () => set({ draftScores: {}, draftCriterionComments: {}, draftFinalComment: '', draftComments: [] }),
}));
