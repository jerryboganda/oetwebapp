/**
 * Writing Module V2 — Zustand store for editor + coach + timer UI state.
 *
 * This is *purely client UI state*. All persistent state (draft body,
 * grades, profile, pathway) lives server-side and flows through
 * `lib/writing/api.ts` and TanStack Query.
 *
 * State shape:
 *   editorMode          — which of the 6 modes the editor is currently in
 *   coachToggled        — whether the AI Coach panel is currently ON
 *   draftRestored       — true if a saved draft was hydrated this session
 *   wordCount           — last computed live word count (for ARIA + footer)
 *   secondsElapsed      — running counter; the editor ticks this every 1s
 *
 * The store stays minimal on purpose; per-feature local state (e.g. open
 * dialogs, in-flight requests) lives in the components themselves.
 */

import { create } from 'zustand';
import type { WritingEditorMode } from './types';

export interface WritingEditorState {
  editorMode: WritingEditorMode;
  coachToggled: boolean;
  draftRestored: boolean;
  wordCount: number;
  secondsElapsed: number;
}

export interface WritingEditorActions {
  setMode(mode: WritingEditorMode): void;
  toggleCoach(): void;
  setCoachToggled(value: boolean): void;
  markDraftRestored(): void;
  resetDraftRestored(): void;
  setWordCount(count: number): void;
  tickTimer(seconds?: number): void;
  resetTimer(): void;
  reset(): void;
}

export type WritingEditorStore = WritingEditorState & WritingEditorActions;

const INITIAL_STATE: WritingEditorState = {
  editorMode: 'practice',
  coachToggled: false,
  draftRestored: false,
  wordCount: 0,
  secondsElapsed: 0,
};

export const useWritingEditorStore = create<WritingEditorStore>((set) => ({
  ...INITIAL_STATE,
  setMode: (mode) =>
    set((s) => ({
      editorMode: mode,
      // Coach is only meaningful in coached + revision modes; auto-disable
      // if we transition out of those.
      coachToggled:
        mode === 'coached' || mode === 'revision' ? s.coachToggled : false,
    })),
  toggleCoach: () => set((s) => ({ coachToggled: !s.coachToggled })),
  setCoachToggled: (value) => set({ coachToggled: value }),
  markDraftRestored: () => set({ draftRestored: true }),
  resetDraftRestored: () => set({ draftRestored: false }),
  setWordCount: (count) => set({ wordCount: Math.max(0, Math.floor(count)) }),
  tickTimer: (seconds = 1) =>
    set((s) => ({ secondsElapsed: s.secondsElapsed + Math.max(0, seconds) })),
  resetTimer: () => set({ secondsElapsed: 0 }),
  reset: () => set({ ...INITIAL_STATE }),
}));

/**
 * Selector helpers for fine-grained subscriptions (avoid re-renders
 * when unrelated slices change).
 */
export const writingEditorSelectors = {
  mode: (s: WritingEditorStore) => s.editorMode,
  coachToggled: (s: WritingEditorStore) => s.coachToggled,
  draftRestored: (s: WritingEditorStore) => s.draftRestored,
  wordCount: (s: WritingEditorStore) => s.wordCount,
  secondsElapsed: (s: WritingEditorStore) => s.secondsElapsed,
};
