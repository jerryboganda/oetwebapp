import type { IncomingMessage, ServerResponse } from 'node:http';
import type { AgentEvent } from './contract.js';
import type { Store } from './store.js';

// SSE framing for GET /v1/sessions/:id/events (CONTRACT.md §4):
//   id: <seq>\nevent: <type>\ndata: <AgentEvent JSON>\n\n
// `?after=N` replays every persisted event with seq > N, then streams live
// events without gaps or duplicates. Idle streams get a `heartbeat` (no id,
// no seq, never persisted) every 15 s.

export function formatSseEvent(event: AgentEvent): string {
  return `id: ${event.seq}\nevent: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`;
}

export function formatHeartbeat(sessionId: string, now: Date = new Date()): string {
  return `event: heartbeat\ndata: ${JSON.stringify({ sessionId, ts: now.toISOString(), type: 'heartbeat', data: {} })}\n\n`;
}

/** Parses `?after=` (or Last-Event-ID); invalid or negative → 0. */
export function parseAfter(value: unknown): number {
  const raw = Array.isArray(value) ? value[0] : value;
  if (typeof raw !== 'string' && typeof raw !== 'number') return 0;
  const n = Number(raw);
  return Number.isSafeInteger(n) && n > 0 ? n : 0;
}

export interface StreamOptions {
  heartbeatMs?: number;
}

export async function streamSessionEvents(
  store: Pick<Store, 'subscribe' | 'iterEvents'>,
  sessionId: string,
  afterSeq: number,
  req: IncomingMessage,
  res: ServerResponse,
  options: StreamOptions = {},
): Promise<void> {
  const heartbeatMs = options.heartbeatMs ?? 15_000;
  let lastSent = afterSeq;
  let replaying = true;
  let closed = false;
  let lastWrite = Date.now();
  const buffered: AgentEvent[] = [];

  const write = (chunk: string): void => {
    if (closed || res.destroyed) return;
    res.write(chunk);
    lastWrite = Date.now();
  };
  const send = (event: AgentEvent): void => {
    if (event.seq <= lastSent) return;
    lastSent = event.seq;
    write(formatSseEvent(event));
  };

  // Subscribe before replaying so nothing appended during the replay is lost.
  const unsubscribe = store.subscribe(sessionId, (event) => {
    if (replaying) buffered.push(event);
    else send(event);
  });
  const heartbeat = setInterval(() => {
    if (Date.now() - lastWrite >= heartbeatMs) write(formatHeartbeat(sessionId));
  }, Math.max(50, Math.min(5_000, Math.floor(heartbeatMs / 3))));
  heartbeat.unref();

  const finished = new Promise<void>((resolve) => {
    const finish = (): void => {
      if (closed) return;
      closed = true;
      clearInterval(heartbeat);
      unsubscribe();
      resolve();
    };
    res.on('close', finish);
    res.on('error', finish);
    req.on('aborted', finish);
  });

  write(': stream open\n\n');
  try {
    for await (const event of store.iterEvents(sessionId, afterSeq)) {
      if (closed) break;
      send(event);
    }
  } finally {
    replaying = false;
    for (const event of buffered.splice(0)) send(event);
  }
  await finished;
}
