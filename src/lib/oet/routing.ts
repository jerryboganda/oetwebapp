import type { OetRole } from "@/types/oet";

const ROLE_PREFIXES = {
  learner: ["/learner", "/app"],
  expert: ["/reviewer", "/expert"],
  admin: ["/cms", "/admin"],
} as const satisfies Record<OetRole, readonly string[]>;

const ROLE_LANDING_PATHS = {
  learner: "/learner/dashboard",
  expert: "/reviewer/queue",
  admin: "/cms/content",
} as const satisfies Record<OetRole, string>;

export function resolveRoleLandingPath(role: OetRole): string {
  return ROLE_LANDING_PATHS[role];
}

export function getRoleFromPath(pathname: string): OetRole | null {
  if (ROLE_PREFIXES.learner.some((prefix) => pathname.startsWith(prefix))) {
    return "learner";
  }
  if (ROLE_PREFIXES.expert.some((prefix) => pathname.startsWith(prefix))) {
    return "expert";
  }
  if (ROLE_PREFIXES.admin.some((prefix) => pathname.startsWith(prefix))) {
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
  return ROLE_PREFIXES[role][0];
}
