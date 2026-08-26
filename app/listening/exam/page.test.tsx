import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ListeningHomeDto, ListeningHomePaperDto } from '@/lib/listening-api';

const { mockGetListeningHome, mockPush, mockStartListeningAttempt, mockSubmitAudioCheck } = vi.hoisted(() => ({
  mockGetListeningHome: vi.fn(),
  mockPush: vi.fn(),
  mockStartListeningAttempt: vi.fn(),
  mockSubmitAudioCheck: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockPush }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/listening-pathway-api', () => ({
  submitAudioCheck: mockSubmitAudioCheck,
}));

vi.mock('@/lib/listening-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/listening-api')>(
    '@/lib/listening-api',
  );
  return {
    ...actual,
    getListeningHome: mockGetListeningHome,
    startListeningAttempt: mockStartListeningAttempt,
  };
});

import ListeningFullExamPage from './page';

function paper(overrides: Partial<ListeningHomePaperDto> = {}): ListeningHomePaperDto {
  return {
    id: 'atlas-st9',
    title: 'Atlas Practice Series — Listening Sample Test 9',
    slug: 'atlas-practice-series-listening-sample-test-09',
    difficulty: 'standard',
    estimatedDurationMinutes: 45,
    publishedAt: '2026-08-18T00:00:00.000Z',
    route: '/listening/paper/atlas-st9',
    sourceKind: 'content_paper',
    objectiveReady: true,
    questionCount: 36,
    tagsCsv: 'listening,atlas-practice-series',
    partACount: 24,
    partBCount: 6,
    partCCount: 6,
    assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
    lastAttempt: null,
    ...overrides,
  };
}

function home(papers: ListeningHomePaperDto[]): ListeningHomeDto {
  return {
    intro: 'Listening practice',
    papers,
    featuredTasks: [],
    activeAttempts: [],
    recentResults: [],
    partCollections: [],
    transcriptBackedReview: {
      title: '',
      route: null,
      availableAfterAttempt: false,
      latestAttemptId: null,
      latestScoreDisplay: null,
    },
    distractorDrills: [],
    drillGroups: [],
    accessPolicyHints: { policy: '', state: 'available', rationale: '', availableAfterAttempt: false },
    mockSets: [],
    emptyStates: { papers: null, activeAttempts: null, recentResults: null },
  };
}

describe('Listening full exam page', () => {
  beforeEach(() => {
    mockGetListeningHome.mockReset();
    mockPush.mockReset();
    mockStartListeningAttempt.mockReset();
    mockSubmitAudioCheck.mockReset();
    mockSubmitAudioCheck.mockResolvedValue({
      success: true,
      currentStage: 'diagnostic',
      audioCheckPassedAt: null,
    });
  });

  it('lists published papers under Atlas and Nova folders instead of mocks', async () => {
    mockGetListeningHome.mockResolvedValue(
      home([
        paper(),
        paper({
          id: 'nova-20',
          title: 'Nova Practice Series — Listening Test 20',
          slug: 'nova-practice-series-listening-20',
          tagsCsv: 'listening,nova-practice-series',
          questionCount: 42,
        }),
        paper({
          id: 'legacy',
          title: 'Older sample paper',
          slug: 'listening-sample-1',
          tagsCsv: null,
        }),
      ]),
    );

    render(<ListeningFullExamPage />);

    expect(await screen.findByRole('button', { name: /atlas practice series/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /nova practice series/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /anna hartford/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /jayden book/i })).not.toBeInTheDocument();
    expect(screen.queryByText('Atlas Practice Series — Listening Sample Test 9')).not.toBeInTheDocument();
    expect(screen.queryByText(/no mock bundles/i)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /atlas practice series/i }));
    expect(await screen.findByText('Atlas Practice Series — Listening Sample Test 9')).toBeInTheDocument();
    expect(screen.queryByText('Older sample paper')).not.toBeInTheDocument();
  });

  it('starts a full exam attempt and opens the paper player', async () => {
    mockGetListeningHome.mockResolvedValue(home([paper()]));
    mockStartListeningAttempt.mockResolvedValue({ attemptId: 'att-1' });

    render(<ListeningFullExamPage />);
    fireEvent.click(await screen.findByRole('button', { name: /atlas practice series/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'Start full exam' }));

    await waitFor(() => {
      expect(mockStartListeningAttempt).toHaveBeenCalledWith('atlas-st9', 'exam');
      expect(mockPush).toHaveBeenCalledWith('/listening/paper/atlas-st9?attemptId=att-1');
    });
  });
});
