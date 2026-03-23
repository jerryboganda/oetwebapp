"use client";

import { create } from "zustand";
import { persist } from "zustand/middleware";

interface WritingDraftState {
  drafts: Record<string, string>;
  saveDraft: (attemptId: string, content: string) => void;
  clearDraft: (attemptId: string) => void;
}

export const useWritingDraftStore = create<WritingDraftState>()(
  persist(
    (set) => ({
      clearDraft: (attemptId) =>
        set((state) => {
          const nextDrafts = { ...state.drafts };
          delete nextDrafts[attemptId];
          return { drafts: nextDrafts };
        }),
      drafts: {},
      saveDraft: (attemptId, content) =>
        set((state) => ({
          drafts: {
            ...state.drafts,
            [attemptId]: content,
          },
        })),
    }),
    {
      name: "oet-writing-drafts",
      partialize: (state) => ({ drafts: state.drafts }),
    }
  )
);
