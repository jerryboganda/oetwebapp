import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const hub = vi.hoisted(() => {
  const state = {
    urls: [] as string[],
    tokenFactories: [] as Array<() => string | Promise<string>>,
  };

  class FakeHubBuilder {
    withUrl(url: string, options: { accessTokenFactory: () => string | Promise<string> }) {
      state.urls.push(url);
      state.tokenFactories.push(options.accessTokenFactory);
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        on: vi.fn(),
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        onclose: vi.fn(),
        start: vi.fn().mockResolvedValue(undefined),
        stop: vi.fn().mockResolvedValue(undefined),
        invoke: vi.fn().mockResolvedValue(undefined),
      };
    }
  }

  return { state, FakeHubBuilder };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: hub.FakeHubBuilder,
  LogLevel: { Information: 2, Warning: 3 },
}));

const authMock = vi.hoisted(() => ({ ensureFreshAccessToken: vi.fn() }));
vi.mock('@/lib/auth-client', () => authMock);

import { connectWritingSubmissionStream, connectWritingTodayStream, type Disposable } from '../realtime';

const hubCases: Array<[string, string, () => Disposable]> = [
  ['submission', '/hubs/writing-submissions', () => connectWritingSubmissionStream('sub-1', { onGradeReady: vi.fn() })],
  ['today', '/hubs/writing-today', () => connectWritingTodayStream({ onUpdate: vi.fn() })],
];

describe('Writing realtime hubs: access token', () => {
  let stream: Disposable | null = null;

  beforeEach(() => {
    hub.state.urls = [];
    hub.state.tokenFactories = [];
    authMock.ensureFreshAccessToken.mockReset();
  });

  afterEach(() => {
    stream?.close();
    stream = null;
  });

  it.each(hubCases)(
    'asks for a fresh token every time SignalR needs one on the %s hub (reconnects after expiry)',
    async (_name, hubPath, connect) => {
      authMock.ensureFreshAccessToken
        .mockResolvedValueOnce('token-at-connect')
        .mockResolvedValueOnce('token-after-refresh')
        .mockResolvedValueOnce('token-after-second-refresh');

      stream = connect();
      await vi.waitFor(() => expect(hub.state.tokenFactories).toHaveLength(1));
      expect(hub.state.urls[0]).toMatch(new RegExp(`${hubPath}$`));

      const factory = hub.state.tokenFactories[0];
      // Each reconnect / long-poll goes through the factory and gets whatever is current then.
      await expect(factory()).resolves.toBe('token-after-refresh');
      await expect(factory()).resolves.toBe('token-after-second-refresh');
    },
  );

  it('falls back to the connect-time token only when refreshing fails', async () => {
    authMock.ensureFreshAccessToken
      .mockResolvedValueOnce('token-at-connect')
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(null);

    stream = connectWritingSubmissionStream('sub-1', { onGradeReady: vi.fn() });
    await vi.waitFor(() => expect(hub.state.tokenFactories).toHaveLength(1));
    const factory = hub.state.tokenFactories[0];

    await expect(factory()).resolves.toBe('token-at-connect');
    await expect(factory()).resolves.toBe('token-at-connect');
  });

  it('never yields undefined when there is no token at all', async () => {
    authMock.ensureFreshAccessToken.mockResolvedValue(null);

    stream = connectWritingTodayStream({ onUpdate: vi.fn() });
    await vi.waitFor(() => expect(hub.state.tokenFactories).toHaveLength(1));

    await expect(hub.state.tokenFactories[0]()).resolves.toBe('');
  });
});
