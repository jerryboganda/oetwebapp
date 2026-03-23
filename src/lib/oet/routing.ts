import type { OetRole } from "@/types/oet";

const ROLE_PREFIXES = {
  learner: "/app",
  expert: "/expert",
  admin: "/admin",
} as const satisfies Record<OetRole, string>;

const ROLE_LANDING_PATHS = {
  learner: "/app/dashboard",
  expert: "/expert/queue",
  admin: "/admin/content",
} as const satisfies Record<OetRole, string>;

export function resolveRoleLandingPath(role: OetRole): string {
  return ROLE_LANDING_PATHS[role];
}

export function getRoleFromPath(pathname: string): OetRole | null {
  if (pathname.startsWith(ROLE_PREFIXES.learner)) {
    return "learner";
  }
  if (pathname.startsWith(ROLE_PREFIXES.expert)) {
    return "expert";
  }
  if (pathname.startsWith(ROLE_PREFIXES.admin)) {
    return "admin";
  }

  return null;
}

export function canAccessRolePath(role: OetRole, pathname: string): boolean {
  const pathRole = getRoleFromPath(pathname);
  if (!pathRole) {
    return true;
  }

  return role === pathRole;
}

export function getRoleSurfacePrefix(role: OetRole): string {
  return ROLE_PREFIXES[role];
}
