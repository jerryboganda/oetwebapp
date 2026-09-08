import { render, screen } from '@testing-library/react';

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

import SpeakingAssessmentCriteriaPage from './page';
import {
  SPEAKING_CLINICAL_CRITERIA,
  SPEAKING_LINGUISTIC_CRITERIA,
} from '@/lib/speaking-candidate-resources';

describe('Speaking Assessment Criteria page', () => {
  it('renders the official linguistic and clinical criteria from the PDF', () => {
    render(<SpeakingAssessmentCriteriaPage />);

    expect(screen.getByRole('heading', { name: 'Speaking Assessment Criteria' })).toBeInTheDocument();
    expect(SPEAKING_LINGUISTIC_CRITERIA).toHaveLength(4);
    expect(SPEAKING_CLINICAL_CRITERIA).toHaveLength(5);

    for (const criterion of SPEAKING_LINGUISTIC_CRITERIA) {
      expect(screen.getAllByText(criterion.name).length).toBeGreaterThanOrEqual(2);
    }
    for (const criterion of SPEAKING_CLINICAL_CRITERIA) {
      expect(screen.getAllByText(criterion.name).length).toBeGreaterThanOrEqual(2);
    }

    expect(screen.getByText(/Pronunciation is easily understood and prosodic features/i)).toBeInTheDocument();
    expect(screen.getAllByText(/Adept use/i).length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByText(/Banfield/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/promedicalenglish/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/rulebook/i)).not.toBeInTheDocument();
    expect(screen.getByText('Back to Speaking').closest('a')).toHaveAttribute('href', '/speaking');
  });
});
