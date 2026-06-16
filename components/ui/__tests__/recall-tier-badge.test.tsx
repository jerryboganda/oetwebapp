import { render, screen } from '@testing-library/react';
import { RecallTierBadge } from '@/components/ui/badge';

describe('RecallTierBadge', () => {
  it('renders nothing below 2', () => {
    const { container: c0 } = render(<RecallTierBadge count={0} />);
    expect(c0).toBeEmptyDOMElement();
    const { container: c1 } = render(<RecallTierBadge count={1} />);
    expect(c1).toBeEmptyDOMElement();
  });

  it('renders a calm 2x tier', () => {
    render(<RecallTierBadge count={2} />);
    const badge = screen.getByText('2x');
    expect(badge).toBeInTheDocument();
    expect(badge.className).toContain('bg-sky-100');
    expect(badge).toHaveAttribute('title', 'Appeared 2 times across recall exams');
  });

  it('renders an elevated 3x tier with a ring', () => {
    render(<RecallTierBadge count={3} />);
    const badge = screen.getByText('3x');
    expect(badge.className).toContain('bg-violet-100');
    expect(badge.className).toContain('ring-1');
  });

  it('renders a top-tier 4x with a gradient and sparkle icon', () => {
    const { container } = render(<RecallTierBadge count={4} />);
    const badge = screen.getByText('4x');
    expect(badge.className).toContain('bg-gradient-to-r');
    // lucide renders an <svg> for the sparkle icon at the top tier
    expect(container.querySelector('svg')).not.toBeNull();
  });

  it('treats counts above 4 as top-tier', () => {
    render(<RecallTierBadge count={7} />);
    const badge = screen.getByText('7x');
    expect(badge.className).toContain('bg-gradient-to-r');
  });
});
