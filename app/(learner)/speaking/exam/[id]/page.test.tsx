import { act, render, screen } from '@testing-library/react';
import type { ExamCandidateCard, SpeakingExamDetail } from '@/lib/api/speaking-exams';

const { router, mockGetExam, mockCompleteMockSection, stopBySession } = vi.hoisted(() => ({
  // One router object: the page's poll callback depends on it.
  router: { push: vi.fn(), replace: vi.fn() },
  mockGetExam: vi.fn(),
  mockCompleteMockSection: vi.fn(),
  stopBySession: {} as Record<string, () => Promise<boolean>>,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'exam-1' }),
  useRouter: () => router,
}));

vi.mock('@/lib/api', () => ({
  ApiError: class ApiError extends Error {},
  apiClient: {},
  completeMockSection: mockCompleteMockSection,
}));

vi.mock('@/lib/api/speaking-exams', () => ({
  getSpeakingExam: mockGetExam,
  finishSpeakingExamIntro: vi.fn(),
  recordSpeakingExamConsent: vi.fn(),
  startSpeakingExamCard: vi.fn(),
}));

vi.mock('@/lib/api/speaking-live-rooms', () => ({
  createLiveRoom: vi.fn(),
  endLiveRoom: vi.fn(),
  issueLiveRoomToken: vi.fn(),
  startRecording: vi.fn(),
}));

vi.mock('@/hooks/useSpeakingSessionRecorder', () => ({ RECORDING_UPLOAD_FAILED: 'Upload failed — Retry upload' }));
vi.mock('@/components/domain/speaking/SpeakingConsentBanner', () => ({ SpeakingConsentBanner: () => null }));
vi.mock('@/components/domain/speaking/SpeakingRulesConsent', () => ({
  SpeakingRulesConsent: () => <div data-testid="speaking-rules-consent" />,
}));
vi.mock('@/components/domain/speaking/LearnerLiveRoomShell', () => ({ LearnerLiveRoomShell: () => null }));

// The panel owns the voice hook; here it only hands the page the card's finalize (stop) function.
vi.mock('@/components/domain/speaking/ExamConversationPanel', async () => {
  const { useEffect } = await import('react');
  return {
    ExamConversationPanel: ({ sessionId, onVoiceStopReady }: {
      sessionId: string;
      onVoiceStopReady?: (stop: (() => Promise<boolean>) | null) => void;
    }) => {
      useEffect(() => {
        onVoiceStopReady?.(stopBySession[sessionId] ?? (() => Promise.resolve(true)));
        return () => onVoiceStopReady?.(null);
      }, [sessionId, onVoiceStopReady]);
      return <div data-testid={`panel-${sessionId}`} />;
    },
  };
});

import SpeakingExamPage from './page';

const NOW = '2026-09-30T12:00:00.000Z';

const card = (overrides: Partial<ExamCandidateCard> = {}): ExamCandidateCard => ({
  cardId: 'card-1',
  professionId: 'medicine',
  scenarioTitle: 'Chest pain',
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
  disclaimer: 'Practice estimate only.',
  // Two cards drawn for one exam can print the same source number.
  displayCardNumber: 4,
  ...overrides,
});

const exam = (overrides: Partial<SpeakingExamDetail> = {}): SpeakingExamDetail => ({
  examId: 'exam-1',
  mode: 'ai',
  state: 'active_a',
  professionId: 'medicine',
  currentCardNumber: 1,
  currentSessionId: 'sess-a',
  currentCard: card(),
  clock: { stage: 'active_a', serverNow: NOW, stageStartedAt: NOW, stageEndsAt: '2026-09-30T12:05:00.000Z', expired: false },
  consentAccepted: true,
  liveVoiceAvailable: true,
  ...overrides,
});

const cardB = () => exam({ state: 'active_b', currentCardNumber: 2, currentSessionId: 'sess-b', currentCard: card() });

async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

async function renderPage() {
  render(<SpeakingExamPage />);
  await flush();
}

