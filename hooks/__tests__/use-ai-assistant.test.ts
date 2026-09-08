import { renderHook, act, waitFor } from '@testing-library/react';
import { useAiAssistant } from '../use-ai-assistant';

// ─── Mock SignalR ────────────────────────────────────────────────────────────

const mockConnection = {
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  invoke: vi.fn().mockResolvedValue(undefined),
  on: vi.fn(),
  off: vi.fn(),
  state: 1, // HubConnectionState.Connected
};

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Disconnected: 0, Connected: 1, Reconnecting: 2 },
  HubConnectionBuilder: vi.fn(),
}));

vi.mock('@/lib/ai-assistant/signalr', () => ({
  createAssistantConnection: vi.fn(async () => mockConnection),
  registerHubCallbacks: vi.fn((_conn, _callbacks) => vi.fn()),
  invokeStartTurn: vi.fn().mockResolvedValue(undefined),
  invokeCancelTurn: vi.fn().mockResolvedValue(undefined),
  mapHubState: vi.fn(() => 'connected'),
  packDocumentAttachment: vi.fn(
    (fileName: string, mimeType: string, text: string) => `${fileName}|${mimeType}|${text}`,
  ),
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
  listThreads: vi.fn().mockResolvedValue([]),
  getMessages: vi.fn().mockResolvedValue([
    { id: 'm1', threadId: 't1', role: 'user', content: 'Hello', createdAt: '2024-01-01T00:00:00Z' },
  ]),
  archiveThread: vi.fn().mockResolvedValue(undefined),
  renameThread: vi.fn().mockResolvedValue(undefined),
  setThreadModel: vi.fn().mockResolvedValue(undefined),
  listAssistantModels: vi.fn().mockResolvedValue({ provider: 'ubag', models: [] }),
}));

// Import the mocked module to access registerHubCallbacks
import {
  createAssistantConnection,
  registerHubCallbacks,
  invokeCancelTurn,
  invokeStartTurn,
} from '@/lib/ai-assistant/signalr';
import {
  listAssistantModels,
  renameThread,
  setThreadModel,
} from '@/lib/ai-assistant/api';

