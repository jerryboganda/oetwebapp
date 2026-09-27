import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HubConnection } from '@microsoft/signalr';

const signalrState = vi.hoisted(() => ({
  withUrlCalls: [] as Array<{ url: string; options: Record<string, unknown> }>,
  innerSends: [] as Array<Record<string, unknown>>,
}));

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn(async () => 'access-token-fixture'),
}));

vi.mock('@microsoft/signalr', () => {
  class HttpClient {
    get(url: string, options: Record<string, unknown> = {}) {
      return this.send({ ...options, method: 'GET', url });
    }
    send(_request: Record<string, unknown>): Promise<unknown> {
      throw new Error('abstract');
    }
    getCookieString(_url: string) {
      return '';
    }
  }
  class DefaultHttpClient extends HttpClient {
    constructor(_logger: unknown) {
      super();
    }
    override send(request: Record<string, unknown>) {
      signalrState.innerSends.push(request);
      return Promise.resolve({ statusCode: 200 });
    }
  }
  const fakeConnection = {
    serverTimeoutInMilliseconds: 0,
    keepAliveIntervalInMilliseconds: 0,
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
  };
  class HubConnectionBuilder {
    withUrl(url: string, options: Record<string, unknown>) {
      signalrState.withUrlCalls.push({ url, options });
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return fakeConnection;
    }
  }
  return {
    HubConnectionBuilder,
    HttpClient,
    DefaultHttpClient,
    NullLogger: { instance: {} },
    HttpTransportType: { LongPolling: 4 },
    LogLevel: { Information: 2, Warning: 3 },
  };
});

import { createOwnerAgentConnection, openOwnerAgentEventStream, resolveOwnerAgentHubUrl } from '../signalr';
import type { AgentEvent } from '../types';

type Subscriber = { next: (e: AgentEvent) => void; error: (err: unknown) => void; complete: () => void };

function createFakeConnection() {
  const subscribers: Subscriber[] = [];
  const streamCalls: Array<[string, string, number]> = [];
  const handlers: { reconnected?: () => void; reconnecting?: () => void; close?: (e?: Error) => void } = {};
  const connection = {
    state: 'Disconnected',
    start: vi.fn(async () => {
      connection.state = 'Connected';
    }),
    stop: vi.fn(async () => {
      connection.state = 'Disconnected';
    }),
    stream: vi.fn((method: string, sessionId: string, afterSeq: number) => {
      streamCalls.push([method, sessionId, afterSeq]);
      return {
        subscribe: (subscriber: Subscriber) => {
          subscribers.push(subscriber);
          return { dispose: vi.fn() };
        },
      };
    }),
  };
  const factory = vi.fn(async (options: { onReconnected?: () => void; onReconnecting?: () => void; onClose?: (e?: Error) => void }) => {
    handlers.reconnected = options.onReconnected;
    handlers.reconnecting = options.onReconnecting;
    handlers.close = options.onClose;
    return connection as unknown as HubConnection;
  });
  return { connection, factory, subscribers, streamCalls, handlers };
}

const SESSION = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';
const event = (seq: number): AgentEvent => ({ seq, sessionId: SESSION, ts: '2026-09-27T10:00:00.000Z', type: 'text_delta', data: { messageId: 'm', text: String(seq) } });

