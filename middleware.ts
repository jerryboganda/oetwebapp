import { NextRequest, NextResponse } from 'next/server';

// Cookie name must match AUTH_INDICATOR_COOKIE in lib/auth-storage.ts
// Duplicated here because Edge runtime cannot import browser-only modules
const AUTH_COOKIE = 'oet_auth';
const CSRF_COOKIE = 'oet_csrf';
const CSRF_HEADER = 'x-csrf-token';

/** Generate a hex CSRF token using Web Crypto (Edge Runtime compatible). */
function generateCsrfToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

// Public routes that don't require authentication
// Convention: all pages under the (auth) route group are public
const PUBLIC_PATHS = new Set([
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
]);

function isPublicPath(pathname: string): boolean {
  if (PUBLIC_PATHS.has(pathname)) return true;
  if (pathname.startsWith('/auth/callback/')) return true;
  return false;
}

/** Mutation methods that require CSRF validation */
const CSRF_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (isPublicPath(pathname)) {
    return NextResponse.next();
  }

  const authCookie = request.cookies.get(AUTH_COOKIE);

  if (!authCookie) {
    const signInUrl = new URL('/sign-in', request.url);
    signInUrl.searchParams.set('next', pathname);
    return NextResponse.redirect(signInUrl);
  }

  // ── CSRF double-submit cookie pattern ──
  // For API proxy mutation requests, verify the CSRF header matches the cookie.
  if (pathname.startsWith('/api/backend/') && CSRF_METHODS.has(request.method)) {
    const csrfCookie = request.cookies.get(CSRF_COOKIE)?.value;
    const csrfHeader = request.headers.get(CSRF_HEADER);
    if (!csrfCookie || !csrfHeader || csrfCookie !== csrfHeader) {
      return NextResponse.json(
        { error: 'CSRF_VALIDATION_FAILED', message: 'Missing or invalid CSRF token' },
        { status: 403 },
      );
    }
  }

  // Ensure every authenticated response has a CSRF cookie
  const response = NextResponse.next();
  const existingCsrf = request.cookies.get(CSRF_COOKIE);
  if (!existingCsrf) {
    const token = generateCsrfToken();
    response.cookies.set(CSRF_COOKIE, token, {
      httpOnly: false, // Must be readable by JS to include in headers
      secure: request.nextUrl.protocol === 'https:',
      sameSite: 'strict',
      path: '/',
      maxAge: 60 * 60 * 24, // 24 hours
    });
  }

  return response;
}

export const config = {
  matcher: [
    '/((?!api|_next/static|_next/image|favicon\\.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|woff|woff2|ttf|eot|css|js|map)$).*)',
  ],
};
