import { render, screen } from '@testing-library/react';

const { fetchMyPastLiveClasses } = vi.hoisted(() => ({ fetchMyPastLiveClasses: vi.fn() }));

vi.mock('@/lib/api', () => ({ fetchMyPastLiveClasses }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => '/me/classes/past',
}));

import MyPastClassesPage from './page';

function pastClass(id: string, title: string, recordingReady: boolean) {
  return {
    id, slug: id, title, description: '', type: 'Workshop', professionTrack: 'medicine', level: 'B',
    creditCost: 0, status: 'Published',
    sessions: [{
      id: `${id}-s1`, scheduledStartAt: '2026-09-30T10:00:00Z', scheduledEndAt: '2026-09-30T11:00:00Z',
      capacity: 10, enrolledCount: 3, status: 'Completed', isEnrolled: true, isJoinAvailable: false,
      creditCost: 0, recordingReady,
    }],
  };
}

describe('My Past Classes page', () => {
  it('offers a recording only when the API says the learner can open it', async () => {
    fetchMyPastLiveClasses.mockResolvedValue([
      pastClass('c1', 'Ready class', true),
      // Completed, but the recording is still processing (or was never made).
      pastClass('c2', 'Processing class', false),
    ]);

    render(<MyPastClassesPage />);

    expect(await screen.findByText('Processing class')).toBeInTheDocument();
    const watch = screen.getAllByRole('link', { name: /watch recording/i });
    expect(watch).toHaveLength(1);
    expect(watch[0]).toHaveAttribute('href', '/me/classes/recordings/c1-s1');
    expect(screen.getByRole('button', { name: /no recording yet/i })).toBeDisabled();
  });
});
