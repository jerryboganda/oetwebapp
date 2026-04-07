// ── Offline Sync Module ──────────────────────────────────────────
// Manages offline content caching, attempt queuing, and sync-on-reconnect

const OFFLINE_DB_NAME = 'oet-offline';
const OFFLINE_DB_VERSION = 1;
const STORES = {
  content: 'content',
  attempts: 'attempts',
  vocabulary: 'vocabulary',
  meta: 'meta',
} as const;

type OfflineAttempt = {
  id: string;
  subtest: string;
  contentId: string;
  payload: unknown;
  createdAt: string;
  synced: boolean;
};

type OfflineContent = {
  id: string;
  type: string;
  data: unknown;
  cachedAt: string;
  expiresAt: string;
};

type SyncStatus = {
  lastSyncedAt: string | null;
  pendingAttempts: number;
  cachedContentCount: number;
  isOnline: boolean;
};

function isBrowser(): boolean {
  return typeof window !== 'undefined' && typeof indexedDB !== 'undefined';
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (!isBrowser()) {
      reject(new Error('IndexedDB not available'));
      return;
    }

    const request = indexedDB.open(OFFLINE_DB_NAME, OFFLINE_DB_VERSION);

    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORES.content)) {
        db.createObjectStore(STORES.content, { keyPath: 'id' });
      }
      if (!db.objectStoreNames.contains(STORES.attempts)) {
        const attemptsStore = db.createObjectStore(STORES.attempts, { keyPath: 'id' });
        attemptsStore.createIndex('synced', 'synced', { unique: false });
      }
      if (!db.objectStoreNames.contains(STORES.vocabulary)) {
        db.createObjectStore(STORES.vocabulary, { keyPath: 'id' });
      }
      if (!db.objectStoreNames.contains(STORES.meta)) {
        db.createObjectStore(STORES.meta, { keyPath: 'key' });
      }
    };

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

// ── Content Cache ───────────────────────────────────────────────

