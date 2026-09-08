import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiAssistantInput } from '../AiAssistantInput';

describe('AiAssistantInput', () => {
  const defaultProps = {
    onSend: vi.fn(),
    onCancel: vi.fn(),
    isStreaming: false,
    disabled: false,
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders text input with placeholder', () => {
    render(<AiAssistantInput {...defaultProps} />);
    expect(screen.getByRole('textbox', { name: /message input/i })).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/type a message/i)).toBeInTheDocument();
  });

  it('renders send button', () => {
    render(<AiAssistantInput {...defaultProps} />);
    expect(screen.getByRole('button', { name: /send message/i })).toBeInTheDocument();
  });

  it('typing updates the input value', async () => {
    const user = userEvent.setup();
    render(<AiAssistantInput {...defaultProps} />);

    const input = screen.getByRole('textbox', { name: /message input/i });
    await user.type(input, 'Hello AI');
    expect(input).toHaveValue('Hello AI');
  });

  it('Enter key sends the message', async () => {
    const user = userEvent.setup();
    const onSend = vi.fn();
    render(<AiAssistantInput {...defaultProps} onSend={onSend} />);

    const input = screen.getByRole('textbox', { name: /message input/i });
    await user.type(input, 'Test message{Enter}');

    expect(onSend).toHaveBeenCalledWith('Test message', expect.anything());
    expect(input).toHaveValue('');
  });

  it('Shift+Enter adds a newline instead of sending', async () => {
    const user = userEvent.setup();
    const onSend = vi.fn();
    render(<AiAssistantInput {...defaultProps} onSend={onSend} />);

    const input = screen.getByRole('textbox', { name: /message input/i });
    await user.type(input, 'Line 1{Shift>}{Enter}{/Shift}Line 2');

    expect(onSend).not.toHaveBeenCalled();
    expect(input).toHaveValue('Line 1\nLine 2');
  });

  it('send button is disabled when input is empty', () => {
    render(<AiAssistantInput {...defaultProps} />);
    expect(screen.getByRole('button', { name: /send message/i })).toBeDisabled();
  });

  it('send button is enabled when input has text', async () => {
    const user = userEvent.setup();
    render(<AiAssistantInput {...defaultProps} />);

    await user.type(screen.getByRole('textbox', { name: /message input/i }), 'hi');
    expect(screen.getByRole('button', { name: /send message/i })).not.toBeDisabled();
  });

  it('input is disabled during streaming', () => {
    render(<AiAssistantInput {...defaultProps} isStreaming />);
    expect(screen.getByRole('textbox', { name: /message input/i })).toBeDisabled();
  });

  it('shows cancel button during streaming', () => {
    render(<AiAssistantInput {...defaultProps} isStreaming />);
    expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /send message/i })).not.toBeInTheDocument();
  });

  it('cancel button calls onCancel', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    render(<AiAssistantInput {...defaultProps} isStreaming onCancel={onCancel} />);

    await user.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('does not send whitespace-only messages', async () => {
    const user = userEvent.setup();
    const onSend = vi.fn();
    render(<AiAssistantInput {...defaultProps} onSend={onSend} />);

    const input = screen.getByRole('textbox', { name: /message input/i });
    await user.type(input, '   {Enter}');

    expect(onSend).not.toHaveBeenCalled();
  });

  it('input is disabled when disabled prop is true', () => {
    render(<AiAssistantInput {...defaultProps} disabled />);
    expect(screen.getByRole('textbox', { name: /message input/i })).toBeDisabled();
  });

  it('attaches an image and sends its bytes with the message', async () => {
    const onSend = vi.fn();
    render(<AiAssistantInput onSend={onSend} onCancel={vi.fn()} isStreaming={false} />);

    const picker = screen.getByTestId('assistant-file-picker') as HTMLInputElement;
    fireEvent.change(picker, {
      target: { files: [new File([new Uint8Array([1, 2, 3])], 'scan.png', { type: 'image/png' })] },
    });

    expect(await screen.findByTestId('assistant-attachments')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Message input'), { target: { value: 'read this' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }));

    expect(onSend).toHaveBeenCalledTimes(1);
    expect(onSend.mock.calls[0][0]).toBe('read this');
    const attachments = onSend.mock.calls[0][1] as {
      images: Array<{ bytes: Uint8Array; mimeType: string }>;
    };
    expect(attachments.images).toHaveLength(1);
    expect(attachments.images[0].mimeType).toBe('image/png');
  });

  it('rejects unsupported file kinds with an alert', async () => {
    const onSend = vi.fn();
    render(<AiAssistantInput onSend={onSend} onCancel={vi.fn()} isStreaming={false} />);

    const picker = screen.getByTestId('assistant-file-picker') as HTMLInputElement;
    fireEvent.change(picker, {
      target: { files: [new File(['x'], 'run.exe', { type: 'application/octet-stream' })] },
    });

    expect(await screen.findByRole('alert')).toHaveTextContent('supported files are');
    expect(onSend).not.toHaveBeenCalled();
  });

  it('extracts a text document and sends it alongside the message', async () => {
    const onSend = vi.fn();
    render(<AiAssistantInput onSend={onSend} onCancel={vi.fn()} isStreaming={false} />);

    const picker = screen.getByTestId('assistant-file-picker') as HTMLInputElement;
    fireEvent.change(picker, {
      target: { files: [new File(['case notes here'], 'notes.txt', { type: 'text/plain' })] },
    });

    expect(await screen.findByText('notes.txt')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }));

    expect(onSend).toHaveBeenCalledTimes(1);
    const attachments = onSend.mock.calls[0][1] as { document: { text: string } };
    expect(attachments.document.text).toContain('case notes here');
  });
});