describe('owner-agent hub stream', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    signalrState.withUrlCalls.length = 0;
    signalrState.innerSends.length = 0;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('streams from afterSeq and resubscribes from the last seen seq after a stream error', async () => {
    const fake = createFakeConnection();
    const received: number[] = [];
    const stream = openOwnerAgentEventStream({
      sessionId: SESSION,
      afterSeq: 3,
      getUnlockTicket: () => 'ticket',
      onEvent: (e) => received.push(e.seq),
      connectionFactory: fake.factory,
    });

    await vi.advanceTimersByTimeAsync(0);
    expect(fake.streamCalls).toEqual([['Stream', SESSION, 3]]);

    fake.subscribers[0].next(event(4));
    fake.subscribers[0].next(event(5));
    fake.subscribers[0].error(new Error('connection dropped'));

    await vi.advanceTimersByTimeAsync(1_000);
    expect(fake.streamCalls[1]).toEqual(['Stream', SESSION, 5]);

    // A resubscribe that replays already-delivered events must not duplicate them.
    fake.subscribers[1].next(event(5));
    fake.subscribers[1].next(event(6));
    expect(received).toEqual([4, 5, 6]);
    expect(stream.lastSeq()).toBe(6);

    await stream.close();
    expect(fake.connection.stop).toHaveBeenCalled();
  });

  it('waits for the hub to reconnect, then resumes from the last seq', async () => {
    const fake = createFakeConnection();
    const states: string[] = [];
    const stream = openOwnerAgentEventStream({
      sessionId: SESSION,
      getUnlockTicket: () => 'ticket',
      onEvent: () => undefined,
      onStateChange: (state) => states.push(state),
      connectionFactory: fake.factory,
    });
    await vi.advanceTimersByTimeAsync(0);
    fake.subscribers[0].next(event(1));
    fake.subscribers[0].next(event(2));

    fake.connection.state = 'Reconnecting';
    fake.handlers.reconnecting?.();
    fake.subscribers[0].error(new Error('Invocation canceled'));
    await vi.advanceTimersByTimeAsync(5_000);
    expect(fake.streamCalls).toHaveLength(1); // no resubscribe while reconnecting

    fake.connection.state = 'Connected';
    fake.handlers.reconnected?.();
    expect(fake.streamCalls[1]).toEqual(['Stream', SESSION, 2]);
    expect(states).toContain('reconnecting');
    expect(states[states.length - 1]).toBe('connected');

    await stream.close();
  });

  it('stops (no retry storm) when the hub rejects the unlock and no newer ticket is held', async () => {
    const fake = createFakeConnection();
    const onUnlockRejected = vi.fn();
    const onError = vi.fn();
    const stream = openOwnerAgentEventStream({
      sessionId: SESSION,
      getUnlockTicket: () => 'ticket-a',
      onEvent: () => undefined,
      onError,
      onUnlockRejected,
      connectionFactory: fake.factory,
    });
    await vi.advanceTimersByTimeAsync(0);
    fake.subscribers[0].error(new Error('An error occurred on the server while streaming results. HubException: owner_agent_unlock_expired'));

    expect(onError).toHaveBeenCalledTimes(1);
    expect(onUnlockRejected).toHaveBeenCalledWith('ticket-a');
    expect(fake.connection.stop).toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(120_000);
    expect(fake.streamCalls).toHaveLength(1);
    expect(fake.factory).toHaveBeenCalledTimes(1);

    await stream.close();
  });

  it('reconnects with the current ticket when the hub rejected an older one', async () => {
    const fake = createFakeConnection();
    let ticket = 'ticket-a';
    const onUnlockRejected = vi.fn();
    const stream = openOwnerAgentEventStream({
      sessionId: SESSION,
      getUnlockTicket: () => ticket,
      onEvent: () => undefined,
      onUnlockRejected,
      connectionFactory: fake.factory,
    });
    await vi.advanceTimersByTimeAsync(0);
    fake.subscribers[0].next(event(7));

    ticket = 'ticket-b'; // re-minted by the refresh scheduler
    fake.subscribers[0].error(new Error('HubException: owner_agent_unlock_expired'));
    await vi.advanceTimersByTimeAsync(1_000);

    expect(onUnlockRejected).not.toHaveBeenCalled();
    expect(fake.factory).toHaveBeenCalledTimes(2);
    expect(fake.streamCalls[1]).toEqual(['Stream', SESSION, 7]);

    await stream.close();
  });

  it('stops on a rejected session id instead of retrying', async () => {
    const fake = createFakeConnection();
    const stream = openOwnerAgentEventStream({
      sessionId: SESSION,
      getUnlockTicket: () => 'ticket',
      onEvent: () => undefined,
      connectionFactory: fake.factory,
    });
    await vi.advanceTimersByTimeAsync(0);
    fake.subscribers[0].error(new Error('HubException: invalid_session_id'));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(fake.streamCalls).toHaveLength(1);
    await stream.close();
  });

  it('does not deliver events after close', async () => {
    const fake = createFakeConnection();
    const onEvent = vi.fn();
    const stream = openOwnerAgentEventStream({ sessionId: SESSION, getUnlockTicket: () => null, onEvent, connectionFactory: fake.factory });
    await vi.advanceTimersByTimeAsync(0);
    await stream.close();
    fake.subscribers[0].next(event(1));
    expect(onEvent).not.toHaveBeenCalled();
  });
});

describe('owner-agent hub connection builder', () => {
  it('targets the owner-agent hub over long polling and stamps the current unlock ticket on every request', async () => {
    signalrState.withUrlCalls.length = 0;
    signalrState.innerSends.length = 0;
    let ticket: string | null = 'ticket-1';

    await createOwnerAgentConnection({ getUnlockTicket: () => ticket });

    expect(resolveOwnerAgentHubUrl()).toBe('/api/backend/v1/owner-agent/hub');
    const { url, options } = signalrState.withUrlCalls[0];
    expect(url).toBe('/api/backend/v1/owner-agent/hub');
    expect(options.transport).toBe(4);
    expect(options.headers).toEqual({ 'X-Owner-Agent-Unlock': 'ticket-1' });

    const httpClient = options.httpClient as { send: (r: Record<string, unknown>) => Promise<unknown> };
    ticket = 'ticket-2';
    await httpClient.send({ method: 'GET', url: '/poll', headers: { 'X-Owner-Agent-Unlock': 'ticket-1', Other: 'x' } });
    expect(signalrState.innerSends[0].headers).toEqual({ 'X-Owner-Agent-Unlock': 'ticket-2', Other: 'x' });

    ticket = null;
    await httpClient.send({ method: 'POST', url: '/send', headers: { 'X-Owner-Agent-Unlock': 'stale' } });
    expect(signalrState.innerSends[1].headers).toEqual({});

    const accessTokenFactory = options.accessTokenFactory as () => Promise<string>;
    await expect(accessTokenFactory()).resolves.toBe('access-token-fixture');
  });
});
