import { redirect } from "next/navigation";
import { getCurrentSessionUser } from "@/lib/auth/session.server";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import { resolveLoginRedirectPath } from "@/lib/auth/session";

export default async function HomePage() {
  const user = await getCurrentSessionUser();

  if (!user) {
    redirect(AUTH_ROUTES.signIn);
  }

  redirect(resolveLoginRedirectPath(user.role));
}
