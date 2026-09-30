import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockVoice, mockRecorder } = vi.hoisted(() => ({
  mockVoice: vi.fn(),
  mockRecorder: vi.fn(),
}));

vi.mock('@/hooks/useSpeakingRealtimeVoice', () => ({ useSpeakingRealtimeVoice: mockVoice }));
vi.mock('@/hooks/useSpeakingSessionRecorder', () => ({ useSpeakingSessionRecorder: mockRecorder }));
// Renders nothing on the web; the native-app recovery path is only asserted by presence.
vi.mock('@/components/domain/speaking/OpenAppSettingsButton', () => ({
  OpenAppSettingsButton: () => <button type="button">Open app settings</button>,
}));

import { ExamConversationPanel } from '../ExamConversationPanel';

function liveVoice(overrides: Record<string, unknown> = {}) {
  return {
    connection: 'connected',
    phase: 'listening',
    preflight: { disclosure: 'Provider disclosure text' },
    captions: [],
    micLevel: 0.4,
    micEnabled: true,
    awaitingCandidateStart: false,
    error: null,
    micPermissionDenied: false,
    ended: false,
    provider: null,
    failedOver: false,
    audioRef: { current: null },
    prepare: vi.fn(),
    start: vi.fn().mockResolvedValue(true),
    stop: vi.fn().mockResolvedValue(true),
    ...overrides,
  };
}

describe('ExamConversationPanel — one mic / voice-activity control', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockRecorder.mockReturnValue({ status: 'recording', error: null, level: 0.3, start: vi.fn().mockResolvedValue(true), stop: vi.fn() });
  });

  it('live voice: one indicator, no pause/stop square, no disclosure checkbox', () => {
    mockVoice.mockReturnValue(liveVoice());
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    expect(screen.getAllByTestId('speaking-mic-indicator')).toHaveLength(1);
    expect(screen.getByText('Live — the patient is listening')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(mockRecorder).not.toHaveBeenCalled();
  });

  it('live voice: when the browser needs a gesture, shows ONE simple Start speaking control', async () => {
    const user = userEvent.setup();
    const voice = liveVoice({ connection: 'ready', micEnabled: false });
    mockVoice.mockReturnValue(voice);
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    expect(screen.getAllByRole('button')).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Start speaking' }));
    expect(voice.start).toHaveBeenCalledWith();
  });

  it('recorder fallback: starts recording on mount, hands out stop, reports speaking started', () => {
    const onStop = vi.fn();
    const onStarted = vi.fn();
    const recorder = { status: 'recording', error: null, level: 0.3, start: vi.fn().mockResolvedValue(true), stop: vi.fn() };
    mockRecorder.mockReturnValue(recorder);
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable={false} onVoiceStopReady={onStop} onSpeakingStarted={onStarted} />);

    expect(recorder.start).toHaveBeenCalledTimes(1);
    expect(onStop).toHaveBeenCalledWith(recorder.stop);
    expect(onStarted).toHaveBeenCalledTimes(1);
    expect(screen.getAllByTestId('speaking-mic-indicator')).toHaveLength(1);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(mockVoice).not.toHaveBeenCalled();
  });

  it('recorder fallback: a recording the server refused is never called received, shows the server message and offers no Start control', () => {
    mockRecorder.mockReturnValue({
      status: 'rejected',
      error: 'The window for uploading this role-play recording has closed.',
      level: 0,
      start: vi.fn(),
      stop: vi.fn(),
    });
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable={false} />);

    expect(screen.getByText('Recording could not be saved')).toBeInTheDocument();
    expect(screen.queryByText('Recording received')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The window for uploading this role-play recording has closed.');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('keeps the live indicator when a later poll reports live voice unavailable (a probe flake must not swap it mid-card)', () => {
    mockVoice.mockReturnValue(liveVoice());
    const { rerender } = render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    rerender(<ExamConversationPanel sessionId="s1" liveVoiceAvailable={false} />);

    expect(screen.getByText('Live — the patient is listening')).toBeInTheDocument();
    expect(mockRecorder).not.toHaveBeenCalled();
  });

  it('keeps the recorder when a later poll reports live voice available', () => {
    const { rerender } = render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable={false} />);

    rerender(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    expect(screen.getByText('Recording — speak to the patient')).toBeInTheDocument();
    expect(mockVoice).not.toHaveBeenCalled();
  });

  it('after a failover the learner sees the normal live state; the provider is only a data attribute', () => {
    mockVoice.mockReturnValue(liveVoice({ provider: 'gemini', failedOver: true }));
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    const indicator = screen.getByTestId('speaking-mic-indicator');
    expect(indicator).toHaveAttribute('data-live-provider', 'gemini');
    expect(indicator).toHaveAttribute('data-live-failover', 'true');
    expect(indicator).toHaveTextContent('Live — the patient is listening');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(indicator.textContent).not.toMatch(/gemini|openai/i);
  });

  it('marks no provider before a connection is live', () => {
    mockVoice.mockReturnValue(liveVoice({ connection: 'connecting', micEnabled: false }));
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    const indicator = screen.getByTestId('speaking-mic-indicator');
    expect(indicator).not.toHaveAttribute('data-live-provider');
    expect(indicator).not.toHaveAttribute('data-live-failover');
    expect(screen.getByText('Connecting to the AI patient…')).toBeInTheDocument();
  });

  it('when every provider failed: one generic alert and ONE Start speaking control that retries the whole chain', async () => {
    const user = userEvent.setup();
    const voice = liveVoice({
      connection: 'error',
      micEnabled: false,
      error: 'The live AI patient could not start. Please try again.',
    });
    mockVoice.mockReturnValue(voice);
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    expect(screen.getByRole('alert')).toHaveTextContent('The live AI patient could not start. Please try again.');
    expect(screen.getByText('Microphone off')).toBeInTheDocument();
    expect(screen.getAllByRole('button')).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Start speaking' }));
    expect(voice.start).toHaveBeenCalledTimes(1);
  });

  it('a refused microphone offers the app-settings recovery next to the message', () => {
    mockVoice.mockReturnValue(liveVoice({
      connection: 'error',
      micEnabled: false,
      micPermissionDenied: true,
      error: 'Microphone permission was blocked.',
    }));
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    expect(screen.getByRole('alert')).toHaveTextContent('Microphone permission was blocked.');
    expect(screen.getByRole('button', { name: 'Open app settings' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start speaking' })).toBeInTheDocument();
  });

  it('a preflight failure keeps the Retry connection control', async () => {
    const user = userEvent.setup();
    const voice = liveVoice({
      connection: 'error',
      micEnabled: false,
      preflight: null,
      error: 'The live AI patient could not start. Please try again.',
    });
    mockVoice.mockReturnValue(voice);
    render(<ExamConversationPanel sessionId="s1" liveVoiceAvailable />);

    await user.click(screen.getByRole('button', { name: 'Retry connection' }));
    expect(voice.prepare).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Start speaking' })).not.toBeInTheDocument();
  });
});
