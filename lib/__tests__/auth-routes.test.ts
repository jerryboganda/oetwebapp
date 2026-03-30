import { describe, expect, it } from 'vitest';

import { defaultRouteForRole, resolveAuthenticatedDestination, resolvePostAuthDestination } from '../auth-routes';

describe('auth routes', () => {
  it('rejects scheme-relative next paths and falls back to the role default', () => {
    expect(resolvePostAuthDestination({ role: 'learner' } as never, '//evil.example.test')).toBe('/');
    expect(resolvePostAuthDestination({ role: 'expert' } as never, '//evil.example.test')).toBe('/expert');
    expect(resolvePostAuthDestination({ role: 'admin' } as never, '//evil.example.test')).toBe('/admin');
  });

  it('still accepts safe role-scoped destinations', () => {
    expect(resolvePostAuthDestination({ role: 'expert' } as never, '/expert/queue')).toBe('/expert/queue');
    expect(resolveAuthenticatedDestination({ role: 'learner', requiresMfa: false, isAuthenticatorEnabled: false } as never, '/reading')).toBe('/reading');
    expect(defaultRouteForRole('admin')).toBe('/admin');
  });
});