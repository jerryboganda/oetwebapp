import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockCreateReport } = vi.hoisted(() => ({
  mockCreateReport: vi.fn(),
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  createReadingAnswerKeyReport: (...args: unknown[]) => mockCreateReport(...args),
}));

import { ReportAnswerControl } from './report-answer-control';

describe('ReportAnswerControl', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCreateReport.mockResolvedValue({
      id: 'akr-1',
      questionId: 'q-1',
      status: 'open',
    });
  });

  it('requires a reason, submits the report, and becomes Reported', async () => {
    const user = userEvent.setup();
    render(
      <ReportAnswerControl
        assessment="reading"
        attemptId="attempt-1"
        questionId="q-1"
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Report this answer' }));
    expect(screen.getByText(/does not change your mark immediately/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Send report' }));
    expect(await screen.findByText(/choose a reason/i)).toBeInTheDocument();
    expect(mockCreateReport).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Official answer looks wrong' }));
    await user.type(screen.getByLabelText('More detail (optional)'), 'The booklet says B.');
    await user.click(screen.getByRole('button', { name: 'Send report' }));

    await waitFor(() => {
      expect(mockCreateReport).toHaveBeenCalledWith('attempt-1', {
        questionId: 'q-1',
        reasonCode: 'wrong_official_answer',
        details: 'The booklet says B.',
      });
    });

    expect(await screen.findByRole('button', { name: 'Reported' })).toBeDisabled();
    expect(screen.getByText(/report received/i)).toBeInTheDocument();
  });

  it('shows Reported when the question already has an open report', () => {
    render(
      <ReportAnswerControl
        assessment="reading"
        attemptId="attempt-1"
        questionId="q-1"
        alreadyReported
      />,
    );

    expect(screen.getByRole('button', { name: 'Reported' })).toBeDisabled();
  });
});
