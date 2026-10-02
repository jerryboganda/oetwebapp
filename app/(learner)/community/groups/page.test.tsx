import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { fetchStudyGroups, joinStudyGroup } = vi.hoisted(() => ({
  fetchStudyGroups: vi.fn(),
  joinStudyGroup: vi.fn(),
}));

vi.mock('@/lib/api/community', () => ({ fetchStudyGroups, joinStudyGroup }));
vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));

import GroupsPage from './page';

const group = (overrides: Partial<Record<string, unknown>>) => ({
  id: 'sg-1',
  name: 'Nursing study circle',
  description: 'Weekly role-play practice.',
  examTypeCode: 'oet',
  memberCount: 3,
  maxMembers: 20,
  createdAt: '2026-09-01T00:00:00Z',
  isJoined: false,
  ...overrides,
});

describe('Study groups page', () => {
  beforeEach(() => {
    fetchStudyGroups.mockReset();
    joinStudyGroup.mockReset();
  });

  it('lists groups from the { total, groups } response, with membership and capacity', async () => {
    fetchStudyGroups.mockResolvedValue({
      total: 3,
      groups: [
        group({}),
        group({ id: 'sg-2', name: 'Medicine writers', isJoined: true }),
        group({ id: 'sg-3', name: 'Full house', memberCount: 20 }),
      ],
    });

    render(<GroupsPage />);

    expect(await screen.findByRole('heading', { name: 'Nursing study circle' })).toBeInTheDocument();
    expect(screen.getByText('3 available')).toBeInTheDocument();
    expect(screen.getByText('Joined')).toBeInTheDocument();
    expect(screen.getByText('Full')).toBeInTheDocument();
    // Only the open, non-member group offers Join; nothing links to a missing detail route.
    expect(screen.getAllByRole('button', { name: 'Join group' })).toHaveLength(1);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('joins a group and reloads the list', async () => {
    fetchStudyGroups
      .mockResolvedValueOnce({ total: 1, groups: [group({})] })
      .mockResolvedValueOnce({ total: 1, groups: [group({ isJoined: true, memberCount: 4 })] });
    joinStudyGroup.mockResolvedValue({ joined: true });

    render(<GroupsPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Join group' }));

    expect(joinStudyGroup).toHaveBeenCalledWith('sg-1');
    expect(await screen.findByText('Joined')).toBeInTheDocument();
    expect(fetchStudyGroups).toHaveBeenCalledTimes(2);
  });

  it('shows a retryable error when the list fails to load', async () => {
    fetchStudyGroups.mockRejectedValueOnce(new Error('Network down')).mockResolvedValueOnce({ total: 0, groups: [] });

    render(<GroupsPage />);

    expect(await screen.findByText('Network down')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /retry/i }));
    await waitFor(() => expect(screen.getByText('No study groups yet')).toBeInTheDocument());
  });
});