describe('useAiAssistant hook', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  /** Helper to get the callbacks passed to registerHubCallbacks after connect */
  function getRegisteredCallbacks() {
    const call = vi.mocked(registerHubCallbacks).mock.calls[0];
    return call?.[1];
  }

  describe('Connection lifecycle', () => {
    it('starts in connecting state when token provided (auto-connect)', () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );
      // Auto-connect fires immediately, so state is connecting or connected
      expect(['connecting', 'connected']).toContain(result.current.connectionState);
      expect(result.current.messages).toEqual([]);
      expect(result.current.isStreaming).toBe(false);
    });

    it('auto-connects successfully with a token', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      // Wait for auto-connect to complete
      await act(async () => {});

      expect(result.current.connectionState).toBe('connected');
      expect(result.current.error).toBeNull();
      expect(mockConnection.start).toHaveBeenCalled();
    });

    it('sets error state on auto-connect failure', async () => {
      mockConnection.start.mockRejectedValueOnce(new Error('Connection refused'));

      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {});

      // Hook sets 'disconnected' and generic error message on failure
      expect(result.current.connectionState).toBe('disconnected');
      expect(result.current.error).toBe('Failed to connect to AI assistant');
    });

    it('sets error state when the dynamically loaded connection factory fails', async () => {
      vi.mocked(createAssistantConnection).mockRejectedValueOnce(new Error('Chunk load failed'));

      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await waitFor(() => {
        expect(result.current.connectionState).toBe('disconnected');
        expect(result.current.error).toBe('Failed to connect to AI assistant');
      });
    });

    it('stops a stale connection when a newer connect attempt supersedes it', async () => {
      const staleConnection = {
        ...mockConnection,
        start: vi.fn().mockResolvedValue(undefined),
        stop: vi.fn().mockResolvedValue(undefined),
      };
      let resolveStaleConnection: (() => void) | undefined;
      const staleConnectionPromise = new Promise<
        Awaited<ReturnType<typeof createAssistantConnection>>
      >((resolve) => {
        resolveStaleConnection = () => resolve(staleConnection as never);
      });

      vi.mocked(createAssistantConnection)
        .mockImplementationOnce(() => staleConnectionPromise)
        .mockResolvedValueOnce(mockConnection as never);

      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });
      await act(async () => {
        resolveStaleConnection?.();
      });

      expect(staleConnection.stop).toHaveBeenCalledOnce();
      expect(result.current.connectionState).toBe('connected');
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

      // Wait for auto-connect to complete
      await act(async () => {});

      // Create thread so activeThread is set
      await act(async () => {
        await result.current.createNewThread('Test');
      });

      await act(async () => {
        await result.current.sendMessage('Hello AI');
      });

      expect(result.current.messages).toHaveLength(1);
      expect(result.current.messages[0].content).toBe('Hello AI');
      expect(result.current.messages[0].role).toBe('user');
      expect(result.current.isStreaming).toBe(true);
    });
  });

  describe('Receiving streaming responses', () => {
    it('accumulates TextDelta events into streamingContent', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      const callbacks = getRegisteredCallbacks();
      expect(callbacks?.onTextDelta).toBeDefined();

      await act(async () => {
        callbacks!.onTextDelta!('Hello');
      });

      expect(result.current.streamingContent).toBe('Hello');

      await act(async () => {
        callbacks!.onTextDelta!(' world');
      });

      expect(result.current.streamingContent).toBe('Hello world');
    });

    it('finalizes message on TurnComplete', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
        await result.current.createNewThread('Test');
      });

      const callbacks = getRegisteredCallbacks();

      await act(async () => {
        callbacks!.onTurnComplete!('m1', 'Full response');
      });

      expect(result.current.isStreaming).toBe(false);
      expect(result.current.streamingContent).toBe('');
      expect(result.current.messages.some((m) => m.content === 'Full response')).toBe(true);
    });
  });

  describe('Tool call handling', () => {
    it('tracks tool calls via ToolCallStart and ToolCallResult', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      const callbacks = getRegisteredCallbacks();

      await act(async () => {
        callbacks!.onToolCallStart!('tc-1', 'search', '{"query":"test"}');
      });

      expect(result.current.activeToolCalls).toHaveLength(1);
      expect(result.current.activeToolCalls[0].toolName).toBe('search');

      await act(async () => {
        callbacks!.onToolCallResult!('tc-1', '{"score": 7.5}', false);
      });

      expect(result.current.activeToolCalls[0].result).toBe('{"score": 7.5}');
    });
  });

  describe('Error states', () => {
    it('sets error on TurnError event', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.connect();
      });

      const callbacks = getRegisteredCallbacks();

      await act(async () => {
        callbacks!.onTurnError!('context_overflow', 'Context window exceeded');
      });

      expect(result.current.error).toBe('[context_overflow] Context window exceeded');
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
        expect(thread?.id).toBe('new-thread-1');
      });

      expect(result.current.thread?.id).toBe('new-thread-1');
      expect(result.current.messages).toEqual([]);
    });

    it('selects existing thread and loads messages', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      // First create a thread so it's in the list
      await act(async () => {
        await result.current.createNewThread('Test');
      });

      // Now select it (the mock creates with id 'new-thread-1')
      await act(async () => {
        await result.current.selectThread('new-thread-1');
      });

      expect(result.current.thread?.id).toBe('new-thread-1');
      // getMessages mock returns 1 message
      expect(result.current.messages).toHaveLength(1);
      expect(result.current.messages[0].content).toBe('Hello');
    });

    it('cancelStream stops streaming and invokes CancelTurn', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      // Need to have a connection and thread for cancelTurn to work
      await act(async () => {
        await result.current.createNewThread('Test');
      });

      await act(async () => {
        result.current.cancelStream();
      });

      expect(result.current.isStreaming).toBe(false);
      expect(result.current.streamingContent).toBe('');
      expect(invokeCancelTurn).toHaveBeenCalled();
    });

    it('renames a thread through the API and updates local state', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.createNewThread('Test');
      });
      await act(async () => {
        await result.current.renameThread('new-thread-1', 'Rounds');
      });

      expect(renameThread).toHaveBeenCalledWith('new-thread-1', 'Rounds');
      expect(result.current.threads.find((t) => t.id === 'new-thread-1')?.title).toBe('Rounds');
    });

    it('loads the UBAG model catalog on connect', async () => {
      vi.mocked(listAssistantModels).mockResolvedValueOnce({
        provider: 'ubag',
        models: ['chatgpt_web', 'deepseek_web'],
      });
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {});

      expect(listAssistantModels).toHaveBeenCalled();
      expect(result.current.availableModels).toEqual(['chatgpt_web', 'deepseek_web']);
    });

    it('pins the model override immediately with rollback on failure', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.createNewThread('Test');
      });

      await act(async () => {
        await result.current.setThreadModel('chatgpt_web');
      });

      expect(setThreadModel).toHaveBeenCalledWith('new-thread-1', 'chatgpt_web');
      expect(result.current.threadModel).toBe('chatgpt_web');

      vi.mocked(setThreadModel).mockRejectedValueOnce(new Error('nope'));
      await act(async () => {
        await result.current.setThreadModel('deepseek_web');
      });

      expect(result.current.threadModel).toBe('chatgpt_web');
      expect(result.current.error).toBe('Failed to change model');
    });

    it('restores the conversation pick from the reloaded row on select', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.createNewThread('Test');
      });
      const { listThreads } = await import('@/lib/ai-assistant/api');
      vi.mocked(listThreads).mockResolvedValueOnce([
        {
          id: 'new-thread-1',
          title: 'Test',
          role: 'admin',
          createdAt: '2024-01-01T00:00:00Z',
          modelOverride: 'deepseek_web|Vision',
        },
      ]);

      await act(async () => {
        await result.current.selectThread('new-thread-1');
      });

      expect(result.current.threadModel).toBe('deepseek_web|Vision');
    });

    it('resets to the default model on a fresh conversation', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.createNewThread('First');
      });
      await act(async () => {
        await result.current.setThreadModel('chatgpt_web');
      });
      expect(result.current.threadModel).toBe('chatgpt_web');

      await act(async () => {
        await result.current.createNewThread('Second');
      });
      expect(result.current.threadModel).toBeNull();
    });

    it('forwards attachments to the hub turn', async () => {
      const { result } = renderHook(() =>
        useAiAssistant({ token: 'test-token' }),
      );

      await act(async () => {
        await result.current.createNewThread('Test');
      });
      await act(async () => {});

      await act(async () => {
        await result.current.sendMessage('look at this', undefined, {
          images: [{ bytes: new Uint8Array([1, 2, 3]), mimeType: 'image/png' }],
          document: { fileName: 'n.pdf', mimeType: 'application/pdf', text: 'hello' },
        });
      });

      expect(result.current.error).toBeNull();
      const args = vi.mocked(invokeStartTurn).mock.calls.at(-1);
      expect(args?.[0]).toBe(mockConnection);
      expect(args?.[1]).toBe('new-thread-1');
      expect(args?.[2]).toBe('look at this');
      const wire = args?.[4] as { imageDataUrls: string[]; document: string };
      expect(wire.imageDataUrls[0]).toMatch(/^data:image\/png;base64,/);
      expect(wire.document).toContain('n.pdf');
    });
  });
});
