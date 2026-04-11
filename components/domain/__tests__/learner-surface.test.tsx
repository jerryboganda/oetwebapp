import { render, screen } from '@testing-library/react';
import { Clock, Sparkles } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceMetaRow } from '../learner-surface';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

describe('LearnerSurfaceMetaRow', () => {
  it('does not render an empty metadata row', () => {
    const { container } = render(<LearnerSurfaceMetaRow items={[{ label: '   ' }]} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('renders meaningful metadata labels', () => {
    render(<LearnerSurfaceMetaRow items={[{ icon: Clock, label: '45 mins' }, { label: 'Timed flow' }]} />);

    expect(screen.getByText('45 mins')).toBeInTheDocument();
    expect(screen.getByText('Timed flow')).toBeInTheDocument();
  });
});

describe('LearnerSurfaceCard', () => {
  it('renders title, description, and primary action while skipping blank metadata', () => {
    const onClick = vi.fn();
    const card: LearnerSurfaceCardModel = {
      kind: 'navigation',
      sourceType: 'frontend_navigation',
      accent: 'primary',
      eyebrow: 'Exam Simulation',
      eyebrowIcon: Sparkles,
      title: 'Full Writing Mock Test',
      description: 'Enter a timed mock flow with a clearer explanation of what happens next.',
      metaItems: [{ label: '   ' }, { icon: Clock, label: 'Timed flow' }],
      primaryAction: {
        label: 'Enter Mock Flow',
        onClick,
      },
    };

    const { container } = render(<LearnerSurfaceCard card={card} />);

    expect(screen.getByText('Full Writing Mock Test')).toBeInTheDocument();
    expect(screen.getByText('Timed flow')).toBeInTheDocument();
    expect(screen.queryByText('   ')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Enter Mock Flow/i })).toBeInTheDocument();
    expect(container.firstChild).not.toHaveClass('before:absolute');
  });
});

describe('LearnerPageHero', () => {
  it('renders compact highlights with label and value pairs', () => {
    render(
      <LearnerPageHero
        eyebrow="Learner Workspace"
        icon={Sparkles}
        title="Focus on today"
        description="See the next actions and evidence that matter right now."
        highlights={[
          { label: 'Exam target', value: '2026-06-27' },
          { label: 'Pending reviews', value: '2 in progress' },
        ]}
      />,
    );

    expect(screen.getByText('Focus on today')).toBeInTheDocument();
    expect(screen.getByText('Exam target')).toBeInTheDocument();
    expect(screen.getByText('2026-06-27')).toBeInTheDocument();
    expect(screen.getByText('Pending reviews')).toBeInTheDocument();
    expect(screen.getByText('2 in progress')).toBeInTheDocument();
  });

  it('does not render highlight chrome when no highlights are provided', () => {
    render(
      <LearnerPageHero
        eyebrow="Learner Workspace"
        icon={Sparkles}
        title="Focus on today"
        description="See the next actions and evidence that matter right now."
      />,
    );

    expect(screen.queryByText('Exam target')).not.toBeInTheDocument();
  });
});
