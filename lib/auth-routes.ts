import type { CurrentUser, UserRole } from '@/lib/types/auth';
import { normalizeAuthNextPath } from '@/lib/auth/routes';

function isSponsorPortalEnabled(): boolean {
  return process.env.NEXT_PUBLIC_SPONSOR_PORTAL_ENABLED === 'true';
}

export function defaultRouteForRole(role: UserRole): string {
  switch (role) {
    case 'admin':
      return '/admin';
    case 'expert':
      return '/expert';
    case 'sponsor':
      return isSponsorPortalEnabled() ? '/sponsor' : '/support';
    default:
      return '/';
  }
}

/**
 * Whether a role satisfies a required-role guard. Admin is a superset of
 * learner — admins can preview learner content without switching sessions.
 */
export function roleSatisfiesRequired(userRole: UserRole | null, requiredRole: UserRole): boolean {
  if (!userRole) return false;
  if (userRole === requiredRole) return true;
  // Admin can also access learner pages (preview / dual-role support)
  if (userRole === 'admin' && requiredRole === 'learner') return true;
  return false;
}

export function roleCanAccessPath(role: UserRole, path: string): boolean {
  const normalizedPath = normalizeAuthNextPath(path);
  if (!normalizedPath) {
    return false;
  }

  if (normalizedPath.startsWith('/admin')) {
    return role === 'admin';
  }

  if (normalizedPath.startsWith('/expert')) {
    return role === 'expert';
  }

  if (normalizedPath.startsWith('/sponsor')) {
    return role === 'sponsor' && isSponsorPortalEnabled();
  }

  // Learner paths: accessible by learner AND admin (preview/dual-role)
  return role === 'learner' || role === 'admin';
}

export function resolvePostAuthDestination(user: CurrentUser, nextPath?: string | null): string {
  const normalizedNext = normalizeAuthNextPath(nextPath);

  if (!normalizedNext) {
    return defaultRouteForRole(user.role);
  }

  if (normalizedNext === '/' && user.role !== 'learner') {
    return defaultRouteForRole(user.role);
  }

  return roleCanAccessPath(user.role, normalizedNext)
    ? normalizedNext
    : defaultRouteForRole(user.role);
}

export function resolveAuthenticatedDestination(user: CurrentUser, nextPath?: string | null): string {
  return resolvePostAuthDestination(user, nextPath);
}
