import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiAssistantPanel } from '../AiAssistantPanel';

// Mock child components
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
      <input
        data-testid="mock-input"
        onChange={() => {}}
        aria-label="Message input"
      />
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

describe('AiAssistantPanel', () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
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

  it('send button adds a message to the list', async () => {
    const user = userEvent.setup();
    render(<AiAssistantPanel onClose={onClose} />);

    await user.click(screen.getByTestId('mock-send'));

    // After sending, message should appear (via state update)
    expect(screen.getByText('test message')).toBeInTheDocument();
  });

  it('shows cancel button during streaming', async () => {
    const user = userEvent.setup();
    render(<AiAssistantPanel onClose={onClose} />);

    // Trigger send to start streaming
    await user.click(screen.getByTestId('mock-send'));
    expect(screen.getByTestId('mock-cancel')).toBeInTheDocument();
  });

  it('cancel button stops streaming', async () => {
    const user = userEvent.setup();
    render(<AiAssistantPanel onClose={onClose} />);

    await user.click(screen.getByTestId('mock-send'));
    await user.click(screen.getByTestId('mock-cancel'));

    // After cancel, the cancel button should be gone
    expect(screen.queryByTestId('mock-cancel')).not.toBeInTheDocument();
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
});
