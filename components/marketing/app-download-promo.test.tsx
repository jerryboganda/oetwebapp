import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AppDownloadPromo } from './app-download-promo';

const platformLabels = [
  'Download the OET app for Windows',
  'Download the OET app for Mac',
  'Get the OET app on Google Play',
  'Download the OET app on the App Store',
];

describe('AppDownloadPromo', () => {
  it.each(['banner', 'card', 'modal'] as const)('renders four equal platform badges in the %s variant', (variant) => {
    render(<AppDownloadPromo variant={variant} onClose={variant === 'modal' ? () => undefined : undefined} />);

    const expectedHrefs = ['/get-app', '/get-app', '/get-app/android-install', '/api/download/ios'];
    platformLabels.forEach((label, index) => {
      const link = screen.getByRole('link', { name: label });
      expect(link).toHaveAttribute('href', expectedHrefs[index]);
      expect(link).toHaveClass(variant === 'banner' ? 'h-16' : 'h-20', 'w-full', 'rounded-2xl');
    });

    expect(screen.getAllByRole('link')).toHaveLength(4);
    expect(screen.queryByText('Download for')).not.toBeInTheDocument();
    expect(screen.queryByText('Download directly')).not.toBeInTheDocument();
    expect(screen.queryByText('iOS app')).not.toBeInTheDocument();
    expect(screen.queryByText('Windows & Mac')).not.toBeInTheDocument();
  });
});
