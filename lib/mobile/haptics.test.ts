import { describe, it, expect, vi, beforeEach } from 'vitest';

// Override the global mock from vitest.setup.ts so we can exercise the real
// implementation in this file.
vi.unmock('@/lib/mobile/haptics');
vi.unmock('./haptics');

const { isNativeMock, hapticsImpact } = vi.hoisted(() => ({
  isNativeMock: vi.fn(() => true),
  hapticsImpact: vi.fn(),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: isNativeMock,
    getPlatform: vi.fn(() => 'ios'),
  },
}));

vi.mock('@capacitor/haptics', () => ({
  Haptics: { impact: hapticsImpact },
  ImpactStyle: { Light: 'LIGHT', Medium: 'MEDIUM', Heavy: 'HEAVY' },
}));

beforeEach(() => {
  vi.resetModules();
  isNativeMock.mockReset().mockReturnValue(true);
  hapticsImpact.mockReset().mockResolvedValue(undefined);
});

async function loadModule() {
  const mod = await vi.importActual<typeof import('./haptics')>('./haptics');
  return mod;
}

describe('triggerImpactHaptic', () => {
  it('exposes a callable function', async () => {
    const { triggerImpactHaptic } = await loadModule();
    expect(typeof triggerImpactHaptic).toBe('function');
  });

  it('is a no-op when running on web', async () => {
    isNativeMock.mockReturnValue(false);
    const { triggerImpactHaptic } = await loadModule();
    await triggerImpactHaptic();
    expect(hapticsImpact).not.toHaveBeenCalled();
  });

  it('calls Haptics.impact with the default MEDIUM style on native', async () => {
    isNativeMock.mockReturnValue(true);
    const { triggerImpactHaptic } = await loadModule();
    await triggerImpactHaptic();
    expect(hapticsImpact).toHaveBeenCalledTimes(1);
    expect(hapticsImpact).toHaveBeenCalledWith({ style: 'MEDIUM' });
  });

  it.each(['LIGHT', 'MEDIUM', 'HEAVY'] as const)(
    'forwards %s impact style on native',
    async (style) => {
      isNativeMock.mockReturnValue(true);
      const { triggerImpactHaptic } = await loadModule();
      await triggerImpactHaptic(style);
      expect(hapticsImpact).toHaveBeenCalledWith({ style });
    },
  );

  it('swallows native plugin errors and resolves quietly', async () => {
    isNativeMock.mockReturnValue(true);
    hapticsImpact.mockRejectedValue(new Error('plugin missing'));
    const { triggerImpactHaptic } = await loadModule();
    await expect(triggerImpactHaptic()).resolves.toBeUndefined();
  });

  it('issues independent calls per invocation on native', async () => {
    isNativeMock.mockReturnValue(true);
    const { triggerImpactHaptic } = await loadModule();
    await triggerImpactHaptic('LIGHT');
    await triggerImpactHaptic('HEAVY');
    expect(hapticsImpact).toHaveBeenCalledTimes(2);
    expect(hapticsImpact).toHaveBeenNthCalledWith(1, { style: 'LIGHT' });
    expect(hapticsImpact).toHaveBeenNthCalledWith(2, { style: 'HEAVY' });
  });

  it('caches the dynamically loaded haptics module across calls', async () => {
    isNativeMock.mockReturnValue(true);
    const { triggerImpactHaptic } = await loadModule();
    await triggerImpactHaptic('LIGHT');
    await triggerImpactHaptic('LIGHT');
    await triggerImpactHaptic('LIGHT');
    expect(hapticsImpact).toHaveBeenCalledTimes(3);
  });
});
