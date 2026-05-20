import { act, renderHook, waitFor } from '@testing-library/react';
import { vi } from 'vitest';

import { useAiAssistant } from './use-ai-assistant';
import { createAiAssistantConnection } from '@/lib/ai-assistant/signalr';
import type { ChatThreadDto, StreamFrame } from '@/lib/ai-assistant/types';

const mocks = vi.hoisted(() => {
  const state: { frameHandler: ((frame: unknown) => void) | null } = { frameHandler: null };
  const connection = {
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
    onFrame: vi.fn((handler: (frame: unknown) => void) => {
      state.frameHandler = handler;
      return vi.fn();
    }),
    onStateChange: vi.fn(() => vi.fn()),
    subscribeThread: vi.fn(async () => undefined),
    unsubscribeThread: vi.fn(async () => undefined),
    startTurn: vi.fn(async () => undefined),
    cancel: vi.fn(async () => undefined),
  };
  const aiAssistantClient = {
    listThreads: vi.fn(),
    getThread: vi.fn(),
    createThread: vi.fn(),
    deleteThread: vi.fn(),
    cancelMessage: vi.fn(),
  };

  return {
    state,
    connection,
    aiAssistantClient,
  };
});

vi.mock('@/lib/ai-assistant/client', () => ({
  aiAssistantClient: mocks.aiAssistantClient,
}));

vi.mock('@/lib/ai-assistant/signalr', () => ({
  createAiAssistantConnection: vi.fn(() => mocks.connection),
}));

const thread: ChatThreadDto = {
  id: 'thread-1',
  title: 'Thread 1',
  isArchived: false,
  createdAt: '2026-05-20T00:00:00.000Z',
  updatedAt: '2026-05-20T00:00:00.000Z',
  messageCount: 0,
};

const secondThread: ChatThreadDto = {
  id: 'thread-2',
  title: 'Thread 2',
  isArchived: false,
  createdAt: '2026-05-20T00:00:00.000Z',
  updatedAt: '2026-05-20T00:00:00.000Z',
  messageCount: 0,
};

async function renderStreamingHook() {
  const rendered = renderHook(() => useAiAssistant());

  await waitFor(() => expect(mocks.aiAssistantClient.listThreads).toHaveBeenCalled());

  await act(async () => {
    await rendered.result.current.selectThread(thread.id);
  });

  const frame: StreamFrame = {
    type: 'MessageStart',
    threadId: thread.id,
    messageId: 'message-1',
    role: 'assistant',
  };
  act(() => {
    mocks.state.frameHandler?.(frame);
  });

  await waitFor(() => expect(rendered.result.current.isStreaming).toBe(true));

  return rendered;
}

