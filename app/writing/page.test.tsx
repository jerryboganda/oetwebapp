import { render, screen } from '@testing-library/react';
import type { WritingTask, WritingSubmission } from '@/lib/mock-data';

const { mockFetchWritingHome, mockFetchWritingTasks, mockFetchWritingSubmissions, mockFetchMockReports, mockFetchWritingEntitlement, mockTrack } = vi.hoisted(() => ({
  mockFetchWritingHome: vi.fn(),
  mockFetchWritingTasks: vi.fn(),
  mockFetchWritingSubmissions: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockFetchWritingEntitlement: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
  usePathname: () => '/writing',
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchWritingHome: mockFetchWritingHome,
  fetchWritingTasks: mockFetchWritingTasks,
  fetchWritingSubmissions: mockFetchWritingSubmissions,
  fetchMockReports: mockFetchMockReports,
  fetchWritingEntitlement: mockFetchWritingEntitlement,
  // Added 2026-05-27: writing/page transitively imports `apiClient` via
  // `getWritingProfile` from lib/writing-pathway-api. Provide a no-op stub so
  // the test environment doesn't trip on the missing export.
  apiClient: {
    get: vi.fn().mockResolvedValue(null),
    post: vi.fn().mockResolvedValue(null),
    put: vi.fn().mockResolvedValue(null),
    patch: vi.fn().mockResolvedValue(null),
    delete: vi.fn().mockResolvedValue(null),
  },
}));

import WritingHome from './page';

describe('Writing home page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchWritingHome.mockResolvedValue({
      recommendedTask: {
        id: 'wt-001',
        title: 'Routine referral — dermatology',
        profession: 'Medicine',
        time: '40 mins',
        criteriaFocus: ['content', 'purpose'],
        scenarioType: 'Routine referral',
      },
      reviewCredits: { available: 2 },
      fullMockEntry: { title: 'Full mock', route: '/mocks/setup' },
    });

    mockFetchWritingTasks.mockResolvedValue([
      {
        id: 'wt-001',
        title: 'Routine referral — dermatology',
        profession: 'Medicine',
        difficulty: 'Medium',
        scenarioType: 'Routine referral',
        time: '40 mins',
        criteriaFocus: 'content',
        caseNotes: 'Case notes here',
      },
    ] satisfies WritingTask[]);

    mockFetchWritingSubmissions.mockResolvedValue([] satisfies WritingSubmission[]);
    mockFetchMockReports.mockResolvedValue([]);
    mockFetchWritingEntitlement.mockResolvedValue({
      allowed: true,
      tier: 'premium',
      remaining: null,
      limitPerWindow: null,
      windowDays: 7,
      resetAt: null,
      reason: 'allowed',
    });
  });

  it('shows rulebook entry points on the writing home surface', async () => {
    render(<WritingHome />);

    expect(await screen.findByText(/Know exactly how your writing is judged/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Open Rulebook/i })).toHaveAttribute('href', '/writing/rulebook');
    expect(screen.getByRole('link', { name: /Model Answers/i })).toHaveAttribute('href', '/writing/model');
  });
});
