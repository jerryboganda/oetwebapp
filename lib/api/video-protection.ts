'use client';

import { apiClient } from '@/lib/api';

/**
 * Capture-protection / tamper telemetry (Course Platform Security
 * Requirements §2). Kinds MUST match the backend whitelist
 * (`VideoProtectionKinds` in Domain/VideoLibraryEntities.cs) — an unknown
 * kind is silently dropped server-side rather than rejecting the whole
 * batch, so a typo here just loses that one signal.
 */
export type VideoProtectionEventKind =
  | 'protection_engaged'
  | 'protection_unavailable'
  | 'capture_detected'
  | 'screenshot_detected'
  | 'watermark_tampered'
  | 'devtools_suspected'
  | 'visibility_hidden'
  | 'focus_lost'
  | 'integrity_signal';

export interface VideoProtectionEventInput {
  kind: VideoProtectionEventKind;
  videoId?: string;
  sessionId?: string;
  metadata?: Record<string, unknown>;
}

export interface VideoProtectionReportOptions {
  /** Flush this event now instead of waiting for the normal telemetry batch. */
  immediate?: boolean;
}

interface QueuedEvent {
  videoId?: string;
  sessionId?: string;
  kind: string;
  occurredAt: string;
  metadata?: Record<string, unknown>;
}

const BATCH_MAX = 20;
const FLUSH_INTERVAL_MS = 5000;

let queue: QueuedEvent[] = [];
let flushTimer: ReturnType<typeof setTimeout> | null = null;
let flushing = false;

function scheduleFlush(): void {
  if (flushTimer !== null || typeof window === 'undefined') return;
  flushTimer = setTimeout(() => {
    flushTimer = null;
    void flush();
  }, FLUSH_INTERVAL_MS);
}

async function flush(): Promise<void> {
  if (flushing || queue.length === 0) return;
  flushing = true;
  const batch = queue.splice(0, BATCH_MAX);
  try {
    await apiClient.post('/v1/video-library/protection-events', { events: batch });
  } catch {
    // Best-effort telemetry — a reporting failure must never surface to the
    // player or interrupt playback.
  } finally {
    flushing = false;
  }
  if (queue.length > 0) scheduleFlush();
}

/** Fire-and-forget: queues the event and flushes on a short timer (or
 * immediately once the queue fills a batch), so a burst of signals (e.g.
 * several risk events in one tick) costs one request, not several. */
export function reportProtectionEvent(
  input: VideoProtectionEventInput,
  options: VideoProtectionReportOptions = {},
): void {
  queue.push({
    videoId: input.videoId,
    sessionId: input.sessionId,
    kind: input.kind,
    occurredAt: new Date().toISOString(),
    metadata: input.metadata,
  });

  if (options.immediate || queue.length >= BATCH_MAX) {
    void flush();
    return;
  }
  scheduleFlush();
}

if (typeof document !== 'undefined') {
  // Flush on tab/app close so nothing queued is lost mid-session.
  document.addEventListener('pagehide', () => {
    void flush();
  });
}
