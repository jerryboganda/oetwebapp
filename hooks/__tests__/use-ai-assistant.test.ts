import { renderHook, act, waitFor } from '@testing-library/react';
import { useAiAssistant } from '../use-ai-assistant';

// ─── Mock SignalR ────────────────────────────────────────────────────────────

const mockConnection = {
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  invoke: vi.fn().mockResolvedValue(undefined),
  on: vi.fn(),
  off: vi.fn(),
};

const mockConnectionBuilder = {
  withUrl: vi.fn().mockReturnThis(),
  withAutomaticReconnect: vi.fn().mockReturnThis(),
  build: vi.fn(() => mockConnection),
};

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(() => mockConnectionBuilder),
}));

// ─── Mock API ────────────────────────────────────────────────────────────────

vi.mock('@/lib/ai-assistant/api', () => ({
  createThread: vi.fn().mockResolvedValue({
    id: 'new-thread-1',
    title: 'New Thread',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    archived: false,
    messageCount: 0,
  }),
  listThreads: vi.fn().mockResolvedValue({ threads: [], total: 0, page: 1, pageSize: 20 }),
  getMessages: vi.fn().mockResolvedValue({
    messages: [
      { id: 'm1', threadId: 't1', role: 'user', content: 'Hello', createdAt: '2024-01-01T00:00:00Z' },
    ],
    total: 1,
  }),
}));

describe('useAiAssistant hook', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('Connection lifecycle', () => {
    it('starts in disconnected state', () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );
      expect(result.current.connectionState).toBe('disconnected');
      expect(result.current.messages).toEqual([]);
      expect(result.current.isStreaming).toBe(false);
    });

    it('connects successfully when connect() is called', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      expect(result.current.connectionState).toBe('connected');
      expect(result.current.error).toBeNull();
      expect(mockConnection.start).toHaveBeenCalled();
    });

    it('transitions to error state on connection failure', async () => {
      mockConnection.start.mockRejectedValueOnce(new Error('Connection refused'));

      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      expect(result.current.connectionState).toBe('error');
      expect(result.current.error).toBe('Connection refused');
    });

    it('disconnects and resets state', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });
      expect(result.current.connectionState).toBe('connected');

      act(() => {
        result.current.disconnect();
      });

      expect(result.current.connectionState).toBe('disconnected');
      expect(mockConnection.stop).toHaveBeenCalled();
    });

    it('sets error when no token is provided', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: null }),
      );

      await act(async () => {
        await result.current.connect();
      });

      expect(result.current.error).toBe('No authentication token');
      expect(result.current.connectionState).not.toBe('connected');
    });
  });

  describe('Sending messages', () => {
    it('adds user message to state and invokes SignalR', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      // Connect and set up a thread
      await act(async () => {
        await result.current.connect();
        await result.current.createNewThread('Test');
      });

      await act(async () => {
        await result.current.sendMessage('Hello AI');
      });

      expect(result.current.messages).toHaveLength(1);
      expect(result.current.messages[0].content).toBe('Hello AI');
      expect(result.current.messages[0].role).toBe('user');
      expect(result.current.isStreaming).toBe(true);
      expect(mockConnection.invoke).toHaveBeenCalledWith('SendMessage', 'new-thread-1', 'Hello AI');
    });
  });

  describe('Receiving streaming responses', () => {
    it('accumulates content_delta events into streamingContent', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      // Get the StreamEvent handler registered with connection.on
      const onCall = mockConnection.on.mock.calls.find(([event]) => event === 'StreamEvent');
      expect(onCall).toBeDefined();

      const handler = onCall![1];

      await act(async () => {
        handler({ type: 'content_delta', messageId: 'm1', delta: 'Hello' });
      });

      expect(result.current.streamingContent).toBe('Hello');

      await act(async () => {
        handler({ type: 'content_delta', messageId: 'm1', delta: ' world' });
      });

      expect(result.current.streamingContent).toBe('Hello world');
    });

    it('finalizes message on content_done', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      const onCall = mockConnection.on.mock.calls.find(([event]) => event === 'StreamEvent');
      const handler = onCall![1];

      await act(async () => {
        handler({ type: 'content_done', messageId: 'm1', content: 'Full response' });
      });

      expect(result.current.isStreaming).toBe(false);
      expect(result.current.streamingContent).toBe('');
      expect(result.current.messages.some((m) => m.content === 'Full response')).toBe(true);
    });
  });

  describe('Tool call handling', () => {
    it('adds tool result messages on tool_call_done', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      const onCall = mockConnection.on.mock.calls.find(([event]) => event === 'StreamEvent');
      const handler = onCall![1];

      await act(async () => {
        handler({
          type: 'tool_call_done',
          messageId: 'm1',
          toolCallId: 'tc-1',
          result: '{"score": 7.5}',
        });
      });

      const toolMsg = result.current.messages.find((m) => m.role === 'tool');
      expect(toolMsg).toBeDefined();
      expect(toolMsg!.content).toBe('{"score": 7.5}');
      expect(toolMsg!.toolCallId).toBe('tc-1');
    });
  });

  describe('Error states', () => {
    it('sets error on stream error event', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      const onCall = mockConnection.on.mock.calls.find(([event]) => event === 'StreamEvent');
      const handler = onCall![1];

      await act(async () => {
        handler({ type: 'error', code: 'context_overflow', message: 'Context window exceeded' });
      });

      expect(result.current.error).toBe('Context window exceeded');
      expect(result.current.isStreaming).toBe(false);
    });
  });

  describe('Thread management', () => {
    it('creates a new thread and resets messages', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        const thread = await result.current.createNewThread('My Thread');
        expect(thread.id).toBe('new-thread-1');
      });

      expect(result.current.thread?.id).toBe('new-thread-1');
      expect(result.current.messages).toEqual([]);
    });

    it('selects existing thread and loads messages', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.selectThread('t1');
      });

      expect(result.current.thread?.id).toBe('t1');
      expect(result.current.messages).toHaveLength(1);
      expect(result.current.messages[0].content).toBe('Hello');
    });

    it('cancelStream stops streaming and invokes CancelStream', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      act(() => {
        result.current.cancelStream();
      });

      expect(result.current.isStreaming).toBe(false);
      expect(result.current.streamingContent).toBe('');
      expect(mockConnection.invoke).toHaveBeenCalledWith('CancelStream');
    });
  });
});
