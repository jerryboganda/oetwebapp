import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

const authGuardSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-guard">{children}</div>);

vi.mock('@/components/auth/auth-guard', () => ({
  AuthGuard: (props: { children: React.ReactNode; requiredRole?: 'learner' | 'expert' | 'admin' }) => authGuardSpy(props),
}));

vi.mock('../top-nav', () => ({
  TopNav: () => <div data-testid="top-nav" />,
}));

vi.mock('../sidebar', () => ({
  Sidebar: () => <div data-testid="sidebar" />,
  BottomNav: () => <div data-testid="bottom-nav" />,
}));

import { AppShell } from '../app-shell';

describe('AppShell', () => {
  it('forwards requiredRole to AuthGuard so privileged pages do not mount for the wrong role', () => {
    render(
      <AppShell requiredRole="admin">
        <div>Admin console</div>
      </AppShell>,
    );

    expect(screen.getByTestId('auth-guard')).toBeInTheDocument();
    expect(authGuardSpy).toHaveBeenCalled();
    expect(authGuardSpy.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ requiredRole: 'admin' }));
  });
});
