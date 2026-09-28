import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockPush,
  mockGetSession,
  mockGetClock,
  mockEnd,
  mockSubmit,
  mockAiAssess,
  mockRecordConsent,
  mockUpload,
} = vi.hoisted(() => ({
  mockPush: vi.fn(),
  mockGetSession: vi.fn(),
  mockGetClock: vi.fn(),
  mockEnd: vi.fn(),
  mockSubmit: vi.fn(),
  mockAiAssess: vi.fn(),
  mockRecordConsent: vi.fn(),
  mockUpload: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'sess-1' }),
  useRouter: () => ({ push: mockPush, replace: vi.fn() }),
}));

vi.mock('@/lib/api', () => ({
  ApiError: class ApiError extends Error {},
  apiClient: { request: vi.fn().mockResolvedValue({}) },
}));

vi.mock('@/lib/api/speaking-sessions', () => ({
  getSpeakingSession: mockGetSession,
  getSpeakingSessionClock: mockGetClock,
  endSpeakingSession: mockEnd,
  submitSpeakingSessionForMarking: mockSubmit,
  runAiAssessment: mockAiAssess,
  recordConsent: mockRecordConsent,
  uploadSpeakingSessionRecording: mockUpload,
}));

vi.mock('@/lib/analytics/speaking-events', () => ({ trackSpeaking: vi.fn() }));

// Live voice is not configured in production today; the recorder fallback runs.
vi.mock('@/hooks/useSpeakingRealtimeVoice', () => ({ useSpeakingRealtimeVoice: vi.fn() }));

import SpeakingSessionRecordingPage from './page';

class FakeMediaRecorder extends EventTarget {
  static isTypeSupported = (type: string) => type === 'audio/webm;codecs=opus';
  state: 'inactive' | 'recording' = 'inactive';
  mimeType: string;
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  constructor(_stream: unknown, options?: { mimeType?: string }) {
    super();
    this.mimeType = options?.mimeType ?? 'audio/webm';
  }
  start() {
    this.state = 'recording';
  }
  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['role-play audio'], { type: this.mimeType }) });
    this.dispatchEvent(new Event('stop'));
  }
}

const SESSION = {
  sessionId: 'sess-1',
  state: 'Active',
  mode: 'ai_self_practice',
  consentVersion: 'recording.v1',
  consentAccepted: true,
  liveVoiceAvailable: false,
  isFreeSample: false,
  rolePlayEndsAt: null,
  rolePlayStartedAt: '2026-09-23T10:00:00Z',
  card: {
    cardId: 'rpc-1',
    professionId: 'medicine',
    scenarioTitle: 'Chest pain review',
    setting: 'General practice',
    candidateRole: 'Doctor',
    interlocutorRole: 'Patient',
    patientName: 'Mr Lee',
    patientAge: '54',
    background: 'Chest pain after exercise.',
    tasks: ['Take a focused history'],
    allowedNotes: false,
    prepTimeSeconds: 180,
    rolePlayTimeSeconds: 300,
    difficulty: 'core',
    criteriaFocus: [],
    disclaimer: 'Practice estimate only.',
  },
};

describe('Active Speaking session (recorder fallback)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop: vi.fn() }] }) },
    });
    mockGetSession.mockResolvedValue(SESSION);
    mockGetClock.mockResolvedValue({ stage: 'active', secondsRemaining: 290, expired: false });
    mockUpload.mockResolvedValue(undefined);
    mockEnd.mockResolvedValue({});
    mockSubmit.mockResolvedValue({});
    mockAiAssess.mockResolvedValue({ state: 'processing' });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the card and exactly one mic indicator and one Finish & submit — no pause/stop/end-early', async () => {
    render(<SpeakingSessionRecordingPage />);

    expect(await screen.findByText('Recording — speak to the patient')).toBeInTheDocument();
    expect(screen.getByTestId('speaking-role-card')).toHaveTextContent('Take a focused history');
    expect(screen.getAllByTestId('speaking-mic-indicator')).toHaveLength(1);
    expect(screen.getAllByRole('button')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Finish & submit' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /pause|stop|end early/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('uploads the recording BEFORE /end → /submit → /ai-assess, then opens the results', async () => {
    const user = userEvent.setup();
    render(<SpeakingSessionRecordingPage />);
    await screen.findByText('Recording — speak to the patient');

    await user.click(screen.getByRole('button', { name: 'Finish & submit' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByRole('checkbox')).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Submit now' }));

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/speaking/sessions/sess-1/results'));
    expect(mockUpload).toHaveBeenCalledWith('sess-1', expect.any(Blob), expect.any(Number));
    const order = [mockUpload, mockEnd, mockSubmit, mockAiAssess].map((fn) => fn.mock.invocationCallOrder[0]);
    expect(order).toEqual([...order].sort((a, b) => a - b));
  });

  it('keeps the recording on upload failure and retries the same audio', async () => {
    const user = userEvent.setup();
    mockUpload.mockRejectedValueOnce(new Error('network down'));
    render(<SpeakingSessionRecordingPage />);
    await screen.findByText('Recording — speak to the patient');

    await user.click(screen.getByRole('button', { name: 'Finish & submit' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Submit now' }));

    expect(await screen.findByText('Upload failed — Retry upload')).toBeInTheDocument();
    expect(mockEnd).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Retry upload' }));

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/speaking/sessions/sess-1/results'));
    expect(mockUpload).toHaveBeenCalledTimes(2);
    expect(mockUpload.mock.calls[1][1]).toBe(mockUpload.mock.calls[0][1]);
    expect(mockEnd).toHaveBeenCalledTimes(1);
  });

  it('asks for Rules + consent (no timed modal) when an older session has no consent yet', async () => {
    mockGetSession.mockResolvedValue({ ...SESSION, consentAccepted: false });
    render(<SpeakingSessionRecordingPage />);

    expect(await screen.findByTestId('speaking-rules-consent')).toBeInTheDocument();
    expect(screen.queryByRole('timer')).not.toBeInTheDocument();
    expect(navigator.mediaDevices.getUserMedia).not.toHaveBeenCalled();
  });
});
