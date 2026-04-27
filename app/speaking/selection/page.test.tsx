import { render, screen } from '@testing-library/react';

const { mockFetchSpeakingTasks, mockTrack } = vi.hoisted(() => ({
  mockFetchSpeakingTasks: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/domain/task-card', () => ({
  TaskCard: ({ title }: { title: string }) => <div>{title}</div>,
}));

vi.mock('@/lib/api', () => ({
  fetchSpeakingTasks: mockFetchSpeakingTasks,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

import SpeakingTaskSelection from './page';

describe('Speaking selection page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchSpeakingTasks.mockResolvedValue([
      {
        id: 'sp-1',
        title: 'Breaking Bad News - Cancer Diagnosis',
        profession: 'Medicine',
        duration: '20 mins',
        difficulty: 'Medium',
        criteriaFocus: 'appropriateness',
        scenarioType: 'Role play',
      },
    ]);
  });

  it('shows speaking rulebook entry points on the selection surface', async () => {
    render(<SpeakingTaskSelection />);

    // Wait for the LearnerSurfaceCard heading to render. Use a heading role
    // matcher so we don't catch substrings inside meta items / description.
    await screen.findByRole('heading', { level: 3, name: /speaking feedback/i });
    expect(screen.getByRole('link', { name: /Speaking rules/i })).toHaveAttribute('href', '/speaking/rulebook');
    expect(screen.getByRole('link', { name: /Breaking bad news/i })).toHaveAttribute('href', '/speaking/rulebook/RULE_44');
  });
});
