import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { triggerResumeMotion } from './lifecycle-motion';

describe('triggerResumeMotion', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    delete document.documentElement.dataset.appResuming;
  });

  afterEach(() => {
    vi.useRealTimers();
    delete document.documentElement.dataset.appResuming;
  });

  it('sets data-app-resuming on <html> immediately', () => {
    triggerResumeMotion();
    expect(document.documentElement.dataset.appResuming).toBe('true');
  });

  it('removes the attribute after the 200ms animation', () => {
    triggerResumeMotion();
    expect(document.documentElement.dataset.appResuming).toBe('true');
    vi.advanceTimersByTime(199);
    expect(document.documentElement.dataset.appResuming).toBe('true');
    vi.advanceTimersByTime(1);
    expect(document.documentElement.dataset.appResuming).toBeUndefined();
  });

  it('returned cleanup clears the timer and the attribute', () => {
    const cleanup = triggerResumeMotion();
    expect(typeof cleanup).toBe('function');
    cleanup!();
    expect(document.documentElement.dataset.appResuming).toBeUndefined();
    // Advancing past the original timeout must not retrigger removal logic
    vi.advanceTimersByTime(500);
    expect(document.documentElement.dataset.appResuming).toBeUndefined();
  });

  it('is a no-op without a document', async () => {
    // Re-import in a context where `document` is briefly undefined
    const originalDocument = globalThis.document;
    // @ts-expect-error testing the SSR guard
    delete globalThis.document;
    try {
      const mod = await import('./lifecycle-motion');
      expect(() => mod.triggerResumeMotion()).not.toThrow();
    } finally {
      globalThis.document = originalDocument;
    }
  });
});
