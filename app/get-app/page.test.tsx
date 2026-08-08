import { render, screen } from '@testing-library/react';
import type { ComponentProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import GetAppPage from './page';

vi.mock('next/image', () => ({
  default: (props: ComponentProps<'img'>) => <img alt="" {...props} />,
}));

describe('GetAppPage download badges', () => {
  it('keeps all platform badges aligned and points iOS at the future release resolver', () => {
    render(<GetAppPage />);

    const badges = [
      screen.getByRole('link', { name: 'Download the OET app for Windows' }),
      screen.getByRole('link', { name: 'Download the OET app for Mac' }),
      screen.getByRole('link', { name: 'Get the OET app on Google Play' }),
      screen.getByRole('link', { name: 'Download the OET iOS app for iPhone and iPad' }),
    ];

    for (const badge of badges) {
      expect(badge).toHaveClass('w-full', 'max-w-[220px]', 'justify-center');
    }
    expect(badges[3]).toHaveAttribute('href', '/api/download/ios');
  });
});
