import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { act } from 'react';

const {
  mockRouterPush,
  mockRouterReplace,
  mockUseAuth,
  mockUseReadingProfile,
  mockGetDiagnosticQuestions,
  mockGetDiagnosticResult,
  mockStartDiagnostic,
  mockSubmitDiagnostic,
} = vi.hoisted(() => ({
  mockRouterPush: vi.fn(),
  mockRouterReplace: vi.fn(),
  mockUseAuth: vi.fn(),
  mockUseReadingProfile: vi.fn(),
  mockGetDiagnosticQuestions: vi.fn(),
  mockGetDiagnosticResult: vi.fn(),
  mockStartDiagnostic: vi.fn(),
  mockSubmitDiagnostic: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockRouterPush,
    replace: mockRouterReplace,
  }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/hooks/useReadingProfile', () => ({
  useReadingProfile: () => mockUseReadingProfile(),
}));

vi.mock('@/lib/reading-pathway-api', () => ({
  getDiagnosticQuestions: (...args: unknown[]) => mockGetDiagnosticQuestions(...args),
  getDiagnosticResult: (...args: unknown[]) => mockGetDiagnosticResult(...args),
  startDiagnostic: (...args: unknown[]) => mockStartDiagnostic(...args),
  submitDiagnostic: (...args: unknown[]) => mockSubmitDiagnostic(...args),
}));

import DiagnosticPage from './page';
import type {
  DiagnosticQuestionDto,
  DiagnosticResultDto,
  LearnerReadingProfileDto,
} from '@/lib/reading-pathway-api';

function buildProfile(overrides: Partial<LearnerReadingProfileDto> = {}): LearnerReadingProfileDto {
  return {
    userId: 'learner-1',
    currentStage: 'diagnostic',
    targetBand: 'B',
    examDate: null,
    hoursPerWeek: 6,
    profession: 'medicine',
    hasTakenBefore: false,
    previousScore: null,
    selfRatedSpeed: 3,
    selfRatedVocabulary: 3,
    readinessScore: null,
    predictedScore: null,
    onboardingCompletedAt: null,
    pathwayGeneratedAt: null,
    weeksRemaining: null,
    diagnosticCompleted: false,
    ...overrides,
  };
}

function buildQuestion(overrides: Partial<DiagnosticQuestionDto> = {}): DiagnosticQuestionDto {
  return {
    id: 'question-1',
    partCode: 'A',
    questionType: 'MultipleChoice',
    displayOrder: 1,
    stem: 'Choose the best answer.',
    options: [
      { value: 'A', label: 'Option A' },
      { value: 'B', label: 'Option B' },
    ],
    textTitle: null,
    textHtml: null,
    skillCode: 'S1',
    ...overrides,
  };
}

function buildDiagnosticResult(overrides: Partial<DiagnosticResultDto> = {}): DiagnosticResultDto {
  return {
    sessionId: 'session-1',
    score: 18,
    totalQuestions: 22,
    skillScores: { S1: 5, S2: 6 },
    estimatedOetBand: 'B',
    estimatedScaledScore: 350,
    durationSeconds: 1800,
    roadmapWeeks: 12,
    completedAt: '2026-06-01T10:00:00Z',
    ...overrides,
  };
}

function createAbortError(): Error & { name: 'AbortError' } {
  return new DOMException('The request timed out.', 'AbortError') as Error & { name: 'AbortError' };
}

function createRecoverableSubmitError(status: number, detail?: { code?: string }): Error & {
  status: number;
  detail?: { code?: string };
} {
  return Object.assign(new Error('Recoverable submit failure'), { status, detail });
}

