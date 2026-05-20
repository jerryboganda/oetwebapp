import { createThread, listThreads, getMessages, archiveThread, sendMessage } from '../api';

// Mock the apiClient dependency
const mockGet = vi.fn();
const mockPost = vi.fn();
const mockDelete = vi.fn();

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
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
    it('lists threads with default pagination', async () => {
      const response = { threads: [], total: 0, page: 1, pageSize: 20 };
      mockGet.mockResolvedValue(response);

      const result = await listThreads();
      expect(result).toEqual(response);
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/threads?page=1&pageSize=20');
    });

    it('lists threads with custom pagination', async () => {
      const response = { threads: [{ id: 't1' }], total: 5, page: 2, pageSize: 10 };
      mockGet.mockResolvedValue(response);

      const result = await listThreads(2, 10);
      expect(result.page).toBe(2);
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/threads?page=2&pageSize=10');
    });

    it('propagates server errors', async () => {
      mockGet.mockRejectedValue(new Error('Internal Server Error'));
      await expect(listThreads()).rejects.toThrow('Internal Server Error');
    });
  });

  describe('getMessages', () => {
    it('fetches messages for a thread', async () => {
      const response = {
        messages: [
          { id: 'm1', threadId: 't1', role: 'user', content: 'Hello', createdAt: '2024-01-01T00:00:00Z' },
          { id: 'm2', threadId: 't1', role: 'assistant', content: 'Hi!', createdAt: '2024-01-01T00:01:00Z' },
        ],
        total: 2,
      };
      mockGet.mockResolvedValue(response);

      const result = await getMessages('t1');
      expect(result.messages).toHaveLength(2);
      expect(result.total).toBe(2);
      expect(mockGet).toHaveBeenCalledWith('/v1/ai-assistant/threads/t1/messages');
    });

    it('supports cursor-based pagination with before parameter', async () => {
      mockGet.mockResolvedValue({ messages: [], total: 0 });

      await getMessages('t1', 'msg-cursor-123');
      expect(mockGet).toHaveBeenCalledWith(
        '/v1/ai-assistant/threads/t1/messages?before=msg-cursor-123',
      );
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

  describe('sendMessage', () => {
    it('sends a message to a thread', async () => {
      const mockMsg = { id: 'm1', threadId: 't1', role: 'assistant', content: 'reply', createdAt: '' };
      mockPost.mockResolvedValue(mockMsg);

      const result = await sendMessage('t1', 'hello');
      expect(result).toEqual(mockMsg);
      expect(mockPost).toHaveBeenCalledWith(
        '/v1/ai-assistant/threads/t1/messages',
        { content: 'hello' },
      );
    });
  });
});
