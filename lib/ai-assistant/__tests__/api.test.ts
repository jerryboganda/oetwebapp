import { createThread, listThreads, getMessages, archiveThread, renameThread, setThreadModel, listAssistantModels } from '../api';

/**
 * These tests previously asserted the client's own invented wrappers
 * (`{threads,total,page,pageSize}`, `{messages,total}`) against a mocked
 * apiClient — so they passed while the real endpoints returned bare arrays and
 * `result.messages` was always undefined. A mock that echoes the shape you
 * asked for proves nothing. They now assert the shape `AiAssistantEndpoints`
 * actually returns, including the `skip`/`take` query it actually reads.
 */

// Mock the apiClient dependency
const mockGet = vi.fn();
const mockPost = vi.fn();
const mockPatch = vi.fn();
const mockDelete = vi.fn();

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    patch: (...args: unknown[]) => mockPatch(...args),
    delete: (...args: unknown[]) => mockDelete(...args),
  },
}));

describe('AI Assistant API client', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('createThread', () => {
    it('creates a thread with default role', async () => {
      const mockThread = {
        id: 'thread-1',
        title: null,
        role: 'learner',
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
        status: 'active',
        messageCount: 0,
      };
      mockPost.mockResolvedValue(mockThread);

      const result = await createThread();
      expect(result).toEqual(mockThread);
      expect(mockPost).toHaveBeenCalledWith(
        '/v1/ai-assistant/threads',
        { role: 'learner', title: null },
      );
    });

    it('creates a thread with explicit role and title', async () => {
      const mockThread = { id: 'thread-2', title: 'Test', role: 'admin', createdAt: '', updatedAt: '', status: 'active', messageCount: 0 };
      mockPost.mockResolvedValue(mockThread);

      const result = await createThread('admin', 'Test');
      expect(result.title).toBe('Test');
      expect(mockPost).toHaveBeenCalledWith(
        '/v1/ai-assistant/threads',
        { role: 'admin', title: 'Test' },
      );
    });

    it('propagates errors from apiClient', async () => {
      mockPost.mockRejectedValue(new Error('Network error'));
      await expect(createThread('learner')).rejects.toThrow('Network error');
    });
  });

  describe('listThreads', () => {
    it('requests skip/take and returns the bare array the server sends', async () => {
      const threads = [{ id: 't1', title: 'One', role: 'learner', createdAt: '' }];
      mockGet.mockResolvedValue(threads);

      const result = await listThreads();
      expect(result).toEqual(threads);
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/threads?skip=0&take=20');
    });

    it('passes custom paging through', async () => {
      mockGet.mockResolvedValue([]);

      await listThreads(20, 10);
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/threads?skip=20&take=10');
    });

    it('degrades to an empty list if the server sends something unexpected', async () => {
      mockGet.mockResolvedValue(null);
      await expect(listThreads()).resolves.toEqual([]);
    });

    it('propagates server errors', async () => {
      mockGet.mockRejectedValue(new Error('Internal Server Error'));
      await expect(listThreads()).rejects.toThrow('Internal Server Error');
    });
  });

  describe('getMessages', () => {
    it('returns the bare array and stamps the thread id onto each row', async () => {
      mockGet.mockResolvedValue([
        { id: 'm1', role: 'user', content: 'Hello', createdAt: '2024-01-01T00:00:00Z' },
        { id: 'm2', role: 'assistant', content: 'Hi!', createdAt: '2024-01-01T00:01:00Z' },
      ]);

      const result = await getMessages('t1');
      expect(result).toHaveLength(2);
      expect(result[0].threadId).toBe('t1');
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/threads/t1/messages?skip=0&take=50');
    });

    it('parses companion citations off the stored JSON', async () => {
      mockGet.mockResolvedValue([
        {
          id: 'm1',
          role: 'assistant',
          content: 'Select only relevant case notes [S1].',
          createdAt: '',
          citationsJson: JSON.stringify([
            {
              ordinal: 1,
              sourceKey: 'rulebook:writing:medicine',
              sourceTitle: 'Writing rulebook — Medicine',
              authority: 'ProfessionApprovedMethod',
              heading: 'W12 — Relevance',
              pageNumber: null,
              timestampSeconds: null,
            },
          ]),
        },
      ]);

      const [message] = await getMessages('t1');
      expect(message.citations).toHaveLength(1);
      expect(message.citations?.[0].sourceTitle).toBe('Writing rulebook — Medicine');
    });

    it('drops malformed citations rather than losing the answer', async () => {
      mockGet.mockResolvedValue([
        { id: 'm1', role: 'assistant', content: 'answer', createdAt: '', citationsJson: 'not json' },
      ]);

      const [message] = await getMessages('t1');
      expect(message.content).toBe('answer');
      expect(message.citations).toBeUndefined();
    });

    it('propagates 404 errors', async () => {
      mockGet.mockRejectedValue(new Error('Not Found'));
      await expect(getMessages('nonexistent')).rejects.toThrow('Not Found');
    });
  });

  describe('archiveThread', () => {
    it('archives a thread successfully', async () => {
      mockDelete.mockResolvedValue(undefined);

      await expect(archiveThread('t1')).resolves.toBeUndefined();
      expect(mockDelete).toHaveBeenCalledWith('/v1/ai-assistant/threads/t1');
    });

    it('propagates 403 errors', async () => {
      mockDelete.mockRejectedValue(new Error('Forbidden'));
      await expect(archiveThread('t1')).rejects.toThrow('Forbidden');
    });
  });

  describe('renameThread', () => {
    it('PATCHes the trimmed title onto the thread route', async () => {
      mockPatch.mockResolvedValue(undefined);

      await renameThread('t1', 'Rounds review');

      expect(mockPatch).toHaveBeenCalledWith('/v1/ai-assistant/threads/t1', { title: 'Rounds review' });
    });
  });

  describe('setThreadModel', () => {
    it('PATCHes the model override, or null to clear it', async () => {
      mockPatch.mockResolvedValue(undefined);

      await setThreadModel('t1', 'chatgpt_web');
      expect(mockPatch).toHaveBeenCalledWith('/v1/ai-assistant/threads/t1/model', { model: 'chatgpt_web' });

      await setThreadModel('t1', null);
      expect(mockPatch).toHaveBeenCalledWith('/v1/ai-assistant/threads/t1/model', { model: null });
    });
  });

  describe('listAssistantModels', () => {
    it('returns Claude API and UBAG as separate groups', async () => {
      mockGet.mockResolvedValue({
        groups: [
          { provider: 'anthropic', label: 'Claude (API)', models: ['claude-sonnet-5'] },
          { provider: 'ubag', label: 'UBAG (browser)', models: ['chatgpt_web', 'deepseek_web'] },
        ],
        models: ['claude-sonnet-5', 'chatgpt_web', 'deepseek_web'],
      });

      const catalog = await listAssistantModels();

      expect(catalog.groups).toHaveLength(2);
      expect(catalog.groups?.[0]?.provider).toBe('anthropic');
      expect(catalog.groups?.[1]?.provider).toBe('ubag');
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/models');
    });
  });

});
