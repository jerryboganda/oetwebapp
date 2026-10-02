import { beforeEach, describe, expect, it } from 'vitest';
import {
  clearDraftShadow,
  draftShadowKey,
  laterPhase,
  minSeconds,
  readDraftShadow,
  reconcileDraft,
  retryDelayMs,
  sweepDraftShadows,
  writeDraftShadow,
  type DraftShadow,
} from '../draft-sync';
import type { WritingDraftV2Dto } from '../types';

function server(overrides: Partial<WritingDraftV2Dto> = {}): WritingDraftV2Dto {
  return {
    userId: 'u1',
    scenarioId: 's1',
    mode: 'practice',
    content: 'Dear Dr Green,',
    wordCount: 3,
    timeSpentSeconds: 10,
    lastSavedAt: '2026-10-02T10:00:00Z',
    version: 4,
    status: 'active',
    phase: 'writing',
    readingSecondsRemaining: 0,
    writingSecondsRemaining: 1200,
    ...overrides,
  };
}

function shadow(overrides: Partial<DraftShadow> = {}): DraftShadow {
  return {
    baseVersion: 4,
    phase: 'writing',
    readingSecondsRemaining: 0,
    writingSecondsRemaining: 1100,
    savedAt: Date.now(),
    ...overrides,
  };
}

beforeEach(() => {
  localStorage.clear();
});

describe('draft shadow storage', () => {
  it('keys the device copy by account, task and mode', () => {
    expect(draftShadowKey('u1', 's1', 'practice')).toBe('oet:writing-draft:v1:u1:s1:practice');
  });

  it('round-trips, clears and rejects unreadable copies', () => {
    const key = draftShadowKey('u1', 's1', 'practice');
    writeDraftShadow(key, shadow({ text: 'abc' }));
    expect(readDraftShadow(key)).toMatchObject({ text: 'abc', baseVersion: 4 });
    clearDraftShadow(key);
    expect(readDraftShadow(key)).toBeNull();
    localStorage.setItem(key, '{not json');
    expect(readDraftShadow(key)).toBeNull();
  });

  it.each([
    ['truncated JSON', '{"text":"Dear Dr Gr'],
    ['null', 'null'],
    ['an array', '[{"savedAt":1}]'],
    ['a number', '42'],
    ['text of the wrong type', JSON.stringify({ text: 12, baseVersion: 1, savedAt: 1 })],
    ['wordCount of the wrong type', JSON.stringify({ text: 'a', wordCount: '1', baseVersion: 1, savedAt: 1 })],
    ['sentText of the wrong type', JSON.stringify({ sentText: ['a'], baseVersion: 1, savedAt: 1 })],
    ['a missing baseVersion', JSON.stringify({ text: 'a', savedAt: 1 })],
    ['a fractional baseVersion', JSON.stringify({ text: 'a', baseVersion: 1.5, savedAt: 1 })],
    ['an unknown phase', JSON.stringify({ baseVersion: 1, phase: 'grading', savedAt: 1 })],
    ['seconds as a string', JSON.stringify({ baseVersion: 1, writingSecondsRemaining: '900', savedAt: 1 })],
    ['a missing savedAt', JSON.stringify({ text: 'a', baseVersion: 1 })],
  ])('treats a corrupt copy (%s) as no copy', (_label, raw) => {
    const key = draftShadowKey('u1', 's1', 'practice');
    localStorage.setItem(key, raw);
    expect(readDraftShadow(key)).toBeNull();
  });

  it('accepts a timers-only copy written after a sync', () => {
    const key = draftShadowKey('u1', 's1', 'practice');
    localStorage.setItem(key, JSON.stringify({ baseVersion: null, phase: null, readingSecondsRemaining: null, writingSecondsRemaining: null, savedAt: 1 }));
    expect(readDraftShadow(key)).toMatchObject({ baseVersion: null, savedAt: 1 });
  });

  it('sweeps copies older than 30 days and unreadable ones, and nothing else', () => {
    const now = Date.UTC(2026, 9, 2);
    writeDraftShadow('oet:writing-draft:v1:u:old:practice', shadow({ savedAt: now - 31 * 86_400_000 }));
    writeDraftShadow('oet:writing-draft:v1:u:fresh:practice', shadow({ savedAt: now - 86_400_000 }));
    localStorage.setItem('oet:writing-draft:v1:u:broken:practice', 'x');
    localStorage.setItem('unrelated', 'keep');

    sweepDraftShadows(now);

    expect(localStorage.getItem('oet:writing-draft:v1:u:old:practice')).toBeNull();
    expect(localStorage.getItem('oet:writing-draft:v1:u:broken:practice')).toBeNull();
    expect(localStorage.getItem('oet:writing-draft:v1:u:fresh:practice')).not.toBeNull();
    expect(localStorage.getItem('unrelated')).toBe('keep');
  });
});

