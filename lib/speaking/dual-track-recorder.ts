/**
 * Speaking Dual-Track Local Audio Safety Recorder
 *
 * Runs a continuous parallel local audio capture throughout the active
 * 5-minute consultation. This provides a resilient safety net alongside
 * the live turn-by-turn WebSocket/SignalR Whisper streaming.
 *
 * If the live network connection drops or packet transmission fails,
 * the full dual-track consultation audio is preserved locally in IndexedDB
 * and can be uploaded / recovered without candidate speech loss.
 */

export interface DualTrackRecorderState {
  isRecording: boolean;
  isPaused: boolean;
  elapsedMs: number;
  chunkCount: number;
  totalBytes: number;
  mimeType: string;
}

export interface DualTrackRecordingResult {
  blob: Blob;
  durationMs: number;
  mimeType: string;
  sizeBytes: number;
  createdAt: string;
}

const DB_NAME = 'oet-speaking-recordings';
const DB_VERSION = 1;
const STORE_NAME = 'dual_track_recordings';

function openIndexedDb(): Promise<IDBDatabase | null> {
  if (typeof window === 'undefined' || !window.indexedDB) {
    return Promise.resolve(null);
  }
  return new Promise((resolve) => {
    try {
      const request = indexedDB.open(DB_NAME, DB_VERSION);
      request.onupgradeneeded = (e) => {
        const db = (e.target as IDBOpenDBRequest).result;
        if (!db.objectStoreNames.contains(STORE_NAME)) {
          db.createObjectStore(STORE_NAME, { keyPath: 'sessionId' });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
}

async function persistToIndexedDb(
  sessionId: string,
  blob: Blob,
  meta: { durationMs: number; mimeType: string; createdAt: string },
): Promise<void> {
  const db = await openIndexedDb();
  if (!db) return;
  return new Promise((resolve) => {
    try {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      const store = tx.objectStore(STORE_NAME);
      store.put({
        sessionId,
        blob,
        durationMs: meta.durationMs,
        mimeType: meta.mimeType,
        createdAt: meta.createdAt,
      });
      tx.oncomplete = () => resolve();
      tx.onerror = () => resolve();
    } catch {
      resolve();
    }
  });
}

export async function retrieveStoredRecording(
  sessionId: string,
): Promise<DualTrackRecordingResult | null> {
  const db = await openIndexedDb();
  if (!db) return null;
  return new Promise((resolve) => {
    try {
      const tx = db.transaction(STORE_NAME, 'readonly');
      const store = tx.objectStore(STORE_NAME);
      const req = store.get(sessionId);
      req.onsuccess = () => {
        const data = req.result;
        if (!data || !data.blob) {
          resolve(null);
          return;
        }
        resolve({
          blob: data.blob,
          durationMs: data.durationMs,
          mimeType: data.mimeType,
          sizeBytes: data.blob.size,
          createdAt: data.createdAt,
        });
      };
      req.onerror = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
}

export async function clearStoredRecording(sessionId: string): Promise<void> {
  const db = await openIndexedDb();
  if (!db) return;
  return new Promise((resolve) => {
    try {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      const store = tx.objectStore(STORE_NAME);
      store.delete(sessionId);
      tx.oncomplete = () => resolve();
      tx.onerror = () => resolve();
    } catch {
      resolve();
    }
  });
}

function pickSupportedMimeType(): string {
  if (typeof MediaRecorder === 'undefined') return 'audio/webm';
  const candidates = [
    'audio/webm;codecs=opus',
    'audio/webm',
    'audio/mp4',
    'audio/ogg;codecs=opus',
    'audio/wav',
  ];
  for (const candidate of candidates) {
    if (MediaRecorder.isTypeSupported(candidate)) {
      return candidate;
    }
  }
  return 'audio/webm';
}

export class DualTrackRecorder {
  private sessionId: string;
  private mediaRecorder: MediaRecorder | null = null;
  private chunks: Blob[] = [];
  private startTime = 0;
  private totalBytes = 0;
  private mimeType: string;
  private isRecordingState = false;
  private isPausedState = false;

  constructor(sessionId: string) {
    this.sessionId = sessionId;
    this.mimeType = pickSupportedMimeType();
  }

  public start(stream: MediaStream): void {
    if (typeof MediaRecorder === 'undefined') {
      console.warn('[DualTrackRecorder] MediaRecorder not supported in this environment.');
      return;
    }
    this.chunks = [];
    this.totalBytes = 0;
    this.startTime = Date.now();
    this.mimeType = pickSupportedMimeType();

    try {
      this.mediaRecorder = new MediaRecorder(stream, {
        mimeType: this.mimeType,
        audioBitsPerSecond: 64_000,
      });

      this.mediaRecorder.ondataavailable = (event: BlobEvent) => {
        if (event.data && event.data.size > 0) {
          this.chunks.push(event.data);
          this.totalBytes += event.data.size;
        }
      };

      // Collect audio chunks every 1000ms for continuous buffering
      this.mediaRecorder.start(1000);
      this.isRecordingState = true;
      this.isPausedState = false;
    } catch (err) {
      console.error('[DualTrackRecorder] Failed to start MediaRecorder:', err);
    }
  }

  public pause(): void {
    if (this.mediaRecorder && this.isRecordingState && !this.isPausedState) {
      try {
        this.mediaRecorder.pause();
        this.isPausedState = true;
      } catch (err) {
        console.warn('[DualTrackRecorder] Pause failed:', err);
      }
    }
  }

  public resume(): void {
    if (this.mediaRecorder && this.isRecordingState && this.isPausedState) {
      try {
        this.mediaRecorder.resume();
        this.isPausedState = false;
      } catch (err) {
        console.warn('[DualTrackRecorder] Resume failed:', err);
      }
    }
  }

  public async stop(): Promise<DualTrackRecordingResult | null> {
    if (!this.mediaRecorder || !this.isRecordingState) {
      return null;
    }

    return new Promise<DualTrackRecordingResult | null>((resolve) => {
      const recorder = this.mediaRecorder!;
      const durationMs = Date.now() - this.startTime;
      const createdAt = new Date().toISOString();

      recorder.onstop = async () => {
        const fullBlob = new Blob(this.chunks, { type: this.mimeType });
        this.isRecordingState = false;
        this.isPausedState = false;

        const result: DualTrackRecordingResult = {
          blob: fullBlob,
          durationMs,
          mimeType: this.mimeType,
          sizeBytes: fullBlob.size,
          createdAt,
        };

        // Persist to IndexedDB for safety
        await persistToIndexedDb(this.sessionId, fullBlob, {
          durationMs,
          mimeType: this.mimeType,
          createdAt,
        });

        resolve(result);
      };

      try {
        recorder.stop();
      } catch (err) {
        console.error('[DualTrackRecorder] Error stopping MediaRecorder:', err);
        resolve(null);
      }
    });
  }

  public getState(): DualTrackRecorderState {
    const elapsedMs = this.startTime > 0 ? Date.now() - this.startTime : 0;
    return {
      isRecording: this.isRecordingState,
      isPaused: this.isPausedState,
      elapsedMs,
      chunkCount: this.chunks.length,
      totalBytes: this.totalBytes,
      mimeType: this.mimeType,
    };
  }

  public getBlobNow(): Blob | null {
    if (this.chunks.length === 0) return null;
    return new Blob(this.chunks, { type: this.mimeType });
  }
}