describe('Reading diagnostic page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockUseReadingProfile.mockReturnValue({
      profile: buildProfile(),
      isLoading: false,
      error: null,
      mutate: vi.fn(),
    });
    mockStartDiagnostic.mockResolvedValue({
      sessionId: 'session-1',
      questionIds: ['question-1'],
      timeLimitMinutes: 25,
    });
    mockGetDiagnosticQuestions.mockResolvedValue([buildQuestion()]);
    mockSubmitDiagnostic.mockResolvedValue(buildDiagnosticResult());
    mockGetDiagnosticResult.mockResolvedValue(buildDiagnosticResult());
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('keeps diagnostic-stage learners on the diagnostic briefing', () => {
    mockUseReadingProfile.mockReturnValue({
      profile: buildProfile({ currentStage: 'diagnostic' }),
      isLoading: false,
      error: null,
      mutate: vi.fn(),
    });

    render(<DiagnosticPage />);

    expect(screen.getByRole('button', { name: /i'm ready, start diagnostic/i })).toBeInTheDocument();
    expect(mockRouterReplace).not.toHaveBeenCalled();
    expect(mockRouterPush).not.toHaveBeenCalled();
  });

  it('keeps diagnostic-stage learners on the diagnostic briefing', () => {
    render(<DiagnosticPage />);

    expect(screen.getByRole('button', { name: /i'm ready, start diagnostic/i })).toBeInTheDocument();
    expect(mockRouterReplace).not.toHaveBeenCalled();
    expect(mockRouterPush).not.toHaveBeenCalled();
  });

  it('recovers from a submit abort and still opens the results page', async () => {
    mockSubmitDiagnostic.mockRejectedValueOnce(createAbortError());
    mockGetDiagnosticResult.mockResolvedValueOnce(buildDiagnosticResult({ sessionId: 'session-1' }));

    render(<DiagnosticPage />);

    fireEvent.click(screen.getByRole('button', { name: /i'm ready, start diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /finish diagnostic/i }));

    await waitFor(() => {
      expect(mockGetDiagnosticResult).toHaveBeenCalled();
    });

    expect(mockGetDiagnosticResult).toHaveBeenCalledWith('session-1', { timeoutMs: 5_000 });

    await waitFor(() => {
      expect(mockRouterPush).toHaveBeenCalledWith('/reading/diagnostic-results?sessionId=session-1');
    });

    expect(screen.queryByText(/could not finish the diagnostic right now/i)).not.toBeInTheDocument();
    expect(window.sessionStorage.getItem('diagnostic_result')).toContain('"sessionId":"session-1"');
  });

  it('recovers from an already-submitted response and still opens the results page', async () => {
    mockSubmitDiagnostic.mockRejectedValueOnce(
      createRecoverableSubmitError(400, { code: 'diagnostic_already_submitted' }),
    );
    mockGetDiagnosticResult.mockResolvedValueOnce(buildDiagnosticResult({ sessionId: 'session-1' }));

    render(<DiagnosticPage />);

    fireEvent.click(screen.getByRole('button', { name: /i'm ready, start diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /finish diagnostic/i }));

    await waitFor(() => {
      expect(mockGetDiagnosticResult).toHaveBeenCalledWith('session-1', { timeoutMs: 5_000 });
    });

    await waitFor(() => {
      expect(mockRouterPush).toHaveBeenCalledWith('/reading/diagnostic-results?sessionId=session-1');
    });

    expect(screen.queryByText(/could not finish the diagnostic right now/i)).not.toBeInTheDocument();
  });

  it('keeps polling when the diagnostic result endpoint returns a transient 503', async () => {
    mockSubmitDiagnostic.mockRejectedValueOnce(createAbortError());
    mockGetDiagnosticResult
      .mockRejectedValueOnce(createRecoverableSubmitError(503))
      .mockResolvedValueOnce(buildDiagnosticResult({ sessionId: 'session-1' }));

    render(<DiagnosticPage />);

    fireEvent.click(screen.getByRole('button', { name: /i'm ready, start diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /finish diagnostic/i }));

    await waitFor(() => {
      expect(mockGetDiagnosticResult).toHaveBeenCalledWith('session-1', { timeoutMs: 5_000 });
    });

    await waitFor(() => {
      expect(mockRouterPush).toHaveBeenCalledWith('/reading/diagnostic-results?sessionId=session-1');
    });
  });

  it('shows a recoverable error when the submit failure is not recoverable', async () => {
    const forbiddenError = Object.assign(new Error('Forbidden'), {
      status: 403,
      detail: { code: 'forbidden' },
    });
    mockSubmitDiagnostic.mockRejectedValueOnce(forbiddenError);

    render(<DiagnosticPage />);

    fireEvent.click(screen.getByRole('button', { name: /i'm ready, start diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /finish diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/could not finish the diagnostic right now/i);
    });

    expect(mockGetDiagnosticResult).not.toHaveBeenCalled();
    expect(mockRouterPush).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
  });

  it('shows the finish error when recovery polling is exhausted', async () => {
    mockSubmitDiagnostic.mockRejectedValueOnce(createAbortError());
    mockGetDiagnosticResult.mockImplementation((_sessionId: string, options?: { timeoutMs?: number }) => {
      const timeoutMs = options?.timeoutMs ?? 5_000;

      return new Promise<DiagnosticResultDto>((_, reject) => {
        setTimeout(() => {
          reject(new DOMException('The request timed out.', 'AbortError'));
        }, timeoutMs);
      });
    });

    render(<DiagnosticPage />);

    fireEvent.click(screen.getByRole('button', { name: /i'm ready, start diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
    });

    vi.useFakeTimers();
    fireEvent.click(screen.getByRole('button', { name: /finish diagnostic/i }));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    expect(screen.getByRole('alert')).toHaveTextContent(/could not finish the diagnostic right now/i);
    expect(mockRouterPush).not.toHaveBeenCalled();
  });

  it('still redirects when sessionStorage rejects the cached result', async () => {
    mockSubmitDiagnostic.mockResolvedValueOnce(buildDiagnosticResult({ sessionId: 'session-1' }));
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    });

    render(<DiagnosticPage />);

    fireEvent.click(screen.getByRole('button', { name: /i'm ready, start diagnostic/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /finish diagnostic/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /finish diagnostic/i }));

    await waitFor(() => {
      expect(mockRouterPush).toHaveBeenCalledWith('/reading/diagnostic-results?sessionId=session-1');
    });

    expect(screen.queryByText(/could not finish the diagnostic right now/i)).not.toBeInTheDocument();
  });
});
