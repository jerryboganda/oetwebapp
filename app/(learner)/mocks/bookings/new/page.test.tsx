import { render, screen } from '@testing-library/react';

const {
  mockCreateMockBookingV2,
  mockFetchMockSpeakingAccess,
  mockFetchMockAvailability,
  mockFetchMockOptions,
} = vi.hoisted(() => ({
  mockCreateMockBookingV2: vi.fn(),
  mockFetchMockSpeakingAccess: vi.fn(),
  mockFetchMockAvailability: vi.fn(),
  mockFetchMockOptions: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));

vi.mock('@/lib/api', () => ({
  createMockBookingV2: mockCreateMockBookingV2,
  fetchMockSpeakingAccess: mockFetchMockSpeakingAccess,
  fetchMockAvailability: mockFetchMockAvailability,
  fetchMockOptions: mockFetchMockOptions,
  isApiError: (err: unknown) => typeof err === 'object' && err !== null && 'code' in err,
}));

import NewMockBookingPage from './page';

describe('Mock tutor booking — tutor room availability (B9)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMockSpeakingAccess.mockResolvedValue({ requiresAiOnly: false, daysUntilExam: 30 });
    mockFetchMockOptions.mockResolvedValue({
      availableBundles: [
        { bundleId: 'bundle-speaking', title: 'Speaking mock', subtest: 'speaking', sections: [], estimatedDurationMinutes: 20, releasePolicy: null },
      ],
    });
  });

  it('shows "Live tutor sessions are temporarily unavailable." on 503 tutor_rooms_unavailable', async () => {
    mockFetchMockAvailability.mockRejectedValue({
      code: 'tutor_rooms_unavailable',
      message: 'Live tutor sessions are temporarily unavailable.',
    });

    render(<NewMockBookingPage />);

    expect(await screen.findByTestId('tutor-rooms-unavailable'))
      .toHaveTextContent('Live tutor sessions are temporarily unavailable.');
    expect(screen.queryByText(/No slots are published/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Book this slot' })).toBeDisabled();
    expect(mockCreateMockBookingV2).not.toHaveBeenCalled();
  });
});
