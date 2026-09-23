import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockVoice, mockRecorder } = vi.hoisted(() => ({
  mockVoice: vi.fn(),
  mockRecorder: vi.fn(),
}));

vi.mock('@/hooks/useSpeakingRealtimeVoice', () => ({ useSpeakingRealtimeVoice: mockVoice }));
vi.mock('@/hooks/useSpeakingSessionRecorder', () => ({ useSpeakingSessionRecorder: mockRecorder }));

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
    ended: false,
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
});
