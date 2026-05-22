import { render, screen } from '@testing-library/react';
import { ReadinessTrendChart } from '../readiness-trend-chart';

describe('ReadinessTrendChart', () => {
  it('renders empty-state message when no data', () => {
    render(<ReadinessTrendChart data={[]} />);
    expect(screen.getByText(/No history yet/i)).toBeInTheDocument();
  });

  it('renders empty-state message when data is undefined-like', () => {
    // Defensive null check — guards against runtime nulls from API failures.
    // @ts-expect-error testing defensive null handling
    render(<ReadinessTrendChart data={null} />);
    expect(screen.getByText(/No history yet/i)).toBeInTheDocument();
  });
});
