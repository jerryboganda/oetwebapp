import {
  isCapacitorLocalHttpAllowed,
  requireCapacitorAppUrl,
  resolveCapacitorAppUrl,
} from '@/lib/mobile/capacitor-config';

describe('capacitor-config', () => {
  it('prefers CAPACITOR_APP_URL over APP_URL', () => {
    expect(
      resolveCapacitorAppUrl({
        CAPACITOR_APP_URL: 'https://mobile.example.com/app/',
        APP_URL: 'https://app.example.com',
      }),
    ).toBe('https://mobile.example.com/app');
  });

  it('falls back to APP_URL and trims trailing slashes', () => {
    expect(
      resolveCapacitorAppUrl({
        APP_URL: 'https://app.example.com/',
      }),
    ).toBe('https://app.example.com');
  });

  it('rejects missing or invalid URLs', () => {
    expect(resolveCapacitorAppUrl({})).toBeNull();
    expect(() => requireCapacitorAppUrl({})).toThrow(/APP_URL or CAPACITOR_APP_URL/);
    expect(resolveCapacitorAppUrl({ APP_URL: 'ftp://example.com' })).toBeNull();
  });

  it('requires HTTPS unless local loopback HTTP is explicitly enabled', () => {
    expect(() => requireCapacitorAppUrl({ APP_URL: 'http://app.example.com' })).toThrow(/requires an HTTPS app URL/);
    expect(() => requireCapacitorAppUrl({ APP_URL: 'http://localhost:3000' })).toThrow(/CAPACITOR_ALLOW_LOCAL_HTTP/);

    const environment = {
      APP_URL: 'http://localhost:3000',
      CAPACITOR_ALLOW_LOCAL_HTTP: 'true',
    };
    expect(requireCapacitorAppUrl(environment)).toBe('http://localhost:3000');
    expect(isCapacitorLocalHttpAllowed('http://localhost:3000', environment)).toBe(true);
  });
});
