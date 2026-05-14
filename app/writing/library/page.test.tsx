import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const fetchWritingTasksMock = vi.fn();
const analyticsTrackMock = vi.fn();

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionItem: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/lib/api', () => ({
  fetchWritingTasks: (...args: unknown[]) => fetchWritingTasksMock(...args),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: (...args: unknown[]) => analyticsTrackMock(...args),
  },
}));

import WritingTaskLibrary from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('WritingTaskLibrary', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('carries selected exam mode and assessor into exam starts', async () => {
    const push = vi.fn();
    fetchWritingTasksMock.mockResolvedValue([
      {
        id: 'task-paper-1',
        title: 'Referral Letter Practice',
        profession: 'Nursing',
        scenarioType: 'Referral',
        letterType: 'Referral',
        difficulty: 'Medium',
        time: '45 min',
        criteriaFocus: 'Purpose',
      },
    ]);

    renderWithRouter(<WritingTaskLibrary />, { router: { push } });

    await waitFor(() => expect(screen.getByText('Referral Letter Practice')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: /^paper$/i }));
    await userEvent.click(screen.getByRole('button', { name: /dr\. ahmed/i }));
    await userEvent.click(screen.getByRole('button', { name: /start exam/i }));

    expect(push).toHaveBeenCalledWith('/writing/player?taskId=task-paper-1&mode=exam&examMode=paper&assessor=instructor');
    expect(analyticsTrackMock).toHaveBeenCalledWith('task_started', {
      taskId: 'task-paper-1',
      subtest: 'writing',
      mode: 'exam',
      examMode: 'paper',
      assessorType: 'instructor',
    });
  });
});