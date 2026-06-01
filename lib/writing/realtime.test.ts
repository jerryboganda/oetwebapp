import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { WritingCoachHintDto } from './types';
import type { WritingCoachHintRequestPayload } from './api';

const { mockEnsureFreshAccessToken, mockRequestWritingCoachHints } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockRequestWritingCoachHints: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));

vi.mock('./api', () => ({
  requestWritingCoachHints: mockRequestWritingCoachHints,
}));

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;

  static instances: MockWebSocket[] = [];

  url: string;
  readyState = MockWebSocket.CONNECTING;
  send = vi.fn();
  close = vi.fn((code?: number, reason?: string) => {
    this.readyState = MockWebSocket.CLOSED;
    this.emit('close', { code, reason });
  });

  private readonly listeners = new Map<string, Array<(event: any) => void>>();

  constructor(url: string) {
    this.url = url;
    MockWebSocket.instances.push(this);
  }

  addEventListener(type: string, listener: (event: any) => void) {
    const existing = this.listeners.get(type) ?? [];
    existing.push(listener);
    this.listeners.set(type, existing);
  }

  emit(type: string, event: any = {}) {
    if (type === 'open') {
      this.readyState = MockWebSocket.OPEN;
    }

    const listeners = this.listeners.get(type) ?? [];
    for (const listener of listeners) {
      listener(event);
    }
  }
}

async function flushMicrotasks() {
  await Promise.resolve();
  await Promise.resolve();
}

describe('writing realtime helpers', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal('WebSocket', MockWebSocket as unknown as typeof WebSocket);
    MockWebSocket.instances = [];
    mockEnsureFreshAccessToken.mockReset();
    mockRequestWritingCoachHints.mockReset();
    mockEnsureFreshAccessToken.mockResolvedValue('token-123');
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('opens the coach websocket, forwards hints, and disposes cleanly', async () => {
    const realtime = await import('./realtime');
    const onHint = vi.fn();
    const onError = vi.fn();
    const onOpen = vi.fn();
    const onClose = vi.fn();
    const onStatusChange = vi.fn();

    const payload: WritingCoachHintRequestPayload = {
      sessionId: 'session-1',
      scenarioId: 'scenario-1',
      letterContent: 'Draft text',
      wordCount: 42,
      letterType: 'LT-RR',
      profession: 'medicine',
    };

    const hint: WritingCoachHintDto = {
      id: 'hint-1',
      sessionId: 'session-1',
      category: 'style',
      text: 'Tighten the opening sentence.',
      ruleId: null,
      charStart: null,
      charEnd: null,
      createdAt: '2026-05-31T00:00:00Z',
      dismissed: false,
    };

    const stream = realtime.connectWritingCoachStream(
      'session-1',
      { onHint, onError, onOpen, onClose, onStatusChange },
      { initialMs: 1, maxMs: 1, maxAttempts: 1 },
      () => payload,
    );

    await flushMicrotasks();

    expect(MockWebSocket.instances).toHaveLength(1);
    const socket = MockWebSocket.instances[0];
    expect(socket.url.startsWith('ws://')).toBe(true);
    expect(socket.url).toContain('/ws/writing/coach/session-1?access_token=token-123');
    expect(onStatusChange).toHaveBeenCalledWith('connecting');

    socket.emit('open');

    expect(onStatusChange).toHaveBeenCalledWith('open');
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(socket.send).toHaveBeenCalledWith(JSON.stringify({ ...payload, sessionId: 'session-1' }));

    socket.emit('message', { data: JSON.stringify({ type: 'hint', hint }) });

    expect(onHint).toHaveBeenCalledWith(hint);
    expect(onError).not.toHaveBeenCalled();

    stream.close();

    expect(socket.close).toHaveBeenCalledWith(1000, 'client-disposal');
    expect(onClose).toHaveBeenCalledWith(true);
  });

  it('polls coach hints over HTTP and stops after disposal', async () => {
    const realtime = await import('./realtime');
    const onHint = vi.fn();
    const payload: WritingCoachHintRequestPayload = {
      sessionId: 'session-2',
      scenarioId: 'scenario-2',
      letterContent: 'Another draft',
      wordCount: 64,
      letterType: 'LT-RR',
      profession: 'medicine',
    };
    const hint: WritingCoachHintDto = {
      id: 'hint-2',
      sessionId: 'session-2',
      category: 'structure',
      text: 'Separate the rationale into its own paragraph.',
      ruleId: 'R01',
      charStart: 4,
      charEnd: 19,
      createdAt: '2026-05-31T00:01:00Z',
      dismissed: false,
    };

    mockRequestWritingCoachHints.mockResolvedValue({ hints: [hint] });

    const disposable = realtime.coachPollingFallback(() => payload, onHint, 10);

    await vi.advanceTimersByTimeAsync(10);
    await flushMicrotasks();

    expect(mockRequestWritingCoachHints).toHaveBeenCalledWith(payload);
    expect(onHint).toHaveBeenCalledWith(hint);

    disposable.close();

    await vi.advanceTimersByTimeAsync(20);
    await flushMicrotasks();

    expect(mockRequestWritingCoachHints).toHaveBeenCalledTimes(1);
  });
});