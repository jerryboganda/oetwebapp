import { beforeEach, describe, expect, it } from 'vitest';

import {
  __resetSubmitKeyTrackerForTests,
  keyForSubmitAction,
} from './submit-keys';

describe('keyForSubmitAction', () => {
  beforeEach(() => {
    __resetSubmitKeyTrackerForTests();
  });

  it('reuses the same key for a double-tap with identical content', () => {
    const first = keyForSubmitAction('scenario-1', 'Dear Doctor, …');
    const second = keyForSubmitAction('scenario-1', 'Dear Doctor, …');

    expect(second).toBe(first);
  });

  it('mints a fresh key when the candidate edited the letter', () => {
    const first = keyForSubmitAction('scenario-1', 'Dear Doctor, …');
    const second = keyForSubmitAction('scenario-1', 'Dear Doctor, updated …');

    expect(second).not.toBe(first);
  });

  it('mints a fresh key for a different scenario', () => {
    const first = keyForSubmitAction('scenario-1', 'Dear Doctor, …');
    const second = keyForSubmitAction('scenario-2', 'Dear Doctor, …');

    expect(second).not.toBe(first);
  });

  it('keeps the latest key stable across retries of the same attempt', () => {
    keyForSubmitAction('scenario-1', 'version one');
    const retryKey = keyForSubmitAction('scenario-1', 'version two');
    const retryAgain = keyForSubmitAction('scenario-1', 'version two');

    expect(retryAgain).toBe(retryKey);
  });
});
