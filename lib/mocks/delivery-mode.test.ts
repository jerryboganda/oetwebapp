import { describe, expect, it } from 'vitest';
import {
  deriveDeliveryMode,
  deliveryModeToReadingPresentation,
  normalizeDeliveryMode,
  type ReadonlyParams,
} from './delivery-mode';

function params(map: Record<string, string>): ReadonlyParams {
  return { get: (k: string) => (k in map ? map[k] : null) };
}

describe('normalizeDeliveryMode', () => {
  it('maps known values', () => {
    expect(normalizeDeliveryMode('paper')).toBe('paper');
    expect(normalizeDeliveryMode('PAPER')).toBe('paper');
    expect(normalizeDeliveryMode('computer')).toBe('computer');
    expect(normalizeDeliveryMode('oet_home')).toBe('oet_home');
    expect(normalizeDeliveryMode('oet-home')).toBe('oet_home');
    expect(normalizeDeliveryMode('home')).toBe('oet_home');
  });

  it('defaults unknown/empty to computer', () => {
    expect(normalizeDeliveryMode('')).toBe('computer');
    expect(normalizeDeliveryMode(null)).toBe('computer');
    expect(normalizeDeliveryMode('garbage')).toBe('computer');
  });
});

describe('deriveDeliveryMode', () => {
  it('reads the deliveryMode the mock launch attaches', () => {
    expect(deriveDeliveryMode(params({ deliveryMode: 'paper' }))).toBe('paper');
    expect(deriveDeliveryMode(params({ deliveryMode: 'oet_home' }))).toBe('oet_home');
    expect(deriveDeliveryMode(params({ deliveryMode: 'computer' }))).toBe('computer');
  });

  it('honors the legacy presentation=paper alias when deliveryMode is absent', () => {
    expect(deriveDeliveryMode(params({ presentation: 'paper' }))).toBe('paper');
  });

  it('prefers deliveryMode over the legacy alias', () => {
    expect(deriveDeliveryMode(params({ deliveryMode: 'computer', presentation: 'paper' }))).toBe('computer');
  });

  it('defaults to computer with no params', () => {
    expect(deriveDeliveryMode(params({}))).toBe('computer');
    expect(deriveDeliveryMode(null)).toBe('computer');
  });
});

describe('deliveryModeToReadingPresentation', () => {
  it('only paper maps to the booklet presentation; oet_home is computer-delivered', () => {
    expect(deliveryModeToReadingPresentation('paper')).toBe('paper');
    expect(deliveryModeToReadingPresentation('computer')).toBe('computer');
    expect(deliveryModeToReadingPresentation('oet_home')).toBe('computer');
  });
});
