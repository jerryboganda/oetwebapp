import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SpeakingSimulationV11FeedbackCard } from './SpeakingSimulationV11FeedbackCard';
import { submitSpeakingSimulationV11Feedback } from '@/lib/api/speaking-simulation-v11';

vi.mock('@/lib/api/speaking-simulation-v11', () => ({
  submitSpeakingSimulationV11Feedback: vi.fn(),
}));

describe('SpeakingSimulationV11FeedbackCard', () => {
  beforeEach(() => vi.clearAllMocks());

  it('requires a rating and submits trimmed feedback for the session', async () => {
    const user = userEvent.setup();
    render(<SpeakingSimulationV11FeedbackCard sessionId="session-1" />);
    expect(screen.getByRole('button', { name: 'Submit feedback' })).toBeDisabled();
    await user.click(screen.getByRole('radio', { name: '4 stars' }));
    await user.type(screen.getByLabelText('Comments (optional)'), '  Helpful practice.  ');
    await user.click(screen.getByRole('button', { name: 'Submit feedback' }));
    await waitFor(() => expect(submitSpeakingSimulationV11Feedback)
      .toHaveBeenCalledWith('session-1', 4, 'Helpful practice.'));
    expect(await screen.findByText('Thanks. Your feedback was saved.')).toBeVisible();
  });

  it('supports keyboard rating selection and retry after a failed request', async () => {
    vi.mocked(submitSpeakingSimulationV11Feedback).mockRejectedValueOnce(new Error('offline'));
    const user = userEvent.setup();
    render(<SpeakingSimulationV11FeedbackCard sessionId="session-2" />);
    await user.click(screen.getByRole('radio', { name: '3 stars' }));
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('radio', { name: '4 stars' })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Submit feedback' }));
    expect(await screen.findByText('Could not submit feedback.')).toBeVisible();
    await user.click(screen.getByRole('button', { name: 'Submit feedback' }));
    expect(await screen.findByText('Thanks. Your feedback was saved.')).toBeVisible();
    expect(submitSpeakingSimulationV11Feedback).toHaveBeenLastCalledWith('session-2', 4, null);
  });
});
