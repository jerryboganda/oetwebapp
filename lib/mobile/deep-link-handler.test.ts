import { describe, expect, it, vi, beforeEach } from 'vitest';

// ── Mock Capacitor Core ─────────────────────────────────────────

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => true),
    getPlatform: vi.fn(() => 'android'),
  },
}));

// ── Mock App Plugin ─────────────────────────────────────────────

const mockApp = {
  addListener: vi.fn(),
  getLaunchUrl: vi.fn(),
};

vi.mock('@capacitor/app', () => ({
  App: mockApp,
}));

import { initializeDeepLinkHandler } from '@/lib/mobile/deep-link-handler';

describe('deep-link-handler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('initializeDeepLinkHandler', () => {
    it('sets up appUrlOpen listener and returns cleanup', async () => {
      mockApp.addListener.mockResolvedValue({ remove: vi.fn() });
      mockApp.getLaunchUrl.mockResolvedValue(null);

      const cleanup = await initializeDeepLinkHandler({ onDeepLink: vi.fn() });

      expect(mockApp.addListener).toHaveBeenCalledWith('appUrlOpen', expect.any(Function));
      expect(mockApp.getLaunchUrl).toHaveBeenCalled();
      expect(typeof cleanup).toBe('function');
    });

    it('invokes onDeepLink for valid URLs', async () => {
      const onDeepLink = vi.fn();
      let urlOpenCallback: ((event: { url: string }) => void) | undefined;

      mockApp.addListener.mockImplementation(async (event: string, cb: unknown) => {
        if (event === 'appUrlOpen') {
          urlOpenCallback = cb as typeof urlOpenCallback;
        }
        return { remove: vi.fn() };
      });
      mockApp.getLaunchUrl.mockResolvedValue(null);

      await initializeDeepLinkHandler({ onDeepLink });

      expect(urlOpenCallback).toBeDefined();
      urlOpenCallback!({ url: 'https://app.oetwithdrhesham.co.uk/dashboard?tab=overview' });

      expect(onDeepLink).toHaveBeenCalledWith({
        url: 'https://app.oetwithdrhesham.co.uk/dashboard?tab=overview',
        path: '/dashboard',
        queryParams: { tab: 'overview' },
      });
    });

    it('rejects URLs from unknown hosts', async () => {
      const onDeepLink = vi.fn();
      let urlOpenCallback: ((event: { url: string }) => void) | undefined;

      mockApp.addListener.mockImplementation(async (event: string, cb: unknown) => {
        if (event === 'appUrlOpen') {
          urlOpenCallback = cb as typeof urlOpenCallback;
        }
        return { remove: vi.fn() };
      });
      mockApp.getLaunchUrl.mockResolvedValue(null);

      await initializeDeepLinkHandler({ onDeepLink });

      urlOpenCallback!({ url: 'https://evil.com/steal-tokens' });

      expect(onDeepLink).not.toHaveBeenCalled();
    });

    it('rejects non-HTTPS URLs', async () => {
      const onDeepLink = vi.fn();
      let urlOpenCallback: ((event: { url: string }) => void) | undefined;

      mockApp.addListener.mockImplementation(async (event: string, cb: unknown) => {
        if (event === 'appUrlOpen') {
          urlOpenCallback = cb as typeof urlOpenCallback;
        }
        return { remove: vi.fn() };
      });
      mockApp.getLaunchUrl.mockResolvedValue(null);

      await initializeDeepLinkHandler({ onDeepLink });

      urlOpenCallback!({ url: 'javascript:alert(1)' });
      expect(onDeepLink).not.toHaveBeenCalled();

      urlOpenCallback!({ url: 'file:///etc/passwd' });
      expect(onDeepLink).not.toHaveBeenCalled();
    });

    it('handles cold-start launch URL', async () => {
      const onDeepLink = vi.fn();
      mockApp.addListener.mockResolvedValue({ remove: vi.fn() });
      mockApp.getLaunchUrl.mockResolvedValue({
        url: 'https://app.oetwithdrhesham.co.uk/exam-guide',
      });

      await initializeDeepLinkHandler({ onDeepLink });

      expect(onDeepLink).toHaveBeenCalledWith({
        url: 'https://app.oetwithdrhesham.co.uk/exam-guide',
        path: '/exam-guide',
        queryParams: {},
      });
    });

    it('cleans up listeners on teardown', async () => {
      const removeFn = vi.fn();
      mockApp.addListener.mockResolvedValue({ remove: removeFn });
      mockApp.getLaunchUrl.mockResolvedValue(null);

      const cleanup = await initializeDeepLinkHandler();
      cleanup();

      expect(removeFn).toHaveBeenCalled();
    });
  });

  describe('H13 device pairing dispatch', () => {
    async function setupPairingListener() {
      let urlOpenCallback: ((event: { url: string }) => void) | undefined;
      mockApp.addListener.mockImplementation(async (event: string, cb: unknown) => {
        if (event === 'appUrlOpen') {
          urlOpenCallback = cb as typeof urlOpenCallback;
        }
        return { remove: vi.fn() };
      });
      mockApp.getLaunchUrl.mockResolvedValue(null);
      return { getCallback: () => urlOpenCallback! };
    }

    it('routes /pair with valid code to onPairing and skips onDeepLink', async () => {
      const onPairing = vi.fn();
      const onDeepLink = vi.fn();
      const { getCallback } = await setupPairingListener();
      await initializeDeepLinkHandler({ onPairing, onDeepLink });

      getCallback()({ url: 'https://app.oetwithdrhesham.co.uk/pair?code=abc123' });

      expect(onPairing).toHaveBeenCalledWith('ABC123');
      expect(onDeepLink).not.toHaveBeenCalled();
    });

    it('falls through to onDeepLink when /pair has no code', async () => {
      const onPairing = vi.fn();
      const onDeepLink = vi.fn();
      const { getCallback } = await setupPairingListener();
      await initializeDeepLinkHandler({ onPairing, onDeepLink });

      getCallback()({ url: 'https://app.oetwithdrhesham.co.uk/pair' });

      expect(onPairing).not.toHaveBeenCalled();
      expect(onDeepLink).toHaveBeenCalledWith(expect.objectContaining({ path: '/pair' }));
    });

    it('falls through to onDeepLink when /pair code is malformed', async () => {
      const onPairing = vi.fn();
      const onDeepLink = vi.fn();
      const { getCallback } = await setupPairingListener();
      await initializeDeepLinkHandler({ onPairing, onDeepLink });

      getCallback()({ url: 'https://app.oetwithdrhesham.co.uk/pair?code=ab1' });
      getCallback()({ url: 'https://app.oetwithdrhesham.co.uk/pair?code=ABC-12' });

      expect(onPairing).not.toHaveBeenCalled();
      expect(onDeepLink).toHaveBeenCalledTimes(2);
    });

    it('ignores /pair from unknown hosts', async () => {
      const onPairing = vi.fn();
      const { getCallback } = await setupPairingListener();
      await initializeDeepLinkHandler({ onPairing });

      getCallback()({ url: 'https://evil.com/pair?code=ABC123' });

      expect(onPairing).not.toHaveBeenCalled();
    });
  });
});