describe('timing helpers', () => {
  it('backs off 2/4/8/16/30 s and stays at 30 s', () => {
    expect([1, 2, 3, 4, 5, 9].map(retryDelayMs)).toEqual([2_000, 4_000, 8_000, 16_000, 30_000, 30_000]);
  });

  it('never gains time and never moves the phase backwards', () => {
    expect(minSeconds(100, 90)).toBe(90);
    expect(minSeconds(null, 90)).toBe(90);
    expect(minSeconds(undefined, null)).toBeNull();
    expect(laterPhase('reading', 'writing')).toBe('writing');
    expect(laterPhase(null, 'reading')).toBe('reading');
  });
});

describe('reconcileDraft (server vs this device)', () => {
  it('no server row and no device copy: a fresh, create-only attempt', () => {
    expect(reconcileDraft(null, null)).toMatchObject({
      text: '', serverText: null, version: 0, conflict: false, source: 'none', phase: null,
    });
  });

  it('server only: the server copy', () => {
    expect(reconcileDraft(server(), null)).toMatchObject({
      text: 'Dear Dr Green,', serverText: 'Dear Dr Green,', version: 4, source: 'server', writingSecondsRemaining: 1200,
    });
  });

  it('device text equal to the server copy is simply synced', () => {
    expect(reconcileDraft(server(), shadow({ text: 'Dear Dr Green,' }))).toMatchObject({
      text: 'Dear Dr Green,', conflict: false, source: 'server', writingSecondsRemaining: 1100,
    });
  });

  it('device text typed on top of the current version wins; timers take the minimum', () => {
    expect(reconcileDraft(server(), shadow({ text: 'Dear Dr Green, more', wordCount: 4 }))).toMatchObject({
      text: 'Dear Dr Green, more', wordCount: 4, serverText: 'Dear Dr Green,', version: 4,
      conflict: false, source: 'device', writingSecondsRemaining: 1100,
    });
  });

  it('a save whose response was lost is recognised as our own', () => {
    const result = reconcileDraft(
      server({ version: 5, content: 'Dear Dr Green, more' }),
      shadow({ baseVersion: 4, sentText: 'Dear Dr Green, more', text: 'Dear Dr Green, more words' }),
    );
    expect(result).toMatchObject({ text: 'Dear Dr Green, more words', version: 5, conflict: false });
  });

  it('flags a conflict when another device saved since, keeping the server clock', () => {
    const result = reconcileDraft(
      server({ version: 6, content: 'Written elsewhere', writingSecondsRemaining: 700 }),
      shadow({ baseVersion: 4, text: 'Written here', writingSecondsRemaining: 300 }),
    );
    expect(result).toMatchObject({
      text: 'Written here', serverText: 'Written elsewhere', version: 6, conflict: true, writingSecondsRemaining: 700,
    });
  });

  it('a 404 with unsynced device text restores it and creates the row', () => {
    expect(reconcileDraft(null, shadow({ baseVersion: 0, text: 'Typed offline' }))).toMatchObject({
      text: 'Typed offline', serverText: null, version: 0, conflict: false, source: 'device',
    });
  });

  it('an API without versions falls back to legacy writes and keeps the device text', () => {
    expect(reconcileDraft(server({ version: undefined }), shadow({ baseVersion: null, text: 'Newer' }))).toMatchObject({
      text: 'Newer', version: null, conflict: false,
    });
  });
});
