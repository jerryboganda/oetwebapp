import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiAssistantPanel } from '../AiAssistantPanel';

/**
 * The panel used to hold its own `useState` message list, and these tests
 * asserted that local behaviour — which passed while `handleSend` was a no-op
 * comment and nothing ever reached the server.
 *
 * It now consumes the AI assistant context, so the tests assert the real
 * contract: the panel delegates to the context and renders what the context
 * reports. See docs/ai-learning-companion/.
 */

const contextValue = {
  messages: [] as Array<{ id: string; content: string }>,
  threads: [] as Array<{ id: string; title: string | null }>,
  activeThread: null as { id: string; title: string | null } | null,
  isStreaming: false,
  streamingContent: '',
  isConnected: true,
  connectionState: 'connected',
  error: null as string | null,
  clearError: vi.fn(),
  sendMessage: vi.fn(),
  cancelTurn: vi.fn(),
  selectThread: vi.fn(),
  createNewThread: vi.fn(),
};

vi.mock('@/contexts/ai-assistant-context', () => ({
  useAiAssistantContext: () => contextValue,
}));

vi.mock('../AiAssistantMessages', () => ({
  AiAssistantMessages: ({ messages, streamingContent }: any) => (
    <div data-testid="messages-list">
      {messages.map((m: any) => (
        <div key={m.id} data-testid={`msg-${m.id}`}>{m.content}</div>
      ))}
      {streamingContent && <div data-testid="streaming">{streamingContent}</div>}
    </div>
  ),
}));

vi.mock('../AiAssistantInput', () => ({
  AiAssistantInput: ({ onSend, onCancel, isStreaming, disabled }: any) => (
    <div data-testid="input-area">
      <input data-testid="mock-input" onChange={() => {}} aria-label="Message input" disabled={disabled} />
      <button data-testid="mock-send" onClick={() => onSend('test message')}>
        Send
      </button>
      {isStreaming && (
        <button data-testid="mock-cancel" onClick={onCancel}>
          Cancel
        </button>
      )}
    </div>
  ),
}));

function resetContext(overrides: Partial<typeof contextValue> = {}) {
  Object.assign(contextValue, {
    messages: [],
    threads: [],
    activeThread: null,
    isStreaming: false,
    streamingContent: '',
    isConnected: true,
    connectionState: 'connected',
    error: null,
    clearError: vi.fn(),
    sendMessage: vi.fn(),
    cancelTurn: vi.fn(),
    selectThread: vi.fn(),
    createNewThread: vi.fn(),
    ...overrides,
  });
}

describe('AiAssistantPanel', () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    resetContext();
  });

  it('renders as a dialog with proper aria label', () => {
    render(<AiAssistantPanel onClose={onClose} />);
    expect(screen.getByRole('dialog', { name: /ai assistant/i })).toBeInTheDocument();
  });

  it('renders message list area', () => {
    render(<AiAssistantPanel onClose={onClose} />);
    expect(screen.getByTestId('messages-list')).toBeInTheDocument();
  });

  it('renders input area', () => {
    render(<AiAssistantPanel onClose={onClose} />);
    expect(screen.getByTestId('input-area')).toBeInTheDocument();
  });

  it('calls onClose when close button is clicked', async () => {
    const user = userEvent.setup();
    render(<AiAssistantPanel onClose={onClose} />);

    await user.click(screen.getByRole('button', { name: /close/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // ─── the contract that the stub silently failed ──────────────────────────

  it('delegates send to the context instead of local state', async () => {
    const user = userEvent.setup();
    render(<AiAssistantPanel onClose={onClose} />);

    await user.click(screen.getByTestId('mock-send'));

    expect(contextValue.sendMessage).toHaveBeenCalledWith('test message');
  });

  it('renders messages supplied by the context', () => {
    resetContext({ messages: [{ id: 'm1', content: 'hello from server' }] });
    render(<AiAssistantPanel onClose={onClose} />);

    expect(screen.getByTestId('msg-m1')).toHaveTextContent('hello from server');
  });

  it('shows streaming content while a turn is in flight', () => {
    resetContext({ isStreaming: true, streamingContent: 'partial answer' });
    render(<AiAssistantPanel onClose={onClose} />);

    expect(screen.getByTestId('streaming')).toHaveTextContent('partial answer');
  });

  it('delegates cancel to the context while streaming', async () => {
    const user = userEvent.setup();
    resetContext({ isStreaming: true });
    render(<AiAssistantPanel onClose={onClose} />);

    await user.click(screen.getByTestId('mock-cancel'));
    expect(contextValue.cancelTurn).toHaveBeenCalledTimes(1);
  });

  it('disables input while disconnected', () => {
    resetContext({ isConnected: false, connectionState: 'reconnecting' });
    render(<AiAssistantPanel onClose={onClose} />);

    expect(screen.getByTestId('mock-input')).toBeDisabled();
    expect(screen.getByTestId('connection-state')).toHaveAttribute('data-state', 'reconnecting');
  });

  it('surfaces an error and allows dismissing it', async () => {
    const user = userEvent.setup();
    resetContext({ error: 'Connection lost' });
    render(<AiAssistantPanel onClose={onClose} />);

    expect(screen.getByRole('alert')).toHaveTextContent('Connection lost');

    await user.click(screen.getByRole('button', { name: /dismiss error/i }));
    expect(contextValue.clearError).toHaveBeenCalledTimes(1);
  });

  it('toggles thread list on button click', async () => {
    const user = userEvent.setup();
    render(<AiAssistantPanel onClose={onClose} />);

    expect(screen.queryByTestId('thread-list')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /thread list/i }));
    expect(screen.getByTestId('thread-list')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /thread list/i }));
    expect(screen.queryByTestId('thread-list')).not.toBeInTheDocument();
  });

  it('lists threads from the context and selects one', async () => {
    const user = userEvent.setup();
    resetContext({ threads: [{ id: 't1', title: 'Writing help' }] });
    render(<AiAssistantPanel onClose={onClose} />);

    await user.click(screen.getByRole('button', { name: /thread list/i }));
    await user.click(screen.getByRole('button', { name: 'Writing help' }));

    expect(contextValue.selectThread).toHaveBeenCalledWith('t1');
  });
});