describe('Speaking exam page', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now: new Date(NOW) });
    mockGetExam.mockReset();
    router.push.mockReset();
    router.replace.mockReset();
    mockCompleteMockSection.mockReset();
    for (const key of Object.keys(stopBySession)) delete stopBySession[key];
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('card labels', () => {
    it('names the two cards A and B by slot, although both print the same source number', async () => {
      mockGetExam.mockResolvedValueOnce(exam()).mockResolvedValue(cardB());
      await renderPage();

      expect(screen.getByText('Role-Play Card A')).toBeInTheDocument();
      expect(screen.getByText('Part 2 — Card A')).toBeInTheDocument();
      expect(screen.queryByText(/No\. 4/)).not.toBeInTheDocument();

      await flush(3_000);

      expect(screen.getByText('Role-Play Card B')).toBeInTheDocument();
      expect(screen.getByText('Part 2 — Card B')).toBeInTheDocument();
      expect(screen.queryByText('Role-Play Card A')).not.toBeInTheDocument();
      expect(screen.queryByText(/No\. 4/)).not.toBeInTheDocument();
    });

    it('labels the preparation card with its slot too', async () => {
      mockGetExam.mockResolvedValue(exam({ state: 'prep_b', currentCardNumber: 2, currentSessionId: 'sess-b' }));
      await renderPage();

      expect(screen.getByText('Role-Play Card B')).toBeInTheDocument();
      expect(screen.getByText('Part 2 — Card B')).toBeInTheDocument();
      expect(screen.queryByText(/No\. 4/)).not.toBeInTheDocument();
    });

    it('prints no card letter when the slot is unknown, and none at the introduction', async () => {
      mockGetExam.mockResolvedValue(exam({ state: 'active_a', currentCardNumber: 0 }));
      await renderPage();
      expect(screen.getByText('Role-Play Card')).toBeInTheDocument();
      expect(screen.getByText('Part 2')).toBeInTheDocument();
    });

    it('shows the introduction without any card heading', async () => {
      mockGetExam.mockResolvedValue(exam({ state: 'intro', currentCardNumber: 0, currentSessionId: null, currentCard: null }));
      await renderPage();

      expect(screen.getByText('Part 1 — Introduction')).toBeInTheDocument();
      expect(screen.queryByTestId('speaking-role-card')).not.toBeInTheDocument();
    });
  });

  describe('moving to the next card', () => {
    it('does not mount Card B until Card A is saved, and saves it once however many polls overlap', async () => {
      let saved!: (value: boolean) => void;
      const stopA = vi.fn(() => new Promise<boolean>((resolve) => {
        saved = resolve;
      }));
      stopBySession['sess-a'] = stopA;
      mockGetExam.mockResolvedValueOnce(exam()).mockResolvedValue(cardB());
      await renderPage();

      // The server has moved on; Card A is still saving.
      await flush(3_000);
      expect(stopA).toHaveBeenCalledTimes(1);
      expect(screen.getByTestId('panel-sess-a')).toBeInTheDocument();
      expect(screen.queryByTestId('panel-sess-b')).not.toBeInTheDocument();

      // Two more poll ticks while that save is in flight: no second poll, no second save.
      await flush(6_000);
      expect(mockGetExam).toHaveBeenCalledTimes(2);
      expect(stopA).toHaveBeenCalledTimes(1);
      expect(screen.queryByTestId('panel-sess-b')).not.toBeInTheDocument();

      await act(async () => {
        saved(true);
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(screen.getByTestId('panel-sess-b')).toBeInTheDocument();
      expect(screen.queryByTestId('panel-sess-a')).not.toBeInTheDocument();
      expect(stopA).toHaveBeenCalledTimes(1);
    });

    it('moves on at once when there was nothing to save (stop resolves true)', async () => {
      stopBySession['sess-a'] = vi.fn().mockResolvedValue(true);
      mockGetExam.mockResolvedValueOnce(exam()).mockResolvedValue(cardB());
      await renderPage();

      await flush(3_000);

      expect(screen.getByTestId('panel-sess-b')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('holds a live card while its transcript will not save, then moves on after the third try', async () => {
      const stopA = vi.fn().mockResolvedValue(false);
      stopBySession['sess-a'] = stopA;
      mockGetExam.mockResolvedValueOnce(exam()).mockResolvedValue(cardB());
      await renderPage();

      await flush(3_000);
      expect(screen.getByRole('alert')).toHaveTextContent('The live voice transcript could not be saved. Retrying before moving to the next card.');
      expect(screen.getByTestId('panel-sess-a')).toBeInTheDocument();

      await flush(3_000);
      expect(screen.getByTestId('panel-sess-a')).toBeInTheDocument();
      expect(stopA).toHaveBeenCalledTimes(2);

      await flush(3_000);
      expect(stopA).toHaveBeenCalledTimes(3);
      expect(screen.getByTestId('panel-sess-b')).toBeInTheDocument();
      expect(screen.queryByTestId('panel-sess-a')).not.toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('never drops a recording: a card recorded for upload stays until its upload lands', async () => {
      const stopA = vi.fn().mockResolvedValue(false);
      stopBySession['sess-a'] = stopA;
      mockGetExam
        .mockResolvedValueOnce(exam({ liveVoiceAvailable: false }))
        .mockResolvedValue({ ...cardB(), liveVoiceAvailable: false });
      await renderPage();

      await flush(15_000);

      expect(stopA).toHaveBeenCalledTimes(5);
      expect(screen.getByTestId('panel-sess-a')).toBeInTheDocument();
      expect(screen.queryByTestId('panel-sess-b')).not.toBeInTheDocument();
      expect(screen.getByRole('alert')).toHaveTextContent('Upload failed — Retry upload');
    });

    it('never drops a recording when a later poll reports live voice back: the card keeps the mode it started in', async () => {
      const stopA = vi.fn().mockResolvedValue(false);
      stopBySession['sess-a'] = stopA;
      mockGetExam
        .mockResolvedValueOnce(exam({ liveVoiceAvailable: false }))
        .mockResolvedValueOnce(exam({ liveVoiceAvailable: true }))
        .mockResolvedValue({ ...cardB(), liveVoiceAvailable: true });
      await renderPage();

      // The next poll reports the same card with live voice back, and the page renders it before the card ends.
      await flush(3_000);
      await flush(12_000);

      expect(stopA).toHaveBeenCalledTimes(4);
      expect(screen.getByTestId('panel-sess-a')).toBeInTheDocument();
      expect(screen.queryByTestId('panel-sess-b')).not.toBeInTheDocument();
      expect(screen.getByRole('alert')).toHaveTextContent('Upload failed — Retry upload');
    });

    it('a live card still moves on after the third failed save when a later poll reports live voice unavailable', async () => {
      const stopA = vi.fn().mockResolvedValue(false);
      stopBySession['sess-a'] = stopA;
      mockGetExam
        .mockResolvedValueOnce(exam({ liveVoiceAvailable: true }))
        .mockResolvedValueOnce(exam({ liveVoiceAvailable: false }))
        .mockResolvedValue({ ...cardB(), liveVoiceAvailable: false });
      await renderPage();

      // The next poll reports the same card with live voice unavailable, and the page renders it before the card ends.
      await flush(3_000);
      await flush(3_000);
      expect(screen.getByRole('alert')).toHaveTextContent('The live voice transcript could not be saved. Retrying before moving to the next card.');

      await flush(6_000);
      expect(stopA).toHaveBeenCalledTimes(3);
      expect(screen.getByTestId('panel-sess-b')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('opens the results once the last card is saved', async () => {
      stopBySession['sess-b'] = vi.fn().mockResolvedValue(true);
      mockGetExam
        .mockResolvedValueOnce(cardB())
        .mockResolvedValue(exam({ state: 'completed', currentCardNumber: 0, currentSessionId: null, currentCard: null }));
      await renderPage();

      await flush(3_000);

      expect(stopBySession['sess-b']).toHaveBeenCalledTimes(1);
      expect(router.replace).toHaveBeenCalledWith('/speaking/exam/exam-1/results');
    });
  });
});
