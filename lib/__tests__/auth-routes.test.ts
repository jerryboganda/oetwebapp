import { defaultRouteForRole, resolveAuthenticatedDestination, resolvePostAuthDestination, roleSatisfiesRequired } from '../auth-routes';

describe('auth routes', () => {
  beforeEach(() => {
    delete process.env.NEXT_PUBLIC_SPONSOR_PORTAL_ENABLED;
  });

  it('rejects scheme-relative next paths and falls back to the role default', () => {
    expect(resolvePostAuthDestination({ role: 'learner' } as never, '//evil.example.test')).toBe('/');
    expect(resolvePostAuthDestination({ role: 'expert' } as never, '//evil.example.test')).toBe('/expert');
    expect(resolvePostAuthDestination({ role: 'admin' } as never, '//evil.example.test')).toBe('/admin');
    expect(resolvePostAuthDestination({ role: 'sponsor' } as never, '//evil.example.test')).toBe('/support');
  });

  it('still accepts safe role-scoped destinations', () => {
    expect(resolvePostAuthDestination({ role: 'expert' } as never, '/expert/queue')).toBe('/expert/queue');
    expect(resolveAuthenticatedDestination({ role: 'learner', requiresMfa: false, isAuthenticatorEnabled: false } as never, '/reading')).toBe('/reading');
    expect(defaultRouteForRole('admin')).toBe('/admin');
    expect(defaultRouteForRole('sponsor')).toBe('/support');
  });

  it('does not force privileged users into MFA setup when the authenticator is not enrolled yet', () => {
    expect(resolveAuthenticatedDestination({
      role: 'expert',
      requiresMfa: true,
      isAuthenticatorEnabled: false,
    } as never, null)).toBe('/expert');
    expect(resolveAuthenticatedDestination({
      role: 'admin',
      requiresMfa: true,
      isAuthenticatorEnabled: false,
    } as never, '/admin/users')).toBe('/admin/users');
  });

  describe('roleSatisfiesRequired', () => {
    it('returns true for exact role match', () => {
      expect(roleSatisfiesRequired('learner', 'learner')).toBe(true);
      expect(roleSatisfiesRequired('admin', 'admin')).toBe(true);
      expect(roleSatisfiesRequired('expert', 'expert')).toBe(true);
    });

    it('allows admin to access learner-required pages (dual-role support)', () => {
      expect(roleSatisfiesRequired('admin', 'learner')).toBe(true);
    });

    it('does not allow other cross-role access', () => {
      expect(roleSatisfiesRequired('learner', 'admin')).toBe(false);
      expect(roleSatisfiesRequired('learner', 'expert')).toBe(false);
      expect(roleSatisfiesRequired('expert', 'admin')).toBe(false);
      expect(roleSatisfiesRequired('expert', 'learner')).toBe(false);
    });

    it('returns false for null role', () => {
      expect(roleSatisfiesRequired(null, 'learner')).toBe(false);
      expect(roleSatisfiesRequired(null, 'admin')).toBe(false);
    });
  });

  it('allows admin to access learner paths via roleCanAccessPath', () => {
    expect(resolvePostAuthDestination({ role: 'admin' } as never, '/listening')).toBe('/listening');
    expect(resolvePostAuthDestination({ role: 'admin' } as never, '/reading')).toBe('/reading');
    expect(resolvePostAuthDestination({ role: 'admin' } as never, '/dashboard')).toBe('/dashboard');
  });
});
