import { render, screen } from '@testing-library/react';

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

import SpeakingIntroQuestionsPage from './page';
import { SPEAKING_INTRO_QUESTIONS } from '@/lib/speaking-candidate-resources';

describe('Speaking Intro Questions page', () => {
  it('renders all 12 global questions with sample answers underneath', () => {
    render(<SpeakingIntroQuestionsPage />);

    expect(screen.getByRole('heading', { name: 'Speaking Intro Questions' })).toBeInTheDocument();
    expect(SPEAKING_INTRO_QUESTIONS).toHaveLength(12);

    // Every question is shown…
    for (const q of SPEAKING_INTRO_QUESTIONS) {
      expect(screen.getByText(q.question, { exact: false })).toBeInTheDocument();
    }

    // …with its sample answer directly beneath (12 labels) and highlighted placeholders.
    expect(screen.getAllByText(/Sample answer — personalise the bracketed details/i)).toHaveLength(12);
    expect(screen.getByText(/My name is/i)).toBeInTheDocument();
    expect(
      screen.getByText('What does a typical working day look like for you?', { exact: false }),
    ).toBeInTheDocument();

    // Profession-neutral: no Medicine/Australia fixed content.
    expect(screen.queryByText(/Australian medical council/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/practicing medicine in Australia/i)).not.toBeInTheDocument();

    // Candidate rule: templates to personalise, not to memorise.
    expect(screen.getByText(/Candidate rule/i)).toBeInTheDocument();
    expect(screen.getByText(/Do not memorise them word-for-word/i)).toBeInTheDocument();
  });
});
