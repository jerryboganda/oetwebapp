import { beforeEach, describe, expect, it } from 'vitest';

import { useWritingEditorStore } from './store';

describe('writing editor store', () => {
  beforeEach(() => {
    useWritingEditorStore.getState().reset();
  });

  it('keeps coach mode enabled only in coached and revision modes', () => {
    const store = useWritingEditorStore.getState();

    store.setCoachToggled(true);
    store.setMode('coached');
    expect(useWritingEditorStore.getState().coachToggled).toBe(true);

    store.setMode('revision');
    expect(useWritingEditorStore.getState().coachToggled).toBe(true);

    store.setMode('practice');
    expect(useWritingEditorStore.getState().coachToggled).toBe(false);
  });

  it('normalizes timer and word count updates', () => {
    const store = useWritingEditorStore.getState();

    store.setWordCount(182.8);
    expect(useWritingEditorStore.getState().wordCount).toBe(182);

    store.setWordCount(-5);
    expect(useWritingEditorStore.getState().wordCount).toBe(0);

    store.tickTimer(4.2);
    expect(useWritingEditorStore.getState().secondsElapsed).toBe(4.2);

    store.tickTimer(-3);
    expect(useWritingEditorStore.getState().secondsElapsed).toBe(4.2);
  });

  it('tracks draft restoration and reset state', () => {
    const store = useWritingEditorStore.getState();

    store.markDraftRestored();
    expect(useWritingEditorStore.getState().draftRestored).toBe(true);

    store.resetDraftRestored();
    expect(useWritingEditorStore.getState().draftRestored).toBe(false);

    store.setMode('revision');
    store.setCoachToggled(true);
    store.setWordCount(200);
    store.tickTimer(30);

    store.reset();

    expect(useWritingEditorStore.getState()).toMatchObject({
      editorMode: 'practice',
      coachToggled: false,
      draftRestored: false,
      wordCount: 0,
      secondsElapsed: 0,
    });
  });
});