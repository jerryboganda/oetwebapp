import { createContext } from 'react';
import { screen } from '@testing-library/react';
const authProviderSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-provider">{children}</div>);
const authGuardSpy = vi.fn(({ children }: { children: React.ReactNode }) => <div data-testid="auth-guard">{children}</div>);
const topNavSpy = vi.fn(({ children }: { children?: React.ReactNode }) => <div data-testid="top-nav">{children}</div>);
const useAuthSpy = vi.fn(() => ({
  isAuthenticated: false,
  loading: false,
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthContext: createContext(null),
  AuthProvider: (props: { children: React.ReactNode }) => authProviderSpy(props),
  useAuth: () => useAuthSpy(),
}));

vi.mock('@/components/auth/auth-guard', () => ({
  AuthGuard: (props: { children: React.ReactNode; requiredRole?: 'learner' | 'expert' | 'admin' }) => authGuardSpy(props),
}));

vi.mock('@/components/layout/top-nav', () => ({
  TopNav: (props: { children?: React.ReactNode }) => topNavSpy(props),
}));

vi.mock('@/components/layout/sidebar', () => ({
  Sidebar: () => <div data-testid="sidebar" />,
  BottomNav: () => <div data-testid="bottom-nav" />,
}));

import { AppShell } from '../app-shell';
import { renderWithRouter } from '@/tests/test-utils';

describe('AppShell', () => {
  it('wraps protected shells in AuthProvider and forwards requiredRole to AuthGuard', () => {
    renderWithRouter(
      <AppShell
        requiredRole="admin"
        mobileMenuSections={[
          {
            label: 'Practice',
            items: [{ href: '/', label: 'Dashboard', icon: <span aria-hidden="true" /> }],
          },
        ]}
      >
        <div>Admin console</div>
      </AppShell>,
    );

    expect(screen.getByTestId('auth-provider')).toBeInTheDocument();
    expect(screen.getByTestId('auth-guard')).toBeInTheDocument();
    expect(authProviderSpy).toHaveBeenCalled();
    expect(authGuardSpy).toHaveBeenCalled();
    expect(authGuardSpy.mock.calls[0]?.[0]).toEqual(expect.objectContaining({ requiredRole: 'admin' }));
    expect(topNavSpy.mock.calls.some(([props]: [Record<string, unknown>]) => Array.isArray(props.sectionedItems) && (props.sectionedItems as unknown[]).length === 1)).toBe(true);
  });
});
