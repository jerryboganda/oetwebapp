import { describe, expect, it, vi, beforeEach } from 'vitest';

// ── Mock Capacitor Core ─────────────────────────────────────────

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => true),
    getPlatform: vi.fn(() => 'ios'),
  },
}));

// ── Mock SecureStorage Plugin ───────────────────────────────────

const mockSecureStorage = {
  getItem: vi.fn(),
  setItem: vi.fn(),
  removeItem: vi.fn(),
  clear: vi.fn(),
};

vi.mock('@aparajita/capacitor-secure-storage', () => ({
  SecureStorage: mockSecureStorage,
}));

import {
  getSecureItem,
  setSecureItem,
  removeSecureItem,
  clearSecureStorage,
  storeAuthTokens,
  getStoredAuthTokens,
  clearAuthTokens,
} from '@/lib/mobile/secure-storage';

describe('secure-storage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getSecureItem', () => {
    it('retrieves a stored value', async () => {
      mockSecureStorage.getItem.mockResolvedValue('secret-token');
      const result = await getSecureItem('auth_access_token');
      expect(result).toBe('secret-token');
      expect(mockSecureStorage.getItem).toHaveBeenCalledWith('auth_access_token');
    });

    it('returns null when key is not found', async () => {
      mockSecureStorage.getItem.mockRejectedValue(new Error('Key not found'));
      const result = await getSecureItem('auth_access_token');
      expect(result).toBeNull();
    });
  });

  describe('setSecureItem', () => {
    it('stores a value and returns true', async () => {
      mockSecureStorage.setItem.mockResolvedValue(undefined);
      const result = await setSecureItem('auth_access_token', 'new-token');
      expect(result).toBe(true);
      expect(mockSecureStorage.setItem).toHaveBeenCalledWith('auth_access_token', 'new-token');
    });

    it('returns false on error', async () => {
      mockSecureStorage.setItem.mockRejectedValue(new Error('write fail'));
      const result = await setSecureItem('auth_access_token', 'value');
      expect(result).toBe(false);
    });
  });

  describe('removeSecureItem', () => {
    it('removes a key and returns true', async () => {
      mockSecureStorage.removeItem.mockResolvedValue(undefined);
      const result = await removeSecureItem('auth_refresh_token');
      expect(result).toBe(true);
    });

    it('returns false on error', async () => {
      mockSecureStorage.removeItem.mockRejectedValue(new Error('fail'));
      const result = await removeSecureItem('auth_refresh_token');
      expect(result).toBe(false);
    });
  });

  describe('clearSecureStorage', () => {
    it('clears all stored keys', async () => {
      mockSecureStorage.clear.mockResolvedValue(undefined);
      const result = await clearSecureStorage();
      expect(result).toBe(true);
      expect(mockSecureStorage.clear).toHaveBeenCalled();
    });
  });

  describe('storeAuthTokens', () => {
    it('stores all three token values', async () => {
      mockSecureStorage.setItem.mockResolvedValue(undefined);
      const result = await storeAuthTokens('access-123', 'refresh-456', '2026-12-31T00:00:00Z');
      expect(result).toBe(true);
      expect(mockSecureStorage.setItem).toHaveBeenCalledTimes(3);
    });
  });

  describe('getStoredAuthTokens', () => {
    it('retrieves all three token values', async () => {
      mockSecureStorage.getItem.mockImplementation(async (key: string) => {
        const store: Record<string, string> = {
          auth_access_token: 'access-123',
          auth_refresh_token: 'refresh-456',
          auth_token_expiry: '2026-12-31T00:00:00Z',
        };
        if (store[key]) return store[key];
        throw new Error('not found');
      });

      const tokens = await getStoredAuthTokens();
      expect(tokens).toEqual({
        accessToken: 'access-123',
        refreshToken: 'refresh-456',
        expiresAt: '2026-12-31T00:00:00Z',
      });
    });

    it('returns nulls for missing tokens', async () => {
      mockSecureStorage.getItem.mockRejectedValue(new Error('not found'));
      const tokens = await getStoredAuthTokens();
      expect(tokens).toEqual({
        accessToken: null,
        refreshToken: null,
        expiresAt: null,
      });
    });
  });

  describe('clearAuthTokens', () => {
    it('removes all three token keys', async () => {
      mockSecureStorage.removeItem.mockResolvedValue(undefined);
      await clearAuthTokens();
      expect(mockSecureStorage.removeItem).toHaveBeenCalledTimes(3);
    });
  });
});
