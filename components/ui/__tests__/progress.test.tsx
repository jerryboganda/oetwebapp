import { render, screen } from '@testing-library/react';
import { ProgressBar } from '../progress';

describe('ProgressBar', () => {
  it('uses ariaLabel when present without forcing duplicate visible text', () => {
    render(<ProgressBar value={62} ariaLabel="Writing readiness 62%" />);

    expect(screen.getByRole('progressbar', { name: 'Writing readiness 62%' })).toBeInTheDocument();
    expect(screen.queryByText('Writing readiness 62%')).not.toBeInTheDocument();
  });

  it('falls back to the visible label for the accessible name', () => {
    render(<ProgressBar value={82} label="Reading" />);

    expect(screen.getByRole('progressbar', { name: 'Reading' })).toBeInTheDocument();
    expect(screen.getByText('Reading')).toBeInTheDocument();
  });
});
