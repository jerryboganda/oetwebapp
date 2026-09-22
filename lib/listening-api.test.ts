const { mockEnsureFreshAccessToken, mockFetchWithTimeout, mockAnnounceCreditUsage } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
  mockAnnounceCreditUsage: vi.fn(),
}));

vi.mock('./auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));

vi.mock('@/lib/credit-feedback', () => ({
  announceCreditUsage: mockAnnounceCreditUsage,
  refreshCreditCards: vi.fn(),
}));

vi.mock('./env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('./network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

import { FREE_SAMPLE_FEEDBACK } from './free-sample';
import { getListeningSession, startListeningAttempt, startListeningPartPracticeAttempt } from './listening-api';

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('listening-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockEnsureFreshAccessToken.mockResolvedValue(null);
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ attemptId: 'attempt-1' }));
    Object.defineProperty(document, 'cookie', {
      configurable: true,
      writable: true,
      value: '',
    });
  });

  it('serializes pathwayStage when starting a scoped Listening attempt', async () => {
    await startListeningAttempt('paper 1', 'practice', { pathwayStage: 'foundation_partA' });

    expect(mockFetchWithTimeout).toHaveBeenCalledWith(
      '/v1/listening-papers/papers/paper%201/attempts',
      expect.objectContaining({ method: 'POST' }),
      undefined,
    );
    const init = mockFetchWithTimeout.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(String(init.body))).toEqual({
      mode: 'practice',
      pathwayStage: 'foundation_partA',
    });
  });

  it('announces the credit usage after an ordinary attempt start', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse({ attemptId: 'attempt-1', feedbackMessage: '1 Listening Credit used.' }),
    );

    await startListeningAttempt('paper-1', 'exam');

    await vi.waitFor(() => expect(mockAnnounceCreditUsage).toHaveBeenCalledWith('listening'));
  });

  it('does not announce credit usage for the free sample (no debit, stale ledger row)', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse({ attemptId: 'attempt-1', feedbackMessage: FREE_SAMPLE_FEEDBACK }),
    );

    await startListeningAttempt('paper-1', 'exam');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(mockAnnounceCreditUsage).not.toHaveBeenCalled();
  });

  it('omits pathwayStage from attempt starts when no scope is supplied', async () => {
    await startListeningAttempt('paper-1', 'practice');

    const init = mockFetchWithTimeout.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(String(init.body))).toEqual({ mode: 'practice' });
  });

  it('serializes pathwayStage on session lookup so scoped in-progress attempts are selected', async () => {
    await getListeningSession('paper 1', {
      mode: 'practice',
      pathwayStage: 'foundation_partA',
    });

    expect(mockFetchWithTimeout).toHaveBeenCalledWith(
      '/v1/listening-papers/papers/paper%201/session?mode=practice&pathwayStage=foundation_partA',
      expect.any(Object),
      undefined,
    );
  });

  it('posts a part-practice start against the published paper part endpoint', async () => {
    await startListeningPartPracticeAttempt('paper 1', 'B');

    expect(mockFetchWithTimeout).toHaveBeenCalledWith(
      '/v1/listening-papers/papers/paper%201/practice/parts/B',
      expect.objectContaining({ method: 'POST' }),
      undefined,
    );
  });
});
