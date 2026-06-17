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

  it('adds a per-set breakdown to the tooltip when occurrences are provided', () => {
    render(<RecallTierBadge count={7} occurrences={{ old: 2, '2023-2025': 3, '2026': 2 }} />);
    const badge = screen.getByText('7x');
    // Sorted by count desc, then code asc (digits before letters).
    expect(badge).toHaveAttribute(
      'title',
      'Appeared 7 times across recall exams — 2023-2025 ×3 · 2026 ×2 · old ×2',
    );
  });

  it('maps set codes to labels in the tooltip when provided', () => {
    render(
      <RecallTierBadge
        count={5}
        occurrences={{ '2026': 3, old: 2 }}
        setLabels={{ '2026': 'Recalls 2026', old: 'Legacy' }}
      />,
    );
    const badge = screen.getByText('5x');
    expect(badge).toHaveAttribute(
      'title',
      'Appeared 5 times across recall exams — Recalls 2026 ×3 · Legacy ×2',
    );
  });
});
