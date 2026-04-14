import type { CurrentUser, UserRole } from '@/lib/types/auth';

export function defaultRouteForRole(role: UserRole): string {
  switch (role) {
    case 'admin':
      return '/admin';
    case 'expert':
      return '/expert';
    case 'sponsor':
      return '/sponsor';
    default:
      return '/';
  }
}

export function roleCanAccessPath(role: UserRole, path: string): boolean {
  if (!isSafeRelativePath(path)) {
    return false;
  }

  if (path.startsWith('/admin')) {
    return role === 'admin';
  }

  if (path.startsWith('/expert')) {
    return role === 'expert';
  }

  if (path.startsWith('/sponsor')) {
    return role === 'sponsor';
  }

  return role === 'learner';
}

function isSafeRelativePath(path: string): boolean {
  return path.startsWith('/') && !path.startsWith('//');
}

export function resolvePostAuthDestination(user: CurrentUser, nextPath?: string | null): string {
  const normalizedNext = nextPath?.trim();

  if (!normalizedNext || !normalizedNext.startsWith('/')) {
    return defaultRouteForRole(user.role);
  }

  return roleCanAccessPath(user.role, normalizedNext)
    ? normalizedNext
    : defaultRouteForRole(user.role);
}

export function resolveAuthenticatedDestination(user: CurrentUser, nextPath?: string | null): string {
  return resolvePostAuthDestination(user, nextPath);
}