describe('useAiAssistant cancel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.state.frameHandler = null;
    mocks.aiAssistantClient.listThreads.mockResolvedValue([thread, secondThread]);
    mocks.aiAssistantClient.getThread.mockImplementation(async (threadId: string) => ({
      thread: threadId === secondThread.id ? secondThread : thread,
      messages: [],
    }));
    mocks.aiAssistantClient.createThread.mockResolvedValue(thread);
    mocks.aiAssistantClient.cancelMessage.mockResolvedValue(undefined);
    mocks.connection.cancel.mockResolvedValue(undefined);
  });

  it('cancels live streaming turns through the SignalR hub', async () => {
    const rendered = await renderStreamingHook();

    await act(async () => {
      await rendered.result.current.cancel();
    });

    expect(mocks.connection.cancel).toHaveBeenCalledWith('message-1');
    expect(mocks.aiAssistantClient.cancelMessage).not.toHaveBeenCalled();
  });

  it('does not connect or load threads when disabled', () => {
    const rendered = renderHook(() => useAiAssistant(false));

    expect(createAiAssistantConnection).not.toHaveBeenCalled();
    expect(mocks.aiAssistantClient.listThreads).not.toHaveBeenCalled();
    expect(rendered.result.current.threads).toHaveLength(0);
    expect(rendered.result.current.isConnected).toBe(false);
  });

  it('falls back to REST cancel when hub cancel fails', async () => {
    mocks.connection.cancel.mockRejectedValueOnce(new Error('reconnecting'));
    const rendered = await renderStreamingHook();

    await act(async () => {
      await rendered.result.current.cancel();
    });

    expect(mocks.connection.cancel).toHaveBeenCalledWith('message-1');
    expect(mocks.aiAssistantClient.cancelMessage).toHaveBeenCalledWith('message-1');
  });

  it('ignores stream frames for non-active threads', async () => {
    const rendered = renderHook(() => useAiAssistant());
    await waitFor(() => expect(mocks.aiAssistantClient.listThreads).toHaveBeenCalled());

    await act(async () => {
      await rendered.result.current.selectThread(thread.id);
    });

    act(() => {
      mocks.state.frameHandler?.({
        type: 'MessageStart',
        threadId: secondThread.id,
        messageId: 'other-message',
        role: 'assistant',
      } satisfies StreamFrame);
    });

    expect(rendered.result.current.isStreaming).toBe(false);
    expect(rendered.result.current.messages).toHaveLength(0);
  });

  it('unsubscribes from the previous thread when switching or clearing threads', async () => {
    const rendered = renderHook(() => useAiAssistant());
    await waitFor(() => expect(mocks.aiAssistantClient.listThreads).toHaveBeenCalled());

    await act(async () => {
      await rendered.result.current.selectThread(thread.id);
    });
    await act(async () => {
      await rendered.result.current.selectThread(secondThread.id);
    });
    await act(async () => {
      await rendered.result.current.selectThread(null);
    });

    expect(mocks.connection.subscribeThread).toHaveBeenCalledWith(thread.id);
    expect(mocks.connection.subscribeThread).toHaveBeenCalledWith(secondThread.id);
    expect(mocks.connection.unsubscribeThread).toHaveBeenCalledWith(thread.id);
    expect(mocks.connection.unsubscribeThread).toHaveBeenCalledWith(secondThread.id);
  });

  it('keeps the active thread subscribed when opening another thread fails', async () => {
    const rendered = renderHook(() => useAiAssistant());
    await waitFor(() => expect(mocks.aiAssistantClient.listThreads).toHaveBeenCalled());

    await act(async () => {
      await rendered.result.current.selectThread(thread.id);
    });

    mocks.aiAssistantClient.getThread.mockRejectedValueOnce(new Error('offline'));
    await act(async () => {
      await rendered.result.current.selectThread(secondThread.id);
    });

    expect(rendered.result.current.activeThread?.id).toBe(thread.id);
    expect(rendered.result.current.error).toContain('Failed to open conversation: offline');
    expect(mocks.connection.unsubscribeThread).not.toHaveBeenCalledWith(thread.id);
    expect(mocks.connection.subscribeThread).not.toHaveBeenCalledWith(secondThread.id);

    act(() => {
      mocks.state.frameHandler?.({
        type: 'MessageStart',
        threadId: thread.id,
        messageId: 'message-after-failed-switch',
        role: 'assistant',
      } satisfies StreamFrame);
    });

    await waitFor(() => expect(rendered.result.current.isStreaming).toBe(true));
  });

  it('subscribes an auto-created first thread before sending the first turn', async () => {
    mocks.aiAssistantClient.createThread.mockResolvedValue(secondThread);
    const rendered = renderHook(() => useAiAssistant());
    await waitFor(() => expect(mocks.aiAssistantClient.listThreads).toHaveBeenCalled());

    await act(async () => {
      await rendered.result.current.sendMessage('Hello assistant');
    });

    expect(mocks.aiAssistantClient.createThread).toHaveBeenCalledWith({ title: 'Hello assistant' });
    expect(mocks.connection.subscribeThread).toHaveBeenCalledWith(secondThread.id);
    expect(mocks.connection.startTurn).toHaveBeenCalledWith(secondThread.id, 'Hello assistant');
  });
});