export async function cacheContent(id: string, type: string, data: unknown): Promise<void> {
  const db = await openDb();
  const tx = db.transaction(STORES.content, 'readwrite');
  const store = tx.objectStore(STORES.content);

  const entry: OfflineContent = {
    id,
    type,
    data,
    cachedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(), // 7 days
  };

  store.put(entry);
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

export async function getCachedContent(id: string): Promise<unknown | null> {
  const db = await openDb();
  const tx = db.transaction(STORES.content, 'readonly');
  const store = tx.objectStore(STORES.content);

  return new Promise((resolve, reject) => {
    const request = store.get(id);
    request.onsuccess = () => {
      const entry = request.result as OfflineContent | undefined;
      if (!entry) {
        resolve(null);
        return;
      }
      // Check expiry
      if (new Date(entry.expiresAt) < new Date()) {
        resolve(null);
        return;
      }
      resolve(entry.data);
    };
    request.onerror = () => reject(request.error);
  });
}

export async function clearExpiredContent(): Promise<number> {
  const db = await openDb();
  const tx = db.transaction(STORES.content, 'readwrite');
  const store = tx.objectStore(STORES.content);
  const now = new Date().toISOString();
  let cleared = 0;

  return new Promise((resolve, reject) => {
    const cursor = store.openCursor();
    cursor.onsuccess = () => {
      const result = cursor.result;
      if (result) {
        const entry = result.value as OfflineContent;
        if (entry.expiresAt < now) {
          result.delete();
          cleared++;
        }
        result.continue();
      } else {
        resolve(cleared);
      }
    };
    cursor.onerror = () => reject(cursor.error);
  });
}

// ── Offline Attempt Queue ───────────────────────────────────────

export async function queueOfflineAttempt(
  subtest: string,
  contentId: string,
  payload: unknown,
): Promise<string> {
  const db = await openDb();
  const tx = db.transaction(STORES.attempts, 'readwrite');
  const store = tx.objectStore(STORES.attempts);

  const id = `offline-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  const attempt: OfflineAttempt = {
    id,
    subtest,
    contentId,
    payload,
    createdAt: new Date().toISOString(),
    synced: false,
  };

  store.put(attempt);
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve(id);
    tx.onerror = () => reject(tx.error);
  });
}

export async function getPendingAttempts(): Promise<OfflineAttempt[]> {
  const db = await openDb();
  const tx = db.transaction(STORES.attempts, 'readonly');
  const store = tx.objectStore(STORES.attempts);
  const index = store.index('synced');

  return new Promise((resolve, reject) => {
    const request = index.getAll(IDBKeyRange.only(0));
    request.onsuccess = () => resolve(request.result as OfflineAttempt[]);
    request.onerror = () => reject(request.error);
  });
}

export async function markAttemptSynced(id: string): Promise<void> {
  const db = await openDb();
  const tx = db.transaction(STORES.attempts, 'readwrite');
  const store = tx.objectStore(STORES.attempts);

  return new Promise((resolve, reject) => {
    const request = store.get(id);
    request.onsuccess = () => {
      const entry = request.result as OfflineAttempt | undefined;
      if (entry) {
        entry.synced = true;
        store.put(entry);
      }
      resolve();
    };
    request.onerror = () => reject(request.error);
  });
}

// ── Sync Engine ─────────────────────────────────────────────────

type SyncCallback = (attempt: OfflineAttempt) => Promise<boolean>;

export async function syncPendingAttempts(syncFn: SyncCallback): Promise<{ synced: number; failed: number }> {
  const pending = await getPendingAttempts();
  let synced = 0;
  let failed = 0;

  for (const attempt of pending) {
    try {
      const success = await syncFn(attempt);
      if (success) {
        await markAttemptSynced(attempt.id);
        synced++;
      } else {
        failed++;
      }
    } catch {
      failed++;
    }
  }

  // Update last sync timestamp
  if (synced > 0) {
    await setMeta('lastSyncedAt', new Date().toISOString());
  }

  return { synced, failed };
}

// ── Online/Offline Detection ────────────────────────────────────

export function isOnline(): boolean {
  if (!isBrowser()) return true;
  return navigator.onLine;
}

export function onConnectivityChange(callback: (online: boolean) => void): () => void {
  if (!isBrowser()) return () => {};

  const handleOnline = () => callback(true);
  const handleOffline = () => callback(false);

  window.addEventListener('online', handleOnline);
  window.addEventListener('offline', handleOffline);

  return () => {
    window.removeEventListener('online', handleOnline);
    window.removeEventListener('offline', handleOffline);
  };
}

// Auto-sync when coming back online
export function enableAutoSync(syncFn: SyncCallback): () => void {
  return onConnectivityChange(async (online) => {
    if (online) {
      await syncPendingAttempts(syncFn);
    }
  });
}

// ── Meta Store ──────────────────────────────────────────────────

async function setMeta(key: string, value: string): Promise<void> {
  const db = await openDb();
  const tx = db.transaction(STORES.meta, 'readwrite');
  tx.objectStore(STORES.meta).put({ key, value });
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

async function getMeta(key: string): Promise<string | null> {
  const db = await openDb();
  const tx = db.transaction(STORES.meta, 'readonly');

  return new Promise((resolve, reject) => {
    const request = tx.objectStore(STORES.meta).get(key);
    request.onsuccess = () => {
      const entry = request.result as { key: string; value: string } | undefined;
      resolve(entry?.value ?? null);
    };
    request.onerror = () => reject(request.error);
  });
}

// ── Status ──────────────────────────────────────────────────────

export async function getOfflineSyncStatus(): Promise<SyncStatus> {
  try {
    const pending = await getPendingAttempts();
    const lastSynced = await getMeta('lastSyncedAt');

    const db = await openDb();
    const tx = db.transaction(STORES.content, 'readonly');
    const contentCount = await new Promise<number>((resolve, reject) => {
      const request = tx.objectStore(STORES.content).count();
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });

    return {
      lastSyncedAt: lastSynced,
      pendingAttempts: pending.length,
      cachedContentCount: contentCount,
      isOnline: isOnline(),
    };
  } catch {
    return {
      lastSyncedAt: null,
      pendingAttempts: 0,
      cachedContentCount: 0,
      isOnline: isOnline(),
    };
  }
}

// ── Vocabulary Offline Cache ────────────────────────────────────

export async function cacheVocabularyTerms(terms: unknown[]): Promise<void> {
  const db = await openDb();
  const tx = db.transaction(STORES.vocabulary, 'readwrite');
  const store = tx.objectStore(STORES.vocabulary);

  for (const term of terms) {
    store.put(term);
  }

  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

export async function getCachedVocabularyTerms(): Promise<unknown[]> {
  const db = await openDb();
  const tx = db.transaction(STORES.vocabulary, 'readonly');

  return new Promise((resolve, reject) => {
    const request = tx.objectStore(STORES.vocabulary).getAll();
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

// ── Clear All Offline Data ──────────────────────────────────────

export async function clearOfflineData(): Promise<void> {
  if (!isBrowser()) return;

  return new Promise((resolve, reject) => {
    const request = indexedDB.deleteDatabase(OFFLINE_DB_NAME);
    request.onsuccess = () => resolve();
    request.onerror = () => reject(request.error);
  });
}
