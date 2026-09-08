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
  it('renders the 11 question set with sample answers and do-not-memorise guidance', () => {
    render(<SpeakingIntroQuestionsPage />);

    expect(screen.getByRole('heading', { name: 'Speaking Intro Questions' })).toBeInTheDocument();
    expect(SPEAKING_INTRO_QUESTIONS).toHaveLength(11);

    for (const q of SPEAKING_INTRO_QUESTIONS) {
      expect(screen.getByText(q.question, { exact: false })).toBeInTheDocument();
    }

    expect(screen.getAllByText(/Sample answer .* personalise/i)).toHaveLength(11);
    expect(screen.getByText(/My name is/i)).toBeInTheDocument();
    expect(screen.getByText('(your first name + last name)')).toBeInTheDocument();
    expect(screen.getByText(/Australian medical council registration process/i)).toBeInTheDocument();

    expect(screen.getByText(/Candidate rule/i)).toBeInTheDocument();
    expect(screen.getByText(/not memorise word-for-word/i)).toBeInTheDocument();
    expect(screen.getByText('Back to Speaking').closest('a')).toHaveAttribute('href', '/speaking');
  });
});
