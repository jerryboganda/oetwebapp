const { mockEnsureFreshAccessToken, mockFetchWithTimeout } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('./auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));

vi.mock('./env', () => ({
  env: { apiBaseUrl: '' },
}));

vi.mock('./network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

import { getListeningSession, startListeningAttempt } from './listening-api';

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
    );
    const init = mockFetchWithTimeout.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(String(init.body))).toEqual({
      mode: 'practice',
      pathwayStage: 'foundation_partA',
    });
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
    );
  });
});
