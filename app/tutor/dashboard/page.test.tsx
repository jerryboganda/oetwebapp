import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import TutorDashboardPage from './page';

vi.mock('@/lib/api', () => ({
  fetchTutorClasses: vi.fn(),
  fetchTutorEarnings: vi.fn(),
}));

import { fetchTutorClasses, fetchTutorEarnings } from '@/lib/api';

describe('TutorDashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the 4 balanced quick actions and dashboard cards', async () => {
    vi.mocked(fetchTutorClasses).mockResolvedValue([
      {
        id: 'cls-1',
        slug: 'oet-speaking-masterclass',
        title: 'OET Speaking Masterclass',
        titleAr: null,
        description: 'Comprehensive speaking strategies',
        descriptionAr: null,
        creditCost: 10,
        type: 'Webinar',
        level: 'AllLevels',
        professionTrack: 'Medicine',
        status: 'Published',
        coverImageUrl: null,
        sessions: [
          {
            id: 'sess-1',
            scheduledStartAt: new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString(),
            scheduledEndAt: new Date(Date.now() + 3 * 60 * 60 * 1000).toISOString(),
            capacity: 20,
            enrolledCount: 8,
            status: 'Scheduled',
            isEnrolled: false,
            isJoinAvailable: false,
            creditCost: 10,
          },
        ],
      },
    ]);

    vi.mocked(fetchTutorEarnings).mockResolvedValue({
      from: '2026-01-01T00:00:00Z',
      to: '2026-06-01T00:00:00Z',
      grossUsd: 1500,
      netUsd: 1200,
      revenueSharePercent: 80,
      lines: [],
    });

    render(<TutorDashboardPage />);

    await waitFor(() => {
      expect(screen.getByText('What would you like to do?')).toBeInTheDocument();
    });

    const scheduleLink = screen.getByRole('link', { name: /schedule class/i });
    expect(scheduleLink).toHaveAttribute('href', '/tutor/classes/new');

    const manageClassesLink = screen.getByRole('link', { name: /manage classes/i });
    expect(manageClassesLink).toHaveAttribute('href', '/tutor/classes');

    const availabilityLink = screen.getByRole('link', { name: /set availability/i });
    expect(availabilityLink).toHaveAttribute('href', '/tutor/availability');

    const earningsLink = screen.getByRole('link', { name: /view earnings/i });
    expect(earningsLink).toHaveAttribute('href', '/tutor/earnings');

    const matches = screen.getAllByText('OET Speaking Masterclass');
    expect(matches.length).toBeGreaterThanOrEqual(1);
  });
});
