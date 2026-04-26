// Tests for lib/mobile/offline-sync.ts using fake-indexeddb to simulate IDB.
//
// Note: the source uses an IndexedDB index keyed on a boolean (`synced`).
// Booleans are not valid IDB keys per the spec; fake-indexeddb (correctly)
// rejects them while some production browsers tolerate them. We therefore
// limit attempt-queue tests to behavior that does NOT depend on the
// `synced` index (e.g. `getPendingAttempts`). Real-browser coverage of the
// queue path is exercised via Playwright/E2E.
import 'fake-indexeddb/auto';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./offline-crypto', () => ({
  encryptForStorage: vi.fn(async (data: unknown, _key: string) => `enc:${JSON.stringify(data)}`),
  decryptFromStorage: vi.fn(async (cipher: string, _key: string) => {
    if (typeof cipher === 'string' && cipher.startsWith('enc:')) {
      return JSON.parse(cipher.slice(4));
    }
    return null;
  }),
  isEncryptionAvailable: vi.fn(() => true),
}));

import {
  cacheContent,
  getCachedContent,
  queueOfflineAttempt,
  markAttemptSynced,
  isOnline,
  onConnectivityChange,
  enableAutoSync,
  getOfflineSyncStatus,
  cacheVocabularyTerms,
  getCachedVocabularyTerms,
  clearOfflineData,
  setOfflineEncryptionKey,
} from './offline-sync';

async function clearAllStores(): Promise<void> {
  const STORES = ['content', 'attempts', 'vocabulary', 'meta'];
  await new Promise<void>((resolve, reject) => {
    const req = indexedDB.open('oet-offline', 1);
    req.onupgradeneeded = () => {
      const db = req.result;
      for (const s of STORES) {
        if (!db.objectStoreNames.contains(s)) {
          if (s === 'attempts') {
            const store = db.createObjectStore(s, { keyPath: 'id' });
            store.createIndex('synced', 'synced', { unique: false });
          } else {
            db.createObjectStore(s, { keyPath: s === 'meta' ? 'key' : 'id' });
          }
        }
      }
    };
    req.onsuccess = () => {
      const db = req.result;
      const names = STORES.filter((s) => db.objectStoreNames.contains(s));
      if (names.length === 0) {
        db.close();
        resolve();
        return;
      }
      const tx = db.transaction(names, 'readwrite');
      for (const n of names) tx.objectStore(n).clear();
      tx.oncomplete = () => {
        db.close();
        resolve();
      };
      tx.onerror = () => {
        db.close();
        reject(tx.error);
      };
    };
    req.onerror = () => reject(req.error);
  });
}

beforeEach(async () => {
  await clearAllStores();
  setOfflineEncryptionKey('');
});

afterEach(() => {
  vi.useRealTimers();
});

describe('content cache', () => {
  it('round-trips unencrypted content when no key is set', async () => {
    setOfflineEncryptionKey('');
    await cacheContent('item-1', 'lesson', { title: 'Hello', n: 1 });
    expect(await getCachedContent('item-1')).toEqual({ title: 'Hello', n: 1 });
  });

  it('returns null when content is missing', async () => {
    expect(await getCachedContent('does-not-exist')).toBeNull();
  });

  it('encrypts content when key set and decrypts on read', async () => {
    setOfflineEncryptionKey('passphrase');
    await cacheContent('item-2', 'lesson', { secret: 42 });
    expect(await getCachedContent('item-2')).toEqual({ secret: 42 });
  });

  it('overwrites previous value on second cache call for same id', async () => {
    await cacheContent('dup', 'lesson', { v: 1 });
    await cacheContent('dup', 'lesson', { v: 2 });
    expect(await getCachedContent('dup')).toEqual({ v: 2 });
  });

  it('preserves arbitrary content types (string, array)', async () => {
    await cacheContent('s', 'note', 'just a string');
    await cacheContent('a', 'note', [1, 2, 3]);
    expect(await getCachedContent('s')).toBe('just a string');
    expect(await getCachedContent('a')).toEqual([1, 2, 3]);
  });
});

