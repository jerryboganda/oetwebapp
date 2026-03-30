import { createContext } from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

const authProviderSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-provider">{children}</div>);
const authGuardSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-guard">{children}</div>);

vi.mock('@/contexts/auth-context', () => ({
  AuthContext: createContext(null),
  AuthProvider: (props: { children: React.ReactNode }) => authProviderSpy(props),
}));

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
  it('wraps protected shells in AuthProvider and forwards requiredRole to AuthGuard', () => {
    render(
      <AppShell requiredRole="admin">
        <div>Admin console</div>
      </AppShell>,
    );

    expect(screen.getByTestId('auth-provider')).toBeInTheDocument();
    expect(screen.getByTestId('auth-guard')).toBeInTheDocument();
    expect(authProviderSpy).toHaveBeenCalled();
    expect(authGuardSpy).toHaveBeenCalled();
    expect(authGuardSpy.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ requiredRole: 'admin' }));
  });
});
