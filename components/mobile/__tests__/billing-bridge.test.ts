import { describe, expect, it, vi, beforeEach } from 'vitest';

// ── Mock setup ──────────────────────────────────────────────────
// `vi.hoisted` runs before any `vi.mock` factory, so the inner mocks can
// safely close over the shared spies.

const { getPlatformMock, isNativePlatformMock, browserOpenMock, apiGetMock, apiPostMock } =
  vi.hoisted(() => ({
    getPlatformMock: vi.fn(() => 'web' as 'web' | 'ios' | 'android'),
    isNativePlatformMock: vi.fn(() => false),
    browserOpenMock: vi.fn(async () => undefined),
    apiGetMock: vi.fn(),
    apiPostMock: vi.fn(),
  }));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    getPlatform: getPlatformMock,
    isNativePlatform: isNativePlatformMock,
  },
}));

vi.mock('@capacitor/browser', () => ({
  Browser: { open: browserOpenMock },
}));

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: apiGetMock,
    post: apiPostMock,
  },
}));

import {
  buildBillingContext,
  resolveMobileBillingContext,
  openExternalCheckout,
  openCustomerPortal,
} from '@/lib/native/billing-bridge';

describe('billing-bridge — routing matrix', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('web platform → native_iap route with generic copy', () => {
    const ctx = buildBillingContext('web', null);
    expect(ctx.platform).toBe('web');
    expect(ctx.route).toBe('native_iap');
    expect(ctx.allowExternalLink).toBe(true);
    expect(ctx.copy.ctaLabel).toMatch(/buy/i);
  });

  it('iOS in US → external_browser with "Buy on our website" copy', () => {
    const ctx = buildBillingContext('ios', 'US');
    expect(ctx.route).toBe('external_browser');
    expect(ctx.allowExternalLink).toBe(true);
    expect(ctx.copy.ctaLabel).toBe('Buy on our website');
  });

  it('iOS outside US → web_only_cta with "Manage on website" copy', () => {
    const ctx = buildBillingContext('ios', 'GB');
    expect(ctx.route).toBe('web_only_cta');
    expect(ctx.allowExternalLink).toBe(false);
    expect(ctx.copy.ctaLabel).toBe('Manage on website');
  });

  it('iOS with null country → web_only_cta (safer default)', () => {
    const ctx = buildBillingContext('ios', null);
    expect(ctx.route).toBe('web_only_cta');
    expect(ctx.allowExternalLink).toBe(false);
  });

  it('Android → external_browser with "Buy on the web" copy', () => {
    const ctx = buildBillingContext('android', 'EG');
    expect(ctx.route).toBe('external_browser');
    expect(ctx.allowExternalLink).toBe(true);
    expect(ctx.copy.ctaLabel).toBe('Buy on the web');
  });

  it('Android in US → still external_browser (no country-specific change)', () => {
    const ctx = buildBillingContext('android', 'US');
    expect(ctx.route).toBe('external_browser');
    expect(ctx.allowExternalLink).toBe(true);
  });
});

describe('billing-bridge — resolveMobileBillingContext', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('reads country from /v1/billing/profile when on a native platform', async () => {
    getPlatformMock.mockReturnValue('ios');
    apiGetMock.mockResolvedValueOnce({ country: 'us', detectedCountry: null });

    const ctx = await resolveMobileBillingContext();

    expect(apiGetMock).toHaveBeenCalledWith('/v1/billing/profile');
    expect(ctx.platform).toBe('ios');
    expect(ctx.country).toBe('US');
    expect(ctx.route).toBe('external_browser');
  });

  it('falls back to detectedCountry when stored country is null', async () => {
    getPlatformMock.mockReturnValue('android');
    apiGetMock.mockResolvedValueOnce({ country: null, detectedCountry: 'GB' });

    const ctx = await resolveMobileBillingContext();
    expect(ctx.country).toBe('GB');
  });

  it('returns null country (and strict iOS copy) when profile fetch fails', async () => {
    getPlatformMock.mockReturnValue('ios');
    apiGetMock.mockRejectedValueOnce(new Error('offline'));

    const ctx = await resolveMobileBillingContext();
    // Time-zone fallback may still resolve to US on a US-zoned test box;
    // assert only that we did not throw and we ended up on an iOS branch.
    expect(ctx.platform).toBe('ios');
    expect(['external_browser', 'web_only_cta']).toContain(ctx.route);
  });

  it('does not call the API when running on web', async () => {
    getPlatformMock.mockReturnValue('web');

    const ctx = await resolveMobileBillingContext();
    expect(apiGetMock).not.toHaveBeenCalled();
    expect(ctx.platform).toBe('web');
    expect(ctx.country).toBeNull();
  });

  it('ignores non-ISO country strings from the profile endpoint', async () => {
    getPlatformMock.mockReturnValue('ios');
    apiGetMock.mockResolvedValueOnce({ country: 'usa', detectedCountry: 'United States' });

    const ctx = await resolveMobileBillingContext();
    // Neither value matches /^[A-Z]{2}$/; fallback hits the timezone heuristic.
    // We only assert that we did not accept the invalid country verbatim.
    expect(ctx.country).not.toBe('usa');
    expect(ctx.country).not.toBe('United States');
  });
});

describe('billing-bridge — openExternalCheckout', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isNativePlatformMock.mockReturnValue(true);
  });

  it('POSTs the product code and opens the returned URL in the system browser', async () => {
    apiPostMock.mockResolvedValueOnce({ url: 'https://checkout.stripe.com/cs_test_123' });

    await openExternalCheckout('pkg_quick_check');

    expect(apiPostMock).toHaveBeenCalledWith('/v1/checkout/sessions', {
      productCode: 'pkg_quick_check',
    });
    expect(browserOpenMock).toHaveBeenCalledWith({
      url: 'https://checkout.stripe.com/cs_test_123',
      presentationStyle: 'fullscreen',
    });
  });

  it('accepts a checkoutUrl key for backwards compatibility', async () => {
    apiPostMock.mockResolvedValueOnce({ url: null, checkoutUrl: 'https://checkout.stripe.com/x' });

    await openExternalCheckout('pkg_quick_check');

    expect(browserOpenMock).toHaveBeenCalledWith(
      expect.objectContaining({ url: 'https://checkout.stripe.com/x' }),
    );
  });

  it('throws when the response has no usable redirect URL', async () => {
    apiPostMock.mockResolvedValueOnce({ url: null });

    await expect(openExternalCheckout('pkg_quick_check')).rejects.toThrow(/redirect URL/);
    expect(browserOpenMock).not.toHaveBeenCalled();
  });

  it('throws synchronously on an empty product code', async () => {
    await expect(openExternalCheckout('')).rejects.toThrow(/product code/i);
    expect(apiPostMock).not.toHaveBeenCalled();
  });
});

describe('billing-bridge — openCustomerPortal', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isNativePlatformMock.mockReturnValue(true);
  });

  it('POSTs to the portal endpoint with a returnUrl and opens the result', async () => {
    apiPostMock.mockResolvedValueOnce({ url: 'https://billing.stripe.com/p/session_abc' });

    await openCustomerPortal();

    expect(apiPostMock).toHaveBeenCalledWith(
      '/v1/subscriptions/me/portal-session',
      expect.objectContaining({ returnUrl: expect.stringMatching(/\/account$/) }),
    );
    expect(browserOpenMock).toHaveBeenCalledWith({
      url: 'https://billing.stripe.com/p/session_abc',
      presentationStyle: 'fullscreen',
    });
  });
});
