import {
  isContentDelta,
  isToolCallStart,
  isStreamError,
  isStreamDone,
  isUserMessage,
  isAssistantMessage,
  isToolMessage,
  isTextDelta,
  isTurnComplete,
  isTurnError,
} from '../types';
import type {
  StreamEvent,
  AiAssistantMessage,
} from '../types';

describe('AI Assistant type guards', () => {
  describe('isContentDelta (legacy compat)', () => {
    it('returns true for content_delta events', () => {
      expect(isContentDelta({ type: 'content_delta' })).toBe(true);
    });

    it('returns false for other event types', () => {
      expect(isContentDelta({ type: 'done' })).toBe(false);
    });
  });

  describe('isTextDelta (primary)', () => {
    it('returns true for TextDelta events', () => {
      const event: StreamEvent = { type: 'TextDelta', text: 'hello' };
      expect(isTextDelta(event)).toBe(true);
    });

    it('returns false for other event types', () => {
      const event: StreamEvent = { type: 'TurnComplete', messageId: 'm1', fullText: 'hi' };
      expect(isTextDelta(event)).toBe(false);
    });
  });

  describe('isToolCallStart (legacy compat)', () => {
    it('returns true for tool_call_start events', () => {
      expect(isToolCallStart({ type: 'tool_call_start' })).toBe(true);
    });

    it('returns true for ToolCallStart events', () => {
      expect(isToolCallStart({ type: 'ToolCallStart' })).toBe(true);
    });

    it('returns false for other events', () => {
      expect(isToolCallStart({ type: 'ToolCallResult' })).toBe(false);
    });
  });

  describe('isStreamError (legacy compat)', () => {
    it('returns true for error events', () => {
      expect(isStreamError({ type: 'error' })).toBe(true);
    });

    it('returns true for TurnError events', () => {
      expect(isStreamError({ type: 'TurnError' })).toBe(true);
    });

    it('returns false for non-error events', () => {
      expect(isStreamError({ type: 'TextDelta' })).toBe(false);
    });
  });

  describe('isTurnError (primary)', () => {
    it('returns true for TurnError events', () => {
      const event: StreamEvent = { type: 'TurnError', code: 'timeout', message: 'Request timed out' };
      expect(isTurnError(event)).toBe(true);
      if (isTurnError(event)) {
        expect(event.code).toBe('timeout');
        expect(event.message).toBe('Request timed out');
      }
    });
  });

  describe('isStreamDone (legacy compat)', () => {
    it('returns true for done events', () => {
      expect(isStreamDone({ type: 'done' })).toBe(true);
    });

    it('returns true for TurnComplete events', () => {
      expect(isStreamDone({ type: 'TurnComplete' })).toBe(true);
    });

    it('returns false for other events', () => {
      expect(isStreamDone({ type: 'TextDelta' })).toBe(false);
    });
  });

  describe('isTurnComplete (primary)', () => {
    it('returns true for TurnComplete events', () => {
      const event: StreamEvent = { type: 'TurnComplete', messageId: 'm1', fullText: 'done' };
      expect(isTurnComplete(event)).toBe(true);
      if (isTurnComplete(event)) {
        expect(event.messageId).toBe('m1');
        expect(event.fullText).toBe('done');
      }
    });
  });

  describe('Message role checks', () => {
    const userMsg: AiAssistantMessage = {
      id: '1', threadId: 't1', role: 'user', content: 'hi', createdAt: '2024-01-01T00:00:00Z',
    };
    const assistantMsg: AiAssistantMessage = {
      id: '2', threadId: 't1', role: 'assistant', content: 'hello', createdAt: '2024-01-01T00:00:00Z',
    };
    const toolMsg: AiAssistantMessage = {
      id: '3', threadId: 't1', role: 'tool', content: '{}', createdAt: '2024-01-01T00:00:00Z',
      toolCalls: [{ id: 'tc1', toolName: 'search', arguments: '{}' }],
    };

    it('isUserMessage identifies user messages', () => {
      expect(isUserMessage(userMsg)).toBe(true);
      expect(isUserMessage(assistantMsg)).toBe(false);
      expect(isUserMessage(toolMsg)).toBe(false);
    });

    it('isAssistantMessage identifies assistant messages', () => {
      expect(isAssistantMessage(assistantMsg)).toBe(true);
      expect(isAssistantMessage(userMsg)).toBe(false);
    });

    it('isToolMessage identifies tool messages', () => {
      expect(isToolMessage(toolMsg)).toBe(true);
      expect(isToolMessage(userMsg)).toBe(false);
    });
  });

  describe('StreamEvent union discrimination', () => {
    it('all primary event types are distinguishable by type field', () => {
      const events: StreamEvent[] = [
        { type: 'TextDelta', text: 'x' },
        { type: 'ToolCallStart', toolCallId: 'tc', toolName: 'fn', args: '{}' },
        { type: 'ToolCallResult', toolCallId: 'tc', result: '{}', isError: false },
        { type: 'TurnComplete', messageId: 'm1', fullText: 'x' },
        { type: 'TurnError', code: 'e', message: 'err' },
      ];

      const types = events.map((e) => e.type);
      const unique = new Set(types);
      expect(unique.size).toBe(5);
    });
  });
});
