/**
 * Listening Audio Pre-buffering & Offline Cache Utility
 *
 * Pre-fetches and caches all scored audio assets (Parts A, B, C) ahead of time
 * into browser memory / Blob URLs so high-stakes timed exams experience zero
 * buffering stutter or network dropouts during one-play playback.
 */

import { fetchAuthorizedObjectUrl } from '@/lib/api';

export interface AudioPrebufferProgress {
  total: number;
  loaded: number;
  failed: number;
  percent: number;
  inProgress: boolean;
}

export interface AudioPrebufferResult {
  success: boolean;
  prebufferedCount: number;
  failedCount: number;
  cachedUrls: Map<string, string>;
  warnings: string[];
}

const memoryAudioCache = new Map<string, { objectUrl: string; createdAt: number }>();

/** Cache TTL: 2 hours */
const CACHE_TTL_MS = 2 * 60 * 60 * 1000;

export function getCachedAudioUrl(url: string): string | null {
  const cached = memoryAudioCache.get(url);
  if (!cached) return null;
  if (Date.now() - cached.createdAt > CACHE_TTL_MS) {
    URL.revokeObjectURL(cached.objectUrl);
    memoryAudioCache.delete(url);
    return null;
  }
  return cached.objectUrl;
}

export function registerCachedAudioUrl(sourceUrl: string, objectUrl: string): void {
  const existing = memoryAudioCache.get(sourceUrl);
  if (existing && existing.objectUrl !== objectUrl) {
    URL.revokeObjectURL(existing.objectUrl);
  }
  memoryAudioCache.set(sourceUrl, { objectUrl, createdAt: Date.now() });
}

export function clearAudioPrebufferCache(): void {
  for (const entry of memoryAudioCache.values()) {
    try {
      URL.revokeObjectURL(entry.objectUrl);
    } catch {
      // ignore
    }
  }
  memoryAudioCache.clear();
}

/**
 * Pre-buffers an array of audio URLs in parallel with concurrency limiting.
 */
export async function prebufferAudioChunks(
  audioUrls: string[],
  onProgress?: (progress: AudioPrebufferProgress) => void,
  concurrency = 3,
): Promise<AudioPrebufferResult> {
  const uniqueUrls = [...new Set(audioUrls.map((u) => u?.trim()).filter(Boolean))];
  const total = uniqueUrls.length;
  if (total === 0) {
    return {
      success: true,
      prebufferedCount: 0,
      failedCount: 0,
      cachedUrls: new Map(),
      warnings: [],
    };
  }

  let loaded = 0;
  let failed = 0;
  const warnings: string[] = [];
  const cachedUrls = new Map<string, string>();

  const report = () => {
    onProgress?.({
      total,
      loaded,
      failed,
      percent: Math.round(((loaded + failed) / total) * 100),
      inProgress: loaded + failed < total,
    });
  };

  report();

  // Process in batches
  for (let i = 0; i < uniqueUrls.length; i += concurrency) {
    const batch = uniqueUrls.slice(i, i + concurrency);
    await Promise.all(
      batch.map(async (url) => {
        try {
          // Check if already cached
          const existing = getCachedAudioUrl(url);
          if (existing) {
            cachedUrls.set(url, existing);
            loaded++;
            report();
            return;
          }

          let objectUrl: string;
          if (!/^https?:\/\//i.test(url) || /\/v1\//i.test(url)) {
            objectUrl = await fetchAuthorizedObjectUrl(url);
          } else {
            const response = await fetch(url, { mode: 'cors' });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const blob = await response.blob();
            objectUrl = URL.createObjectURL(blob);
          }

          // Warm the native Audio decoding pipeline in the background. This is
          // best-effort only and must never gate the prebuffer result: on some
          // WebView/jsdom Audio stubs neither `canplaythrough` nor `error`
          // ever fires, so awaiting it would stall the caller for the full
          // fallback timeout per chunk (8s × batch). The cached URL is fully
          // usable without warming.
          try {
            void warmAudioDecoder(objectUrl);
          } catch {
            // ignore — warming is advisory
          }
          registerCachedAudioUrl(url, objectUrl);
          cachedUrls.set(url, objectUrl);
          loaded++;
        } catch (err) {
          failed++;
          const msg = err instanceof Error ? err.message : 'Unknown prebuffer error';
          warnings.push(`Failed to pre-buffer audio chunk (${url}): ${msg}`);
        } finally {
          report();
        }
      }),
    );
  }

  return {
    success: failed === 0,
    prebufferedCount: loaded,
    failedCount: failed,
    cachedUrls,
    warnings,
  };
}

function warmAudioDecoder(objectUrl: string): Promise<void> {
  return new Promise((resolve) => {
    if (typeof window === 'undefined' || typeof Audio === 'undefined') {
      resolve();
      return;
    }
    const audio = new Audio();
    audio.preload = 'auto';
    let timeoutId: number | null = null;

    const cleanup = () => {
      if (timeoutId !== null) window.clearTimeout(timeoutId);
      audio.removeEventListener('canplaythrough', onReady);
      audio.removeEventListener('error', onReady);
    };

    const onReady = () => {
      cleanup();
      resolve();
    };

    timeoutId = window.setTimeout(onReady, 8000);
    audio.addEventListener('canplaythrough', onReady, { once: true });
    audio.addEventListener('error', onReady, { once: true });
    audio.src = objectUrl;
    audio.load();
  });
}
