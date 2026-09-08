import { render, screen } from '@testing-library/react';
import { NotificationCenter } from '../notification-center';

describe('NotificationCenter without NotificationCenterProvider', () => {
  it('renders nothing instead of throwing when the workspace provider is absent', () => {
    expect(() => render(<NotificationCenter />)).not.toThrow();
    expect(screen.queryByRole('button', { name: /notifications/i })).not.toBeInTheDocument();
  });
});
