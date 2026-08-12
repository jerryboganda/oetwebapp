import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockResolveClientIdentity = vi.hoisted(() => vi.fn());

vi.mock('@/lib/client-version', () => ({
  resolveClientIdentity: mockResolveClientIdentity,
}));

import { buildTechReadinessProbe, parseBrowserIdentity } from './tech-readiness-probe';

describe('tech-readiness-probe', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockResolveClientIdentity.mockResolvedValue({ platform: 'web', version: null });
  });

  it('captures bounded browser, shell, and network telemetry without using it as a gate', async () => {
    Object.defineProperty(navigator, 'userAgent', {
      configurable: true,
      value: 'Mozilla/5.0 Chrome/128.0.0.0 Safari/537.36',
    });
    Object.defineProperty(navigator, 'connection', {
      configurable: true,
      value: { effectiveType: '4g', downlink: 12.5, rtt: 48, saveData: false },
    });
    mockResolveClientIdentity.mockResolvedValue({ platform: 'desktop', version: '1.2.3' });

    await expect(buildTechReadinessProbe({ audioOk: true, durationMs: 1800 })).resolves.toMatchObject({
      audioOk: true,
      durationMs: 1800,
      deviceType: 'desktop',
      appVersion: '1.2.3',
      browserName: 'Chrome',
      browserVersion: '128.0.0.0',
      networkEffectiveType: '4g',
      networkDownlinkMbps: 12.5,
      networkRttMs: 48,
      networkSaveData: false,
    });
  });

  it('parses supported browser identities and returns null for unknown user agents', () => {
    expect(parseBrowserIdentity('Mozilla/5.0 Edg/126.0.0.0')).toEqual({ name: 'Edge', version: '126.0.0.0' });
    expect(parseBrowserIdentity('unknown-client')).toEqual({ name: null, version: null });
  });
});
