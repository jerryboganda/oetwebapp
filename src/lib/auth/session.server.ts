import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import type { OetRole } from "@/types/oet";
import {
  findMockUserByUsername,
  resolveLoginRedirectPath,
  type MockSessionUser,
} from "@/lib/auth/session";
import { AUTH_ROUTES } from "@/lib/auth/routes";

const SESSION_COOKIE_NAME = "oet-session-user";

export async function getCurrentSessionUser(): Promise<MockSessionUser | null> {
  const cookieStore = await cookies();
  const username = cookieStore.get(SESSION_COOKIE_NAME)?.value;

  return findMockUserByUsername(username) ?? null;
}

export async function setCurrentSessionUser(username: string): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(SESSION_COOKIE_NAME, username, {
    httpOnly: true,
    maxAge: 60 * 60 * 8,
    path: "/",
    sameSite: "lax",
  });
}

export async function clearCurrentSessionUser(): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.delete(SESSION_COOKIE_NAME);
}

export async function requireRoleAccess(
  allowedRoles: OetRole[]
): Promise<MockSessionUser> {
  const sessionUser = await getCurrentSessionUser();

  if (!sessionUser) {
    redirect(AUTH_ROUTES.signIn);
  }

  if (!allowedRoles.includes(sessionUser.role)) {
    redirect(resolveLoginRedirectPath(sessionUser.role));
  }

  return sessionUser;
}
