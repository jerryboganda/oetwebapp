import { defaultRouteForRole, resolveAuthenticatedDestination, resolvePostAuthDestination, roleSatisfiesRequired } from '../auth-routes';
import { appendAuthNextParam } from '../auth/routes';

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

  it.each([
    '/placement-test?utm_source=search&utm_campaign=general%20english',
    '/placement-test/attempts/session-123?intent=opaque-entry#resume',
  ])('preserves the complete placement destination %s', (destination) => {
    expect(resolveAuthenticatedDestination({ role: 'learner' } as never, destination)).toBe(destination);
    const signIn = new URL(appendAuthNextParam('/sign-in', destination), 'https://app.example.test');
    expect(signIn.searchParams.get('next')).toBe(destination);
  });

  it.each([
    '/\\evil.example.test',
    '/%2f%2fevil.example.test',
    '/%5cevil.example.test',
    '/%252f%252fevil.example.test',
    '/%2e%2e%2f%2fevil.example.test',
    '/\tevil.example.test',
    '/placement-test%0a',
    '/placement-test%ZZ',
  ])('rejects unsafe or malformed next paths in every auth link: %s', (destination) => {
    expect(resolveAuthenticatedDestination({ role: 'learner' } as never, destination)).toBe('/');
    expect(appendAuthNextParam('/sign-in', destination)).toBe('/sign-in');
  });

  it.each([
    '/placement-test/../admin/users',
    '/placement-test/%2e%2e/admin/users',
    '/%61dmin/users',
    '/%65xpert/queue',
    '/%73ponsor/learners',
  ])('applies role restrictions to the resolved path: %s', (destination) => {
    expect(resolveAuthenticatedDestination({ role: 'learner' } as never, destination)).toBe('/');
  });

  it('routes privileged users away from the learner root after sign-in', () => {
    expect(resolvePostAuthDestination({ role: 'admin' } as never, '/')).toBe('/admin');
    expect(resolvePostAuthDestination({ role: 'expert' } as never, '/')).toBe('/expert');
    expect(resolvePostAuthDestination({ role: 'sponsor' } as never, '/')).toBe('/support');
    expect(resolvePostAuthDestination({ role: 'learner' } as never, '/')).toBe('/');
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
