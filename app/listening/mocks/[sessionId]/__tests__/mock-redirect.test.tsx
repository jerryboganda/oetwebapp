import { waitFor } from '@testing-library/react';

// Mirror the player test's hoisted-mock style so the api layer + router are
// stubbed before the page module evaluates.
const { mockFetchMockSession, mockStartMockSection, mockReplace } = vi.hoisted(() => ({
  mockFetchMockSession: vi.fn(),
  mockStartMockSection: vi.fn(),
  mockReplace: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchMockSession: mockFetchMockSession,
  startMockSection: mockStartMockSection,
}));

import ListeningMockSessionPage from '../page';
import { renderWithRouter } from '@/tests/test-utils';

const listeningLaunchRoute =
  '/listening/player/paper-listening?mockAttemptId=mock-1&mockSectionId=section-listening&paperId=paper-listening&mockMode=exam&strictness=exam&deliveryMode=computer&strictTimer=true';

function buildSession(overrides?: { sectionStates?: unknown[] }) {
  return {
    sessionId: 'mock-1',
    state: 'in_progress',
    config: { id: 'mock-1', title: 'Full OET Mock Test', mode: 'exam' },
    resumeRoute: '/mocks/player/mock-1',
    sectionStates: overrides?.sectionStates ?? [
      {
        id: 'section-listening',
        title: 'Listening section',
        subtest: 'listening',
        state: 'ready',
        reviewAvailable: false,
        reviewSelected: false,
        launchRoute:
          '/listening/player/paper-listening?mockAttemptId=mock-1&mockSectionId=section-listening',
        contentPaperId: 'paper-listening',
      },
    ],
  };
}

describe('Listening mock session redirect', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('resolves the mock listening section and replaces with the server launch route', async () => {
    mockFetchMockSession.mockResolvedValue(buildSession());
    mockStartMockSection.mockResolvedValue({
      id: 'section-listening',
      subtest: 'listening',
      state: 'in_progress',
      launchRoute: listeningLaunchRoute,
    });

    renderWithRouter(<ListeningMockSessionPage />, {
      params: { sessionId: 'mock-1' },
      router: { replace: mockReplace },
    });

    await waitFor(() => expect(mockStartMockSection).toHaveBeenCalledWith('mock-1', 'section-listening'));
    expect(mockReplace).toHaveBeenCalledTimes(1);
    expect(mockReplace).toHaveBeenCalledWith(listeningLaunchRoute);
    // The carried params the strict player relies on are present on the target.
    const target = mockReplace.mock.calls[0][0] as string;
    expect(target).toContain('/listening/player/');
    expect(target).toContain('mockAttemptId=mock-1');
    expect(target).toContain('mockSectionId=section-listening');
    expect(target).toContain('strictness=exam');
    expect(target).toContain('deliveryMode=computer');
  });

  it('falls back to the player in exam mode when the id is not a mock attempt', async () => {
    // A bare content-paper id is not a resolvable mock attempt — fetch rejects.
    mockFetchMockSession.mockRejectedValue(new Error('not found'));

    renderWithRouter(<ListeningMockSessionPage />, {
      params: { sessionId: 'paper-legacy-123' },
      router: { replace: mockReplace },
    });

    await waitFor(() => expect(mockReplace).toHaveBeenCalled());
    expect(mockStartMockSection).not.toHaveBeenCalled();
    expect(mockReplace).toHaveBeenCalledWith('/listening/player/paper-legacy-123?mode=exam');
  });

  it('falls back to the player when a mock attempt has no listening section', async () => {
    mockFetchMockSession.mockResolvedValue(
      buildSession({
        sectionStates: [
          {
            id: 'section-reading',
            title: 'Reading section',
            subtest: 'reading',
            state: 'ready',
            reviewAvailable: false,
            reviewSelected: false,
            launchRoute: '/reading/paper/paper-reading?mockAttemptId=mock-1',
            contentPaperId: 'paper-reading',
          },
        ],
      }),
    );

    renderWithRouter(<ListeningMockSessionPage />, {
      params: { sessionId: 'mock-1' },
      router: { replace: mockReplace },
    });

    await waitFor(() => expect(mockReplace).toHaveBeenCalled());
    expect(mockStartMockSection).not.toHaveBeenCalled();
    expect(mockReplace).toHaveBeenCalledWith('/listening/player/mock-1?mode=exam');
  });
});
