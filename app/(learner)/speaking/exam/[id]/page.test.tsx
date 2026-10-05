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

import { ApiError } from '@/lib/api';
import { finishSpeakingExamIntro } from '@/lib/api/speaking-exams';
import SpeakingExamPage from './page';

const NOW = '2026-09-30T12:00:00.000Z';

// What the shared client throws when a proxy answers with an HTML 502: the raw text stays in `message` for logs,
// the learner-facing `userMessage` is plain words (lib/api/client.ts).
// `@/lib/api` is mocked above with a message-only ApiError, so it is built through that shape.
const proxyBadGateway = () => Object.assign(new (ApiError as unknown as new (message: string) => Error)('Request failed: 502'), {
  status: 502,
  userMessage: 'Something went wrong on our side. Please try again in a moment.',
});

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

    it('never promises a next card after the last one: Card B says its results open', async () => {
      stopBySession['sess-b'] = vi.fn().mockResolvedValue(false);
      mockGetExam
        .mockResolvedValueOnce(cardB())
        .mockResolvedValue(exam({ state: 'completed', currentCardNumber: 0, currentSessionId: null, currentCard: null }));
      await renderPage();

      await flush(3_000);

      const alert = screen.getByRole('alert');
      expect(alert).toHaveTextContent('The live voice transcript could not be saved. Retrying before your results open.');
      expect(alert).not.toHaveTextContent(/next card/i);
    });
  });

  describe('a server error while polling (owner spec 4 Oct 2026: never a raw "Request failed: 502")', () => {
    const prepB = () => exam({ state: 'prep_b', currentCardNumber: 2, currentSessionId: 'sess-b' });

    it('is retried silently while the exam is on screen, and the learner never sees the raw text', async () => {
      mockGetExam
        .mockResolvedValueOnce(prepB())
        .mockRejectedValueOnce(proxyBadGateway())
        .mockRejectedValueOnce(proxyBadGateway())
        .mockResolvedValue(prepB());
      await renderPage();

      await flush(3_000); // first failed poll
      await flush(3_000); // second failed poll
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(document.body.textContent).not.toMatch(/Request failed/);

      await flush(3_000); // recovered
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(screen.getByText('Part 2 — Card B')).toBeInTheDocument();
    });

    it('tells the learner in plain words after a run of failures, and clears it when polling recovers', async () => {
      mockGetExam
        .mockResolvedValueOnce(prepB())
        .mockRejectedValueOnce(proxyBadGateway())
        .mockRejectedValueOnce(proxyBadGateway())
        .mockRejectedValueOnce(proxyBadGateway())
        .mockResolvedValue(prepB());
      await renderPage();

      await flush(9_000); // three failed polls in a row
      const alert = screen.getByRole('alert');
      expect(alert).toHaveTextContent('Something went wrong on our side. Please try again in a moment.');
      expect(alert).not.toHaveTextContent(/Request failed|502/);
      // The card is still there: the attempt is untouched.
      expect(screen.getByText('Part 2 — Card B')).toBeInTheDocument();

      await flush(3_000);
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('uses plain words for an error that is not an API error either', async () => {
      mockGetExam
        .mockResolvedValueOnce(prepB())
        .mockRejectedValue(new TypeError('Failed to fetch'));
      await renderPage();

      await flush(9_000);

      const alert = screen.getByRole('alert');
      expect(alert).toHaveTextContent('We could not load this step. Please try again.');
      expect(alert).not.toHaveTextContent(/Failed to fetch/);
    });

    it('still shows a first-load failure straight away: there is nothing else on screen', async () => {
      mockGetExam.mockRejectedValue(proxyBadGateway());
      await renderPage();

      expect(screen.getByText('Something went wrong on our side. Please try again in a moment.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    });
  });

  describe('the live AI session queue (owner decision 5 Oct 2026)', () => {
    const waitingIntro = (position = 2) => exam({
      state: 'intro',
      currentCardNumber: 0,
      currentSessionId: null,
      currentCard: null,
      consentAccepted: true,
      admission: { status: 'waiting', position, queueLength: 5, estimatedWaitSeconds: 130, pollAfterSeconds: 4 },
    });

    beforeEach(() => {
      vi.mocked(finishSpeakingExamIntro).mockReset();
    });

    it('shows the place in the line instead of the Begin button, and repeats finish-intro while waiting', async () => {
      mockGetExam.mockResolvedValue(waitingIntro());
      vi.mocked(finishSpeakingExamIntro).mockResolvedValue(waitingIntro(1));
      await renderPage();

      expect(screen.getByTestId('speaking-admission-wait')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /begin part 2/i })).not.toBeInTheDocument();
      expect(screen.getByTestId('speaking-admission-position')).toHaveTextContent('Position 2 of 5');
      expect(screen.queryByRole('timer')).not.toBeInTheDocument();

      await flush(4_000);

      expect(finishSpeakingExamIntro).toHaveBeenCalledWith('exam-1');
      // The retry's answer moves the learner up the line.
      expect(screen.getByTestId('speaking-admission-position')).toHaveTextContent('Position 1 of 5');
    });

    it('moves on to Card A the moment a retry is admitted', async () => {
      mockGetExam.mockResolvedValue(waitingIntro());
      vi.mocked(finishSpeakingExamIntro).mockResolvedValue(exam({ state: 'prep_a', currentSessionId: 'sess-a' }));
      await renderPage();

      await flush(4_000);

      expect(screen.queryByTestId('speaking-admission-wait')).not.toBeInTheDocument();
      expect(screen.getByText('Part 2 — Card A')).toBeInTheDocument();
    });

    it('ends the wait when the refusal cannot be cured by waiting, and shows the message and the Begin button', async () => {
      mockGetExam.mockResolvedValue(waitingIntro());
      const noCredits = Object.assign(new (ApiError as unknown as new (message: string) => Error)('Payment required'), {
        status: 402,
        userMessage: 'You do not have enough credits to start this activity.',
      });
      vi.mocked(finishSpeakingExamIntro).mockRejectedValue(noCredits);
      await renderPage();

      await flush(4_000);

      expect(screen.queryByTestId('speaking-admission-wait')).not.toBeInTheDocument();
      expect(screen.getByRole('alert')).toHaveTextContent('You do not have enough credits to start this activity.');
      expect(screen.getByRole('button', { name: /begin part 2/i })).toBeInTheDocument();
    });

    it('shows the Begin button, not the queue, for an exam that is not waiting', async () => {
      mockGetExam.mockResolvedValue(exam({
        state: 'intro',
        currentCardNumber: 0,
        currentSessionId: null,
        currentCard: null,
        consentAccepted: true,
        admission: null,
      }));
      await renderPage();

      expect(screen.queryByTestId('speaking-admission-wait')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: /begin part 2/i })).toBeInTheDocument();
    });
  });
});
