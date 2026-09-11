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

// A tool-role message's name rides the wire DTO's scalar `toolName` field
// (AiAssistantMessageDto), never `toolCalls` -- that array is only ever
// populated on the preceding assistant message that issued the call.
// `AiMessage` doesn't model this scalar (the component reads it off the raw
// payload via a cast), so fixtures attach it the same untyped way real API
// data arrives instead of the `toolCalls` shape that never occurs in practice.
const makeToolMessage = (toolName: string, overrides: Partial<AiMessage> = {}): AiMessage =>
  ({ ...makeMessage({ role: 'tool', ...overrides }), toolName }) as AiMessage;

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
      makeToolMessage('search_content', { id: 'm1', content: '{"result": "found 3 items"}' }),
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

  it('renders tool card with toolName from the message', () => {
    const messages = [makeToolMessage('unknown_tool', { id: 'm1', content: '{}' })];
    render(<AiAssistantMessages messages={messages} />);

    expect(screen.getByText(/unknown_tool/)).toBeInTheDocument();
  });

  it('falls back to "unknown" when a tool message carries no toolName', () => {
    const messages = [makeMessage({ id: 'm1', role: 'tool', content: '{}' })];
    render(<AiAssistantMessages messages={messages} />);

    expect(screen.getByTestId('tool-call-card')).toHaveTextContent('Tool: unknown');
  });

  // A long tool-heavy turn (e.g. a large-codebase admin task) can spend whole
  // minutes with no text streamed at all. Without a visible "still working"
  // signal during that gap, the screen looks indistinguishable from frozen.
  it('shows a thinking indicator while streaming with no text yet', () => {
    render(<AiAssistantMessages messages={[]} streamingContent="" streamingStatus="thinking" />);

    expect(screen.getByTestId('thinking-indicator')).toBeInTheDocument();
    expect(screen.queryByTestId('streaming-cursor')).not.toBeInTheDocument();
  });

  it('shows an in-progress card for a tool call still running', () => {
    render(
      <AiAssistantMessages
        messages={[]}
        streamingContent=""
        streamingStatus="tool-calling"
        activeToolCalls={[{ id: 'tc-1', toolName: 'search_codebase', arguments: '{}' }]}
      />,
    );

    expect(screen.getByTestId('active-tool-calls')).toBeInTheDocument();
    expect(screen.getByText(/Running: search_codebase/)).toBeInTheDocument();
    // Tool-calling has its own per-call indicator; showing "Thinking…" too
    // on top of it would be redundant noise on the same empty-content gap.
    expect(screen.queryByTestId('thinking-indicator')).not.toBeInTheDocument();
  });

  it('shows a finished tool call once its result arrives', () => {
    render(
      <AiAssistantMessages
        messages={[]}
        streamingContent=""
        streamingStatus="tool-calling"
        activeToolCalls={[{ id: 'tc-1', toolName: 'search_codebase', arguments: '{}', result: '{"matches":3}' }]}
      />,
    );

    expect(screen.getByText(/Ran: search_codebase/)).toBeInTheDocument();
  });

  it('marks a failed tool call distinctly', () => {
    render(
      <AiAssistantMessages
        messages={[]}
        streamingContent=""
        streamingStatus="tool-calling"
        activeToolCalls={[{ id: 'tc-1', toolName: 'write_file', arguments: '{}', result: '{}', isError: true }]}
      />,
    );

    expect(screen.getByText(/Failed: write_file/)).toBeInTheDocument();
  });
});
