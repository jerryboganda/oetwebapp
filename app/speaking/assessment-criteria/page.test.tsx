import { render, screen } from '@testing-library/react';

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

import SpeakingAssessmentCriteriaPage from './page';
import { SPEAKING_CRITERIA } from '@/lib/speaking-candidate-resources';

describe('Speaking Assessment Criteria page', () => {
  it('renders all 9 criteria with weights, natively (no PDF)', () => {
    render(<SpeakingAssessmentCriteriaPage />);

    expect(screen.getByRole('heading', { name: 'Speaking Assessment Criteria' })).toBeInTheDocument();
    expect(SPEAKING_CRITERIA).toHaveLength(9);

    for (const c of SPEAKING_CRITERIA) {
      expect(screen.getByText(c.name, { exact: false })).toBeInTheDocument();
    }
    expect(screen.getAllByText('6 points').length).toBeGreaterThanOrEqual(4);
    expect(screen.getAllByText('3 points').length).toBeGreaterThanOrEqual(5);

    expect(screen.queryByText(/Banfield/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/promedicalenglish/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/rulebook/i)).not.toBeInTheDocument();
  });
});
