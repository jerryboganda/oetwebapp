import { describe, it, expect } from 'vitest';
import { AUTH_ROUTES, getAuthFlowLinks, type AuthScreenKey } from './routes';

describe('AUTH_ROUTES', () => {
  it('exposes the canonical route paths', () => {
    expect(AUTH_ROUTES).toEqual({
      signIn: '/sign-in',
      signUp: '/register',
      signUpSuccess: '/register/success',
      passwordReset: '/forgot-password',
      passwordResetOtp: '/forgot-password/verify',
      passwordCreate: '/reset-password',
      passwordResetSuccess: '/reset-password/success',
      twoStepVerification: '/verify-email',
      terms: '/terms',
    });
  });
});

describe('getAuthFlowLinks', () => {
  const expected: Record<AuthScreenKey, { primary: string; secondary: string }> = {
    signIn: { primary: AUTH_ROUTES.signUp, secondary: AUTH_ROUTES.passwordReset },
    signUp: { primary: AUTH_ROUTES.signIn, secondary: AUTH_ROUTES.twoStepVerification },
    passwordReset: { primary: AUTH_ROUTES.signIn, secondary: AUTH_ROUTES.passwordResetOtp },
    passwordResetOtp: {
      primary: AUTH_ROUTES.passwordReset,
      secondary: AUTH_ROUTES.passwordCreate,
    },
    passwordCreate: {
      primary: AUTH_ROUTES.signIn,
      secondary: AUTH_ROUTES.passwordResetSuccess,
    },
    twoStepVerification: { primary: AUTH_ROUTES.signIn, secondary: AUTH_ROUTES.signUp },
  };

  it.each(Object.keys(expected) as AuthScreenKey[])(
    'returns the documented primary/secondary for %s',
    (screen) => {
      expect(getAuthFlowLinks(screen)).toEqual(expected[screen]);
    },
  );

  it('always returns paths that exist in AUTH_ROUTES', () => {
    const valid = new Set(Object.values(AUTH_ROUTES));
    for (const screen of Object.keys(expected) as AuthScreenKey[]) {
      const links = getAuthFlowLinks(screen);
      expect(valid.has(links.primary)).toBe(true);
      expect(valid.has(links.secondary)).toBe(true);
    }
  });
});
