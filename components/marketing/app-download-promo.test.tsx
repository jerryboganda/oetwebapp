import { render, screen } from '@testing-library/react';
import { AppDownloadPromo } from './app-download-promo';

describe('AppDownloadPromo', () => {
  it('renders a compact, actionable responsive strip', () => {
    render(<AppDownloadPromo variant="banner" />);

    const strip = screen.getByRole('region', { name: /official OET apps/i });
    expect(strip).toBeInTheDocument();
    expect(screen.getByText(/Keep your account in sync/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Windows & Mac/i })).toHaveAttribute('href', '/get-app');
    expect(screen.getByRole('link', { name: /Google Play/i })).toHaveAttribute('href', '/get-app/android-install');
    expect(screen.getByRole('link', { name: /iPhone and iPad/i })).toHaveAttribute('href', '/get-app');
    expect(screen.queryByText(/Download Official Apps for Video Access/i)).not.toBeInTheDocument();
  });
});
