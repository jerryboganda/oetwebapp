import { render, screen } from '@testing-library/react';

const { mockFetchMySpeakingRecordings } = vi.hoisted(() => ({
  mockFetchMySpeakingRecordings: vi.fn(),
}));

vi.mock('@/lib/api/speaking-compliance', () => ({
  deleteSpeakingRecording: vi.fn(),
  fetchMySpeakingRecordings: mockFetchMySpeakingRecordings,
}));

vi.mock('@/lib/analytics/speaking-events', () => ({
  trackSpeaking: vi.fn(),
}));

import SpeakingRecordingsPage from './page';

describe('Speaking recordings page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it.each([
    ['ConversationHub', 'Live microphone clip'],
    ['LiveKitEgress', 'Live tutor recording'],
    ['ClientMediaRecorder', 'Role-play recording'],
  ])('labels %s audio as %s', async (source, label) => {
    mockFetchMySpeakingRecordings.mockResolvedValue({
      recordings: [{
        recordingId: 'recording-1',
        sessionId: 'session-1',
        createdAt: '2026-10-03T12:00:00Z',
        mode: 'AiSelfPractice',
        professionId: 'medicine',
        scenarioTitle: 'Consultation',
        durationSeconds: 5,
        mimeType: 'audio/webm',
        source,
        isArchived: false,
        retentionExpiresAt: '2026-12-31T12:00:00Z',
      }],
    });

    render(<SpeakingRecordingsPage />);

    expect(await screen.findByText(label)).toBeInTheDocument();
  });

  it('explains the limits of live microphone clips', async () => {
    mockFetchMySpeakingRecordings.mockResolvedValue({ recordings: [] });

    render(<SpeakingRecordingsPage />);

    expect(await screen.findByText('You don\'t have any saved Speaking audio.')).toBeInTheDocument();
    expect(screen.getByText(/echo cancellation is enabled, but speaker or background audio may still be picked up/)).toBeInTheDocument();
  });
});