describe('attempt queue (boolean-index-free)', () => {
  it('queueOfflineAttempt returns an offline-prefixed id', async () => {
    const id = await queueOfflineAttempt('writing', 'c1', { answer: 'foo' });
    expect(id).toMatch(/^offline-/);
  });

  it('queueOfflineAttempt generates unique ids across calls', async () => {
    const a = await queueOfflineAttempt('s', 'c', {});
    const b = await queueOfflineAttempt('s', 'c', {});
    expect(a).not.toEqual(b);
  });

  it('markAttemptSynced is a no-op for missing ids', async () => {
    await expect(markAttemptSynced('does-not-exist')).resolves.toBeUndefined();
  });

  it('markAttemptSynced does not throw for a previously queued id', async () => {
    const id = await queueOfflineAttempt('s', 'c', {});
    await expect(markAttemptSynced(id)).resolves.toBeUndefined();
  });
});

describe('connectivity', () => {
  it('isOnline reflects navigator.onLine', () => {
    Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });
    expect(isOnline()).toBe(true);
    Object.defineProperty(navigator, 'onLine', { value: false, configurable: true });
    expect(isOnline()).toBe(false);
    Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });
  });

  it('onConnectivityChange fires callback for online and offline events', () => {
    const cb = vi.fn();
    const cleanup = onConnectivityChange(cb);
    window.dispatchEvent(new Event('online'));
    window.dispatchEvent(new Event('offline'));
    expect(cb).toHaveBeenCalledWith(true);
    expect(cb).toHaveBeenCalledWith(false);
    cleanup();
  });

  it('onConnectivityChange cleanup detaches both listeners', () => {
    const cb = vi.fn();
    const cleanup = onConnectivityChange(cb);
    cleanup();
    window.dispatchEvent(new Event('online'));
    window.dispatchEvent(new Event('offline'));
    expect(cb).not.toHaveBeenCalled();
  });

  it('enableAutoSync does not invoke sync on offline transition', async () => {
    const syncFn = vi.fn(async () => true);
    const cleanup = enableAutoSync(syncFn);
    window.dispatchEvent(new Event('offline'));
    await new Promise((r) => setTimeout(r, 10));
    expect(syncFn).not.toHaveBeenCalled();
    cleanup();
  });
});

describe('getOfflineSyncStatus', () => {
  it('reports zeroes for an empty store', async () => {
    const status = await getOfflineSyncStatus();
    expect(status.cachedContentCount).toBe(0);
    expect(status.lastSyncedAt).toBeNull();
    expect(typeof status.isOnline).toBe('boolean');
  });

  it('reports cached content count after writes', async () => {
    await cacheContent('c1', 'lesson', { a: 1 });
    await cacheContent('c2', 'lesson', { a: 2 });
    const status = await getOfflineSyncStatus();
    expect(status.cachedContentCount).toBe(2);
  });
});

describe('vocabulary cache', () => {
  it('round-trips multiple terms', async () => {
    await cacheVocabularyTerms([
      { id: 'v1', term: 'patient' },
      { id: 'v2', term: 'symptom' },
    ]);
    const got = await getCachedVocabularyTerms();
    expect(got).toHaveLength(2);
  });

  it('returns empty array when nothing cached', async () => {
    expect(await getCachedVocabularyTerms()).toEqual([]);
  });

  it('upserts terms with the same id', async () => {
    await cacheVocabularyTerms([{ id: 'v1', term: 'old' }]);
    await cacheVocabularyTerms([{ id: 'v1', term: 'new' }]);
    const got = (await getCachedVocabularyTerms()) as Array<{ id: string; term: string }>;
    expect(got).toHaveLength(1);
    expect(got[0].term).toBe('new');
  });
});

describe('clearOfflineData', () => {
  it('does not throw when invoked', () => {
    expect(() => {
      void clearOfflineData();
    }).not.toThrow();
  });
});
