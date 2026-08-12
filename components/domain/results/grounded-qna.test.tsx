import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockAskPassage, mockAskListening } = vi.hoisted(() => ({
  mockAskPassage: vi.fn(),
  mockAskListening: vi.fn(),
}));

vi.mock('@/lib/reading-pathway-api', () => ({
  askAiAboutPassage: mockAskPassage,
}));

vi.mock('@/lib/listening-api', () => ({
  askListeningQuestionGroundedAi: mockAskListening,
}));

import { GroundedReadingPassageQna } from './grounded-reading-passage-qna';
import { GroundedListeningQuestionQna } from './grounded-listening-question-qna';

describe('grounded result Q&A panels', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the grounded Reading reply and advisory disclosure', async () => {
    mockAskPassage.mockResolvedValue({
      reply: 'The passage states that early review reduces avoidable delays.',
      history: [
        { role: 'user', content: 'What is the main point?' },
        { role: 'assistant', content: 'The passage states that early review reduces avoidable delays.' },
      ],
      grounded: true,
      advisoryOnly: true,
      marksUnaffected: true,
    });

    const user = userEvent.setup();
    render(<GroundedReadingPassageQna attemptId="attempt-1" passageId="passage-1" />);
    await user.type(screen.getByRole('textbox'), 'What is the main point?');
    await user.click(screen.getByRole('button', { name: 'Ask AI' }));

    expect((await screen.findAllByText('The passage states that early review reduces avoidable delays.')).length).toBe(1);
    expect(screen.getByText(/marks are unaffected/i)).toBeInTheDocument();
  });

  it('renders the grounded Listening reply and advisory disclosure', async () => {
    mockAskListening.mockResolvedValue({
      reply: 'The stored transcript supports option B.',
      history: [
        { role: 'user', content: 'Why is B correct?' },
        { role: 'assistant', content: 'The stored transcript supports option B.' },
      ],
      grounded: true,
      advisoryOnly: true,
      marksUnaffected: true,
    });

    const user = userEvent.setup();
    render(<GroundedListeningQuestionQna attemptId="attempt-1" questionId="question-1" />);
    await user.type(screen.getByRole('textbox'), 'Why is B correct?');
    await user.click(screen.getByRole('button', { name: 'Ask AI' }));

    expect((await screen.findAllByText('The stored transcript supports option B.')).length).toBe(1);
    expect(screen.getByText(/marks are unaffected/i)).toBeInTheDocument();
  });
});
