import { describe, it, expect, vi, beforeEach } from 'vitest';
import { NextRequest } from 'next/server';

// Must import after mocking if needed
import { middleware, config } from './middleware';

function createRequest(pathname: string, cookies?: Record<string, string>): NextRequest {
  const url = new URL(pathname, 'http://localhost:3000');
  const request = new NextRequest(url);
  if (cookies) {
    for (const [name, value] of Object.entries(cookies)) {
      request.cookies.set(name, value);
    }
  }
  return request;
}

describe('middleware', () => {
  describe('unauthenticated access to protected routes', () => {
    it('redirects /dashboard to /sign-in?next=/dashboard', () => {
      const request = createRequest('/dashboard');
      const response = middleware(request);

      expect(response.status).toBe(307);
      const location = new URL(response.headers.get('location')!);
      expect(location.pathname).toBe('/sign-in');
      expect(location.searchParams.get('next')).toBe('/dashboard');
    });

    it('redirects /expert/cases to /sign-in?next=/expert/cases', () => {
      const request = createRequest('/expert/cases');
      const response = middleware(request);

      expect(response.status).toBe(307);
      const location = new URL(response.headers.get('location')!);
      expect(location.pathname).toBe('/sign-in');
      expect(location.searchParams.get('next')).toBe('/expert/cases');
    });

    it('redirects /admin/users to /sign-in?next=/admin/users', () => {
      const request = createRequest('/admin/users');
      const response = middleware(request);

      expect(response.status).toBe(307);
      const location = new URL(response.headers.get('location')!);
      expect(location.pathname).toBe('/sign-in');
      expect(location.searchParams.get('next')).toBe('/admin/users');
    });
  });

  describe('authenticated access to protected routes', () => {
    it('allows /dashboard when auth cookie is present', () => {
      const request = createRequest('/dashboard', { oet_auth: '1' });
      const response = middleware(request);

      expect(response.status).toBe(200);
      expect(response.headers.get('location')).toBeNull();
    });

    it('allows /expert/cases when auth cookie is present', () => {
      const request = createRequest('/expert/cases', { oet_auth: '1' });
      const response = middleware(request);

      expect(response.status).toBe(200);
    });
  });

  describe('public auth routes pass through without cookie check', () => {
    const publicRoutes = [
      '/sign-in',
      '/register',
      '/register/success',
      '/terms',
      '/forgot-password',
      '/forgot-password/verify',
      '/reset-password',
      '/reset-password/success',
      '/verify-email',
      '/mfa/challenge',
      '/mfa/setup',
      '/mfa/recovery',
      '/auth/callback',
    ];

    for (const route of publicRoutes) {
      it(`allows ${route} without auth cookie`, () => {
        const request = createRequest(route);
        const response = middleware(request);

        expect(response.status).toBe(200);
        expect(response.headers.get('location')).toBeNull();
      });
    }

    it('allows /auth/callback/google without auth cookie (prefix match)', () => {
      const request = createRequest('/auth/callback/google');
      const response = middleware(request);

      expect(response.status).toBe(200);
      expect(response.headers.get('location')).toBeNull();
    });
  });

  describe('matcher config', () => {
    const matcherPattern = config.matcher[0];

    it('has a matcher defined', () => {
      expect(config.matcher).toBeDefined();
      expect(config.matcher.length).toBeGreaterThan(0);
    });

    // Next.js matcher patterns use path-to-regexp internally, not raw RegExp.
    // We validate the negative lookahead structure to ensure exclusions are present.
    it('excludes /api routes via negative lookahead', () => {
      expect(matcherPattern).toContain('(?!api');
    });

    it('excludes /_next/static routes via negative lookahead', () => {
      expect(matcherPattern).toContain('_next/static');
    });

    it('excludes /_next/image routes via negative lookahead', () => {
      expect(matcherPattern).toContain('_next/image');
    });

    it('excludes static asset extensions via negative lookahead', () => {
      expect(matcherPattern).toContain('svg');
      expect(matcherPattern).toContain('png');
      expect(matcherPattern).toContain('jpg');
      expect(matcherPattern).toContain('ico');
      expect(matcherPattern).toContain('woff2');
    });

    it('excludes favicon.ico via negative lookahead', () => {
      expect(matcherPattern).toContain('favicon');
    });
  });

  describe('no JWT parsing or role checks', () => {
    it('does not inspect cookie value beyond existence check', () => {
      const request = createRequest('/dashboard', { oet_auth: 'any-value' });
      const response = middleware(request);

      expect(response.status).toBe(200);
      expect(response.headers.get('location')).toBeNull();
    });
  });
});
