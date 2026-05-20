import { render, screen } from '@testing-library/react';
import { AiAssistantMessages } from '../AiAssistantMessages';
import type { AiMessage } from '@/lib/ai-assistant/types';

const makeMessage = (overrides: Partial<AiMessage> = {}): AiMessage => ({
  id: 'msg-1',
  threadId: 'thread-1',
  role: 'user',
  content: 'Hello there',
  createdAt: '2024-01-01T00:00:00Z',
  ...overrides,
});

describe('AiAssistantMessages', () => {
  it('shows empty state when no messages and no streaming', () => {
    render(<AiAssistantMessages messages={[]} />);
    expect(screen.getByText(/start a conversation/i)).toBeInTheDocument();
  });

  it('renders user messages correctly', () => {
    const messages = [makeMessage({ id: 'm1', role: 'user', content: 'What is OET?' })];
    render(<AiAssistantMessages messages={messages} />);

    expect(screen.getByTestId('user-message')).toBeInTheDocument();
    expect(screen.getByText('What is OET?')).toBeInTheDocument();
  });

  it('renders assistant messages correctly', () => {
    const messages = [
      makeMessage({ id: 'm1', role: 'assistant', content: 'OET is an English test for healthcare.' }),
    ];
    render(<AiAssistantMessages messages={messages} />);

    expect(screen.getByTestId('assistant-message')).toBeInTheDocument();
    expect(screen.getByText('OET is an English test for healthcare.')).toBeInTheDocument();
  });

  it('renders tool call cards', () => {
    const messages = [
      makeMessage({
        id: 'm1',
        role: 'tool',
        content: '{"result": "found 3 items"}',
        toolCalls: [{ id: 'tc-1', toolName: 'search_content', arguments: '{}' }],
      }),
    ];
    render(<AiAssistantMessages messages={messages} />);

    expect(screen.getByTestId('tool-call-card')).toBeInTheDocument();
    expect(screen.getByText(/search_content/)).toBeInTheDocument();
    expect(screen.getByText(/found 3 items/)).toBeInTheDocument();
  });

  it('shows streaming cursor when streamingContent is provided', () => {
    render(
      <AiAssistantMessages messages={[]} streamingContent="Thinking about..." />,
    );

    expect(screen.getByTestId('streaming-message')).toBeInTheDocument();
    expect(screen.getByTestId('streaming-cursor')).toBeInTheDocument();
    expect(screen.getByText('Thinking about...')).toBeInTheDocument();
  });

  it('does not show empty state when streaming is active', () => {
    render(
      <AiAssistantMessages messages={[]} streamingContent="" />,
    );

    // streamingContent is defined (empty string), so no empty state
    expect(screen.queryByText(/start a conversation/i)).not.toBeInTheDocument();
    expect(screen.getByTestId('streaming-message')).toBeInTheDocument();
  });

  it('renders multiple messages in order', () => {
    const messages = [
      makeMessage({ id: 'm1', role: 'user', content: 'First' }),
      makeMessage({ id: 'm2', role: 'assistant', content: 'Second' }),
      makeMessage({ id: 'm3', role: 'user', content: 'Third' }),
    ];
    render(<AiAssistantMessages messages={messages} />);

    const allText = screen.getByText('First').compareDocumentPosition(screen.getByText('Second'));
    // Node.DOCUMENT_POSITION_FOLLOWING = 4
    expect(allText & 4).toBe(4);
  });

  it('renders tool card with toolName from first toolCall', () => {
    const messages = [
      makeMessage({ id: 'm1', role: 'tool', content: '{}', toolCalls: [{ id: 'tc-abc', toolName: 'unknown_tool', arguments: '{}' }] }),
    ];
    render(<AiAssistantMessages messages={messages} />);

    expect(screen.getByText(/unknown_tool/)).toBeInTheDocument();
  });
});
