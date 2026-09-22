import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  createPlacementSession,
  derivePlacementDeviceClass,
  detectPlacementDeviceClass,
} from '@/lib/api/placement';

/**
 * The engine records which kind of device took the test so results can be
 * read in context (a phone Writing sample is not a keyboard sample). The class
 * comes from viewport width plus pointer capability - never from user-agent
 * sniffing.
 */

const originalWidth = window.innerWidth;
const originalMatchMedia = window.matchMedia;

function setViewport(width: number, coarsePointer: boolean) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn((query: string) => ({ matches: coarsePointer && query.includes('coarse'), media: query })),
  });
}

afterEach(() => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: originalWidth });
  Object.defineProperty(window, 'matchMedia', { configurable: true, writable: true, value: originalMatchMedia });
  // Drops the own-property override, exposing any prototype value again.
  Reflect.deleteProperty(navigator, 'maxTouchPoints');
  vi.unstubAllGlobals();
});

const CASES: Array<[number, boolean, string]> = [
  [375, true, 'mobile'],
  [767, false, 'mobile'],
  [768, true, 'tablet'],
  [1023, true, 'tablet'],
  // A narrow desktop window has a fine pointer, so it is not a tablet.
  [900, false, 'desktop'],
  [1024, true, 'desktop'],
  [1440, false, 'desktop'],
];

describe('derivePlacementDeviceClass', () => {
  it.each(CASES)('%ipx (coarse pointer: %s) is %s', (width, coarse, expected) => {
    expect(derivePlacementDeviceClass(width, coarse)).toBe(expected);
  });
});

describe('detectPlacementDeviceClass', () => {
  it('reads the live viewport and pointer', () => {
    setViewport(820, true);
    expect(detectPlacementDeviceClass()).toBe('tablet');
    setViewport(390, true);
    expect(detectPlacementDeviceClass()).toBe('mobile');
    setViewport(1280, false);
    expect(detectPlacementDeviceClass()).toBe('desktop');
  });

  it('falls back to touch points when matchMedia is unavailable', () => {
    setViewport(800, false);
    Object.defineProperty(window, 'matchMedia', { configurable: true, writable: true, value: undefined });
    Object.defineProperty(navigator, 'maxTouchPoints', { configurable: true, value: 5 });
    expect(detectPlacementDeviceClass()).toBe('tablet');
  });
});

describe('createPlacementSession', () => {
  it('sends the derived deviceClass with the target goal', async () => {
    setViewport(390, true);
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ session_id: 'ses_1', ruleset_version: 'v1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const created = await createPlacementSession();

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(String(init.body))).toEqual({ targetGoal: 'General', deviceClass: 'mobile' });
    expect(created).toEqual({ sessionId: 'ses_1', rulesetVersion: 'v1' });
  });
});
