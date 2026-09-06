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

  it('shows the two native candidate resources (criteria + intro questions)', async () => {
    render(<SpeakingTaskSelection />);

    expect(await screen.findByText('Prepare for your OET Speaking')).toBeInTheDocument();
    expect(
      screen.getByText('Review the assessment criteria and the common introductory questions used across professions.'),
    ).toBeInTheDocument();

    expect(screen.getByText('Speaking Assessment Criteria')).toBeInTheDocument();
    expect(screen.getByText('Speaking Intro Questions')).toBeInTheDocument();

    expect(screen.getByRole('link', { name: /Open Assessment Criteria/i })).toHaveAttribute(
      'href',
      '/speaking/assessment-criteria',
    );
    expect(screen.getByRole('link', { name: /Open Intro Questions/i })).toHaveAttribute(
      'href',
      '/speaking/intro-questions',
    );
  });

  it('exposes no internal rulebook surface in the resource block', async () => {
    render(<SpeakingTaskSelection />);

    await screen.findByText('Prepare for your OET Speaking');

    expect(screen.queryByText(/rulebook/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Open Speaking Rules')).not.toBeInTheDocument();
    expect(screen.queryByText('Breaking Bad News')).not.toBeInTheDocument();
    expect(screen.queryByText('See the exact rules behind your speaking feedback')).not.toBeInTheDocument();
    expect(screen.queryByText('Speaking criteria')).not.toBeInTheDocument();
    expect(screen.queryByText('Breaking bad news')).not.toBeInTheDocument();
  });
});
