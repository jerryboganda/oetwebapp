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
      screen.getByRole('link', { name: 'Download the OET app on the App Store' }),
    ];

    const expectedHrefs = ['/api/download/windows', '/api/download/mac', '/get-app/android-install', '/api/download/ios'];
    badges.forEach((badge, index) => {
      expect(badge).toHaveClass('w-full', 'max-w-[220px]', 'justify-center');
      expect(badge).toHaveClass('h-20', 'rounded-2xl');
      expect(badge).toHaveAttribute('href', expectedHrefs[index]);
    });
  });
});
